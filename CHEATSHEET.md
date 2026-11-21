# Diagnostics cheatsheet

Every public type of `3DEngine.Diagnostics` with its members, a line each of its declaration and
the first sentence of its summary, in the order the README first names it. A host adds its
`Command`s and `Setting`s to a `Registry`, runs lines through a `ConsoleModel` over it and keeps the
settings in a `SettingsFile`. The README's section Binding it in your engine gives the conventions,
the calls safe from any thread among them, and the smallest binding. The list is written by
`Cheatsheet.Write` from the built library, and this text by hand.

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

enum SettingKind                                         // What a setting's value is, which decides how a console reads it and a panel steps it
Toggle = 0,                                              // On or off, a Boolean
Whole = 1,                                               // A whole number, an Int32
Number = 2,                                              // A number with a fraction, a Single or a Double
Choice = 3,                                              // One of an enum's names
Text = 4,                                                // Any text, a String

class ConsoleModel                                       // A console's content with no drawing, the lines it shows, the line being entered's history, completion and hint, over a Registry
int DefaultDepth = 2000;                                 // How many lines the console keeps by default, the oldest dropped first
ConsoleModel(Registry registry, int depth = 2000);       // Makes a console over a registry
bool Open { get; set; }                                  // Whether the console is shown, which the console's key turns over
bool ShowInfo { get; set; }                              // Whether Lines gives the plain lines and the entered ones
bool ShowWarnings { get; set; }                          // Whether Lines gives the warnings
bool ShowErrors { get; set; }                            // Whether Lines gives the errors
string Search { get; set; }                              // A text Lines keeps the lines holding it of, whatever its case
long Written { get; }                                    // How many lines were ever written, which a drawer compares to know that a line came
IReadOnlyList<string> History { get; }                   // The lines entered, oldest first, a line entered twice in a row held once
void Write(ConsoleLevel level, string text);             // Writes a text into the console, a line for each of its lines
IReadOnlyList<ConsoleLine> Lines();                      // The kept lines the filters let through, oldest first
void Clear();                                            // Drops every kept line
Answer Submit(string line);                              // Runs an entered line, writes it and its answer into the console and keeps it in the history
string Back(string current);                             // The line before the one shown in the history, as the up key asks for it
string Forward(string current);                          // The line after the one shown in the history, and past the last the line that was being entered
Completion Complete(string typed);                       // What the typed start of a line completes to, by prefix
string Hint(string typed);                               // What a console shows under the line being entered, a command's usage or a setting in a sentence, of the name typed or of the one name the typed start leaves

record struct ConsoleLine                                // One line a console shows
ConsoleLine(string Text, ConsoleLevel Level);
string Text { get; init; }                               // The line's text, with no line end
ConsoleLevel Level { get; init; }                        // What the line is

enum ConsoleLevel                                        // What a console's line is, which a console colors and filters by
Input = 0,                                               // A line the player entered, shown again above its answer
Info = 1,                                                // An answer or a host's plain line
Warning = 2,                                             // A host's warning
Error = 3,                                               // A failed line's answer or a host's error

record Completion                                        // What a typed start of a line completes to
Completion(string Text, IReadOnlyList<string> Candidates);
string Text { get; init; }                               // The line with the common start of every candidate in place of its last word, followed by a space where one candidate is left
IReadOnlyList<string> Candidates { get; init; }          // The names or the values the last word could be, in order

class SettingsFile                                       // The settings kept across runs in one text file, a line for each setting whose value differs from its default
SettingsFile(string path);                               // Makes the file at a path, which is read and written when asked
string Path { get; }                                     // The file's path
SettingsFile For(string game);                           // The file of a game in the player's folder, settings.txt in a folder of the game's name under the system's folder of applications' data
IReadOnlyList<string> Read(Registry registry);           // Reads the file into a registry's settings as a start does, and gives each line it refused with its reason
bool Write(Registry registry);                           // Writes the file from a registry's settings where its text would change, and says whether it wrote
string Text(Registry registry);                          // The text Write writes for a registry's settings
```
