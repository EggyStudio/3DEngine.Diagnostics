using ImGuiNET;
using Xunit;

namespace Diagnostics.Tests;

/// <summary>
/// The console and the panel drawn with ImGui into a picture on the CPU, and steered through
/// ImGui's own keys. ImGui's context is one for the process, so every fact that draws is in this
/// class, whose facts run one after another.
/// </summary>
public sealed class ImGuiDrawingTests
{
    private enum Quality { Low, Medium, High }

    private static Registry Make()
    {
        var registry = new Registry();
        registry.Add(new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true));
        registry.Add(new Setting<float>("render.scale", "The share of the window's size a frame is drawn at.", 1) { Low = 0.5, High = 2, Step = 0.25 });
        registry.Add(new Setting<Quality>("render.quality", "How fine the frame is drawn.", Quality.Medium));
        registry.Add(new Setting<bool>("render.meshlets", "Whether meshes are drawn as meshlets.", false) { AtNextStart = true });
        registry.Add(new Setting<int>("sound.voices", "How many sounds play at once.", 32) { Low = 1, High = 64 });
        registry.Add(new Command("entity.count", "Counts the scene's entities.", "entity.count [<kind>]", _ => new Answer("7 entities")));
        return registry;
    }

    // The folder the pictures go to where the environment names one, a day's folder of the dump.
    private static void Keep(ImGuiFrames frames, string name)
    {
        if (Environment.GetEnvironmentVariable("DIAGNOSTICS_PICTURES") is { Length: > 0 } folder)
            ImGuiFrames.WritePng(Path.Combine(folder, name), frames.Picture, frames.Width, frames.Height);
    }

    [Fact]
    public void A_Closed_Console_And_Panel_Draw_Nothing_And_Open_Ones_Draw()
    {
        var registry = Make();
        var (console, panel) = (new ConsoleModel(registry), new PanelModel(registry));
        var (drawnConsole, drawnPanel) = (new ImGuiConsole(console), new ImGuiPanel(panel));
        using var frames = new ImGuiFrames(960, 540);
        void Draw()
        {
            drawnConsole.Draw(frames.Size);
            drawnPanel.Draw(frames.Size);
        }

        frames.Frame(Draw);
        Assert.Equal((0, 0), (frames.Vertices, frames.Drawn()));

        console.Open = true;
        frames.Frame(Draw);
        var consoleAlone = frames.Drawn();
        Assert.True(frames.Vertices > 0 && consoleAlone > 1000, $"{frames.Vertices} vertices and {consoleAlone} pixels");

        // The console stands in the top 40% of the window and nowhere under it.
        var below = Enumerable.Range((int)(540 * 0.4f) + 1, 540 - (int)(540 * 0.4f) - 1)
            .Sum(y => Enumerable.Range(0, 960).Count(x => frames.Picture[(y * 960 + x) * 4] != 255));
        Assert.Equal(0, below);

        // A window sized to what it holds is measured in its first frame and drawn from its second.
        console.Open = false;
        panel.Open = true;
        frames.Frame(Draw);
        frames.Frame(Draw);
        Assert.True(frames.Drawn() > 1000, $"{frames.Drawn()} pixels");
    }

    [Fact]
    public void A_Line_Typed_Into_The_Console_Is_Completed_Submitted_And_Recalled()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var drawn = new ImGuiConsole(console);
        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);

        // The field takes the keyboard as the console opens.
        frames.Frame(Draw);
        frames.Frame(Draw);
        Assert.True(drawn.Typing);

        // The player's edit shows the popup and the ghost, and Tab fills the selected candidate.
        frames.Type("render.v", Draw);
        Assert.Equal(("render.v", true, "sync"), (console.Typed, console.PopupShown, console.Ghost));
        frames.Tap(ImGuiKey.Tab, Draw);
        Assert.Equal(("render.vsync ", false), (console.Typed, console.PopupShown));
        frames.Type("off", Draw);
        frames.Tap(ImGuiKey.Enter, Draw);
        Assert.Equal(["render.vsync off"], console.History);
        Assert.Equal("off", registry.FindSetting("render.vsync")!.Text);
        Assert.Equal("", console.Typed);

        // The field has the keyboard again, and with no popup up brings the line back.
        frames.Frame(Draw);
        Assert.True(drawn.Typing);
        frames.Tap(ImGuiKey.UpArrow, Draw);
        Assert.Equal("render.vsync off", console.Typed);
        Assert.False(console.PopupShown);
    }

    [Fact]
    public void While_The_Popup_Shows_The_Arrows_Move_Its_Selection_And_Tab_Fills_It()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var drawn = new ImGuiConsole(console);
        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);

        frames.Frame(Draw);
        frames.Frame(Draw);
        frames.Type("render.s", Draw);
        Assert.Equal(["render.scale"], console.Candidates);
        frames.Type("c", Draw);
        frames.Frame(Draw);
        Assert.True(console.PopupShown);

        // The popup is a box above the field and the ghost follows the text, so the frame with
        // the popup hidden, the same line set by the program, differs from it in many pixels.
        var shown = frames.Picture.ToArray();
        console.Typed = "render.sc";
        frames.Frame(Draw);
        var differing = Enumerable.Range(0, shown.Length / 4).Count(i => !shown.AsSpan(i * 4, 4).SequenceEqual(frames.Picture.AsSpan(i * 4, 4)));
        Assert.True(differing > 800, $"{differing} pixels differ with the popup hidden");
        console.Edit("render.s");
        console.Edit("render.sc");
        frames.Frame(Draw);

        // Up moves the selection, which wraps on to the one candidate, and recalls no line though
        // the history holds one.
        console.Submit("render.vsync");
        frames.Tap(ImGuiKey.UpArrow, Draw);
        Assert.Equal(("render.sc", 0), (console.Typed, console.SelectedIndex));
        frames.Tap(ImGuiKey.DownArrow, Draw);
        Assert.Equal("render.sc", console.Typed);

        // Tab fills the candidate and hides the popup.
        frames.Tap(ImGuiKey.Tab, Draw);
        Assert.Equal(("render.scale ", false), (console.Typed, console.PopupShown));
    }

    [Fact]
    public void The_Panel_Is_Steered_By_ImGuis_Arrows_Enter_And_Escape()
    {
        var registry = Make();
        var panel = new PanelModel(registry) { Open = true };
        panel.Root.AddText("Game").AddAction("Count the entities", "entity.count");
        var drawn = new ImGuiPanel(panel);
        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);

        frames.Frame(Draw);
        Keep(frames, "panel-first-page.png");
        frames.Tap(ImGuiKey.Enter, Draw);
        Assert.Equal("render", panel.Title);
        frames.Tap(ImGuiKey.RightArrow, Draw);
        frames.Tap(ImGuiKey.DownArrow, Draw);
        frames.Tap(ImGuiKey.DownArrow, Draw);
        frames.Tap(ImGuiKey.RightArrow, Draw);
        Assert.Equal(["on", "Medium", "1.25", "on"], panel.Rows.Select(row => row.Value));
        Assert.Equal(2, panel.Selected);
        Keep(frames, "panel-render-page.png");

        // A pad's lower face button accepts and its right one goes back, as Enter and Escape do.
        frames.Tap(ImGuiKey.GamepadFaceRight, Draw);
        Assert.Equal("Settings", panel.Title);
        frames.Tap(ImGuiKey.Escape, Draw);
        Assert.False(panel.Open);
        frames.Frame(Draw);
        Assert.Equal(0, frames.Vertices);

        // A host that steers the model itself turns the reading off.
        panel.Open = true;
        drawn.ReadsKeys = false;
        frames.Tap(ImGuiKey.DownArrow, Draw);
        Assert.Equal(0, panel.Selected);
    }

    [Fact]
    public void The_Console_Leaves_No_Strip_Under_Its_Field_Where_There_Is_No_Hint()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var drawn = new ImGuiConsole(console);
        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);
        frames.Frame(Draw);
        frames.Frame(Draw);

        // The window ends at the 216th row. Going up from the row over its border through the
        // window's own color reaches the field, and what lies between is the strip under it.
        var height = (int)MathF.Round(540 * 0.4f);
        var ground = frames.Picture[((height - 3) * 960 + 480) * 4];
        bool Plain(int y) => Enumerable.Range(16, 928).All(x => frames.Picture[(y * 960 + x) * 4] == ground);
        var strip = 0;
        while (Plain(height - 2 - strip)) strip++;
        Assert.True(strip <= ImGui.GetStyle().WindowPadding.Y + 2, $"{strip} rows under the field");
        Assert.Equal("", console.Hint);

        // A hint takes its row where the name typed has one, inside the window.
        frames.Type("render.vsync", Draw);
        frames.Frame(Draw);
        var lower = 0;
        while (Plain(height - 2 - lower)) lower++;
        Assert.True(console.Hint.Length > 0 && lower is > 0 and <= 10, $"{lower} rows under a hint");
    }

    [Fact]
    public void A_Line_The_Host_Sets_Stays_While_The_Field_Has_The_Keyboard()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var drawn = new ImGuiConsole(console);
        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);
        frames.Frame(Draw);
        frames.Frame(Draw);
        frames.Type("ab", Draw);
        Assert.True(drawn.Typing);
        Assert.Equal("ab", console.Typed);

        // The field keeps its own text while it has the keyboard, so the line the host set has to
        // reach it, and the player's next key goes on from there.
        console.Typed = "render.vsync";
        frames.Frame(Draw);
        Assert.Equal("render.vsync", console.Typed);
        frames.Frame(Draw);
        Assert.Equal("render.vsync", console.Typed);
        frames.Type("!", Draw);
        Assert.Equal("render.vsync!", console.Typed);
    }

    [Fact]
    public void The_Console_Is_Drawn_With_Its_Popup_And_Ghost_For_A_Picture()
    {
        var registry = Make();
        registry.Add(new Setting<string>("gfx.renderer", "Which renderer draws.", "main"));
        var console = new ConsoleModel(registry) { Open = true };
        var drawn = new ImGuiConsole(console) { Height = 0.7f };
        console.Submit("help render.scale");
        console.Submit("render.scale 3");
        console.Write(ConsoleLevel.Warning, "The shader cache is cold.");
        console.Submit("entity.count");
        console.Submit("render.vsync off");

        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);
        frames.Frame(Draw);
        frames.Frame(Draw);

        // Six candidates, the first five that begin with the typed word and one whose later part
        // does, the second selected.
        frames.Type("re", Draw);
        console.Navigate(1);
        frames.Frame(Draw);
        Assert.Equal(6, console.Candidates.Count);
        Assert.Equal("nder.quality", console.Ghost);
        Assert.True(frames.Drawn() > 5000, $"{frames.Drawn()} pixels");
        Keep(frames, "console-popup.png");
    }
}
