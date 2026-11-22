using Xunit;

namespace Diagnostics.Tests;

public class PanelModelTests
{
    private enum Quality { Low, Medium, High }

    private static (PanelModel Panel, Registry Registry) Make()
    {
        var registry = new Registry();
        registry.Add(new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true));
        registry.Add(new Setting<float>("render.scale", "The share of the window's size a frame is drawn at.", 1) { Low = 0.5, High = 2, Step = 0.25 });
        registry.Add(new Setting<Quality>("render.quality", "How fine the frame is drawn.", Quality.Medium));
        registry.Add(new Setting<bool>("render.meshlets", "Whether meshes are drawn as meshlets.", false) { AtNextStart = true });
        registry.Add(new Setting<int>("sound.voices", "How many sounds play at once.", 32) { Low = 1, High = 64 });
        registry.Add(new Setting<string>("name", "The player's name.", "Player"));
        registry.Add(new Command("game.quit", "Ends the game.", "game.quit", _ => new Answer("Bye.")));
        return (new PanelModel(registry), registry);
    }

    private static void Press(PanelModel panel, params PanelInput[] inputs)
    {
        foreach (var input in inputs) panel.Press(input);
    }

    [Fact]
    public void The_First_Page_Holds_A_Page_For_Each_First_Word_And_The_Hosts_Rows()
    {
        var (panel, _) = Make();
        panel.Root.AddText("Game").AddAction("Quit", "game.quit");

        Assert.Equal("Settings", panel.Title);
        Assert.Equal(
            [
                new(PanelRowKind.Page, "general", "", true, false, false),
                new(PanelRowKind.Page, "render", "", false, false, false),
                new(PanelRowKind.Page, "sound", "", false, false, false),
                new(PanelRowKind.Text, "Game", "", false, false, false),
                new(PanelRowKind.Action, "Quit", "", false, false, false),
            ],
            panel.Rows);

        // Down passes the row of text, and stays at the last row.
        Press(panel, PanelInput.Down, PanelInput.Down, PanelInput.Down, PanelInput.Down);
        Assert.Equal(4, panel.Selected);
        panel.Press(PanelInput.Accept);
        Assert.Equal("Bye.", panel.Answer?.Text);
        Press(panel, PanelInput.Up);
        Assert.Equal(2, panel.Selected);
    }

    [Fact]
    public void A_Page_Of_Settings_Is_Stepped_With_Left_And_Right()
    {
        var (panel, registry) = Make();
        Press(panel, PanelInput.Down, PanelInput.Accept);
        Assert.Equal("render", panel.Title);
        Assert.Equal(["meshlets", "quality", "scale", "vsync"], panel.Rows.Select(row => row.Label));
        Assert.Equal(["off", "Medium", "1", "on"], panel.Rows.Select(row => row.Value));

        // A setting for the next start shows the value set and its mark.
        Press(panel, PanelInput.Right);
        Assert.Equal(new PanelRow(PanelRowKind.Setting, "meshlets", "on", true, Pending: true, Overridden: false), panel.Rows[0]);

        // A choice goes to its neighbor and past the last to the first, by accept as well.
        Press(panel, PanelInput.Down, PanelInput.Right);
        Assert.Equal("High", panel.Rows[1].Value);
        Press(panel, PanelInput.Accept);
        Assert.Equal("Low", panel.Rows[1].Value);
        Press(panel, PanelInput.Left);
        Assert.Equal("High", panel.Rows[1].Value);

        // A number moves by its step and stops at its range.
        Press(panel, PanelInput.Down, PanelInput.Right, PanelInput.Right, PanelInput.Right, PanelInput.Right, PanelInput.Right);
        Assert.Equal("2", panel.Rows[2].Value);
        Press(panel, PanelInput.Left);
        Assert.Equal("1.75", panel.Rows[2].Value);
        Assert.Equal("render.scale is 1.75, 1 by default.", registry.Run("render.scale").Text);

        // Back leaves the page with the first page's row still selected, and then the panel.
        panel.Open = true;
        Press(panel, PanelInput.Back);
        Assert.Equal(("Settings", 1, true), (panel.Title, panel.Selected, panel.Open));
        Press(panel, PanelInput.Back);
        Assert.False(panel.Open);
    }

    [Fact]
    public void Each_Answer_The_Panel_Gets_Is_Told_With_The_Name_Of_The_Setting_Or_The_Line_Of_The_Action()
    {
        var (panel, registry) = Make();
        registry.Add(new Command("game.boom", "Throws.", "game.boom", _ => throw new InvalidOperationException("No world is lent.")));
        panel.Root.AddAction("Boom", "game.boom").AddAction("Quit", "game.quit");
        var told = new List<(string Name, string? Code, bool Thrown, string Text)>();
        panel.Answered += (name, answer) => told.Add((name, answer.Code, answer.Error is not null, answer.Text));

        // The render page, where accept turns a toggle over and right steps a number.
        Press(panel, PanelInput.Down, PanelInput.Accept, PanelInput.Accept, PanelInput.Down, PanelInput.Down, PanelInput.Right);
        Press(panel, PanelInput.Back, PanelInput.Down, PanelInput.Down, PanelInput.Accept, PanelInput.Down, PanelInput.Accept);

        Assert.Equal(
            [
                ("render.meshlets", null, false, "render.meshlets is on at the next start."),
                ("render.scale", null, false, "render.scale is 1.25."),
                ("game.boom", Answer.Failed, true, "game.boom failed. No world is lent."),
                ("game.quit", null, false, "Bye."),
            ],
            told);
        Assert.Equal("Bye.", panel.Answer?.Text);
    }

    [Fact]
    public void A_Whole_Number_Steps_By_One_And_A_Text_Refuses_A_Step()
    {
        var (panel, _) = Make();
        Press(panel, PanelInput.Down, PanelInput.Down, PanelInput.Accept, PanelInput.Right);
        Assert.Equal(("sound", "33"), (panel.Title, panel.Rows[0].Value));

        Press(panel, PanelInput.Back, PanelInput.Up, PanelInput.Up, PanelInput.Accept, PanelInput.Right);
        Assert.Equal(("general", "Player"), (panel.Title, panel.Rows[0].Value));
        Assert.Equal(Answer.BadArgument, panel.Answer?.Code);
    }

    [Fact]
    public void Ten_Steps_Of_A_Tenth_Give_One()
    {
        var gain = new Setting<float>("sound.gain", "How loud the game is.", 0) { Low = 0, High = 1 };
        for (var i = 0; i < 10; i++) gain.Move(1);
        Assert.Equal("1", gain.Text);
        for (var i = 0; i < 3; i++) gain.Move(-1);
        Assert.Equal("0.7", gain.Text);
    }

    [Fact]
    public void A_Setting_Added_Later_Appears_And_A_Hosts_Page_Opens()
    {
        var (panel, registry) = Make();
        var page = new PanelPage("About").AddText("A game.").Add(registry.FindSetting("render.vsync")!, "Vertical sync");
        panel.Root.AddPage(page);
        registry.Add(new Setting<bool>("audio.mute", "Whether the game is silent.", false));
        Assert.Equal(["audio", "general", "render", "sound", "About"], panel.Rows.Select(row => row.Label));

        Press(panel, PanelInput.Down, PanelInput.Down, PanelInput.Down, PanelInput.Down, PanelInput.Accept);
        // The first row is text, so the row after it is the selected one.
        Assert.Equal(("About", 1), (panel.Title, panel.Selected));
        Press(panel, PanelInput.Accept);
        Assert.Equal(new PanelRow(PanelRowKind.Setting, "Vertical sync", "off", true, false, false), panel.Rows[1]);
    }

    [Fact]
    public void An_Empty_Panel_Selects_Nothing_And_Takes_Every_Input()
    {
        var registry = new Registry();
        var panel = new PanelModel(registry, "Empty") { Open = true };
        Assert.Equal(("Empty", -1), (panel.Title, panel.Selected));
        Press(panel, PanelInput.Up, PanelInput.Down, PanelInput.Left, PanelInput.Right, PanelInput.Accept, PanelInput.Back);
        Assert.False(panel.Open);
    }
}
