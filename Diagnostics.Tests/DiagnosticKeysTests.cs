using Xunit;

namespace Diagnostics.Tests;

public class DiagnosticKeysTests
{
    private static (DiagnosticKeys Keys, Registry Registry, OverlayModel Overlay, PanelModel Panel, ConsoleModel Console) Make()
    {
        var registry = new Registry();
        var (overlay, panel, console) = (new OverlayModel(), new PanelModel(registry), new ConsoleModel(registry));
        return (new DiagnosticKeys(registry) { Overlay = overlay, Panel = panel, Console = console }, registry, overlay, panel, console);
    }

    private static KeyAction? Tap(DiagnosticKeys keys, string key, bool typing = false)
    {
        keys.KeyDown(key, typing);
        return keys.KeyUp(key);
    }

    [Fact]
    public void The_Five_Keys_Default_To_F1_To_F5_And_Turn_Their_Parts_Over()
    {
        var (keys, registry, overlay, panel, console) = Make();
        var shots = 0;
        keys.ScreenshotAsked += () => shots++;
        Assert.Equal(
            ["keys.console F5", "keys.hide_interface F1", "keys.overlay F3", "keys.panel F4", "keys.screenshot F2"],
            registry.Run("list keys.").Text.Split('\n'));

        Assert.Equal(KeyAction.HideInterface, Tap(keys, "F1"));
        Assert.True(keys.InterfaceHidden);
        Assert.Equal(KeyAction.Screenshot, Tap(keys, "f2"));
        Assert.Equal(1, shots);
        Assert.Equal(KeyAction.Overlay, Tap(keys, "F3"));
        Assert.True(overlay.Shown);
        Assert.Equal(KeyAction.Panel, Tap(keys, "F4"));
        Assert.True(panel.Open);
        Assert.Equal(KeyAction.Console, Tap(keys, "F5"));
        Assert.True(console.Open);
        Tap(keys, "F5");
        Assert.False(console.Open);
        Assert.Null(Tap(keys, "F6"));
    }

    [Fact]
    public void An_Action_Is_Taken_On_Release_And_Never_By_A_Chord()
    {
        var (keys, _, overlay, panel, _) = Make();

        keys.KeyDown("F3");
        Assert.False(overlay.Shown);
        keys.KeyDown("G");
        Assert.Null(keys.KeyUp("G"));
        Assert.Null(keys.KeyUp("F3"));
        Assert.False(overlay.Shown);

        // A key held before the bound key went down makes a chord as well.
        keys.KeyDown("LeftControl");
        keys.KeyDown("F4");
        Assert.Null(keys.KeyUp("F4"));
        keys.KeyUp("LeftControl");
        Assert.False(panel.Open);

        // The panel's key alone opens the panel, also right after the overlay's key.
        Tap(keys, "F3");
        Assert.Equal(KeyAction.Panel, Tap(keys, "F4"));
        Assert.True(panel.Open);

        // The overlay's key held with the panel's key is a chord, and neither is taken.
        keys.KeyDown("F3");
        keys.KeyDown("F4");
        Assert.Null(keys.KeyUp("F4"));
        Assert.Null(keys.KeyUp("F3"));
        Assert.True(overlay.Shown && panel.Open);
    }

    [Fact]
    public void Shift_With_The_Overlays_Key_Turns_The_Detail_Over()
    {
        var (keys, _, overlay, _, _) = Make();
        keys.KeyDown("LeftShift");
        Assert.Equal(KeyAction.Overlay, Tap(keys, "F3"));
        keys.KeyUp("LeftShift");
        Assert.True(overlay.Shown && overlay.Detailed);

        // The key alone hides the overlay and its detail with it.
        Tap(keys, "F3");
        Assert.False(overlay.Shown || overlay.Detailed);

        Tap(keys, "F3");
        keys.KeyDown("RightShift");
        Tap(keys, "F3");
        Assert.True(overlay.Shown && overlay.Detailed);
        Tap(keys, "F3");
        Assert.True(overlay.Shown);
        Assert.False(overlay.Detailed);
    }

    [Fact]
    public void A_Key_Is_Rebound_By_Its_Setting_And_Kept_By_The_File()
    {
        var (keys, registry, _, _, console) = Make();
        Assert.True(registry.Run("keys.console `").Done);
        Assert.Null(Tap(keys, "F5"));
        Assert.Equal(KeyAction.Console, Tap(keys, "`"));
        Assert.True(console.Open);

        // A key that types is the field's while a field has the keyboard, and a longer name is
        // taken.
        Assert.Null(Tap(keys, "`", typing: true));
        Assert.True(console.Open);
        Assert.Equal(KeyAction.Panel, Tap(keys, "F4", typing: true));

        // An empty name binds no key.
        registry.Run("keys.panel \"\"");
        Assert.Null(Tap(keys, "F4"));
        Assert.Equal("keys.console `\nkeys.panel \"\"\n", new SettingsFile("unused").Text(registry));
        Assert.Equal("F4", keys.KeyOf(KeyAction.Panel).Default);
    }

    [Fact]
    public void An_Action_Is_Taken_With_No_Key_And_Without_Its_Part()
    {
        var registry = new Registry();
        var keys = new DiagnosticKeys(registry);
        keys.Take(KeyAction.Overlay);
        keys.Take(KeyAction.Panel);
        keys.Take(KeyAction.Console);
        keys.Take(KeyAction.HideInterface);
        Assert.True(keys.InterfaceHidden);
        Assert.Throws<ArgumentException>(() => new DiagnosticKeys(registry));
    }
}
