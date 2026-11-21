using System.Text;

namespace Diagnostics;

/// <summary>
/// The settings kept across runs in one text file, a line for each setting whose value differs
/// from its default.
/// </summary>
/// <remarks>
/// <para>
/// A line is a setting's name, a space and its value as a console line holds it, a text with a
/// blank in quotes, in UTF-8. The file holds the saved settings that differ from their defaults,
/// by name, and after them every other line it was read with as it stood, a line of a name the
/// registry does not know, a comment after <c>#</c> or a blank line, so a setting of a part of the
/// program that did not load this run is kept for the run it does. A default that changes between
/// two versions of a program changes for every player who never set the value, since the file
/// never held it.
/// </para>
/// <para>
/// A host reads the file once its settings are added and writes it as the player leaves the
/// settings or the program ends, both on its own thread.
/// </para>
/// </remarks>
public sealed class SettingsFile
{
    private readonly List<string> _kept = [];
    private string? _held;

    /// <summary>Makes the file at a path, which is read and written when asked.</summary>
    /// <param name="path">The file's path.</param>
    public SettingsFile(string path) => Path = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The file's path.</summary>
    public string Path { get; }

    /// <summary>
    /// The file of a game in the player's folder, <c>settings.txt</c> in a folder of the game's
    /// name under the system's folder of applications' data.
    /// </summary>
    /// <param name="game">The game's name, a folder's name.</param>
    public static SettingsFile For(string game)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        var data = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return new SettingsFile(System.IO.Path.Combine(data, game, "settings.txt"));
    }

    /// <summary>
    /// Reads the file into a registry's settings as a start does, and gives each line it refused
    /// with its reason.
    /// </summary>
    /// <remarks>
    /// A missing file reads as an empty one. A value a setting refuses leaves the setting at its
    /// default and its line is not written again.
    /// </remarks>
    /// <param name="registry">The registry whose settings take the values.</param>
    public IReadOnlyList<string> Read(Registry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _kept.Clear();
        var refused = new List<string>();
        _held = File.Exists(Path) ? File.ReadAllText(Path, Encoding.UTF8).ReplaceLineEndings("\n") : "";
        var lines = _held.Split('\n');
        // The text's last line end leaves an empty last part, which is no line of the file.
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
        {
            var line = CommandLine.Parse(lines[i]);
            if (registry.FindSetting(line.Name) is not { } setting)
            {
                _kept.Add(lines[i]);
                continue;
            }

            var answer = line.Rest.Length == 0
                ? Answer.Fail(Answer.BadArgument, $"{setting.Name} has no value.")
                : setting.TryStart(line.Words.Count == 1 ? line.Words[0] : line.Rest);
            if (!answer.Done) refused.Add($"{System.IO.Path.GetFileName(Path)} line {i + 1} is refused. {answer.Text}");
        }

        return refused;
    }

    /// <summary>
    /// Writes the file from a registry's settings where its text would change, and says whether it
    /// wrote.
    /// </summary>
    /// <remarks>
    /// The text goes to a file beside the path first, which then takes the path's place, so a
    /// program stopped midway leaves the file as it was.
    /// </remarks>
    /// <param name="registry">The registry whose settings are kept.</param>
    public bool Write(Registry registry)
    {
        var text = Text(registry);
        if (text == _held) return false;
        if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        var beside = Path + ".tmp";
        File.WriteAllText(beside, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(beside, Path, overwrite: true);
        _held = text;
        return true;
    }

    /// <summary>The text <see cref="Write"/> writes for a registry's settings.</summary>
    /// <param name="registry">The registry whose settings are kept.</param>
    public string Text(Registry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var text = new StringBuilder();
        foreach (var setting in registry.Settings)
            if (setting.Saved && setting.StoredText != setting.DefaultText)
                text.Append(setting.Name).Append(' ').Append(CommandLine.Quote(setting.StoredText)).Append('\n');

        // A kept line whose name a setting has taken since the read gives way to the setting.
        foreach (var line in _kept)
            if (registry.FindSetting(CommandLine.Parse(line).Name) is null)
                text.Append(line).Append('\n');
        return text.ToString();
    }
}
