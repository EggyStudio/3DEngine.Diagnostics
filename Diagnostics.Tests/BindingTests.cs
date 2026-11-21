using Xunit;

namespace Diagnostics.Tests;

/// <summary>
/// The README's binding block run word for word against a host that stands for an engine's own
/// calls.
/// </summary>
public sealed class BindingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "diagnostics-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class Host(string settingsPath)
    {
        public string SettingsPath => settingsPath;

        public bool Vsync { get; private set; } = true;

        public List<string> Logged { get; } = [];

        public bool GetVsync() => Vsync;

        public void SetVsync(bool on) => Vsync = on;

        public string Screenshot(string? path) => $"Wrote {path ?? "shot.png"}.";

        public void Log(string line) => Logged.Add(line);

        public int Entities => 7;

        public int Shots { get; private set; }

        public double FrameSeconds => 1 / 60.0;

        public bool FieldIsTyping => false;

        public List<string> KeysPressed { get; } = [];

        public List<string> KeysReleased { get; } = [];

        public IEnumerable<string> PassTimings() => ["shadow 1.2 ms"];

        public void Screenshot() => Shots++;

        public bool Pressed(string key) => KeysPressed.Contains(key);
    }

    [Fact]
    public void The_Readmes_Second_Block_Binds_The_Overlay_The_Panel_And_The_Keys()
    {
        var host = new Host(Path.Combine(_folder, "settings.txt"));
        var registry = new Registry();
        registry.Add(new Command("game.quit", "Ends the game.", "game.quit", _ => new Answer("Bye.")));
        var console = new ConsoleModel(registry);

        // The block of the README's section The overlay, the panel and the keys, its part run once.
        var overlay = new OverlayModel();
        overlay.AddSection("scene", OverlayPlace.Left, () => [$"entities {host.Entities}"]);
        overlay.AddSection("passes", OverlayPlace.Right, host.PassTimings, detail: true);
        var panel = new PanelModel(registry);
        panel.Root.AddAction("Quit", "game.quit");
        var keys = new DiagnosticKeys(registry) { Overlay = overlay, Panel = panel, Console = console };
        keys.ScreenshotAsked += host.Screenshot;

        // Its part run each frame, over four frames of keys.
        string[] none = [];
        foreach (var (pressed, released) in new[] { (new[] { "F2", "F4" }, none), (none, ["F2", "F4"]), (["F4"], none), (none, ["F4"]) })
        {
            host.KeysPressed.Clear();
            host.KeysReleased.Clear();
            host.KeysPressed.AddRange(pressed);
            host.KeysReleased.AddRange(released);

            overlay.Frame(host.FrameSeconds);
            foreach (var key in host.KeysPressed) keys.KeyDown(key, host.FieldIsTyping);
            foreach (var key in host.KeysReleased) keys.KeyUp(key);
            if (panel.Open && host.Pressed("Down")) panel.Press(PanelInput.Down);
        }

        // The two keys held together were a chord, and the panel's key alone opened the panel.
        Assert.Equal(0, host.Shots);
        Assert.True(panel.Open);
        Assert.Equal(["entities 7"], overlay.Lines(OverlayPlace.Left));
        Assert.Equal(4, overlay.FrameTimes.Count);
    }

    [Fact]
    public void The_Readmes_Block_Binds_A_Host()
    {
        var host = new Host(Path.Combine(_folder, "settings.txt"));
        // The README's block from here to the end marker, word for word.
        // Once, as the host starts, on any thread.
        var registry = Registry.Process;
        registry.Add(new Command("screen.shot", "Writes a picture of the frame.", "screen.shot [<path>]",
            line => new Answer(host.Screenshot(line.Words.Count > 0 ? line.Words[0] : null))));
        registry.Add(new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true,
            host.GetVsync, host.SetVsync) { EnvironmentVariable = "GAME_VSYNC" });

        // Once the settings are added, on the host's thread.
        var file = new SettingsFile(host.SettingsPath);
        foreach (var refusal in file.Read(registry)) host.Log(refusal);
        var console = new ConsoleModel(registry);

        // From any thread, a line of the host's log or a command's late answer.
        console.Write(ConsoleLevel.Warning, "The shader cache is cold.");

        // On the host's thread, a line the player entered or a socket handed over.
        var answer = console.Submit("render.vsync off");
        if (!answer.Done) host.Log($"{answer.Code} {answer.Text}");

        // As the player leaves the settings and as the program ends.
        file.Write(registry);
        // The end marker.

        Assert.False(host.Vsync);
        Assert.Empty(host.Logged);
        Assert.Equal("render.vsync off\n", File.ReadAllText(host.SettingsPath));
        Assert.Equal("Wrote a.png.", registry.Run("screen.shot a.png").Text);
        Assert.Equal(3, console.Lines().Count);
        Assert.True(registry.Remove("screen.shot") && registry.Remove("render.vsync"));
    }
}
