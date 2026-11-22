# Diagnostics cheatsheet

Every public type of `3DEngine.Diagnostics` with its members, a line each of its declaration and
the first sentence of its summary, in the order the README first names it. A host adds its
`Command`s and `Setting`s to a `Registry`, runs lines through a `ConsoleModel` over it and keeps the
settings in a `SettingsFile`, and shows an `OverlayModel` and a `PanelModel` that `DiagnosticKeys`
turn over. The README's section Binding it in your engine gives the conventions,
the calls safe from any thread among them, and the smallest binding. The list is written by
`Cheatsheet.Write` from the built library, and this text by hand. The ImGui library's two types
follow under a heading of their own.

<!-- written by Cheatsheet.Write from the built library -->
```csharp
class Registry                                           // The commands and the settings of a process by name, in one namespace, and what runs a line
Registry();                                              // Makes a registry holding its own three commands
Registry Process { get; }                                // The registry of the process, which a host's generated code adds its commands to
IReadOnlyList<Command> Commands { get; }                 // The commands, by name
IReadOnlyList<Setting> Settings { get; }                 // The settings, by name
bool IsName(string name);                                // Whether a text is a name, lowercase words joined by dots
void Add(Command command);                               // Adds a command
void Add(Setting setting);                               // Adds a setting
bool Remove(string name);                                // Takes a command or a setting out, and says whether there was one
Command? FindCommand(string name);                       // The command of a name, or null
Setting? FindSetting(string name);                       // The setting of a name, or null
IReadOnlyList<string> Starting(string prefix);           // The names of the commands and the settings that begin with a prefix, in order
Answer Run(string line);                                 // Runs a line, a command or the reading or setting of a setting

record Command                                           // One console command, its name, what it does, the words it takes and what runs it
Command(string Name, string Summary, string Usage, Func<CommandLine, Answer> Run);
string Name { get; init; }                               // The command's dotted name, as entity.set, the line's first word
string Summary { get; init; }                            // What the command does, a sentence a console's help shows
string Usage { get; init; }                              // The words the command takes, its name first
Func<CommandLine, Answer> Run { get; init; }             // Runs the command on a line, on the host's thread
IReadOnlyList<Argument> Arguments { get; init; }         // The words the command takes, in their order, empty where none is declared

record struct Argument                                   // One word a command takes, for a console's help and completion
Argument(string Name, string Kind, bool Optional = false, bool TakesLine = false);
string Name { get; init; }                               // The argument's name as a usage shows it
string Kind { get; init; }                               // What the word is, as int or path, the host's own word, with name for a command's or a setting's name, which a console completes
bool Optional { get; init; }                             // Whether the word may be left out
bool TakesLine { get; init; }                            // Whether the argument is the rest of the line as typed

class CommandLine                                        // A line as a console takes it, its first word the name and the rest its words
string Name { get; }                                     // The line's first word, a command's or a setting's name, empty for a blank line
string Rest { get; }                                     // The line after its name as it was typed, with the blanks at its ends taken off
IReadOnlyList<string> Words { get; }                     // The words after the name, split as Split splits them
CommandLine Parse(string line);                          // Reads a line into its name and the rest
IReadOnlyList<string> Split(string text);                // Splits a text into words at blanks, a quoted word kept whole
string Quote(string word);                               // A word as a line holds it, in quotes where it is empty or holds a blank, a quote or a backslash, so Split reads it back as one word

record Answer                                            // What a command or a set of a setting answers, its text and a code where it failed
string NoSuchCommand = "NO_SUCH_COMMAND";                // The code of a line whose first word names no command and no setting
string BadArgument = "BAD_ARGUMENT";                     // The code of a word a command or a setting cannot read
string OutOfRange = "OUT_OF_RANGE";                      // The code of a number outside a setting's range
string Failed = "FAILED";                                // The code of a command whose run threw
Answer(string Text, string? Code = null);
string Text { get; init; }                               // What a console shows, one line or several
string? Code { get; init; }                              // The failure's code in capitals, as BAD_ARGUMENT, which a script reads, or null where the line was done
bool Done { get; }                                       // Whether the line was done, which it was where there is no code
Answer Fail(string code, string text);                   // An answer that failed with a code and its reason

class CommandAttribute                                   // Marks a static method as a console command, which a host's generator reads to add it to Process
CommandAttribute(string name, string summary);
string Name { get; }                                     // The command's dotted name
string Summary { get; }                                  // What the command does, a sentence a console's help shows

class Setting                                            // One setting as a console, a panel and the settings file read it, its name, its value as text and what it takes, whatever its type
string Name { get; }                                     // The setting's dotted name, as render.vsync, in the commands' namespace
string Summary { get; }                                  // What the setting decides, a sentence a console's help and a panel show
SettingKind Kind { get; }                                // What the value is
bool AtNextStart { get; init; }                          // Whether a set takes effect at the next start, the running value staying until then
bool Saved { get; init; }                                // Whether the settings file keeps the value, which it does by default
string? EnvironmentVariable { get; init; }               // The environment variable that overrides the value while it is set, or null for none
bool Overridden { get; }                                 // Whether the environment gives the running value, EnvironmentVariable being set to a value the setting takes
string? EnvironmentRefusal { get; }                      // Why the environment's value was refused, or null where it was taken or none is set
string Text { get; }                                     // The running value as text
string DefaultText { get; }                              // The default as text
string StoredText { get; }                               // The value the settings file keeps as text, the running one, or the one last set where the environment overrides it or it waits for the next start
string? PendingText { get; }                             // The value that takes effect at the next start as text, or null where none waits
IReadOnlyList<string> Choices { get; }                   // The texts the value takes where they are few, an enum's names or off and on
string Takes { get; }                                    // What the value takes in words, its range or its choices, empty for any text
Answer TrySet(string text);                              // Sets the value from text, or refuses it with its reason
Answer TryStart(string text);                            // Sets the value from text as the program starts, so a value for the next start takes effect
Answer Reset();                                          // Sets the value to its default, as TrySet sets one
Answer Move(int direction);                              // Moves the value one step as a panel's left and right do, a toggle over, a choice to its neighbor past the last to the first, a number by its step held to its range
string Describe();                                       // The setting in a sentence, its value, its default where it differs, and whether the environment gives it or a value waits for the next start
event Action<Setting>? Changed;                          // Raised after a set changed the running or the stored value

class Setting<T>                                         // A setting of one type, a Boolean, an Int32, a Single, a Double, an enum or a String
Setting(string name, string summary, T default, Func<T>? get = null, Action<T>? set = null);  // Makes a setting
T Default { get; }                                       // The value where nothing sets it
double? Low { get; init; }                               // The lowest number the value takes, or null for no bound
double? High { get; init; }                              // The highest number the value takes, or null for no bound
double Step { get; init; }                               // How far a panel moves a number by one press, zero for the kind's own step
T Value { get; }                                         // The running value, the environment's where it overrides
SettingKind Kind { get; }                                // What the value is
bool Overridden { get; }                                 // Whether the environment gives the running value, EnvironmentVariable being set to a value the setting takes
string? EnvironmentRefusal { get; }                      // Why the environment's value was refused, or null where it was taken or none is set
string Text { get; }                                     // The running value as text
string DefaultText { get; }                              // The default as text
string StoredText { get; }                               // The value the settings file keeps as text, the running one, or the one last set where the environment overrides it or it waits for the next start
IReadOnlyList<string> Choices { get; }                   // The texts the value takes where they are few, an enum's names or off and on
string Takes { get; }                                    // What the value takes in words, its range or its choices, empty for any text
Answer TrySet(T value);                                  // Sets the value, or refuses one out of its range with its reason
Answer Move(int direction);                              // Moves the value one step as a panel's left and right do, a toggle over, a choice to its neighbor past the last to the first, a number by its step held to its range

enum SettingKind                                         // What a setting's value is, which decides how a console reads it and a panel steps it
Toggle = 0,                                              // On or off, a Boolean
Whole = 1,                                               // A whole number, an Int32
Number = 2,                                              // A number with a fraction, a Single or a Double
Choice = 3,                                              // One of an enum's names
Text = 4,                                                // Any text, a String

class ConsoleModel                                       // A console's content with no drawing, a plain list of every line written, the line being entered with the candidates its start leaves, their selection and the ghost text, and the history, over a Registry
int DefaultDepth = 2000;                                 // How many lines the console keeps by default, the oldest dropped first
int PopupRows = 6;                                       // How many rows of candidates the popup shows at most
ConsoleModel(Registry registry, int depth = 2000);       // Makes a console over a registry
bool Open { get; set; }                                  // Whether the console is shown, which the console's key turns over
long Written { get; }                                    // How many lines were ever written, which a drawer compares to know that a line came
IReadOnlyList<string> History { get; }                   // The lines entered, oldest first, a line entered twice in a row held once
string Typed { get; set; }                               // The line being entered
IReadOnlyList<string> Candidates { get; }                // The names or values the word being typed could be, the names that begin with it first and then those with a later dotted part that begins with it, each in order, or none
bool PopupShown { get; }                                 // Whether the popup of candidates is shown, once the player edited and a candidate is left
int SelectedIndex { get; }                               // The selected candidate's index, or -1 where there is none
int FirstVisible { get; }                                // The index of the first of the PopupRows rows the popup shows, which keeps the selected candidate in view
string Ghost { get; }                                    // The rest of the selected candidate after the word being typed, where the popup shows and the candidate begins with the word, whatever its case, else empty
string Hint { get; }                                     // What a console shows under the line being entered, a command's usage or a setting in a sentence, of the name typed or of the one name the typed start leaves, or empty
void Write(ConsoleLevel level, string text);             // Writes a text into the console, a line for each of its lines
IReadOnlyList<ConsoleLine> Lines();                      // Every kept line, oldest first
void Clear();                                            // Drops every kept line
Answer Submit(string line);                              // Runs an entered line, writes it and its answer into the console and keeps it in the history
Answer Enter();                                          // Runs the line being entered as the Enter key does, and empties it
void Edit(string text);                                  // The player's edit of the line being entered, which shows the popup
void Navigate(int direction);                            // Takes the up key, which is -1, or the down key, which is 1, and moves the popup's selection where the popup shows, past the last candidate to the first and back, else walks the history
bool Accept();                                           // Fills the selected candidate in place of the word being typed, followed by a space, as the Tab key does, the first candidate where none is selected, and says whether there was one

record struct ConsoleLine                                // One line a console shows
ConsoleLine(string Text, ConsoleLevel Level, int Count = 1, long Index = 0);
string Text { get; init; }                               // The line's text, with no line end
ConsoleLevel Level { get; init; }                        // What the line is
int Count { get; init; }                                 // How many times the line was written in a row, 1 for one writing
long Index { get; init; }                                // The number of lines written before the line's first writing, that is Written as it stood then, which does not change as the line repeats or as older lines are dropped, so a drawer keys what it measured of the line by it

enum ConsoleLevel                                        // What a console's line is, which a console colors and filters by
Input = 0,                                               // A line the player entered, shown again above its answer
Info = 1,                                                // An answer or a host's plain line
Warning = 2,                                             // A host's warning
Error = 3,                                               // A failed line's answer or a host's error

class OverlayModel                                       // The debug overlay's content with no drawing, named sections of lines a host fills, the last frames' times and their statistics
int StripFrames = 240;                                   // How many frame times are kept, the strip's width in frames
double RefreshSeconds = 0.25;                            // How many seconds pass between two askings of the sections
OverlayModel();                                          // Makes an overlay holding its own section, frame
bool Shown { get; set; }                                 // Whether the overlay is shown, which the overlay's key turns over
bool Detailed { get; set; }                              // Whether the sections added as detail are shown too, which Shift with the overlay's key turns over
IReadOnlyList<float> FrameTimes { get; }                 // The kept frame times in milliseconds, oldest first, 240 at most
FrameStats Stats { get; }                                // The statistics of the kept frame times, zeros before the first frame
void AddSection(string name, OverlayPlace place, Func<IEnumerable<string>> lines, bool detail = false);  // Adds a section of lines, in the place of one of the same name where there is one
bool RemoveSection(string name);                         // Takes a section out, and says whether there was one
void Frame(double seconds);                              // Keeps a frame's time and asks the sections for their lines again where a quarter of a second has passed since they were asked
void Refresh();                                          // Asks the sections for their lines at once
IReadOnlyList<string> Lines(OverlayPlace place);         // The lines of a place as the sections last gave them, in the sections' order

enum OverlayPlace                                        // Where on the screen a section of the overlay's lines stands
Head = 0,                                                // The first lines at the top left, the version and the frame's rate
Beside = 1,                                              // Beside the head, to its right on the same rows
Left = 2,                                                // The left column under the head
Right = 3,                                               // The right column

record struct FrameStats                                 // What the kept frame times say of the frame's rate
FrameStats(double Milliseconds, double AverageMilliseconds, double LowFramesPerSecond);
double Milliseconds { get; init; }                       // The last frame's time in milliseconds
double AverageMilliseconds { get; init; }                // The mean time of the kept frames in milliseconds
double LowFramesPerSecond { get; init; }                 // The 1% low, the rate in frames a second of the mean time of the slowest hundredth of the kept frames, one frame at least

class PanelModel                                         // The settings panel's content with no drawing, pages of rows steered by six inputs, so arrows and a pad serve alike
PanelModel(Registry registry, string title = "Settings");  // Makes a panel over a registry's settings
bool Open { get; set; }                                  // Whether the panel is shown, which the panel's key turns over
PanelPage Root { get; }                                  // The host's own rows of the first page, under the pages of settings
string Title { get; }                                    // The shown page's title
int Selected { get; }                                    // The index of the selected row among Rows, or -1 where none can be
Answer? Answer { get; }                                  // What the last accept or step answered, a refusal's reason among it, or null before any
IReadOnlyList<PanelRow> Rows { get; }                    // The shown page's rows
void Press(PanelInput input);                            // Takes an input

class PanelPage                                          // A page of the settings panel, a title and the rows a host gives it
PanelPage(string title);
string Title { get; }                                    // The page's title
PanelPage Add(Setting setting, string? label = null);    // Adds a setting's row
PanelPage AddAction(string label, string line);          // Adds a row that runs a command line on accept
PanelPage AddPage(PanelPage page);                       // Adds a row that opens another page on accept
PanelPage AddText(string text);                          // Adds a line of text

record PanelRow                                          // A row of the settings panel as it is drawn
PanelRow(PanelRowKind Kind, string Label, string Value, bool Selected, bool Pending, bool Overridden);
PanelRowKind Kind { get; init; }                         // What the row is
string Label { get; init; }                              // The row's words on the left
string Value { get; init; }                              // A setting's value as the player set it, on the right, and empty for another row
bool Selected { get; init; }                             // Whether the row is the one the inputs act on
bool Pending { get; init; }                              // Whether the value takes effect at the next start
bool Overridden { get; init; }                           // Whether the environment holds the running value, the one shown being kept for the file

enum PanelRowKind                                        // What a row of the settings panel is
Setting = 0,                                             // A setting, its value stepped with left and right
Action = 1,                                              // A command line run on accept
Page = 2,                                                // A page opened on accept
Text = 3,                                                // A line of text, which is never selected

enum PanelInput                                          // What steers the settings panel, the same six from arrows and from a pad
Up = 0,                                                  // To the row above, past a row of text, staying at the first
Down = 1,                                                // To the row below, past a row of text, staying at the last
Left = 2,                                                // A setting's value one step down or to the choice before
Right = 3,                                               // A setting's value one step up or to the next choice
Accept = 4,                                              // Opens a page, runs an action, or turns a toggle or a choice to its next
Back = 5,                                                // Back to the page before, and from the first page out of the panel

class DiagnosticKeys                                     // The five keys as actions a player rebinds, each a text setting holding a key's name, and what they turn over
DiagnosticKeys(Registry registry);                       // Makes the keys and adds their five settings to a registry
OverlayModel? Overlay { get; init; }                     // The overlay the overlay's key turns over, or null for none
PanelModel? Panel { get; init; }                         // The panel the panel's key turns over, or null for none
ConsoleModel? Console { get; init; }                     // The console the console's key turns over, or null for none
bool InterfaceHidden { get; set; }                       // Whether the program's interface is hidden, which a host reads before it draws it
Setting<string> KeyOf(KeyAction action);                 // The setting that holds an action's key
void KeyDown(string key, bool typing = false);           // Takes a key that went down
KeyAction? KeyUp(string key);                            // Takes a key that came up, and gives the action it took, or null
void Take(KeyAction action, bool shift = false);         // Takes an action with no key, as a pad's button or a command does
event Action? ScreenshotAsked;                           // Raised when a screenshot is asked for, which the host takes its own way

enum KeyAction                                           // What one of the five keys does, the same on every host
HideInterface = 0,                                       // Hides and shows the program's interface, F1 by default
Screenshot = 1,                                          // Asks the host for a screenshot, F2 by default
Overlay = 2,                                             // Shows and hides the debug overlay, with Shift its detail, F3 by default
Panel = 3,                                               // Opens and closes the settings panel, F4 by default
Console = 4,                                             // Opens and closes the console, F5 by default

class SettingsFile                                       // The settings kept across runs in one text file, a line for each setting whose value differs from its default
SettingsFile(string path);                               // Makes the file at a path, which is read and written when asked
string Path { get; }                                     // The file's path
SettingsFile For(string game);                           // The file of a game in the player's folder, settings.txt in a folder of the game's name under the system's folder of applications' data
IReadOnlyList<string> Read(Registry registry);           // Reads the file into a registry's settings as a start does, and gives each line it refused with its reason
bool Write(Registry registry);                           // Writes the file from a registry's settings where its text would change, and says whether it wrote
string Text(Registry registry);                          // The text Write writes for a registry's settings
```

## The ImGui library

Every public type of `3DEngine.Diagnostics.ImGui`, the second library, which draws the console and
the panel for a host that has an ImGui context. Its list is written by a second call of
`Cheatsheet.Write`, which a test of the module makes, and stands last on the page.

<!-- written by a second call of Cheatsheet.Write from the built ImGui library -->
```csharp
class ImGuiConsole                                       // Draws a ConsoleModel with Dear ImGui, in a host's ImGui frame
ImGuiConsole(ConsoleModel model);
float Height { get; set; }                               // The share of the host's window's height the console takes, 0.4 by default
bool Typing { get; }                                     // Whether the console's field had the keyboard when it was last drawn, which a host gives its key reading as typing
void Draw(Vector2 window);                               // Draws the console where its model is open, and nothing where it is not

class ImGuiPanel                                         // Draws a PanelModel with Dear ImGui, in a host's ImGui frame
ImGuiPanel(PanelModel model);
bool ReadsKeys { get; set; }                             // Whether the panel reads its six inputs from ImGui's keys while it is open, which it does by default
float Width { get; set; }                                // The panel's width in ImGui's units, 520 by default
void Draw(Vector2 window);                               // Draws the panel where its model is open, and nothing where it is not
```
