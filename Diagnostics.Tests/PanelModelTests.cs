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

    [Fact]
    public void A_Locked_Row_Shows_As_One_That_Only_Reads_And_Takes_Input_Again_Once_The_Lock_Lets_Go()
    {
        var registry = new Registry();
        var shown = false;
        registry.Add(new Setting<bool>("window.resizable", "Whether the window resizes.", true) { Lock = () => shown ? null : "The window is not shown." });
        var panel = new PanelModel(registry);
        Press(panel, PanelInput.Accept);
        Assert.Equal([new PanelRow(PanelRowKind.Setting, "resizable", "on", true, false, false, ReadOnly: true)], panel.Rows);
        Press(panel, PanelInput.Right);
        Assert.Equal((Answer.NotSettable, "The window is not shown."), (panel.Answer?.Code, panel.Answer?.Text));

        shown = true;
        Press(panel, PanelInput.Right);
        Assert.Equal([new PanelRow(PanelRowKind.Setting, "resizable", "off", true, false, false)], panel.Rows);
    }

    [Fact]
    public void A_Text_Row_With_An_Offer_Steps_Through_The_List_Asked_Afresh_On_Right_And_Accept()
    {
        var registry = new Registry();
        var sizes = new List<string> { "native", "1920x1080" };
        registry.Add(new Setting<string>("window.size", "The size.", "native") { Offer = () => sizes });
        registry.Add(new Setting<string>("window.title", "The title.", "Game"));
        var panel = new PanelModel(registry);
        Press(panel, PanelInput.Accept);

        Press(panel, PanelInput.Accept);
        Assert.Equal("1920x1080", panel.Rows[0].Value);
        sizes.Add("1280x720");
        Press(panel, PanelInput.Right);
        Assert.Equal("1280x720", panel.Rows[0].Value);
        Press(panel, PanelInput.Left);
        Assert.Equal("1920x1080", panel.Rows[0].Value);

        // A text with no offer has no step, and accept opens a field for it.
        Press(panel, PanelInput.Down, PanelInput.Right);
        Assert.Equal("Game", panel.Rows[1].Value);
        Assert.Equal(Answer.BadArgument, panel.Answer?.Code);
        Assert.Null(panel.Editing);
    }

    [Fact]
    public void A_Text_Row_With_No_Offer_Opens_A_Field_That_Writes_The_Setting_And_Back_Puts_It_Away()
    {
        var registry = new Registry();
        registry.Add(new Setting<string>("window.title", "The title.", "Game",
            apply: value => value.Length > 8 ? "No more than 8 characters." : null));
        registry.Add(new Setting<string>("window.icon", "The icon.", "a.png") { ReadOnly = true });
        registry.Add(new Setting<bool>("window.vsync", "Whether a frame waits.", true));
        var panel = new PanelModel(registry);
        Press(panel, PanelInput.Accept);
        Assert.Equal(["icon", "title", "vsync"], panel.Rows.Select(row => row.Label));

        // A row that only reads has no field.
        Press(panel, PanelInput.Accept);
        Assert.Null(panel.Editing);
        Press(panel, PanelInput.Down, PanelInput.Accept);
        Assert.Equal("Game", panel.Editing);
        Assert.Equal("Game", panel.Rows[1].Editing);
        Assert.Null(panel.Rows[0].Editing);

        // The field has the keys, so the other inputs do nothing, and the text is only a draft.
        panel.Edit("Gamer");
        Press(panel, PanelInput.Down, PanelInput.Left, PanelInput.Right);
        Assert.Equal(("Gamer", 1), (panel.Editing, panel.Selected));
        Assert.Equal("Game", panel.Rows[1].Value);
        Assert.Equal("Game", ((Setting<string>)registry.FindSetting("window.title")!).Value);

        // Accept writes it, and the field is closed.
        Press(panel, PanelInput.Accept);
        Assert.Equal(("Gamer", null), (panel.Rows[1].Value, panel.Editing));
        Assert.Equal("Gamer", ((Setting<string>)registry.FindSetting("window.title")!).Value);
        Assert.True(panel.Answer?.Done);

        // A refused text leaves the field open with its reason and the setting as it was.
        Press(panel, PanelInput.Accept);
        panel.Edit("Far too long");
        Press(panel, PanelInput.Accept);
        Assert.Equal(("Far too long", "Gamer"), (panel.Editing, panel.Rows[1].Value));
        Assert.Equal((Answer.Refused, "No more than 8 characters."), (panel.Answer?.Code, panel.Answer?.Text));

        // Back puts the field away without a write, and a second leaves the page.
        Press(panel, PanelInput.Back);
        Assert.Equal(("Gamer", null, "window"), (panel.Rows[1].Value, panel.Editing, panel.Title));
        Press(panel, PanelInput.Back);
        Assert.Equal("Settings", panel.Title);

        // Edit with no field open does nothing, and closing the panel puts a field away.
        panel.Edit("x");
        Assert.Null(panel.Editing);
        Press(panel, PanelInput.Accept, PanelInput.Down, PanelInput.Accept);
        Assert.NotNull(panel.Editing);
        panel.Open = false;
        Assert.Null(panel.Editing);
    }

    [Fact]
    public void A_Setting_Marked_Out_Of_The_Panel_Has_No_Row_While_The_Console_Still_Reads_And_Sets_It()
    {
        var registry = new Registry();
        registry.Add(new Setting<int>("hidden.seed", "The seed.", 1) { InPanel = false });
        registry.Add(new Setting<bool>("window.vsync", "Whether a frame waits.", true));
        registry.Add(new Setting<int>("window.fps", "The frame rate.", 60) { InPanel = false });
        var panel = new PanelModel(registry);
        panel.Root.Add(registry.FindSetting("hidden.seed")!);

        // A page whose settings are all out has no row, and the host's own add of one shows none.
        Assert.Equal(["window"], panel.Rows.Where(row => row.Kind == PanelRowKind.Page).Select(row => row.Label));
        Assert.Equal(PanelRowKind.Page, panel.Rows[0].Kind);
        Press(panel, PanelInput.Accept);
        Assert.Equal(["vsync"], panel.Rows.Select(row => row.Label));

        Assert.Equal("window.fps is 60.", registry.Run("window.fps").Text);
        Assert.Equal("window.fps is 30.", registry.Run("window.fps 30").Text);
        Assert.Equal("hidden.seed is 9.", registry.Run("hidden.seed 9").Text);
    }

    [Fact]
    public void A_Page_Lists_Settings_With_An_Order_First_By_It_And_By_Name_Among_Equals()
    {
        var registry = new Registry();
        registry.Add(new Setting<bool>("window.fullscreen", "Whether it fills the screen.", false));
        registry.Add(new Setting<bool>("window.vsync", "Whether a frame waits.", true) { Order = 2 });
        registry.Add(new Setting<int>("window.fps", "The frame rate.", 60) { Order = 2 });
        registry.Add(new Setting<int>("window.width", "The width.", 800) { Order = -1 });
        registry.Add(new Setting<int>("window.height", "The height.", 600));
        registry.Add(new Setting<int>("window.depth", "The depth.", 24) { Order = 10 });
        var panel = new PanelModel(registry);
        Press(panel, PanelInput.Accept);

        // Order -1, then the two of 2 by name, then 10, then the ones with none by name.
        Assert.Equal(["width", "fps", "vsync", "depth", "fullscreen", "height"], panel.Rows.Select(row => row.Label));

        // The console's listing and completion stay by name, whatever the order.
        Assert.Equal(
            ["window.depth", "window.fps", "window.fullscreen", "window.height", "window.vsync", "window.width"],
            registry.Settings.Select(setting => setting.Name));
        var console = new ConsoleModel(registry);
        console.Edit("window.");
        Assert.Equal(
            ["window.depth", "window.fps", "window.fullscreen", "window.height", "window.vsync", "window.width"],
            console.Candidates);
    }

    [Fact]
    public void A_Row_That_Only_Reads_Says_So_And_A_Refused_Value_Leaves_The_Row_Where_It_Was()
    {
        var registry = new Registry();
        registry.Add(new Setting<string>("build.version", "The version.", "dev", () => "1.4") { ReadOnly = true });
        registry.Add(new Setting<bool>("build.debug", "Whether it is a debug build.", false, () => true) { ReadOnly = true });
        registry.Add(new Setting<int>("build.limit", "The limit.", 6, apply: value => value > 6 ? "No more than 6 fit." : null) { Low = 1, High = 9 });
        var panel = new PanelModel(registry);
        Press(panel, PanelInput.Accept);
        Assert.Equal(
            [
                new PanelRow(PanelRowKind.Setting, "debug", "on", true, false, false, ReadOnly: true),
                new PanelRow(PanelRowKind.Setting, "limit", "6", false, false, false),
                new PanelRow(PanelRowKind.Setting, "version", "1.4", false, false, false, ReadOnly: true),
            ],
            panel.Rows);

        Press(panel, PanelInput.Right);
        Assert.Equal(Answer.NotSettable, panel.Answer?.Code);
        Press(panel, PanelInput.Accept);
        Assert.Equal((Answer.NotSettable, "on"), (panel.Answer?.Code, panel.Rows[0].Value));

        // The step to 7 is refused by the setter, and the row keeps 6.
        Press(panel, PanelInput.Down, PanelInput.Right);
        Assert.Equal(("6", "No more than 6 fit.", Answer.Refused), (panel.Rows[1].Value, panel.Answer?.Text, panel.Answer?.Code));
        Press(panel, PanelInput.Left);
        Assert.Equal("5", panel.Rows[1].Value);
    }
}
