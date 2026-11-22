namespace Diagnostics;

/// <summary>What one of the five keys does, the same on every host.</summary>
public enum KeyAction
{
    /// <summary>Hides and shows the program's interface, F1 by default.</summary>
    HideInterface,

    /// <summary>Asks the host for a screenshot, F2 by default.</summary>
    Screenshot,

    /// <summary>Shows and hides the debug overlay, with Shift its detail, F3 by default.</summary>
    Overlay,

    /// <summary>Opens and closes the settings panel, F5 by default.</summary>
    Panel,

    /// <summary>Opens and closes the console, F4 by default.</summary>
    Console,
}

/// <summary>
/// The five keys as actions a player rebinds, each a text setting holding a key's name, and what
/// they turn over.
/// </summary>
/// <remarks>
/// <para>
/// A key's name is the host's own word for it, compared whatever its case, and an empty name
/// binds no key. The settings are <c>keys.hide_interface</c>, <c>keys.screenshot</c>,
/// <c>keys.overlay</c>, <c>keys.panel</c> and <c>keys.console</c>, so the console, the panel and
/// the settings file rebind them.
/// </para>
/// <para>
/// A host gives every key that goes down and comes up. An action is taken as its key comes up
/// where no other key went down or was held meanwhile, so a chord that holds the key stays the
/// program's and turns nothing over. A key whose name holds <c>Shift</c> is no other key, and
/// Shift held as the overlay's key comes up turns the overlay's detail over and shows the overlay.
/// While a field is typing, a key whose name is one character belongs to the field and is not
/// taken.
/// </para>
/// <para>Every member is called on the host's thread.</para>
/// </remarks>
public sealed class DiagnosticKeys : IDisposable
{
    private static readonly (KeyAction Action, string Name, string Default, string Summary)[] Table =
    [
        (KeyAction.HideInterface, "keys.hide_interface", "F1", "The key that hides and shows the interface."),
        (KeyAction.Screenshot, "keys.screenshot", "F2", "The key that takes a screenshot."),
        (KeyAction.Overlay, "keys.overlay", "F3", "The key that shows and hides the debug overlay."),
        (KeyAction.Panel, "keys.panel", "F5", "The key that opens and closes the settings panel."),
        (KeyAction.Console, "keys.console", "F4", "The key that opens and closes the console."),
    ];

    private readonly Registry _registry;
    private readonly Setting<string>[] _keys;
    private readonly OverlayModel? _overlay;
    private readonly PanelModel? _panel;
    private readonly ConsoleModel? _console;
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    private string? _candidate;
    private bool _chord;
    private bool _disposed;
    private int _shift;

    /// <summary>Makes the keys and adds their five settings to a registry.</summary>
    /// <param name="registry">The registry that takes the settings.</param>
    public DiagnosticKeys(Registry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _keys = [.. Table.Select(row => new Setting<string>(row.Name, row.Summary, row.Default))];
        foreach (var key in _keys) registry.Add(key);
    }

    /// <summary>
    /// Takes the five settings out of the registry and ends the keys, so a process that makes keys
    /// for one app after another adds a new set for each.
    /// </summary>
    /// <remarks>
    /// A setting is taken out only where the registry still holds the very setting these keys
    /// added, so a host that replaced one with its own keeps its own. Once the keys are disposed
    /// <see cref="KeyDown"/>, <see cref="KeyUp"/> and <see cref="Take"/> do nothing, and a second
    /// call of this one does nothing. It is called on the host's thread.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var key in _keys)
            if (ReferenceEquals(_registry.FindSetting(key.Name), key))
                _registry.Remove(key.Name);
    }

    /// <summary>
    /// The panels of the program and which of them is open, the overlay, the panel and the console
    /// given to these keys and any a host joins, of which one is open at a time.
    /// </summary>
    public OpenPanels Panels { get; } = new();

    /// <summary>The overlay the overlay's key turns over, or null for none.</summary>
    public OverlayModel? Overlay
    {
        get => _overlay;
        init
        {
            _overlay = value;
            if (value is null) return;
            Panels.Join(OpenPanels.OverlayName, () => value.Shown, open =>
            {
                value.Shown = open;
                // The overlay alone hidden hides its detail with it.
                if (!open) value.Detailed = false;
            });
            value.WhenOpened = () => Panels.Opened(OpenPanels.OverlayName);
        }
    }

    /// <summary>The panel the panel's key turns over, or null for none.</summary>
    public PanelModel? Panel
    {
        get => _panel;
        init
        {
            _panel = value;
            if (value is null) return;
            Panels.Join(OpenPanels.PanelName, () => value.Open, open => value.Open = open, takesInput: true);
            value.WhenOpened = () => Panels.Opened(OpenPanels.PanelName);
        }
    }

    /// <summary>The console the console's key turns over, or null for none.</summary>
    public ConsoleModel? Console
    {
        get => _console;
        init
        {
            _console = value;
            if (value is null) return;
            Panels.Join(OpenPanels.ConsoleName, () => value.Open, open => value.Open = open, takesInput: true);
            value.WhenOpened = () => Panels.Opened(OpenPanels.ConsoleName);
        }
    }

    /// <summary>
    /// Whether the program's interface is hidden, which a host reads before it draws it.
    /// </summary>
    public bool InterfaceHidden { get; set; }

    /// <summary>Raised when a screenshot is asked for, which the host takes its own way.</summary>
    public event Action? ScreenshotAsked;

    /// <summary>The setting that holds an action's key.</summary>
    /// <param name="action">The action.</param>
    public Setting<string> KeyOf(KeyAction action) => _keys[(int)action];

    /// <summary>Takes a key that went down.</summary>
    /// <param name="key">The key's name.</param>
    /// <param name="typing">Whether a text field has the keyboard.</param>
    public void KeyDown(string key, bool typing = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_disposed) return;
        if (IsShift(key))
        {
            _shift++;
            return;
        }

        var alone = _held.Count == 0;
        if (!_held.Add(key)) return;
        if (alone && !(typing && key.Length == 1) && ActionOf(key) is not null)
            (_candidate, _chord) = (key, false);
        else
            _chord = true;
    }

    /// <summary>Takes a key that came up, and gives the action it took, or null.</summary>
    /// <param name="key">The key's name.</param>
    public KeyAction? KeyUp(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_disposed) return null;
        if (IsShift(key))
        {
            _shift = Math.Max(0, _shift - 1);
            return null;
        }

        _held.Remove(key);
        if (!string.Equals(key, _candidate, StringComparison.OrdinalIgnoreCase)) return null;
        var taken = _chord ? null : ActionOf(key);
        _candidate = null;
        if (taken is { } action) Take(action, _shift > 0);
        return taken;
    }

    /// <summary>Takes an action with no key, as a pad's button or a command does.</summary>
    /// <param name="action">The action.</param>
    /// <param name="shift">Whether the overlay's detail is meant.</param>
    public void Take(KeyAction action, bool shift = false)
    {
        if (_disposed) return;
        switch (action)
        {
            case KeyAction.HideInterface:
                InterfaceHidden = !InterfaceHidden;
                break;
            case KeyAction.Screenshot:
                ScreenshotAsked?.Invoke();
                break;
            case KeyAction.Overlay when Overlay is { } overlay:
                if (shift)
                {
                    overlay.Detailed = !overlay.Detailed;
                    Panels.Show(OpenPanels.OverlayName);
                }
                else
                    Panels.Toggle(OpenPanels.OverlayName);
                overlay.Refresh();
                break;
            case KeyAction.Panel when Panel is { } panel:
                Panels.Toggle(OpenPanels.PanelName);
                break;
            case KeyAction.Console when Console is { } console:
                Panels.Toggle(OpenPanels.ConsoleName);
                break;
        }
    }

    private static bool IsShift(string key) => key.Contains("Shift", StringComparison.OrdinalIgnoreCase);

    private KeyAction? ActionOf(string key)
    {
        foreach (var row in Table)
            if (string.Equals(_keys[(int)row.Action].Value, key, StringComparison.OrdinalIgnoreCase)) return row.Action;
        return null;
    }
}
