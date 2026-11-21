using System.Globalization;

namespace Diagnostics;

/// <summary>What a setting's value is, which decides how a console reads it and a panel steps
/// it.</summary>
public enum SettingKind
{
    /// <summary>On or off, a <see cref="bool"/>.</summary>
    Toggle,

    /// <summary>A whole number, an <see cref="int"/>.</summary>
    Whole,

    /// <summary>A number with a fraction, a <see cref="float"/> or a
    /// <see cref="double"/>.</summary>
    Number,

    /// <summary>One of an enum's names.</summary>
    Choice,

    /// <summary>Any text, a <see cref="string"/>.</summary>
    Text,
}

/// <summary>
/// One setting as a console, a panel and the settings file read it, its name, its value as text
/// and what it takes, whatever its type.
/// </summary>
/// <remarks>
/// A setting is read from any thread where its host's getter is, and set on the host's thread.
/// </remarks>
public abstract class Setting
{
    private protected Setting(string name, string summary)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    /// <summary>The setting's dotted name, as <c>render.vsync</c>, in the commands'
    /// namespace.</summary>
    public string Name { get; }

    /// <summary>What the setting decides, a sentence a console's help and a panel show.</summary>
    public string Summary { get; }

    /// <summary>What the value is.</summary>
    public abstract SettingKind Kind { get; }

    /// <summary>
    /// Whether a set takes effect at the next start, the running value staying until then.
    /// </summary>
    public bool AtNextStart { get; init; }

    /// <summary>Whether the settings file keeps the value, which it does by default.</summary>
    public bool Saved { get; init; } = true;

    /// <summary>
    /// The environment variable that overrides the value while it is set, or null for none.
    /// </summary>
    public string? EnvironmentVariable { get; init; }

    /// <summary>
    /// Whether the environment gives the running value, <see cref="EnvironmentVariable"/> being set
    /// to a value the setting takes.
    /// </summary>
    public abstract bool Overridden { get; }

    /// <summary>
    /// Why the environment's value was refused, or null where it was taken or none is set.
    /// </summary>
    public abstract string? EnvironmentRefusal { get; }

    /// <summary>The running value as text.</summary>
    public abstract string Text { get; }

    /// <summary>The default as text.</summary>
    public abstract string DefaultText { get; }

    /// <summary>
    /// The value the settings file keeps as text, the running one, or the one last set where the
    /// environment overrides it or it waits for the next start.
    /// </summary>
    public abstract string StoredText { get; }

    /// <summary>
    /// The value that takes effect at the next start as text, or null where none waits.
    /// </summary>
    public string? PendingText => AtNextStart && !Overridden && StoredText != Text ? StoredText : null;

    /// <summary>The texts the value takes where they are few, an enum's names or off and
    /// on.</summary>
    public abstract IReadOnlyList<string> Choices { get; }

    /// <summary>What the value takes in words, its range or its choices, empty for any
    /// text.</summary>
    public abstract string Takes { get; }

    /// <summary>Raised after a set changed the running or the stored value.</summary>
    public event Action<Setting>? Changed;

    /// <summary>Sets the value from text, or refuses it with its reason.</summary>
    /// <remarks>
    /// A value the environment overrides is kept for the settings file and the running one stays,
    /// and a value for the next start waits as <see cref="PendingText"/>, each said in the answer.
    /// </remarks>
    /// <param name="text">The value as a console line or a file holds it.</param>
    public Answer TrySet(string text) => Set(text, starting: false);

    /// <summary>
    /// Sets the value from text as the program starts, so a value for the next start takes effect.
    /// </summary>
    /// <param name="text">The value as the settings file holds it.</param>
    public Answer TryStart(string text) => Set(text, starting: true);

    /// <summary>Sets the value to its default, as <see cref="TrySet"/> sets one.</summary>
    public Answer Reset() => TrySet(DefaultText);

    /// <summary>
    /// The setting in a sentence, its value, its default where it differs, and whether the
    /// environment gives it or a value waits for the next start.
    /// </summary>
    public string Describe()
    {
        var said = $"{Name} is {CommandLine.Quote(Text)}";
        if (Overridden) said += $" from the environment's {EnvironmentVariable}";
        if (PendingText is { } pending) said += $", {CommandLine.Quote(pending)} at the next start";
        if (Text != DefaultText) said += $", {CommandLine.Quote(DefaultText)} by default";
        return said + ".";
    }

    private protected abstract Answer Set(string text, bool starting);

    private protected void RaiseChanged() => Changed?.Invoke(this);
}

/// <summary>
/// A setting of one type, a <see cref="bool"/>, an <see cref="int"/>, a <see cref="float"/>, a
/// <see cref="double"/>, an enum or a <see cref="string"/>.
/// </summary>
/// <remarks>
/// <para>
/// The host gives a getter and a setter where it keeps the value itself, and the setting holds the
/// value where it gives none. A setter is called on the thread that sets, the host's.
/// </para>
/// <para>
/// The environment's variable is read once, as the first value is asked for or set. Where it holds
/// a value the setting takes, that value is given to the setter and stays the running one, and a
/// later set is kept for the settings file alone.
/// </para>
/// </remarks>
/// <typeparam name="T">The value's type.</typeparam>
public sealed class Setting<T> : Setting where T : notnull
{
    private readonly Func<T> _get;
    private readonly Action<T> _set;
    private readonly object _gate = new();
    private T _held;
    private T _stored;
    private bool _read;
    private bool _overridden;
    private string? _refusal;

    /// <summary>Makes a setting.</summary>
    /// <param name="name">The dotted name.</param>
    /// <param name="summary">What the setting decides, a sentence.</param>
    /// <param name="default">The value where nothing sets it.</param>
    /// <param name="get">The host's running value, or null where the setting holds it.</param>
    /// <param name="set">What applies a value in the host, or null where nothing does.</param>
    /// <exception cref="NotSupportedException">The type is none a setting holds.</exception>
    public Setting(string name, string summary, T @default, Func<T>? get = null, Action<T>? set = null)
        : base(name, summary)
    {
        Kind = @default switch
        {
            bool => SettingKind.Toggle,
            int => SettingKind.Whole,
            float or double => SettingKind.Number,
            Enum => SettingKind.Choice,
            string => SettingKind.Text,
            _ => throw new NotSupportedException($"A setting holds a bool, an int, a float, a double, an enum or a string, not {typeof(T).Name}."),
        };
        Default = @default;
        _held = _stored = @default;
        _get = get ?? (() => _held);
        if (set is null) _set = value => _held = value;
        else if (get is null) _set = value => { _held = value; set(value); };
        else _set = set;
        Choices = Kind switch
        {
            SettingKind.Toggle => ["off", "on"],
            SettingKind.Choice => Enum.GetNames(typeof(T)),
            _ => [],
        };
    }

    /// <summary>The value where nothing sets it.</summary>
    public T Default { get; }

    /// <summary>The lowest number the value takes, or null for no bound.</summary>
    public double? Low { get; init; }

    /// <summary>The highest number the value takes, or null for no bound.</summary>
    public double? High { get; init; }

    /// <summary>How far a panel moves a number by one press, zero for the kind's own
    /// step.</summary>
    public double Step { get; init; }

    /// <summary>The running value, the environment's where it overrides.</summary>
    public T Value
    {
        get
        {
            ReadEnvironment();
            return _get();
        }
    }

    /// <inheritdoc/>
    public override SettingKind Kind { get; }

    /// <inheritdoc/>
    public override bool Overridden
    {
        get
        {
            ReadEnvironment();
            return _overridden;
        }
    }

    /// <inheritdoc/>
    public override string? EnvironmentRefusal
    {
        get
        {
            ReadEnvironment();
            return _refusal;
        }
    }

    /// <inheritdoc/>
    public override string Text => Format(Value);

    /// <inheritdoc/>
    public override string DefaultText => Format(Default);

    /// <inheritdoc/>
    public override string StoredText => Format(Overridden || AtNextStart ? _stored : Value);

    /// <inheritdoc/>
    public override IReadOnlyList<string> Choices { get; }

    /// <inheritdoc/>
    public override string Takes => Kind switch
    {
        SettingKind.Toggle or SettingKind.Choice => "one of " + string.Join(", ", Choices),
        SettingKind.Text => "",
        _ when Low is { } low && High is { } high => $"{Number(low)} to {Number(high)}",
        _ when Low is { } low => $"{Number(low)} or more",
        _ when High is { } high => $"{Number(high)} or less",
        _ => Kind == SettingKind.Whole ? "a whole number" : "a number",
    };

    /// <summary>Sets the value, or refuses one out of its range with its reason.</summary>
    /// <param name="value">The value.</param>
    public Answer TrySet(T value) => Take(value, starting: false);

    private protected override Answer Set(string text, bool starting)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var value)
            ? Take(value, starting)
            : Answer.Fail(Answer.BadArgument, $"{Name} takes {(Takes.Length > 0 ? Takes : "text")}, not {CommandLine.Quote(text)}.");
    }

    private Answer Take(T value, bool starting)
    {
        if (Refusal(value) is { } refusal) return Answer.Fail(Answer.OutOfRange, refusal);
        ReadEnvironment();
        var before = (Text, StoredText);
        _stored = value;
        var said = $"{Name} is {CommandLine.Quote(Format(value))}.";
        if (_overridden)
            said = $"{Name} is kept as {CommandLine.Quote(Format(value))}, and the environment's {EnvironmentVariable} holds it at {CommandLine.Quote(Text)}.";
        else if (AtNextStart && !starting)
            said = Format(value) == Text ? said : $"{Name} is {CommandLine.Quote(Format(value))} at the next start.";
        else
            _set(value);
        if (before != (Text, StoredText)) RaiseChanged();
        return new Answer(said);
    }

    // The variable is read once and late, since the property that names it is set after the
    // constructor has run.
    private void ReadEnvironment()
    {
        lock (_gate)
        {
            if (_read) return;
            _read = true;
            if (EnvironmentVariable is not { } variable || Environment.GetEnvironmentVariable(variable) is not { } text) return;
            if (!TryParse(text, out var value))
                _refusal = $"The environment's {variable} holds {CommandLine.Quote(text)}, where {Name} takes {(Takes.Length > 0 ? Takes : "text")}.";
            else if (Refusal(value) is { } refusal)
                _refusal = $"The environment's {variable} is refused. {refusal}";
            else
            {
                _overridden = true;
                _set(value);
            }
        }
    }

    private string? Refusal(T value)
    {
        if (Kind is not (SettingKind.Whole or SettingKind.Number)) return null;
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return double.IsNaN(number) || number < Low || number > High
            ? $"{Name} takes {Takes}, not {Format(value)}."
            : null;
    }

    private static string Number(double number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Format(T value) => value switch
    {
        bool on => on ? "on" : "off",
        Enum choice => choice.ToString(),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private bool TryParse(string text, out T value)
    {
        const NumberStyles style = NumberStyles.Float;
        var culture = CultureInfo.InvariantCulture;
        object? read = Default switch
        {
            bool => text.ToLowerInvariant() switch
            {
                "on" or "true" or "1" => true,
                "off" or "false" or "0" => false,
                _ => null,
            },
            int => int.TryParse(text, NumberStyles.Integer, culture, out var whole) ? whole : null,
            float => float.TryParse(text, style, culture, out var single) ? single : null,
            double => double.TryParse(text, style, culture, out var number) ? number : null,
            // A number is no name of an enum's, though Enum.TryParse would take one.
            Enum => text.Length > 0 && !char.IsAsciiDigit(text[0]) && text[0] != '-' && text[0] != '+'
                && Enum.TryParse(typeof(T), text, ignoreCase: true, out var choice) && Enum.IsDefined(typeof(T), choice!) ? choice : null,
            _ => text,
        };
        value = read is null ? Default : (T)read;
        return read is not null;
    }
}
