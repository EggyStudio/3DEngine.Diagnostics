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
/// settings or the program ends, both on its own thread. The file keeps what a player set through
/// the console, the panel or the file itself, <see cref="Setting.SavedText"/>, and never a value
/// the program wrote through a getter. The line of a setting that is locked as the file is read
/// is not applied, since the lock refuses it, nor is it told as a fault, and the file keeps it as
/// it stands for as long as the setting is locked and holds no value of its own to write.
/// </para>
/// <para>
/// A setting added to the registry after the read is given the value its line holds, as a start
/// gives it, and the file is written when such a setting is set, not only at the close, since a
/// part of the program that loads late may not be there at the close. A line refused and a write
/// that failed are told through <see cref="Trouble"/>, on the thread that added or set.
/// </para>
/// <para>
/// A host disposes the file as its program stops. The file then writes once more where a value is
/// unsaved, stops hearing the registry and writes nothing again, so a setting added after the stop
/// leaves the file as it was.
/// </para>
/// </remarks>
public sealed class SettingsFile : IDisposable
{
    private readonly List<(int Number, string Text)> _kept = [];
    private readonly object _gate = new();
    private readonly List<Setting> _listening = [];
    private Registry? _registry;
    private string? _held;
    private bool _stopped;

    /// <summary>Makes the file at a path, which is read and written when asked.</summary>
    /// <param name="path">The file's path.</param>
    public SettingsFile(string path) => Path = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The file's path.</summary>
    public string Path { get; }

    /// <summary>
    /// Raised with a sentence where a line of the file was refused by a setting added after the
    /// read, or the file could not be written after such a setting was set.
    /// </summary>
    public event Action<string>? Trouble;

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
    /// default and its line is not written again. The file then listens to the registry, so a
    /// setting added later is given its line and written when it is set, until the file is
    /// disposed. A file that was disposed cannot read again.
    /// </remarks>
    /// <param name="registry">The registry whose settings take the values.</param>
    public IReadOnlyList<string> Read(Registry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            Hush();
            _registry = registry;
            registry.SettingAdded += Added;
            _kept.Clear();
            var refused = new List<string>();
            _held = File.Exists(Path) ? File.ReadAllText(Path, Encoding.UTF8).ReplaceLineEndings("\n") : "";
            var lines = _held.Split('\n');
            // The text's last line end leaves an empty last part, which is no line of the file.
            var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
            for (var i = 0; i < count; i++)
            {
                var line = CommandLine.Parse(lines[i]);
                if (registry.FindSetting(line.Name) is not { } setting || setting.Locked is not null)
                    _kept.Add((i + 1, lines[i]));
                else if (Apply(setting, line, i + 1) is { } trouble)
                    refused.Add(trouble);
            }

            return refused;
        }
    }

    /// <summary>
    /// Writes the file from a registry's settings where its text would change, and says whether it
    /// wrote.
    /// </summary>
    /// <remarks>
    /// The text goes to a file beside the path first, which then takes the path's place, so a
    /// program stopped midway leaves the file as it was. A file that was disposed writes nothing.
    /// </remarks>
    /// <param name="registry">The registry whose settings are kept.</param>
    public bool Write(Registry registry)
    {
        lock (_gate)
        {
            if (_stopped) return false;
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
    }

    /// <summary>
    /// Writes the file once more where a value is unsaved, then stops hearing the registry, so
    /// nothing a setting does afterward reaches the file.
    /// </summary>
    /// <remarks>
    /// A write that fails is told through <see cref="Trouble"/> and the file stops all the same,
    /// since a program stopping has nowhere to take the exception. A second call does nothing.
    /// </remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopped) return;
            try
            {
                if (_registry is { } registry) Write(registry);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trouble?.Invoke($"{System.IO.Path.GetFileName(Path)} is not written. {error.Message}");
            }

            Hush();
            _stopped = true;
        }
    }

    /// <summary>The text <see cref="Write"/> writes for a registry's settings.</summary>
    /// <param name="registry">The registry whose settings are kept.</param>
    public string Text(Registry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        lock (_gate)
        {
            var text = new StringBuilder();
            foreach (var setting in registry.Settings)
                if (Writes(setting))
                    text.Append(setting.Name).Append(' ').Append(CommandLine.Quote(setting.SavedText)).Append('\n');

            // A kept line whose name a setting has taken since the read gives way to the setting,
            // unless the setting is locked and holds no line of its own to give.
            foreach (var (_, line) in _kept)
                if (registry.FindSetting(CommandLine.Parse(line).Name) is not { } setting || (setting.Locked is not null && !Writes(setting)))
                    text.Append(line).Append('\n');
            return text.ToString();
        }
    }

    private static bool Writes(Setting setting) => setting.Saved && !setting.ReadOnly && setting.SavedText != setting.DefaultText;

    // A setting added after the read takes the line the file holds for it, as a start does, and
    // from then on a set of it writes the file.
    private void Added(Setting setting)
    {
        lock (_gate)
        {
            if (_stopped) return;
            var at = _kept.FindIndex(kept => CommandLine.Parse(kept.Text).Name == setting.Name);
            if (at >= 0 && setting.Locked is null)
            {
                var (number, text) = _kept[at];
                _kept.RemoveAt(at);
                if (Apply(setting, CommandLine.Parse(text), number) is { } refused) Trouble?.Invoke(refused);
            }

            // After the line is applied, so that applying it writes nothing.
            setting.Changed += Written;
            _listening.Add(setting);
        }
    }

    // The file hears the registry's additions and its late settings' changes no more.
    private void Hush()
    {
        if (_registry is not null) _registry.SettingAdded -= Added;
        foreach (var setting in _listening) setting.Changed -= Written;
        _listening.Clear();
        _registry = null;
    }

    private void Written(Setting setting)
    {
        try
        {
            if (_registry is { } registry) Write(registry);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trouble?.Invoke($"{System.IO.Path.GetFileName(Path)} is not written. {error.Message}");
        }
    }

    private string? Apply(Setting setting, CommandLine line, int number)
    {
        var answer = line.Rest.Length == 0
            ? Answer.Fail(Answer.BadArgument, $"{setting.Name} has no value.")
            : setting.TryStart(line.Words.Count == 1 ? line.Words[0] : line.Rest);
        return answer.Done ? null : $"{System.IO.Path.GetFileName(Path)} line {number} is refused. {answer.Text}";
    }
}
