namespace Diagnostics;

/// <summary>What steers the settings panel, the same six from arrows and from a pad.</summary>
public enum PanelInput
{
    /// <summary>To the row above, past a row of text, staying at the first.</summary>
    Up,

    /// <summary>To the row below, past a row of text, staying at the last.</summary>
    Down,

    /// <summary>A setting's value one step down or to the choice before.</summary>
    Left,

    /// <summary>A setting's value one step up or to the next choice.</summary>
    Right,

    /// <summary>
    /// Opens a page, runs an action, turns a toggle or a choice to its next, opens a text's field
    /// and sets what an open field holds.
    /// </summary>
    Accept,

    /// <summary>Back to the page before, and from the first page out of the panel.</summary>
    Back,
}

/// <summary>What a row of the settings panel is.</summary>
public enum PanelRowKind
{
    /// <summary>A setting, its value stepped with left and right.</summary>
    Setting,

    /// <summary>A command line run on accept.</summary>
    Action,

    /// <summary>A page opened on accept.</summary>
    Page,

    /// <summary>A line of text, which is never selected.</summary>
    Text,
}

/// <summary>A row of the settings panel as it is drawn.</summary>
/// <param name="Kind">What the row is.</param>
/// <param name="Label">The row's words on the left.</param>
/// <param name="Value">
/// A setting's value as the player set it, on the right, and empty for another row.
/// </param>
/// <param name="Selected">Whether the row is the one the inputs act on.</param>
/// <param name="Pending">Whether the value takes effect at the next start.</param>
/// <param name="Overridden">
/// Whether the environment holds the running value, the one shown being kept for the file.
/// </param>
/// <param name="ReadOnly">
/// Whether the setting only reads, always or for now, so the row shows its value and no input
/// changes it.
/// </param>
/// <param name="Editing">
/// The text in the row's field while the row is being edited, or null where it is not, so a host
/// draws a text field in place of the value.
/// </param>
public sealed record PanelRow(PanelRowKind Kind, string Label, string Value, bool Selected, bool Pending, bool Overridden, bool ReadOnly = false, string? Editing = null);

/// <summary>A page of the settings panel, a title and the rows a host gives it.</summary>
/// <param name="title">The page's title.</param>
public sealed class PanelPage(string title)
{
    internal List<(PanelRowKind Kind, string Label, object? Target)> Entries { get; } = [];

    /// <summary>The page's title.</summary>
    public string Title { get; } = title ?? throw new ArgumentNullException(nameof(title));

    /// <summary>
    /// Adds a setting's row, or nothing where the setting is not <see cref="Setting.InPanel"/>.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="label">The row's words, the setting's name where none is given.</param>
    public PanelPage Add(Setting setting, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(setting);
        // A setting the panel is not to show has no row, whichever page it would stand on.
        return setting.InPanel ? With(PanelRowKind.Setting, label ?? setting.Name, setting) : this;
    }

    /// <summary>Adds a row that runs a command line on accept.</summary>
    /// <param name="label">The row's words.</param>
    /// <param name="line">The line the registry runs.</param>
    public PanelPage AddAction(string label, string line) =>
        With(PanelRowKind.Action, label, line ?? throw new ArgumentNullException(nameof(line)));

    /// <summary>Adds a row that opens another page on accept.</summary>
    /// <param name="page">The page.</param>
    public PanelPage AddPage(PanelPage page) =>
        With(PanelRowKind.Page, (page ?? throw new ArgumentNullException(nameof(page))).Title, page);

    /// <summary>Adds a line of text.</summary>
    /// <param name="text">The text.</param>
    public PanelPage AddText(string text) => With(PanelRowKind.Text, text, null);

    private PanelPage With(PanelRowKind kind, string label, object? target)
    {
        Entries.Add((kind, label ?? throw new ArgumentNullException(nameof(label)), target));
        return this;
    }
}

/// <summary>
/// The settings panel's content with no drawing, pages of rows steered by six inputs, so arrows
/// and a pad serve alike.
/// </summary>
/// <remarks>
/// <para>
/// The first page holds a page for each first word of the registry's settings' names, so
/// <c>render.vsync</c> and <c>render.scale</c> stand on the page <c>render</c> as <c>vsync</c> and
/// <c>scale</c>, a setting of one word standing on the page <c>general</c>, and under them the rows
/// of <see cref="Root"/>, which a host fills with pages and actions of its own. The pages of
/// settings are read from the registry as they are shown, so a setting added later appears.
/// </para>
/// <para>
/// Repeating an input while a key or a stick is held is the host's. Every member is called on the
/// host's thread.
/// </para>
/// </remarks>
public sealed class PanelModel
{
    private readonly Registry _registry;
    private readonly List<(PanelPage Page, int Selected, int Top)> _path = [];
    private Setting? _editing;
    private bool _open;

    /// <summary>Makes a panel over a registry's settings.</summary>
    /// <param name="registry">The registry.</param>
    /// <param name="title">The first page's title.</param>
    public PanelModel(Registry registry, string title = "Settings")
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Root = new PanelPage(title);
    }

    /// <summary>
    /// Whether the panel is shown, which the panel's key turns over.
    /// Opening it closes the other panels where
    /// <see cref="DiagnosticKeys"/> holds them, see <see cref="OpenPanels"/>.
    /// </summary>
    public bool Open
    {
        get => _open;
        set
        {
            var opened = value && !_open;
            _open = value;
            if (!value) Cancel();
            if (opened) WhenOpened?.Invoke();
        }
    }

    internal Action? WhenOpened { get; set; }

    /// <summary>
    /// Raised for each answer the panel gets, a setting's step or toggle and an action's line
    /// alike, with the setting's name or the action's line and the answer, so a host logs what was
    /// moved and the <see cref="Answer.Error"/> of an action that threw.
    /// </summary>
    /// <remarks>
    /// It is raised on the host's thread, in <see cref="Press"/>, after <see cref="Answer"/> is
    /// set. A subscriber's exception is the host's and reaches the caller of <see cref="Press"/>.
    /// </remarks>
    public event Action<string, Answer>? Answered;

    /// <summary>
    /// The text in the field of the row being edited, or null where no row is, which a host reads
    /// to draw a text field for the selected row.
    /// </summary>
    /// <remarks>
    /// A text setting that offers no list of values is edited in a field, as the console takes it
    /// typed. Accept on its row opens the field with the value in it, <see cref="Edit"/> gives the
    /// text as it is typed, accept sets it and back puts it away. While a field is open the other
    /// inputs do nothing, since the field has the keys.
    /// </remarks>
    public string? Editing { get; private set; }

    /// <summary>Gives the text of the field as it is typed, where a row is being edited.</summary>
    /// <param name="text">The field's whole text.</param>
    public void Edit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_editing is not null) Editing = text;
    }

    /// <summary>The host's own rows of the first page, under the pages of settings.</summary>
    public PanelPage Root { get; }

    /// <summary>The shown page's title.</summary>
    public string Title => _path.Count == 0 ? Root.Title : _path[^1].Page.Title;

    /// <summary>
    /// The index of the selected row among <see cref="Rows"/>, or -1 where none can be.
    /// </summary>
    public int Selected
    {
        get
        {
            var entries = Entries();
            return Settle(entries, _path.Count == 0 ? _rootSelected : _path[^1].Selected);
        }
    }

    /// <summary>
    /// What the last accept or step answered, a refusal's reason among it, or null before any.
    /// </summary>
    public Answer? Answer { get; private set; }

    /// <summary>The shown page's rows.</summary>
    public IReadOnlyList<PanelRow> Rows
    {
        get
        {
            var entries = Entries();
            var selected = Selected;
            return entries.Select((entry, i) => entry.Target is Setting setting
                ? new PanelRow(entry.Kind, entry.Label, setting.StoredText, i == selected, setting.PendingText is not null, setting.Overridden,
                    setting.ReadOnly || setting.Locked is not null, ReferenceEquals(setting, _editing) ? Editing : null)
                : new PanelRow(entry.Kind, entry.Label, "", i == selected, false, false)).ToList();
        }
    }

    private int _rootSelected;
    private int _rootTop;
    private int _visible;

    /// <summary>
    /// How many rows the host shows at once, or 0 where it shows them all, which is the default.
    /// </summary>
    /// <remarks>
    /// A host that cannot show a long page whole gives the count it can, and draws the rows from
    /// <see cref="Top"/> that many, so the selected row stays among them.
    /// </remarks>
    public int Visible
    {
        get => _visible;
        set => _visible = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// The index among <see cref="Rows"/> of the first row shown, 0 where <see cref="Visible"/> is
    /// 0 or the page fits.
    /// </summary>
    /// <remarks>
    /// The window of <see cref="Visible"/> rows from here follows the selection by the least it
    /// can, so a step down past its last row moves it down one and a step up past its first row
    /// moves it up one, by keys and by a pad alike, and a page keeps its place while the player is
    /// on another and when it is shown again.
    /// </remarks>
    public int Top
    {
        get
        {
            var top = _path.Count == 0 ? _rootTop : _path[^1].Top;
            if (_visible == 0) return 0;
            var selected = Selected;
            top = Math.Clamp(top, 0, Math.Max(0, Entries().Count - _visible));
            if (selected >= 0 && selected < top) top = selected;
            else if (selected >= top + _visible) top = selected - _visible + 1;
            if (_path.Count == 0) _rootTop = top;
            else _path[^1] = (_path[^1].Page, _path[^1].Selected, top);
            return top;
        }
    }

    /// <summary>Takes an input.</summary>
    /// <param name="input">The input.</param>
    public void Press(PanelInput input)
    {
        if (_editing is not null)
        {
            PressEditing(input);
            return;
        }

        var entries = Entries();
        var at = Selected;
        switch (input)
        {
            case PanelInput.Up or PanelInput.Down:
                var step = input == PanelInput.Up ? -1 : 1;
                for (var i = at + step; i >= 0 && i < entries.Count; i += step)
                    if (entries[i].Kind != PanelRowKind.Text)
                    {
                        Select(i);
                        break;
                    }

                break;
            case PanelInput.Back:
                if (_path.Count == 0) Open = false;
                else _path.RemoveAt(_path.Count - 1);
                break;
            case PanelInput.Left or PanelInput.Right when at >= 0 && entries[at].Target is Setting setting:
                Take(setting.Name, setting.Move(input == PanelInput.Left ? -1 : 1));
                break;
            case PanelInput.Accept when at >= 0:
                Select(at);
                switch (entries[at].Target)
                {
                    case Setting choice when choice.Kind is SettingKind.Toggle or SettingKind.Choice
                        || (choice.Kind == SettingKind.Text && choice.Choices.Count > 0):
                        Take(choice.Name, choice.Move(1));
                        break;
                    case Setting { Kind: SettingKind.Text, ReadOnly: false, Locked: null } field:
                        (_editing, Editing) = (field, field.StoredText);
                        break;
                    case string line:
                        Take(line, _registry.Run(line));
                        break;
                    case PanelPage page:
                        _path.Add((page, 0, 0));
                        break;
                }

                break;
        }
    }

    // A field open takes accept as the set and back as the way out, and no other input.
    private void PressEditing(PanelInput input)
    {
        if (input == PanelInput.Back)
            Cancel();
        else if (input == PanelInput.Accept && _editing is { } setting)
        {
            // A refusal leaves the field open with the text typed, and its reason in the answer.
            var answer = setting.TrySet(Editing ?? "");
            Take(setting.Name, answer);
            if (answer.Done) Cancel();
        }
    }

    private void Cancel() => (_editing, Editing) = (null, null);

    private void Take(string name, Answer answer)
    {
        Answer = answer;
        Answered?.Invoke(name, answer);
    }

    private void Select(int index)
    {
        if (_path.Count == 0) _rootSelected = index;
        else _path[^1] = (_path[^1].Page, index, _path[^1].Top);
    }

    // The selected row is held to the page as it stands, since its settings can have changed, and
    // moved off a row of text to the nearest row after or before it.
    private static int Settle(List<(PanelRowKind Kind, string Label, object? Target)> entries, int wanted)
    {
        wanted = Math.Clamp(wanted, 0, Math.Max(0, entries.Count - 1));
        for (var i = wanted; i < entries.Count; i++)
            if (entries[i].Kind != PanelRowKind.Text) return i;
        for (var i = wanted - 1; i >= 0; i--)
            if (entries[i].Kind != PanelRowKind.Text) return i;
        return -1;
    }

    private List<(PanelRowKind Kind, string Label, object? Target)> Entries()
    {
        if (_path.Count > 0) return _path[^1].Page.Entries;
        var entries = new List<(PanelRowKind, string, object?)>();
        foreach (var group in _registry.Settings.Where(setting => setting.InPanel).GroupBy(Group).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            // The page is made again each time, with the settings the registry holds then.
            var page = new PanelPage(group.Key);
            // A setting with an order stands before one with none, by that number and then by name.
            var listed = group.OrderBy(setting => setting.Order is null).ThenBy(setting => setting.Order)
                .ThenBy(setting => setting.Name, StringComparer.Ordinal);
            foreach (var setting in listed) page.Add(setting, setting.Name.Contains('.') ? setting.Name[(group.Key.Length + 1)..] : setting.Name);
            entries.Add((PanelRowKind.Page, group.Key, page));
        }

        entries.AddRange(Root.Entries);
        return entries;
    }

    private static string Group(Setting setting) =>
        setting.Name.IndexOf('.') is var dot and > 0 ? setting.Name[..dot] : "general";
}
