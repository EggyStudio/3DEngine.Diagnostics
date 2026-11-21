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
/// <param name="Text">The line's text, with no line end.</param>
/// <param name="Level">What the line is.</param>
public readonly record struct ConsoleLine(string Text, ConsoleLevel Level);

/// <summary>What a typed start of a line completes to.</summary>
/// <param name="Text">
/// The line with the common start of every candidate in place of its last word, followed by a
/// space where one candidate is left.
/// </param>
/// <param name="Candidates">The names or the values the last word could be, in order.</param>
public sealed record Completion(string Text, IReadOnlyList<string> Candidates);

/// <summary>
/// A console's content with no drawing, the lines it shows, the line being entered's history,
/// completion and hint, over a <see cref="Registry"/>.
/// </summary>
/// <remarks>
/// <see cref="Write(ConsoleLevel, string)"/> and <see cref="Lines"/> are safe from any thread, so a
/// host's log and a socket's late answer write from where they are. Everything else is called on
/// the host's thread, <see cref="Submit"/> among it, which runs the command there.
/// </remarks>
public sealed class ConsoleModel
{
    /// <summary>How many lines the console keeps by default, the oldest dropped first.</summary>
    public const int DefaultDepth = 2000;

    private const int HistoryDepth = 100;

    private readonly Registry _registry;
    private readonly object _gate = new();
    private readonly Queue<ConsoleLine> _lines = new();
    private readonly List<string> _history = [];
    private readonly int _depth;
    private int _at;
    private string _draft = "";

    /// <summary>Makes a console over a registry.</summary>
    /// <param name="registry">The commands and the settings its lines run.</param>
    /// <param name="depth">How many lines it keeps.</param>
    public ConsoleModel(Registry registry, int depth = DefaultDepth)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        _depth = depth;
    }

    /// <summary>Whether the console is shown, which the console's key turns over.</summary>
    public bool Open { get; set; }

    /// <summary>Whether <see cref="Lines"/> gives the plain lines and the entered ones.</summary>
    public bool ShowInfo { get; set; } = true;

    /// <summary>Whether <see cref="Lines"/> gives the warnings.</summary>
    public bool ShowWarnings { get; set; } = true;

    /// <summary>Whether <see cref="Lines"/> gives the errors.</summary>
    public bool ShowErrors { get; set; } = true;

    /// <summary>A text <see cref="Lines"/> keeps the lines holding it of, whatever its
    /// case.</summary>
    public string Search { get; set; } = "";

    /// <summary>
    /// How many lines were ever written, which a drawer compares to know that a line came.
    /// </summary>
    public long Written { get; private set; }

    /// <summary>The lines entered, oldest first, a line entered twice in a row held once.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>Writes a text into the console, a line for each of its lines.</summary>
    /// <param name="level">What the lines are.</param>
    /// <param name="text">The text.</param>
    public void Write(ConsoleLevel level, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
            foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
            {
                _lines.Enqueue(new(line, level));
                if (_lines.Count > _depth) _lines.Dequeue();
                Written++;
            }
    }

    /// <summary>The kept lines the filters let through, oldest first.</summary>
    public IReadOnlyList<ConsoleLine> Lines()
    {
        lock (_gate)
            return _lines.Where(line => line.Level switch
            {
                ConsoleLevel.Warning => ShowWarnings,
                ConsoleLevel.Error => ShowErrors,
                _ => ShowInfo,
            } && line.Text.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Drops every kept line.</summary>
    public void Clear()
    {
        lock (_gate) _lines.Clear();
    }

    /// <summary>
    /// Runs an entered line, writes it and its answer into the console and keeps it in the history.
    /// </summary>
    /// <param name="line">The line as entered.</param>
    public Answer Submit(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var entered = line.Trim();
        if (entered.Length == 0) return new Answer("");
        if (_history.Count == 0 || _history[^1] != entered) _history.Add(entered);
        if (_history.Count > HistoryDepth) _history.RemoveAt(0);
        (_at, _draft) = (_history.Count, "");

        Write(ConsoleLevel.Input, "> " + entered);
        var answer = _registry.Run(entered);
        if (answer.Text.Length > 0) Write(answer.Done ? ConsoleLevel.Info : ConsoleLevel.Error, answer.Text);
        return answer;
    }

    /// <summary>The line before the one shown in the history, as the up key asks for it.</summary>
    /// <param name="current">The line being entered, kept to come back to.</param>
    public string Back(string current)
    {
        if (_history.Count == 0) return current;
        if (_at >= _history.Count) (_at, _draft) = (_history.Count, current);
        if (_at > 0) _at--;
        return _history[_at];
    }

    /// <summary>
    /// The line after the one shown in the history, and past the last the line that was being
    /// entered.
    /// </summary>
    /// <param name="current">The line shown.</param>
    public string Forward(string current)
    {
        if (_at >= _history.Count) return current;
        _at++;
        return _at == _history.Count ? _draft : _history[_at];
    }

    /// <summary>What the typed start of a line completes to, by prefix.</summary>
    /// <remarks>
    /// The first word completes to the names of commands and settings. A later word completes to a
    /// setting's choices after its name, and to names after a command whose argument at that place
    /// is of the kind <c>name</c>.
    /// </remarks>
    /// <param name="typed">The line up to the cursor.</param>
    public Completion Complete(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        var start = typed.TrimStart();
        var cut = start.LastIndexOfAny([' ', '\t']) + 1;
        var (before, word) = (start[..cut], start[cut..]);
        var candidates = Candidates(before, word);
        if (candidates.Count == 0) return new Completion(typed, candidates);

        var common = candidates[0];
        foreach (var candidate in candidates)
        {
            var same = 0;
            while (same < common.Length && same < candidate.Length && char.ToLowerInvariant(common[same]) == char.ToLowerInvariant(candidate[same])) same++;
            common = common[..same];
        }

        return new Completion(before + common + (candidates.Count == 1 ? " " : ""), candidates);
    }

    /// <summary>
    /// What a console shows under the line being entered, a command's usage or a setting in a
    /// sentence, of the name typed or of the one name the typed start leaves.
    /// </summary>
    /// <param name="typed">The line being entered.</param>
    public string Hint(string typed)
    {
        var name = CommandLine.Parse(typed ?? throw new ArgumentNullException(nameof(typed))).Name;
        if (name.Length == 0) return "";
        if (_registry.FindCommand(name) is null && _registry.FindSetting(name) is null)
        {
            if (_registry.Starting(name) is not [var only]) return "";
            name = only;
        }

        return _registry.FindCommand(name)?.Usage ?? _registry.FindSetting(name)?.Describe() ?? "";
    }

    private IReadOnlyList<string> Candidates(string before, string word)
    {
        var words = CommandLine.Split(before);
        if (words.Count == 0) return _registry.Starting(word);
        if (_registry.FindSetting(words[0]) is { } setting)
            return words.Count == 1
                ? setting.Choices.Where(choice => choice.StartsWith(word, StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
        return _registry.FindCommand(words[0]) is { } command && words.Count <= command.Arguments.Count && command.Arguments[words.Count - 1].Kind == "name"
            ? _registry.Starting(word)
            : [];
    }
}
