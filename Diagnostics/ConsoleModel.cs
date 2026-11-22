namespace Diagnostics;

/// <summary>What a console's line is, which a console colors and filters by.</summary>
public enum ConsoleLevel
{
    /// <summary>A line the player entered, shown again above its answer.</summary>
    Input,

    /// <summary>An answer or a host's plain line.</summary>
    Info,

    /// <summary>A host's warning.</summary>
    Warning,

    /// <summary>A failed line's answer or a host's error.</summary>
    Error,
}

/// <summary>One line a console shows.</summary>
/// <remarks>
/// A line a host writes with <see cref="ConsoleModel.Write(ConsoleLevel, string)"/> that repeats
/// the line before it, in its text and its level, is kept as that one line with a count, so a
/// warning written every frame stays one line. Only lines a host writes at
/// <see cref="ConsoleLevel.Info"/>, <see cref="ConsoleLevel.Warning"/> and
/// <see cref="ConsoleLevel.Error"/> collapse. A line the player entered and the answer to it never
/// do, since a console shows what was typed each time it was typed.
/// </remarks>
/// <param name="Text">The line's text, with no line end.</param>
/// <param name="Level">What the line is.</param>
/// <param name="Count">How many times the line was written in a row, 1 for one writing.</param>
/// <param name="Index">
/// The number of lines written before the line's first writing, that is
/// <see cref="ConsoleModel.Written"/> as it stood then, which does not change as the line repeats
/// or as older lines are dropped, so a drawer keys what it measured of the line by it.
/// </param>
/// <param name="Stamp">
/// What the host's stamp function gave as the line was last written, a frame's number as a host
/// gives it, which a repeat moves to its latest writing, or 0 where the console has no such
/// function.
/// </param>
public readonly record struct ConsoleLine(string Text, ConsoleLevel Level, int Count = 1, long Index = 0, long Stamp = 0);

/// <summary>
/// A console's content with no drawing, a plain list of every line written, the line being
/// entered with the candidates its start leaves, their selection and the ghost text, and the
/// history, over a <see cref="Registry"/>.
/// </summary>
/// <remarks>
/// <para>
/// The line being entered is <see cref="Typed"/>. A player's edit goes in through
/// <see cref="Edit"/>, which builds the <see cref="Candidates"/> of the word being typed, and a
/// line set by the program or recalled from the history builds them without showing them. The
/// popup of candidates is shown once the player edited, <see cref="PopupShown"/>, and
/// <see cref="Ghost"/> is the rest of the selected candidate after what is typed. Up and down
/// move the selection while the popup shows and walk the history otherwise,
/// <see cref="Navigate"/>, and Tab fills the selected candidate, <see cref="Accept"/>.
/// </para>
/// <para>
/// A word is completed by the names of commands and settings that begin with it, then those with a
/// later dotted part that begins with it, so <c>vsync</c> finds <c>render.vsync</c>. After a
/// setting's name it is completed by the setting's choices, and where a command's argument is of
/// the kind <c>name</c> by names.
/// </para>
/// <para>
/// <see cref="Write(ConsoleLevel, string)"/> and <see cref="Lines"/> are safe from any thread, so a
/// host's log and a socket's late answer write from where they are. Everything else is called on
/// the host's thread, <see cref="Submit"/> and <see cref="Enter"/> among it, which run the command
/// there.
/// </para>
/// </remarks>
public sealed class ConsoleModel
{
    /// <summary>How many lines the console keeps by default, the oldest dropped first.</summary>
    public const int DefaultDepth = 2000;

    /// <summary>How many rows of candidates the popup shows at most.</summary>
    public const int PopupRows = 6;

    private const int HistoryDepth = 100;

    private readonly Registry _registry;
    private readonly object _gate = new();
    private readonly LinkedList<ConsoleLine> _lines = new();
    private readonly List<string> _history = [];
    private readonly int _depth;
    private readonly Func<long>? _stamp;
    private bool _lastWritten;
    private int _at;
    private string _draft = "";
    private string _typed = "";
    private bool _edited;
    private IReadOnlyList<string> _candidates = [];
    private int _selected = -1;
    private int _first;

    /// <summary>Makes a console over a registry.</summary>
    /// <param name="registry">The commands and the settings its lines run.</param>
    /// <param name="depth">How many lines it keeps.</param>
    /// <param name="stamp">
    /// What a line is stamped with as it is written, as the host's frame number, called on the
    /// writing thread before the console's lock is taken, or null for none, which stamps 0.
    /// </param>
    public ConsoleModel(Registry registry, int depth = DefaultDepth, Func<long>? stamp = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        (_depth, _stamp) = (depth, stamp);
    }

    /// <summary>
    /// Raised for each line kept, with the line as it stands and whether it repeated the line
    /// before it, so a host writes every line to its log file, the lines the player entered and
    /// their answers among them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is raised on the thread that wrote the line, after the lock is let go, so a subscriber
    /// may read the console and the writing of other threads goes on meanwhile. The line of a
    /// repeat has its <see cref="ConsoleLine.Count"/> as it stands after it. The lines of one
    /// writing are raised in order, and the lines of two threads writing at once may be raised in
    /// either order.
    /// </para>
    /// <para>
    /// A subscriber's exception is the host's. It reaches the code that wrote the line, which
    /// <see cref="Write(ConsoleLevel, string)"/> or <see cref="Submit"/> then does not finish,
    /// after the line is kept, and the lines of that writing not yet raised are not raised. A
    /// subscriber that writes to the console from inside the event raises the event again for its
    /// own line, so it must not write a line that brings itself back.
    /// </para>
    /// </remarks>
    public event Action<ConsoleLine, bool>? LineWritten;

    /// <summary>
    /// Raised when a line is run, with the line as it was entered and its answer, however it was
    /// entered, <see cref="Submit"/> or <see cref="Enter"/>, so a host sees the answer of a line
    /// typed into a drawn console, <see cref="Answer.Error"/> among it.
    /// </summary>
    /// <remarks>
    /// It is raised on the thread that ran the line, after the line's echo and its answer are
    /// written, and no lock is held, so a subscriber may read the console. A blank line is no line
    /// run. A subscriber's exception is the host's, as that of <see cref="LineWritten"/> is, and it
    /// reaches the caller of <see cref="Submit"/> or <see cref="Enter"/>, whose line is run and
    /// written by then.
    /// </remarks>
    public event Action<string, Answer>? LineRun;

    /// <summary>Whether the console is shown, which the console's key turns over.</summary>
    public bool Open { get; set; }

    /// <summary>
    /// How many lines were ever written, which a drawer compares to know that a line came.
    /// </summary>
    public long Written { get; private set; }

    /// <summary>The lines entered, oldest first, a line entered twice in a row held once.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// The line being entered. Setting it replaces the line as the program does, and the popup
    /// stays hidden until the player edits.
    /// </summary>
    public string Typed
    {
        get => _typed;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Replace(value, edited: false);
        }
    }

    /// <summary>
    /// The names or values the word being typed could be, the names that begin with it first and
    /// then those with a later dotted part that begins with it, each in order, or none.
    /// </summary>
    public IReadOnlyList<string> Candidates => _candidates;

    /// <summary>
    /// Whether the popup of candidates is shown, once the player edited and a candidate is left.
    /// </summary>
    public bool PopupShown => _edited && _candidates.Count > 0;

    /// <summary>The selected candidate's index, or -1 where there is none.</summary>
    public int SelectedIndex => _selected;

    /// <summary>
    /// The index of the first of the <see cref="PopupRows"/> rows the popup shows, which keeps the
    /// selected candidate in view.
    /// </summary>
    public int FirstVisible => _first;

    /// <summary>
    /// The rest of the selected candidate after the word being typed, where the popup shows and the
    /// candidate begins with the word, whatever its case, else empty.
    /// </summary>
    public string Ghost
    {
        get
        {
            if (!PopupShown) return "";
            var word = CurrentWord(_typed);
            return _candidates[_selected].StartsWith(word, StringComparison.OrdinalIgnoreCase)
                ? _candidates[_selected][word.Length..]
                : "";
        }
    }

    /// <summary>
    /// What a console shows under the line being entered, a command's usage or a setting in a
    /// sentence, of the name typed or of the one name the typed start leaves, or empty.
    /// </summary>
    public string Hint
    {
        get
        {
            var name = CommandLine.Parse(_typed).Name;
            if (name.Length == 0) return "";
            if (_registry.FindCommand(name) is null && _registry.FindSetting(name) is null)
            {
                if (_registry.Starting(name) is not [var only]) return "";
                name = only;
            }

            return _registry.FindCommand(name)?.Usage ?? _registry.FindSetting(name)?.Describe() ?? "";
        }
    }

    /// <summary>Writes a text into the console, a line for each of its lines.</summary>
    /// <remarks>
    /// A line that repeats the one before it, in its text and its level, where that one was written
    /// here too, is counted on it as <see cref="ConsoleLine.Count"/> and added no more, whatever
    /// its level but <see cref="ConsoleLevel.Input"/>. <see cref="Written"/> counts each writing.
    /// </remarks>
    /// <param name="level">What the lines are, which a drawer colors them by.</param>
    /// <param name="text">The text.</param>
    public void Write(ConsoleLevel level, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Append(level, text, byHost: true);
    }

    // A line a host writes that repeats the last line kept, which a host wrote too, is counted on
    // it. The entered lines and the answers to them are kept as they come.
    private void Append(ConsoleLevel level, string text, bool byHost)
    {
        var told = LineWritten is null ? null : new List<(ConsoleLine, bool)>();
        // The host's function runs before the lock is taken, so it may read the console.
        var stamp = _stamp?.Invoke() ?? 0;
        lock (_gate)
            foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
            {
                var collapse = byHost && level != ConsoleLevel.Input;
                var repeated = collapse && _lastWritten && _lines.Last is { } last && last.Value.Text == line && last.Value.Level == level;
                if (repeated)
                    _lines.Last!.Value = _lines.Last.Value with { Count = _lines.Last.Value.Count + 1, Stamp = stamp };
                else
                {
                    _lines.AddLast(new ConsoleLine(line, level, Index: Written, Stamp: stamp));
                    if (_lines.Count > _depth) _lines.RemoveFirst();
                    _lastWritten = collapse;
                }

                told?.Add((_lines.Last!.Value, repeated));
                Written++;
            }

        // The subscribers hear of the lines once the lock is let go.
        if (told is null || LineWritten is not { } raise) return;
        foreach (var (line, repeated) in told) raise(line, repeated);
    }

    /// <summary>Every kept line, oldest first.</summary>
    public IReadOnlyList<ConsoleLine> Lines()
    {
        lock (_gate) return _lines.ToList();
    }

    /// <summary>Drops every kept line.</summary>
    public void Clear()
    {
        lock (_gate) _lines.Clear();
    }

    /// <summary>
    /// Runs an entered line, writes it and its answer into the console and keeps it in the history.
    /// </summary>
    /// <remarks>
    /// The line being entered is left as it is, so a line a socket handed over does not take the
    /// player's typing with it.
    /// </remarks>
    /// <param name="line">The line as entered.</param>
    public Answer Submit(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var entered = line.Trim();
        if (entered.Length == 0) return new Answer("");
        if (_history.Count == 0 || _history[^1] != entered) _history.Add(entered);
        if (_history.Count > HistoryDepth) _history.RemoveAt(0);
        (_at, _draft) = (_history.Count, "");

        Append(ConsoleLevel.Input, "> " + entered, byHost: false);
        var answer = _registry.Run(entered);
        if (answer.Text.Length > 0) Append(answer.Done ? ConsoleLevel.Info : ConsoleLevel.Error, answer.Text, byHost: false);
        LineRun?.Invoke(entered, answer);
        return answer;
    }

    /// <summary>Runs the line being entered as the Enter key does, and empties it.</summary>
    public Answer Enter()
    {
        // The field is emptied first, so a subscriber of LineRun that throws leaves no line to run
        // again.
        var line = _typed;
        Replace("", edited: false);
        return Submit(line);
    }

    /// <summary>The player's edit of the line being entered, which shows the popup.</summary>
    /// <param name="text">The line after the edit.</param>
    public void Edit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == _typed) return;
        Replace(text, edited: true);
    }

    /// <summary>
    /// Takes the up key, which is -1, or the down key, which is 1, and moves the popup's selection
    /// where the popup shows, past the last candidate to the first and back, else walks the
    /// history.
    /// </summary>
    /// <remarks>
    /// Up in the history recalls the line before the one shown, keeping the line that was being
    /// entered for the way back, and down past the last gives that line again. A recalled line
    /// shows no popup.
    /// </remarks>
    /// <param name="direction">Below zero for up, else down.</param>
    public void Navigate(int direction)
    {
        if (PopupShown)
        {
            var count = _candidates.Count;
            _selected = ((_selected + (direction < 0 ? -1 : 1)) % count + count) % count;
            if (_selected < _first) _first = _selected;
            else if (_selected >= _first + PopupRows) _first = _selected - PopupRows + 1;
            return;
        }

        if (direction < 0)
        {
            if (_history.Count == 0) return;
            if (_at >= _history.Count) (_at, _draft) = (_history.Count, _typed);
            if (_at > 0) _at--;
            Replace(_history[_at], edited: false);
        }
        else if (_at < _history.Count)
        {
            _at++;
            Replace(_at == _history.Count ? _draft : _history[_at], edited: false);
        }
    }

    /// <summary>
    /// Fills the selected candidate in place of the word being typed, followed by a space, as the
    /// Tab key does, the first candidate where none is selected, and says whether there was one.
    /// </summary>
    /// <remarks>
    /// The candidates are those of the line as it stands, so Tab completes a recalled line too,
    /// and the popup stays hidden until the next edit.
    /// </remarks>
    public bool Accept()
    {
        if (_candidates.Count == 0) return false;
        var word = CurrentWord(_typed);
        Replace(_typed[..^word.Length] + _candidates[Math.Max(_selected, 0)] + " ", edited: false);
        return true;
    }

    private void Replace(string text, bool edited)
    {
        (_typed, _edited) = (text, edited);
        _candidates = Find(text);
        (_selected, _first) = (_candidates.Count > 0 ? 0 : -1, 0);
    }

    private static string CurrentWord(string typed) => typed[(typed.LastIndexOfAny([' ', '\t']) + 1)..];

    private IReadOnlyList<string> Find(string typed)
    {
        var word = CurrentWord(typed);
        var words = CommandLine.Split(typed[..^word.Length]);
        if (words.Count == 0) return word.Length == 0 ? [] : Names(word);
        if (_registry.FindSetting(words[0]) is { } setting)
            return words.Count == 1
                ? setting.Choices.Where(choice => choice.StartsWith(word, StringComparison.OrdinalIgnoreCase)
                    && !choice.Equals(word, StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
        return _registry.FindCommand(words[0]) is { } command && words.Count <= command.Arguments.Count
            && command.Arguments[words.Count - 1].Kind == "name"
            ? Names(word)
            : [];
    }

    // The names that begin with the word, then those with a later dotted part that does. A name
    // the word spells out is left out, since it is complete.
    private List<string> Names(string word)
    {
        var all = _registry.Starting("").Where(name => !name.Equals(word, StringComparison.OrdinalIgnoreCase)).ToList();
        var first = all.Where(name => name.StartsWith(word, StringComparison.OrdinalIgnoreCase));
        var later = all.Where(name => !name.StartsWith(word, StringComparison.OrdinalIgnoreCase)
            && name.Split('.').Skip(1).Any(part => part.StartsWith(word, StringComparison.OrdinalIgnoreCase)));
        return [.. first, .. later];
    }
}
