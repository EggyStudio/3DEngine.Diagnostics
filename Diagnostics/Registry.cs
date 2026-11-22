using System.Text;

namespace Diagnostics;

/// <summary>
/// The commands and the settings of a process by name, in one namespace, and what runs a line.
/// </summary>
/// <remarks>
/// <para>
/// A name is lowercase words of letters, digits and underscores joined by dots, its first a
/// letter, as <c>render.vsync</c>, and names a command or a setting, never both. A line whose
/// first word is a command's name runs it. A line whose first word is a setting's name answers
/// with the setting where nothing follows and sets it where a value does, so
/// <c>render.vsync off</c> turns it off.
/// </para>
/// <para>
/// A registry is made with three commands of its own, <c>help</c>, <c>list</c> and <c>reset</c>,
/// which a host removes where it has its own.
/// </para>
/// <para>
/// Adding, removing, finding and listing are safe from any thread, so a command is added as an
/// assembly loads. <see cref="Run"/> runs the command on the calling thread, and a host calls it
/// on its own thread, handing a line a socket gave over to that thread first.
/// </para>
/// </remarks>
public sealed class Registry
{
    private readonly object _gate = new();
    private readonly SortedDictionary<string, object> _byName = new(StringComparer.Ordinal);

    /// <summary>Makes a registry holding its own three commands.</summary>
    public Registry()
    {
        Add(new Command("help", "Lists the commands, or says what one command or setting takes.", "help [<name>]", Help)
        {
            Arguments = [new("name", "name", Optional: true)],
        });
        Add(new Command("list", "Lists the commands and the settings whose names begin as given.", "list [<prefix>]", List)
        {
            Arguments = [new("prefix", "name", Optional: true)],
        });
        Add(new Command("reset", "Sets a setting to its default.", "reset <name>", Reset)
        {
            Arguments = [new("name", "name")],
        });
    }

    /// <summary>The registry of the process, which a host's generated code adds its commands
    /// to.</summary>
    public static Registry Process { get; } = new();

    /// <summary>The commands, by name.</summary>
    public IReadOnlyList<Command> Commands => All<Command>();

    /// <summary>The settings, by name.</summary>
    public IReadOnlyList<Setting> Settings => All<Setting>();

    /// <summary>Whether a text is a name, lowercase words joined by dots.</summary>
    /// <param name="name">The text.</param>
    public static bool IsName(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsAsciiLetterLower(name[0]) || name[^1] == '.') return false;
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '.' ? name[i - 1] == '.' : !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_') return false;
        }

        return true;
    }

    /// <summary>Adds a command.</summary>
    /// <param name="command">The command.</param>
    /// <exception cref="ArgumentException">
    /// The name is no name, or a command or a setting has it.
    /// </exception>
    public void Add(Command command) => Add((command ?? throw new ArgumentNullException(nameof(command))).Name, command);

    /// <summary>Adds a setting.</summary>
    /// <param name="setting">The setting.</param>
    /// <exception cref="ArgumentException">
    /// The name is no name, or a command or a setting has it.
    /// </exception>
    public void Add(Setting setting)
    {
        Add((setting ?? throw new ArgumentNullException(nameof(setting))).Name, setting);
        SettingAdded?.Invoke(setting);
    }

    /// <summary>
    /// Raised after a setting is added, on the thread that added it and outside the registry's
    /// lock, so a handler finds the setting by its name and may add or remove others.
    /// </summary>
    /// <remarks>
    /// A settings file listens to it, to give a setting added after the start the value its file
    /// holds. A command added raises nothing.
    /// </remarks>
    public event Action<Setting>? SettingAdded;

    /// <summary>Takes a command or a setting out, and says whether there was one.</summary>
    /// <param name="name">Its name.</param>
    public bool Remove(string name)
    {
        lock (_gate) return _byName.Remove(name);
    }

    /// <summary>The command of a name, or null.</summary>
    /// <param name="name">The name.</param>
    public Command? FindCommand(string name) => Find(name) as Command;

    /// <summary>The setting of a name, or null.</summary>
    /// <param name="name">The name.</param>
    public Setting? FindSetting(string name) => Find(name) as Setting;

    /// <summary>The names of the commands and the settings that begin with a prefix, in
    /// order.</summary>
    /// <param name="prefix">The prefix, empty for every name.</param>
    public IReadOnlyList<string> Starting(string prefix)
    {
        lock (_gate) return _byName.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    /// <summary>Runs a line, a command or the reading or setting of a setting.</summary>
    /// <remarks>
    /// A command that throws answers with <see cref="Answer.Failed"/> and the exception's message,
    /// so one fault ends no console, and the answer carries the exception as
    /// <see cref="Answer.Error"/> for a host to log. A blank line answers with no text.
    /// </remarks>
    /// <param name="line">The line as typed.</param>
    public Answer Run(string line)
    {
        var parsed = CommandLine.Parse(line);
        if (parsed.Name.Length == 0) return new Answer("");
        switch (Find(parsed.Name))
        {
            case Command command:
                try
                {
                    return command.Run(parsed) ?? new Answer("");
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    return Answer.Fail(Answer.Failed, $"{command.Name} failed. {error.Message}") with { Error = error };
                }
            case Setting setting when parsed.Rest.Length == 0:
                return new Answer(setting.Describe());
            case Setting setting:
                // One word is the value with its quotes off, and more are the value as typed, so a
                // text of several words needs no quotes.
                return setting.TrySet(parsed.Words.Count == 1 ? parsed.Words[0] : parsed.Rest);
            default:
                return Answer.Fail(Answer.NoSuchCommand, $"No command and no setting is named {parsed.Name}.");
        }
    }

    private void Add(string name, object entry)
    {
        if (!IsName(name))
            throw new ArgumentException($"\"{name}\" is no name, which is lowercase words joined by dots, as render.vsync.", nameof(entry));
        lock (_gate)
        {
            if (_byName.TryGetValue(name, out var held))
                throw new ArgumentException($"The name {name} is taken by a {(held is Command ? "command" : "setting")}.", nameof(entry));
            _byName.Add(name, entry);
        }
    }

    private object? Find(string name)
    {
        lock (_gate) return _byName.GetValueOrDefault(name);
    }

    private List<T> All<T>()
    {
        lock (_gate) return _byName.Values.OfType<T>().ToList();
    }

    private Answer Help(CommandLine line)
    {
        if (line.Words.Count == 0)
            return new Answer(string.Join("\n", Commands.Select(command => $"{command.Name}  {command.Summary}")));
        var name = line.Words[0];
        switch (Find(name))
        {
            case Command command:
                return new Answer($"{command.Usage}\n{command.Summary}");
            case Setting setting:
                var said = new StringBuilder(setting.Describe()).Append('\n').Append(setting.Summary);
                if (setting.Takes.Length > 0) said.Append($"\nIt takes {setting.Takes}.");
                if (setting.AtNextStart && !setting.ReadOnly) said.Append("\nA set takes effect at the next start.");
                if (setting.EnvironmentRefusal is { } refusal) said.Append('\n').Append(refusal);
                return new Answer(said.ToString());
            default:
                return Answer.Fail(Answer.NoSuchCommand, $"No command and no setting is named {name}.");
        }
    }

    private Answer List(CommandLine line)
    {
        var prefix = line.Words.Count > 0 ? line.Words[0] : "";
        List<object> found;
        lock (_gate) found = _byName.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(pair => pair.Value).ToList();
        return new Answer(string.Join("\n", found.Select(entry => entry switch
        {
            Setting { Overridden: true } setting => $"{setting.Name} {CommandLine.Quote(setting.Text)} (environment)",
            Setting setting => $"{setting.Name} {CommandLine.Quote(setting.Text)}",
            _ => ((Command)entry).Name,
        })));
    }

    private Answer Reset(CommandLine line)
    {
        if (line.Words.Count != 1) return Answer.Fail(Answer.BadArgument, "reset takes a setting's name.");
        return FindSetting(line.Words[0])?.Reset()
            ?? Answer.Fail(Answer.NoSuchCommand, $"No setting is named {line.Words[0]}.");
    }
}
