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
    public void Disposed_Keys_Take_Their_Five_Settings_Out_So_Another_Set_Of_Keys_Is_Made_For_The_Registry()
    {
        var (keys, registry, _, _, _) = Make();
        keys.Dispose();
        Assert.Equal("", registry.Run("list keys.").Text);
        Assert.Null(registry.FindSetting("keys.console"));

        // The registry of a process that makes an app after an app takes a second set, and a third.
        using var second = new DiagnosticKeys(registry);
        Assert.Equal(5, registry.Settings.Count(setting => setting.Name.StartsWith("keys.")));
        second.Dispose();
        second.Dispose();
        using var third = new DiagnosticKeys(registry);
        Assert.NotNull(registry.FindSetting("keys.overlay"));
    }

    [Fact]
    public void Dispose_Removes_Only_The_Settings_The_Keys_Added_And_Ends_Their_Key_Handling()
    {
        var (keys, registry, overlay, _, console) = Make();
        // A host that took a setting out and added its own of the name keeps its own.
        registry.Remove("keys.panel");
        var own = new Setting<string>("keys.panel", "The host's own.", "P");
        registry.Add(own);
        registry.Remove("keys.screenshot");
        keys.Dispose();

        Assert.Same(own, registry.FindSetting("keys.panel"));
        Assert.Equal(["keys.panel"], registry.Settings.Select(setting => setting.Name));

        // A disposed set of keys turns nothing over, whatever key comes up.
        Assert.Null(Tap(keys, "F5"));
        Assert.False(console.Open);
        keys.Take(KeyAction.Overlay);
        Assert.False(overlay.Shown);
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

        // The overlay's key held with the panel's key is a chord, and neither is taken, so the
        // panel opened by its own key stays open and the overlay it closed stays closed.
        keys.KeyDown("F3");
        keys.KeyDown("F4");
        Assert.Null(keys.KeyUp("F4"));
        Assert.Null(keys.KeyUp("F3"));
        Assert.True(panel.Open);
        Assert.False(overlay.Shown);
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

    [Fact]
    public void Opening_One_Of_The_Three_Closes_The_Other_Two_For_Each_Pair_And_The_Same_Key_Closes_Its_Own()
    {
        var (keys, _, overlay, panel, console) = Make();
        bool[] Open() => [overlay.Shown, panel.Open, console.Open];
        Assert.Null(keys.Panels.Open);

        // Each of the three, then each of the others after it, so every ordered pair is held.
        string[] names = [OpenPanels.OverlayName, OpenPanels.PanelName, OpenPanels.ConsoleName];
        string[] open = ["F3", "F4", "F5"];
        for (var first = 0; first < 3; first++)
        for (var second = 0; second < 3; second++)
        {
            if (first == second) continue;
            Tap(keys, open[first]);
            Assert.Equal(names[first], keys.Panels.Open);
            Tap(keys, open[second]);
            Assert.Equal(names[second], keys.Panels.Open);
            Assert.Equal([second == 0, second == 1, second == 2], Open());

            // The same key again closes its own, and none is open.
            Tap(keys, open[second]);
            Assert.Equal([false, false, false], Open());
            Assert.Null(keys.Panels.Open);
        }
    }

    [Fact]
    public void A_Panel_Set_Open_Directly_Closes_The_Others_And_Closing_It_Closes_Nothing_Else()
    {
        var (keys, _, overlay, panel, console) = Make();
        console.Open = true;
        panel.Open = true;
        Assert.False(console.Open);
        Assert.Equal(OpenPanels.PanelName, keys.Panels.Open);
        overlay.Shown = true;
        Assert.False(panel.Open);
        console.Open = true;
        Assert.False(overlay.Shown);

        // Setting what is open open again leaves it, and closing it leaves none.
        console.Open = true;
        Assert.True(console.Open);
        console.Open = false;
        Assert.Null(keys.Panels.Open);
    }

    [Fact]
    public void The_Overlays_Detail_Shows_The_Overlay_And_Closes_The_Others_And_Closing_The_Overlay_Ends_It()
    {
        var (keys, _, overlay, _, console) = Make();
        Tap(keys, "F5");
        keys.KeyDown("LeftShift");
        Tap(keys, "F3");
        keys.KeyUp("LeftShift");
        Assert.True(overlay.Shown && overlay.Detailed);
        Assert.False(console.Open);

        // The console opened over it closes it, and its detail with it.
        Tap(keys, "F5");
        Assert.False(overlay.Shown || overlay.Detailed);
    }

    [Fact]
    public void The_Key_That_Hides_The_Interface_And_The_Picture_Key_Change_Nothing_Of_What_Is_Open()
    {
        var (keys, _, _, panel, _) = Make();
        var shots = 0;
        keys.ScreenshotAsked += () => shots++;
        Tap(keys, "F4");
        Tap(keys, "F1");
        Tap(keys, "F2");
        Assert.True(panel.Open);
        Assert.Equal(OpenPanels.PanelName, keys.Panels.Open);
        Assert.True(keys.InterfaceHidden);
        Assert.Equal(1, shots);
        Tap(keys, "F1");
        Assert.True(panel.Open);
    }

    [Fact]
    public void A_Panel_A_Host_Joins_Takes_Part_In_The_Rule_As_The_Three_Do()
    {
        var (keys, _, overlay, panel, console) = Make();
        var inventory = false;
        keys.Panels.Join("inventory", () => inventory, open => inventory = open);
        Assert.Equal([OpenPanels.OverlayName, OpenPanels.PanelName, OpenPanels.ConsoleName, "inventory"], keys.Panels.Names);

        Tap(keys, "F5");
        keys.Panels.Show("inventory");
        Assert.True(inventory);
        Assert.False(console.Open);
        Assert.Equal("inventory", keys.Panels.Open);

        Tap(keys, "F4");
        Assert.True(panel.Open);
        Assert.False(inventory);

        // The host opened it another way and says so.
        inventory = true;
        keys.Panels.Opened("inventory");
        Assert.False(panel.Open);
        Assert.False(overlay.Shown);

        Assert.True(keys.Panels.Toggle("inventory") is false);
        Assert.Null(keys.Panels.Open);
        Assert.True(keys.Panels.Toggle("inventory"));
        keys.Panels.CloseAll();
        Assert.False(inventory);
        Assert.Null(keys.Panels.Open);
        Assert.Throws<ArgumentException>(() => keys.Panels.Show("none"));
    }

    [Fact]
    public void Keys_With_Some_Of_The_Three_Missing_Hold_The_Rule_Among_The_Ones_Given()
    {
        var registry = new Registry();
        var (panel, console) = (new PanelModel(registry), new ConsoleModel(registry));
        var keys = new DiagnosticKeys(registry) { Panel = panel, Console = console };
        Assert.Equal([OpenPanels.PanelName, OpenPanels.ConsoleName], keys.Panels.Names);
        keys.Take(KeyAction.Overlay);
        keys.Take(KeyAction.Panel);
        keys.Take(KeyAction.Console);
        Assert.True(console.Open);
        Assert.False(panel.Open);
    }
}
