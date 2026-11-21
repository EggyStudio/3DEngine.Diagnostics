# Review

The repository is empty. No run yet, the workflow coming with item 2.

## Now

The module holds what both engines carry twice today for a developer and a player at the keyboard.
Each engine has a registry of console commands filled by a `[Command]` attribute, a host that runs a
line, and an overlay's model of lines, nearly the same in shape and apart in keys and names. Neither
has a registry of settings, and neither hosts the Modeling module's commands. The module writes
these once. Its core holds commands, settings and the content of the overlay, the console and the
settings panel as plain data with no graphics reference, and a second small part beside it draws the
console and the panel with ImGui for a host that takes it. Inside the module no engine is named or
targeted, the API is general with hooks a host fills. The order is the other modules', the
documents, the skeleton, the design, the code, then the engines taking it.

1. **The documents, one commit.** AGENTS.md, README.md with the link row first (`[Devlog]` and
    `[Cheatsheet]` to this repository's files, a blank line, then `# Diagnostics`),
    `.github/STYLE.md`, `COMMITS.md` and `CODE_OF_CONDUCT.md`, LICENSE, `.gitignore` with
    `/.github/dump/` in it and `.gitattributes`, each as Materials and Sky have them with the
    module's name in place. DEVLOG.md and CHEATSHEET.md begin here, the cheatsheet listing nothing
    the module does not have yet. The Tools module comes as a submodule at `modules/Tools` for the
    shared checks and scripts, pinned by its `scripts/pin.py`. This commit is the first test of the
    commit form here (N 7.2 and N 7.6), and it carries NORM.md and this file.

2. **The skeleton, one commit after item 1.** `Diagnostics.slnx`, `Diagnostics/Diagnostics.csproj`
    as a class library on net10.0 with assembly and package id `3DEngine.Diagnostics` and the
    namespace `Diagnostics`, `Diagnostics.ImGui/` as the second library that alone references ImGui,
    `Diagnostics.Tests/` on xunit with a test that the core loads, a test that the core's assembly
    references no ImGui and no graphics assembly, and the shared checks from Tools,
    `build/version.txt` at `0.1`, and `.github/workflows/build.yml` on a push with `test (os)` on
    ubuntu-24.04, windows-latest and macos-15, the strict build and the suite, as the other modules'
    workflow has it. Green on the three systems before item 4. The module is never published as a
    package, the engines embed it.

3. **The design, as the first Reply, written beside items 1 and 2.** From a read of both engines'
    console, overlay and options code and of Modeling's commands, in at most sixty lines, the public
    shapes before any code. A command (a dotted name, a summary, its arguments, a run that returns
    text or a failure code), a setting (a dotted name in the same namespace as commands, a type, a
    default, a range or choices, a getter and a setter a host gives, whether it takes effect at the
    next start, whether the environment overrides it), the registry holding both for the process,
    the console's model (a line in, lines out, history, completion by prefix), the overlay's model
    (named lines a host fills each frame), the panel's model (pages of settings steered by up, down,
    left, right, accept and back, so a pad and arrows serve alike), the keys as rebindable actions
    with defaults F1 hide the interface, F2 screenshot, F3 overlay, F4 settings panel, F5 console
    (the screenshot itself stays a host's), and the settings file (one a game in the player's
    folder, name and value lines, only values that differ from their default, unknown names kept).
    Name what each engine would delete on taking it and what stays theirs. Questions that are a
    host's to answer go in the Reply and wait for review before item 4.

4. **The core's registry and console, after item 3 is reviewed.** Commands and settings with their
    tests first (a name twice refused, a setting out of its range refused with its reason, a line
    parsed with quotes, completion, the file written and read back with only changed values, a value
    the environment overrides shown as such), no drawing yet. Then the overlay's and the panel's
    models, then the ImGui part, each its own commit with a picture where there is something to see
    (N 4.8 to N 4.10).

## Verdicts

None yet.

## Decisions

1. **The core is free of any graphics reference, the ImGui part stands beside it.** A host without
    ImGui draws the models its own way.

2. **Settings are saved to one file a game in the player's folder,** only values that differ from
    their default. A game's earlier options fold into it on the game's side.

3. **The keys are the same for every host and rebindable.** F1 hides the interface, F2 takes a
    screenshot, F3 shows the overlay, F4 the settings panel, F5 the console.

4. **The settings panel is a paged list steered by arrows or a pad,** not a window for a mouse.

5. **Commands and settings share one namespace by dotted prefix, and the registry is the
    process's.** The command's shape is Modeling's with a failure code added.

## Replies

Item 3's first Reply, the design, read against 3DEngine's `6e5acdbf`, BevyCSharp's `789886e0`,
Modeling's `b8d6834c` and the voxel game's `0106269f`. Every type is in the namespace `Diagnostics`,
the ImGui library's too, since a namespace ending in `ImGui` hides the binding's `ImGui` class.

**A name** is lowercase words joined by dots, and one name is a command or a setting, never both,
so a name added twice is refused whichever it was.

**A command** is `Command(Name, Summary, Usage, Arguments, Func<CommandLine, Answer> Run)`, an
argument `Argument(Name, Kind, Optional, TakesLine)` as BevyCSharp's `CommandParameter`. A
`CommandLine` gives `Rest`, the line after the name as typed, and `Words`, split at blanks with
double quotes and backslash escapes, so Modeling's whole-line run and the engines' typed words are
both one adapter. `Answer(Text, Code)` is done where `Code` is null, a code in capitals as
Modeling's, the module's own being `NO_SUCH_COMMAND`, `BAD_ARGUMENT`, `OUT_OF_RANGE` and `FAILED`
for a run that threw.

**A setting** is `Setting<T>` over bool, int, float, an enum's choices or text, with `Name`,
`Summary`, `Default`, `Low`, `High` and `Step` or `Choices`, a `Get` and a `Set` the host gives (the
module holds the value where it gives none), `AtNextStart`, `Saved`, `EnvironmentVariable` and
`Changed`. `TrySet(text)` answers a refusal with its reason, the range or the choices. A setting
for the next start keeps the set value as `Pending` and `Get` stays the running one. A variable
set in the environment gives the value, `Overridden` reads true, and a set is kept for the file
and answered as overridden. Values are text in the invariant culture.

**The registry** is `Registry`, an instance a test makes, with `Registry.Process` the process's
one. `Add`, `Remove`, `Find`, `Commands`, `Settings`, `Starting(prefix)` and `Run(line)`. A line
naming a setting reads it with no word after and sets it with one, `render.vsync off`, and `help`,
`list` and `reset` are the module's own commands.

**The console** is `ConsoleModel(registry)`, `Submit(line)` echoing the line and its answer into a
ring of 2,000 `ConsoleLine(Text, Level)`, `Write(level, text)` for a host's log, `Back` and
`Forward` over the history, `Complete(typed)` giving the names of the prefix and their common
start, a choice's values after a setting's name, `Hint(typed)`, a filter by level and search.

**The overlay** is `OverlayModel` with four places, `Head`, `Beside`, `Left` and `Right`, as both
engines lay it out. A host adds `Section(name, place, Func<IEnumerable<string>>)` and calls
`Frame(seconds)`, which keeps 240 frame times, gives `Stats` (now, average, 1% low) and asks the
sections again four times a second. The module fills no line itself but the frame's.

**The panel** is `PanelModel(registry)`, a page for each first word of the settings' names and a
host's own pages, its rows a setting, an action running a command, a page or text, read as label,
value and marks for pending and overridden. `Press(Up | Down | Left | Right | Accept | Back)` is all
it takes, left and right stepping by `Step` or to the next choice. Repeating a held key is the
host's.

**The keys** are `KeyAction` (HideInterface, Screenshot, Overlay, Panel, Console), each a text
setting `keys.<action>` holding a key's name, F1 to F5 by default, so the console, the panel and
the file rebind them. A host gives `KeyDown(name)`, `KeyUp(name)` and whether a field is typing,
and the module answers the action, taken on release where no other key went down as both engines
take F3, with Shift for the overlay's detail. It toggles its own models and raises
`ScreenshotAsked` for the host.

**The file** is `SettingsFile(path)`, with `SettingsFile.For(game)` under the system's application
data folder, UTF-8 lines of a name, a space and a value, saved settings that differ from their
default alone, sorted by name, a line of an unknown name kept as it is, a refused value reported
and its setting at its default, written through a temporary file and only where the text changed.
A changed default needs no version, since the file never held it.

**3DEngine deletes** `ConsoleCommands`, `ConsoleCommand`, `CommandParameter`, the overlay's lists,
sections, strip and `Stats`, the chord rule of `DebugKeys`, and `EditMeshCommands`' `model.*`, and
gains the typed console. **BevyCSharp deletes** the same registry, `ConsoleLog`'s ring,
`ConsoleView`, `ImGuiConsole`, the model half of `Debug.Overlay`, the key statics and
`ConsoleModelCommands`. The games drop their panels' rows, `Option`, `OptionsFile` and `setting`.
**Theirs stay** the `[Command]` attribute and generators, which emit `Registry.Process.Add`, lending
the world, `Fail`, `Hold` and `Later`, the sockets, the numbers' sources, applying a setting,
reading keys, drawing the overlay, screenshots, recording and their own commands' names.

Questions a host answers, held until reviewed.

1. Does `[Command]` stay each engine's, as proposed, or move here with the generators kept there?
2. A late answer (`Hold`, `Later`) stays the host's, which writes it to the console. Agreed?
3. F3 with F4 is a game's chord in two games. The release rule leaves chords to the host, so F4
   alone opens the panel and the chord stands. Is that the wanted reading?
