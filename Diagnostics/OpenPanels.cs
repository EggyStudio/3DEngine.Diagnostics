namespace Diagnostics;

/// <summary>
/// The rule that one of a program's panels is open at a time, the debug overlay, the settings
/// panel, the console and any a host adds, and which one it is.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DiagnosticKeys"/> makes one and joins the overlay, the panel and the console it is
/// given, named <see cref="OverlayName"/>, <see cref="PanelName"/> and <see cref="ConsoleName"/>.
/// Opening one of them, by its key, by <see cref="DiagnosticKeys.Take"/> or by setting its
/// <c>Open</c> or <c>Shown</c>, closes the others, and the same key again closes its own, so a host
/// has no code of its own for it and reads <see cref="Open"/> to know which is open, or none, and
/// <see cref="HoldsInput"/> to know whether the open one takes the program's input.
/// The key that hides the interface and the key that takes a picture change nothing of it.
/// </para>
/// <para>
/// A host's own panel joins with <see cref="Join"/>, given how to read and set whether it is open.
/// The host calls <see cref="Show"/> or <see cref="Toggle"/> where it opens the panel, or
/// <see cref="Opened"/> after it did so another way, and the panel is closed when another opens.
/// Every member is called on the host's thread.
/// </para>
/// </remarks>
public sealed class OpenPanels
{
    /// <summary>The name the debug overlay joins by.</summary>
    public const string OverlayName = "overlay";

    /// <summary>The name the settings panel joins by.</summary>
    public const string PanelName = "panel";

    /// <summary>The name the console joins by.</summary>
    public const string ConsoleName = "console";

    private readonly List<Panel> _panels = [];
    private readonly record struct Panel(string Name, Func<bool> IsOpen, Action<bool> Set, bool TakesInput);

    private string? _last;
    private bool _closing;

    /// <summary>The names of the panels joined, in the order they joined.</summary>
    public IReadOnlyList<string> Names => [.. _panels.Select(panel => panel.Name)];

    /// <summary>
    /// The name of the open panel, or null where none is open.
    /// </summary>
    /// <remarks>
    /// Where a host opened its own panel without telling this, so that two are open, it is the one
    /// opened last of those told, else the first joined of the open ones.
    /// </remarks>
    public string? Open
    {
        get
        {
            if (_last is not null && _panels.Any(panel => panel.Name == _last && panel.IsOpen())) return _last;
            foreach (var panel in _panels)
                if (panel.IsOpen()) return panel.Name;
            return null;
        }
    }

    /// <summary>
    /// Whether a panel that takes input is open, which a host reads each frame and holds keyboard,
    /// pointer and pad from its program while it is true.
    /// </summary>
    /// <remarks>
    /// It is true while the console or the settings panel is open and while a host's panel that
    /// joined as taking input is, and false with none open and with the overlay open, which only
    /// shows. The keys that open and close the panels, hide the interface and take a picture are
    /// <see cref="DiagnosticKeys"/>' own and still act, the host passing them on whatever this
    /// says.
    /// </remarks>
    public bool HoldsInput => _panels.Any(panel => panel.TakesInput && panel.IsOpen());

    /// <summary>Joins a panel, in the place of one of the same name where there is one.</summary>
    /// <param name="name">The panel's name.</param>
    /// <param name="isOpen">Says whether the panel is open.</param>
    /// <param name="set">Opens the panel for true and closes it for false.</param>
    /// <param name="takesInput">
    /// Whether the panel takes input while it is open, as a menu or a field does, so that
    /// <see cref="HoldsInput"/> is true while it is. False, the default, is a panel that only
    /// shows.
    /// </param>
    public void Join(string name, Func<bool> isOpen, Action<bool> set, bool takesInput = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(isOpen);
        ArgumentNullException.ThrowIfNull(set);
        var at = _panels.FindIndex(panel => panel.Name == name);
        if (at < 0) _panels.Add(new Panel(name, isOpen, set, takesInput));
        else _panels[at] = new Panel(name, isOpen, set, takesInput);
    }

    /// <summary>Opens a panel and closes the others.</summary>
    /// <param name="name">The panel's name, one that joined.</param>
    public void Show(string name)
    {
        var panel = Find(name);
        if (!panel.IsOpen()) panel.Set(true);
        Opened(name);
    }

    /// <summary>Closes an open panel and opens a closed one, which closes the others.</summary>
    /// <param name="name">The panel's name, one that joined.</param>
    /// <returns>Whether the panel is open after.</returns>
    public bool Toggle(string name)
    {
        var panel = Find(name);
        if (panel.IsOpen())
        {
            panel.Set(false);
            return false;
        }

        Show(name);
        return true;
    }

    /// <summary>Closes every panel, which leaves none open.</summary>
    public void CloseAll()
    {
        foreach (var panel in _panels)
            if (panel.IsOpen()) panel.Set(false);
    }

    /// <summary>
    /// Says that a panel was opened another way than <see cref="Show"/>, and closes the others.
    /// </summary>
    /// <param name="name">The panel's name, one that joined.</param>
    public void Opened(string name)
    {
        var opened = Find(name);
        if (_closing) return;
        _closing = true;
        try
        {
            foreach (var panel in _panels)
                if (panel.Name != name && panel.IsOpen()) panel.Set(false);
        }
        finally
        {
            _closing = false;
        }

        _last = opened.Name;
    }

    private Panel Find(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var at = _panels.FindIndex(panel => panel.Name == name);
        return at < 0 ? throw new ArgumentException($"No panel is joined by the name {name}.", nameof(name)) : _panels[at];
    }
}
