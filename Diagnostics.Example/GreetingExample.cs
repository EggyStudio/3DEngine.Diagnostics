using System.Globalization;

namespace Diagnostics.Example;

/// <summary>How a greeting is written.</summary>
public enum GreetingStyle
{
    /// <summary>As typed.</summary>
    Plain,

    /// <summary>In capitals.</summary>
    Shout,

    /// <summary>In small letters.</summary>
    Whisper,
}

/// <summary>
/// A console program that binds the core as a host does, four commands, one of which answers
/// late, and four settings of four kinds, a settings file read at the start and written at the
/// end, and lines run through a <see cref="ConsoleModel"/> whose lines are printed.
/// </summary>
/// <remarks>
/// <para>
/// <c>dotnet run --project Diagnostics.Example -- "greet.hello" "greet.name Ada" "greet.hello"</c>
/// runs the three lines. With no argument the lines are read from standard input, so a script can
/// be piped in. Each line is printed after <c>&gt; </c>, followed by what it answered, and a
/// failed line's answer after <c>! </c>. The exit code is 0, or 1 where a line failed.
/// </para>
/// <para>
/// The program makes a registry of its own where a game adds to <see cref="Registry.Process"/>,
/// so it can be run more than once in a process, as its test does.
/// </para>
/// </remarks>
public static class GreetingExample
{
    /// <summary>
    /// The folder the settings file goes to, under the system's temporary folder.
    /// </summary>
    public static string DefaultFolder { get; } = Path.Combine(Path.GetTempPath(), "diagnostics-example");

    /// <summary>
    /// The environment variable that overrides <c>greet.style</c> while it is set.
    /// </summary>
    public const string StyleVariable = "GREETING_STYLE";

    /// <summary>Runs the lines and prints what they answered.</summary>
    /// <param name="lines">The lines, or none to read them from <paramref name="input"/>.</param>
    /// <param name="input">Where the lines come from where the arguments hold none.</param>
    /// <param name="output">Where the lines and their answers are printed.</param>
    /// <param name="folder">The folder that holds <c>settings.txt</c>.</param>
    /// <returns>0, or 1 where a line failed.</returns>
    public static int Run(string[] lines, TextReader input, TextWriter output, string folder)
    {
        var registry = new Registry();
        var console = new ConsoleModel(registry);
        var name = new Setting<string>("greet.name", "Whom the greeting is for.", "world");
        var repeat = new Setting<int>("greet.repeat", "How many times the greeting is said.", 1) { Low = 1, High = 5 };
        var style = new Setting<GreetingStyle>("greet.style", "How the greeting is written.", GreetingStyle.Plain)
        {
            EnvironmentVariable = StyleVariable,
        };
        var exclaim = new Setting<bool>("greet.exclaim", "Whether a greeting ends with an exclamation mark.", false)
        {
            AtNextStart = true,
        };
        foreach (var setting in new Setting[] { name, repeat, style, exclaim }) registry.Add(setting);

        registry.Add(new Command("greet.hello", "Says the greeting as the settings write it.", "greet.hello [<name>]",
            line =>
            {
                var who = line.Words.Count > 0 ? line.Words[0] : name.Value;
                var said = $"Hello, {who}{(exclaim.Value ? "!" : ".")}";
                said = style.Value switch
                {
                    GreetingStyle.Shout => said.ToUpperInvariant(),
                    GreetingStyle.Whisper => said.ToLowerInvariant(),
                    _ => said,
                };
                return new Answer(string.Join(' ', Enumerable.Repeat(said, repeat.Value)));
            })
        {
            Arguments = [new("name", "text", Optional: true)],
        });

        registry.Add(new Command("greet.sum", "Adds numbers.", "greet.sum <number>...", line =>
        {
            double sum = 0;
            foreach (var word in line.Words)
                if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) sum += number;
                else return Answer.Fail(Answer.BadArgument, $"{word} is no number.");
            return line.Words.Count == 0
                ? Answer.Fail(Answer.BadArgument, "greet.sum takes numbers.")
                : new Answer(sum.ToString(CultureInfo.InvariantCulture));
        })
        {
            Arguments = [new("number", "float")],
        });

        registry.Add(new Command("greet.echo", "Says the rest of the line as it was typed.", "greet.echo <line>",
            line => new Answer(line.Rest))
        {
            Arguments = [new("line", "text", TakesLine: true)],
        });

        // A command whose answer is not ready answers at once and writes the rest through the
        // console where it is, from whatever thread it ends on, since Write is safe from any. A
        // host's work ends when it ends, and here the work waits for the lines to be run, so the
        // output is the same every run.
        var gate = new TaskCompletionSource();
        var late = new List<Task>();
        registry.Add(new Command("greet.later", "Answers at once and greets again a moment later.", "greet.later [<name>]",
            line =>
            {
                // The setting is read here, on the host's thread, and not in the task.
                var who = line.Words.Count > 0 ? line.Words[0] : name.Value;
                late.Add(Task.Run(async () =>
                {
                    await gate.Task;
                    console.Write(ConsoleLevel.Info, $"Hello, {who}, found a moment later.");
                }));
                return new Answer($"Looking for {who}.");
            })
        {
            Arguments = [new("name", "text", Optional: true)],
        });

        // The file is read once the settings are added, and a value it holds that a setting
        // refuses is said and leaves that setting at its default.
        var file = new SettingsFile(Path.Combine(folder, "settings.txt"));
        foreach (var refusal in file.Read(registry)) console.Write(ConsoleLevel.Warning, refusal);

        if (lines.Length == 0) lines = ReadLines(input);
        var failed = false;
        foreach (var line in lines)
            failed |= !console.Submit(line).Done;
        gate.SetResult();
        Task.WaitAll([.. late]);

        foreach (var line in console.Lines())
            output.WriteLine(line.Level switch
            {
                ConsoleLevel.Error or ConsoleLevel.Warning => "! " + line.Text,
                _ => line.Text,
            });
        output.WriteLine(file.Write(registry) ? "settings.txt written" : "settings.txt unchanged");
        return failed ? 1 : 0;
    }

    private static string[] ReadLines(TextReader input)
    {
        var lines = new List<string>();
        while (input.ReadLine() is { } line)
            if (line.Trim().Length > 0) lines.Add(line);
        return [.. lines];
    }
}
