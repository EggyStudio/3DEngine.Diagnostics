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
    public void A_Collapsed_Line_Shows_Its_Count_After_Its_Text_Only_Where_It_Is_Over_One()
    {
        // The rightmost drawn pixel of the console's first line, written once and then twice.
        int Right(int times)
        {
            var console = new ConsoleModel(new Registry()) { Open = true };
            var drawn = new ImGuiConsole(console);
            using var frames = new ImGuiFrames(960, 540);
            void Draw() => drawn.Draw(frames.Size);
            for (var i = 0; i < times; i++) console.Write(ConsoleLevel.Warning, "The shader cache is cold.");
            frames.Frame(Draw);
            frames.Frame(Draw);
            // The text is lighter than the window it stands on.
            var ground = frames.Picture[(20 * 960 + 940) * 4 + 2];
            for (var x = 900; x > 0; x--)
                for (var y = 8; y < 28; y++)
                    if (Math.Abs(frames.Picture[(y * 960 + x) * 4 + 2] - ground) > 40) return x;
            return 0;
        }

        var (once, twice, again) = (Right(1), Right(2), Right(1));
        Assert.Equal(once, again);
        Assert.True(twice > once + 10, $"{once} for one writing, {twice} for two");
    }

    [Fact]
    public void A_Line_Wider_Than_The_Window_Wraps_And_The_Log_Has_No_Horizontal_Scrollbar()
    {
        // The pixels off the ground below the first line, where only a wrapped second line, or
        // a scrollbar along the log's lower edge, leaves any.
        int Below(string text)
        {
            var console = new ConsoleModel(new Registry()) { Open = true };
            var drawn = new ImGuiConsole(console);
            using var frames = new ImGuiFrames(960, 540);
            void Draw() => drawn.Draw(frames.Size);
            console.Write(ConsoleLevel.Warning, text);
            frames.Frame(Draw);
            frames.Frame(Draw);
            var ground = frames.Picture[(20 * 960 + 940) * 4 + 2];
            var ink = 0;
            for (var y = 30; y < 60; y++)
                for (var x = 0; x < 900; x++)
                    if (Math.Abs(frames.Picture[(y * 960 + x) * 4 + 2] - ground) > 40) ink++;
            return ink;
        }

        var wide = string.Join(' ', Enumerable.Repeat("The shader cache is cold.", 12));
        Assert.Equal(0, Below("The shader cache is cold."));
        Assert.True(Below(wide) > 0);
    }

    [Fact]
    public void A_Selected_Row_That_Only_Reads_Carries_No_Marks_Round_Its_Value()
    {
        // The vertices of the panel's page with the one setting selected, where the two marks
        // round its value add glyphs to the frame where there are any.
        int Drawn(bool readOnly)
        {
            var registry = new Registry();
            registry.Add(new Setting<string>("build.mode", "The mode.", "dev") { ReadOnly = readOnly });
            var panel = new PanelModel(registry) { Open = true };
            var drawn = new ImGuiPanel(panel);
            using var frames = new ImGuiFrames(960, 540);
            void Draw() => drawn.Draw(frames.Size);
            frames.Frame(Draw);
            frames.Tap(ImGuiKey.Enter, Draw);
            frames.Frame(Draw);
            Assert.True(panel.Rows[0].Selected);
            return frames.Vertices;
        }

        var (reads, steps) = (Drawn(true), Drawn(false));
        Assert.True(reads < steps, $"{reads} vertices where it reads, {steps} where it steps");
    }

    // How far a pixel's channels part, which is nothing in a gray.
    private static int Hue(byte[] picture, int pixel) =>
        Math.Max(picture[pixel * 4], Math.Max(picture[pixel * 4 + 1], picture[pixel * 4 + 2]))
        - Math.Min(picture[pixel * 4], Math.Min(picture[pixel * 4 + 1], picture[pixel * 4 + 2]));

    [Fact]
    public void The_Console_And_The_Panel_Carry_No_Hue_Of_The_Hosts_Theme_And_Failures_Keep_Theirs()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var panel = new PanelModel(registry);
        var (drawnConsole, drawnPanel) = (new ImGuiConsole(console) { Height = 0.5f }, new ImGuiPanel(panel));
        using var frames = new ImGuiFrames(960, 540);

        // A host theme that is blue everywhere a console could take a color from.
        var colors = ImGui.GetStyle().Colors;
        for (var i = 0; i < (int)ImGuiCol.COUNT; i++) colors[i] = new(0.1f, 0.3f, 0.9f, 1f);
        var before = Enumerable.Range(0, (int)ImGuiCol.COUNT).Select(i => ImGui.GetStyle().Colors[i]).ToArray();
        var kept = true;
        void Draw()
        {
            drawnConsole.Draw(frames.Size);
            drawnPanel.Draw(frames.Size);
            // The host's own windows keep its theme, the palette being popped after the drawing.
            kept &= Enumerable.Range(0, (int)ImGuiCol.COUNT).All(i => ImGui.GetStyle().Colors[i] == before[i]);
        }

        console.Submit("help render.scale");
        frames.Frame(Draw);
        frames.Frame(Draw);
        frames.Type("re", Draw);
        frames.Frame(Draw);
        Assert.True(console.PopupShown);

        Assert.Equal(0, Enumerable.Range(0, 960 * 540).Count(i => Hue(frames.Picture, i) > 4));
        Assert.True(kept);

        // The panel, steered to a row, has none either.
        (console.Open, panel.Open) = (false, true);
        frames.Frame(Draw);
        frames.Frame(Draw);
        frames.Tap(ImGuiKey.Enter, Draw);
        Assert.True(frames.Drawn() > 3000);
        Assert.Equal(0, Enumerable.Range(0, 960 * 540).Count(i => Hue(frames.Picture, i) > 4));
        (console.Open, panel.Open) = (true, false);

        // A warning and an error line are the lines that stand out by their hue.
        console.Write(ConsoleLevel.Warning, "The shader cache is cold.");
        console.Write(ConsoleLevel.Error, "The device was lost.");
        frames.Frame(Draw);
        frames.Frame(Draw);
        var failures = Enumerable.Range(0, 960 * 540).Count(i => Hue(frames.Picture, i) > 60);
        Assert.True(failures > 40, $"{failures} pixels of a hue");
        Assert.True(kept);
    }

    [Fact]
    public void The_Popup_Joins_The_Field_With_No_Row_Of_Window_Ground_Between_And_Equal_Edges()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var drawn = new ImGuiConsole(console) { Height = 0.5f };
        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);
        console.Submit("entity.count");
        frames.Frame(Draw);
        frames.Frame(Draw);
        frames.Type("re", Draw);
        frames.Frame(Draw);
        Assert.True(console.PopupShown);

        // The window's own ground is the color of its empty part, where no text stands.
        var picture = frames.Picture;
        int Red(int x, int y) => picture[(y * 960 + x) * 4];
        var ground = Red(700, 100);
        var height = (int)MathF.Round(540 * 0.5f);

        // Down the right of the box, from under the window's border to over its border at the foot,
        // the rows that are not ground are one run, the popup and the field as one.
        var rows = Enumerable.Range(4, height - 8).Where(y => Red(700, y) != ground).ToArray();
        Assert.True(rows.Length > 100, $"{rows.Length} rows");
        Assert.Equal(rows[^1] - rows[0] + 1, rows.Length);

        // The left and the right edge of a row of the popup and of the field are the same.
        (int Left, int Right) Edges(int y) => (
            Enumerable.Range(0, 960).First(x => Red(x, y) != ground && x > 4),
            Enumerable.Range(0, 960).Last(x => Red(x, y) != ground && x < 955));
        Assert.Equal(Edges(rows[0] + 6), Edges(rows[^1] - 4));
    }

    [Fact]
    public void The_Field_Takes_The_Popups_Black_While_The_Popup_Shows_And_Keeps_Its_Ground_Otherwise()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var drawn = new ImGuiConsole(console) { Height = 0.5f };
        using var frames = new ImGuiFrames(960, 540);
        void Draw() => drawn.Draw(frames.Size);
        console.Submit("entity.count");
        frames.Frame(Draw);
        frames.Frame(Draw);
        int Red(int x, int y) => frames.Picture[(y * 960 + x) * 4];

        // With no popup, the field is the half-alpha ground over the window's, the lighter of two.
        var field = Enumerable.Range(4, 270 - 8).Where(y => Red(700, y) != Red(700, 100)).ToArray();
        var alone = Red(700, field[^1] - 4);
        Assert.True(alone > 0, $"{alone} for the field alone");

        frames.Type("re", Draw);
        frames.Frame(Draw);
        Assert.True(console.PopupShown);

        // With it, the popup's rows and the field's are one shade, the box's inside a single black.
        var rows = Enumerable.Range(4, 270 - 8).Where(y => Red(700, y) != Red(700, 100)).ToArray();
        var (popup, joined) = (Red(700, rows[0] + 2), Red(700, rows[^1] - 4));
        Assert.Equal(popup, joined);
        Assert.True(joined < alone, $"{joined} joined and {alone} alone");
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
