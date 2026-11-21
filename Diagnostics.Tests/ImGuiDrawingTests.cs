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

        frames.Type("render.v", Draw);
        Assert.Equal("render.v", drawn.Typed);
        frames.Tap(ImGuiKey.Tab, Draw);
        Assert.Equal("render.vsync ", drawn.Typed);
        frames.Type("off", Draw);
        frames.Tap(ImGuiKey.Enter, Draw);
        Assert.Equal(["render.vsync off"], console.History);
        Assert.Equal("off", registry.FindSetting("render.vsync")!.Text);
        Assert.Equal("", drawn.Typed);

        // The field has the keyboard again, and up brings the line back.
        frames.Frame(Draw);
        Assert.True(drawn.Typing);
        frames.Tap(ImGuiKey.UpArrow, Draw);
        Assert.Equal("render.vsync off", drawn.Typed);
        Keep(frames, "console.png");
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
    public void The_Console_And_The_Panel_Are_Drawn_Together_For_A_Picture()
    {
        var registry = Make();
        var console = new ConsoleModel(registry) { Open = true };
        var panel = new PanelModel(registry) { Open = true };
        var (drawnConsole, drawnPanel) = (new ImGuiConsole(console), new ImGuiPanel(panel) { ReadsKeys = false });
        console.Submit("help render.scale");
        console.Submit("render.scale 3");
        console.Write(ConsoleLevel.Warning, "The shader cache is cold.");
        console.Submit("entity.count");
        drawnConsole.Typed = "render.q";
        panel.Press(PanelInput.Accept);
        panel.Press(PanelInput.Right);
        panel.Press(PanelInput.Down);

        using var frames = new ImGuiFrames(960, 540);
        void Draw()
        {
            drawnConsole.Draw(frames.Size);
            drawnPanel.Draw(frames.Size);
        }

        frames.Frame(Draw);
        frames.Frame(Draw);
        Assert.True(frames.Drawn() > 5000, $"{frames.Drawn()} pixels");
        Keep(frames, "console-and-panel.png");
    }
}
