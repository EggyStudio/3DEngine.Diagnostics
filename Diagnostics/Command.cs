using System.Text;

namespace Diagnostics;

/// <summary>What a command or a set of a setting answers, its text and a code where it
/// failed.</summary>
/// <param name="Text">What a console shows, one line or several.</param>
/// <param name="Code">
/// The failure's code in capitals, as <c>BAD_ARGUMENT</c>, which a script reads, or null where the
/// line was done.
/// </param>
/// <param name="Error">
/// The exception a command threw, which <see cref="Registry.Run"/> sets where it answers with
/// <see cref="Answer.Failed"/>, and null in every other answer. It is for the host's log, with its
/// stack, and is not shown to a player, whose console shows <paramref name="Text"/>. Two answers
/// compare it by reference, as an exception is compared, so two failures of the same text are equal
/// only where one exception is carried by both.
/// </param>
public sealed record Answer(string Text, string? Code = null, Exception? Error = null)
{
    /// <summary>The code of a line whose first word names no command and no setting.</summary>
    public const string NoSuchCommand = "NO_SUCH_COMMAND";

    /// <summary>The code of a word a command or a setting cannot read.</summary>
    public const string BadArgument = "BAD_ARGUMENT";

    /// <summary>The code of a number outside a setting's range.</summary>
    public const string OutOfRange = "OUT_OF_RANGE";

    /// <summary>The code of a command whose run threw.</summary>
    public const string Failed = "FAILED";

    /// <summary>Whether the line was done, which it was where there is no code.</summary>
    public bool Done => Code is null;

    /// <summary>An answer that failed with a code and its reason.</summary>
    /// <param name="code">The failure's code in capitals, the module's or the host's own.</param>
    /// <param name="text">The reason, a sentence a console shows.</param>
    public static Answer Fail(string code, string text) => new(text, code ?? throw new ArgumentNullException(nameof(code)));
}

/// <summary>One word a command takes, for a console's help and completion.</summary>
/// <param name="Name">The argument's name as a usage shows it.</param>
/// <param name="Kind">
/// What the word is, as <c>int</c> or <c>path</c>, the host's own word, with <c>name</c> for a
/// command's or a setting's name, which a console completes.
/// </param>
/// <param name="Optional">Whether the word may be left out.</param>
/// <param name="TakesLine">Whether the argument is the rest of the line as typed.</param>
public readonly record struct Argument(string Name, string Kind, bool Optional = false, bool TakesLine = false);

/// <summary>One console command, its name, what it does, the words it takes and what runs
/// it.</summary>
/// <param name="Name">The command's dotted name, as <c>entity.set</c>, the line's
/// first word.</param>
/// <param name="Summary">What the command does, a sentence a console's help
/// shows.</param>
/// <param name="Usage">The words the command takes, its name first.</param>
/// <param name="Run">Runs the command on a line, on the host's thread.</param>
public sealed record Command(string Name, string Summary, string Usage, Func<CommandLine, Answer> Run)
{
    /// <summary>The words the command takes, in their order, empty where none is
    /// declared.</summary>
    public IReadOnlyList<Argument> Arguments { get; init; } = [];
}

/// <summary>
/// Marks a static method as a console command, which a host's generator reads to add it to
/// <see cref="Registry.Process"/>.
/// </summary>
/// <remarks>
/// The module reads no attribute itself. A host's generator emits, for each marked method, a call
/// of <see cref="Registry.Add(Command)"/> on <see cref="Registry.Process"/> with a
/// <see cref="Command"/> whose run reads the method's parameters from
/// <see cref="CommandLine.Words"/> and answers with an <see cref="Answer"/>.
/// </remarks>
/// <param name="name">The command's dotted name.</param>
/// <param name="summary">What the command does, a sentence.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute(string name, string summary) : Attribute
{
    /// <summary>The command's dotted name.</summary>
    public string Name { get; } = name;

    /// <summary>What the command does, a sentence a console's help shows.</summary>
    public string Summary { get; } = summary;
}

/// <summary>A line as a console takes it, its first word the name and the rest its words.</summary>
/// <remarks>
/// Words part at spaces and tabs. A word in double quotes keeps its blanks, and inside the quotes a
/// backslash keeps the quote or the backslash after it, so <c>say "a \"b\" c"</c> has one word. A
/// command that takes its line whole reads <see cref="Rest"/>, which is as it was typed.
/// </remarks>
public sealed class CommandLine
{
    private IReadOnlyList<string>? _words;

    private CommandLine(string name, string rest)
    {
        Name = name;
        Rest = rest;
    }

    /// <summary>The line's first word, a command's or a setting's name, empty for a blank
    /// line.</summary>
    public string Name { get; }

    /// <summary>The line after its name as it was typed, with the blanks at its ends taken
    /// off.</summary>
    public string Rest { get; }

    /// <summary>The words after the name, split as <see cref="Split"/> splits them.</summary>
    public IReadOnlyList<string> Words => _words ??= Split(Rest);

    /// <summary>Reads a line into its name and the rest.</summary>
    /// <param name="line">The line as typed.</param>
    public static CommandLine Parse(string line)
    {
        var trimmed = (line ?? throw new ArgumentNullException(nameof(line))).Trim();
        var cut = trimmed.IndexOfAny([' ', '\t']);
        return cut < 0 ? new(trimmed, "") : new(trimmed[..cut], trimmed[(cut + 1)..].Trim());
    }

    /// <summary>Splits a text into words at blanks, a quoted word kept whole.</summary>
    /// <param name="text">The text, a line's rest.</param>
    public static IReadOnlyList<string> Split(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false, any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted && c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                word.Append(text[++i]);
            else if (c == '"')
                (quoted, any) = (!quoted, true);
            else if (!quoted && c is ' ' or '\t')
            {
                if (any) words.Add(word.ToString());
                word.Clear();
                any = false;
            }
            else
            {
                word.Append(c);
                any = true;
            }
        }

        if (any) words.Add(word.ToString());
        return words;
    }

    /// <summary>
    /// A word as a line holds it, in quotes where it is empty or holds a blank, a quote or a
    /// backslash, so <see cref="Split"/> reads it back as one word.
    /// </summary>
    /// <param name="word">The word.</param>
    public static string Quote(string word) =>
        word.Length > 0 && word.IndexOfAny([' ', '\t', '"', '\\']) < 0
            ? word
            : '"' + word.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
}
