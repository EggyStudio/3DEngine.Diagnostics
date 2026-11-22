using System.Globalization;
using System.Numerics;

namespace Diagnostics;

/// <summary>What a setting's value is, which decides how a console reads it and a panel steps
/// it.</summary>
public enum SettingKind
{
    /// <summary>On or off, a <see cref="bool"/>.</summary>
    Toggle,

    /// <summary>A whole number, an <see cref="int"/>, a <see cref="uint"/> or a
    /// <see cref="ulong"/>.</summary>
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
    /// Whether the setting only reads, shown with its value and answering every set, step and
    /// reset as <see cref="Answer.NotSettable"/>.
    /// </summary>
    /// <remarks>
    /// A setting that only reads is never kept in the settings file, takes no environment variable
    /// and has no next start, whatever else is set on it.
    /// </remarks>
    public bool ReadOnly { get; init; }

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
    /// The value the settings file keeps as text, the one a player last set through the console,
    /// the panel or the file, or the default where none was.
    /// </summary>
    /// <remarks>
    /// A value the program wrote through its own variable, the one its getter returns, is not a
    /// player's and never reaches the file, so a window resized by the program does not stay at its
    /// size for the next start.
    /// </remarks>
    public abstract string SavedText { get; }

    /// <summary>
    /// The value that takes effect at the next start as text, or null where none waits.
    /// </summary>
    public string? PendingText => AtNextStart && !ReadOnly && !Overridden && StoredText != Text ? StoredText : null;

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
    /// Moves the value one step as a panel's left and right do, a toggle over, a choice to its
    /// neighbor past the last to the first, a number by its step held to its range.
    /// </summary>
    /// <remarks>
    /// The step starts from the value last set, the one <see cref="StoredText"/> gives, so a value
    /// that waits for the next start or that the environment overrides still moves as it is
    /// stepped. A text has no step and refuses.
    /// </remarks>
    /// <param name="direction">Below zero for down or before, else up or next.</param>
    public abstract Answer Move(int direction);

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
/// A setting of one type, a <see cref="bool"/>, an <see cref="int"/>, a <see cref="uint"/>, a
/// <see cref="ulong"/>, a <see cref="float"/>, a <see cref="double"/>, an enum or a
/// <see cref="string"/>.
/// </summary>
/// <remarks>
/// <para>
/// The host gives a getter and a setter where it keeps the value itself, and the setting holds the
/// value where it gives none. A setter is called on the thread that sets, the host's. A setter
/// that can refuse a value is given as <c>apply</c> instead, and returns its reason, which the
/// answer carries as <see cref="Answer.Refused"/> with nothing stored or changed.
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
    private readonly Func<T, string?> _set;
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
    /// <param name="apply">
    /// What applies a value in the host and gives the reason where it cannot, or null where it did,
    /// in place of <paramref name="set"/>.
    /// </param>
    /// <exception cref="NotSupportedException">The type is none a setting holds.</exception>
    /// <exception cref="ArgumentException">Both a setter and a refusing one are given.</exception>
    public Setting(string name, string summary, T @default, Func<T>? get = null, Action<T>? set = null, Func<T, string?>? apply = null)
        : base(name, summary)
    {
        Kind = @default switch
        {
            bool => SettingKind.Toggle,
            int or uint or ulong => SettingKind.Whole,
            float or double => SettingKind.Number,
            Enum => SettingKind.Choice,
            string => SettingKind.Text,
            _ => throw new NotSupportedException($"A setting holds a bool, an int, a uint, a ulong, a float, a double, an enum or a string, not {typeof(T).Name}."),
        };
        Default = @default;
        _held = _stored = @default;
        if (set is not null && apply is not null)
            throw new ArgumentException("A setting takes a setter or a setter that refuses, not both.", nameof(apply));
        _get = get ?? (() => _held);
        if (apply is not null)
            _set = get is null ? value => Held(value, apply(value)) : apply;
        else if (set is not null)
            _set = get is null ? value => { _held = value; set(value); return null; } : value => { set(value); return null; };
        else
            _set = get is null ? value => Held(value, null) : _ => null;
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
    public override string StoredText => Format(ReadOnly || !(Overridden || AtNextStart) ? Value : _stored);

    /// <inheritdoc/>
    public override string SavedText => Format(_stored);

    /// <inheritdoc/>
    public override IReadOnlyList<string> Choices { get; }

    /// <inheritdoc/>
    public override string Takes => Kind switch
    {
        SettingKind.Toggle or SettingKind.Choice => "one of " + string.Join(", ", Choices),
        SettingKind.Text => "",
        _ when Unsigned => $"{Number(Math.Max(Low ?? 0, 0))} to {(High is { } high ? Number(high) : Limits().Max.ToString(CultureInfo.InvariantCulture))}",
        _ when Low is { } low && High is { } high => $"{Number(low)} to {Number(high)}",
        _ when Low is { } low => $"{Number(low)} or more",
        _ when High is { } high => $"{Number(high)} or less",
        _ => Kind == SettingKind.Whole ? "a whole number" : "a number",
    };

    /// <summary>Sets the value, or refuses one out of its range with its reason.</summary>
    /// <param name="value">The value.</param>
    public Answer TrySet(T value) => Take(value, starting: false);

    /// <inheritdoc/>
    public override Answer Move(int direction)
    {
        if (ReadOnly) return NotSettable();
        var up = direction >= 0;
        TryParse(StoredText, out var from);
        switch (Kind)
        {
            case SettingKind.Toggle:
                return TrySet(from.Equals(true) ? "off" : "on");
            case SettingKind.Choice:
                var at = Array.IndexOf(Enum.GetValues(typeof(T)), from);
                return TrySet(Choices[(at + (up ? 1 : Choices.Count - 1)) % Choices.Count]);
            case SettingKind.Text:
                return Answer.Fail(Answer.BadArgument, $"{Name} is text, which no step changes.");
            case SettingKind.Whole:
                // Whole numbers step in decimals, which hold all eight bytes exactly where a double
                // would round the last digits away.
                var grid = (decimal)(Step > 0 ? Step : 1);
                var next = Convert.ToDecimal(from, CultureInfo.InvariantCulture) + (up ? grid : -grid);
                next = Math.Round(next / grid) * grid;
                var (min, max) = Limits();
                var low = Low is { } lower ? (decimal)Math.Clamp(Math.Ceiling(lower), -7e28, 7e28) : min;
                var high = High is { } upper ? (decimal)Math.Clamp(Math.Floor(upper), -7e28, 7e28) : max;
                next = Math.Min(Math.Max(next, Math.Max(low, min)), Math.Min(high, max));
                return TrySet(next.ToString(CultureInfo.InvariantCulture));
            default:
                var step = Step > 0 ? Step : 0.1;
                var number = Convert.ToDouble(from, CultureInfo.InvariantCulture) + (up ? step : -step);
                // Steps of a fraction are held to the step's own grid, so ten steps of 0.1 give 1.
                number = Math.Round(Math.Round(number / step) * step, 9);
                number = Math.Clamp(number, Low ?? double.MinValue, High ?? double.MaxValue);
                return TrySet(number.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private protected override Answer Set(string text, bool starting)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (ReadOnly) return NotSettable();
        // A whole number of an unsigned type that is a number and no fit is out of its range, which
        // is a better word than one a player cannot read.
        if (Unsigned && BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var big)
            && (big.Sign < 0 || big > new BigInteger(Limits().Max)))
            return Answer.Fail(Answer.OutOfRange, $"{Name} takes {Takes}, not {big}.");
        return TryParse(text, out var value)
            ? Take(value, starting)
            : Answer.Fail(Answer.BadArgument, $"{Name} takes {(Takes.Length > 0 ? Takes : "text")}, not {CommandLine.Quote(text)}.");
    }

    private Answer Take(T value, bool starting)
    {
        if (ReadOnly) return NotSettable();
        if (Refusal(value) is { } refusal) return Answer.Fail(Answer.OutOfRange, refusal);
        ReadEnvironment();
        var before = (Text, StoredText);
        var said = $"{Name} is {CommandLine.Quote(Format(value))}.";
        if (_overridden)
            said = $"{Name} is kept as {CommandLine.Quote(Format(value))}, and the environment's {EnvironmentVariable} holds it at {CommandLine.Quote(Text)}.";
        else if (AtNextStart && !starting)
            said = Format(value) == Text ? said : $"{Name} is {CommandLine.Quote(Format(value))} at the next start.";
        else if (_set(value) is { } reason)
            // The host could not apply it, so nothing stands changed, the stored value included.
            return Answer.Fail(Answer.Refused, reason);
        _stored = value;
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
            if (ReadOnly || EnvironmentVariable is not { } variable || Environment.GetEnvironmentVariable(variable) is not { } text) return;
            if (!TryParse(text, out var value))
                _refusal = $"The environment's {variable} holds {CommandLine.Quote(text)}, where {Name} takes {(Takes.Length > 0 ? Takes : "text")}.";
            else if (Refusal(value) is { } refusal)
                _refusal = $"The environment's {variable} is refused. {refusal}";
            else if (_set(value) is { } reason)
                _refusal = $"The environment's {variable} is refused. {reason}";
            else
                _overridden = true;
        }
    }

    private string? Refusal(T value)
    {
        bool refused;
        switch (Kind)
        {
            case SettingKind.Whole:
                // Exact in a big integer, where a double would round an eight byte value.
                var whole = value switch { int i => i, uint u => u, ulong l => l, _ => BigInteger.Zero };
                var low = Unsigned ? Math.Max(Low ?? 0, 0) : Low;
                refused = (low is { } lowest && !double.IsNegativeInfinity(lowest) && whole < new BigInteger(Math.Ceiling(lowest)))
                    || (High is { } highest && !double.IsPositiveInfinity(highest) && whole > new BigInteger(Math.Floor(highest)));
                break;
            case SettingKind.Number:
                // Neither NaN nor an infinity is a value, whatever bound the setting has.
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                refused = !double.IsFinite(number) || number < Low || number > High;
                break;
            default:
                return null;
        }

        return refused ? $"{Name} takes {Takes}, not {Format(value)}." : null;
    }

    private bool Unsigned => Default is uint or ulong;

    // What the type holds, as decimals, which no double can for eight bytes.
    private (decimal Min, decimal Max) Limits() => Default switch
    {
        uint => (uint.MinValue, uint.MaxValue),
        ulong => (ulong.MinValue, ulong.MaxValue),
        _ => (int.MinValue, int.MaxValue),
    };

    private string? Held(T value, string? reason)
    {
        if (reason is null) _held = value;
        return reason;
    }

    private Answer NotSettable() => Answer.Fail(Answer.NotSettable, $"{Name} is read only.");

    // A whole number past what a double holds exactly is written in all its digits.
    private static string Number(double number) =>
        Math.Abs(number) >= 1e15 && number == Math.Floor(number) && double.IsFinite(number)
            ? new BigInteger(number).ToString(CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);

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
            uint => uint.TryParse(text, NumberStyles.Integer, culture, out var unsigned) ? unsigned : null,
            ulong => ulong.TryParse(text, NumberStyles.Integer, culture, out var wide) ? wide : null,
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
