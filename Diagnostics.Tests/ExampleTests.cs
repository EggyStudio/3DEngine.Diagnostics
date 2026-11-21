using Diagnostics.Example;
using Xunit;

namespace Diagnostics.Tests;

/// <summary>
/// The example program run with scripts of lines, its output held word for word and its settings
/// file read back by a second run.
/// </summary>
public sealed class ExampleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "diagnostics-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string Settings => Path.Combine(_folder, "settings.txt");

    // The variable is the example's alone and no other fact reads it, and each run sets it as the
    // run needs it, so a machine that has it set changes nothing.
    private (int Exit, string Output) Run(string[] lines, string input = "", string? style = null)
    {
        Environment.SetEnvironmentVariable(GreetingExample.StyleVariable, style);
        try
        {
            var output = new StringWriter { NewLine = "\n" };
            var exit = GreetingExample.Run(lines, new StringReader(input), output, _folder);
            return (exit, output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(GreetingExample.StyleVariable, null);
        }
    }

    [Fact]
    public void A_Script_Of_Lines_Runs_Prints_Its_Answers_And_Writes_The_File()
    {
        var (exit, output) = Run(
        [
            "greet.hello", "greet.style shout", "greet.repeat 2", "greet.hello Ada", "greet.sum 1 2 3.5",
            "greet.sum 1 x", "greet.repeat 9", "greet.echo  a  \"b\"", "greet.exclaim on", "list greet.", "nothing",
        ]);

        Assert.Equal(1, exit);
        Assert.Equal(
            """
            > greet.hello
            Hello, world.
            > greet.style shout
            greet.style is Shout.
            > greet.repeat 2
            greet.repeat is 2.
            > greet.hello Ada
            HELLO, ADA. HELLO, ADA.
            > greet.sum 1 2 3.5
            6.5
            > greet.sum 1 x
            ! x is no number.
            > greet.repeat 9
            ! greet.repeat takes 1 to 5, not 9.
            > greet.echo  a  "b"
            a  "b"
            > greet.exclaim on
            greet.exclaim is on at the next start.
            > list greet.
            greet.echo
            greet.exclaim off
            greet.hello
            greet.name world
            greet.repeat 2
            greet.style Shout
            greet.sum
            > nothing
            ! No command and no setting is named nothing.
            settings.txt written

            """.ReplaceLineEndings("\n"),
            output);

        // Only the three values that differ from their defaults are kept, the one for the next
        // start among them.
        Assert.Equal("greet.exclaim on\ngreet.repeat 2\ngreet.style Shout\n", File.ReadAllText(Settings));
    }

    [Fact]
    public void A_Second_Run_Reads_The_File_Back_And_Takes_Its_Lines_From_Standard_Input()
    {
        Run(["greet.style shout", "greet.repeat 2", "greet.exclaim on"]);

        var (exit, output) = Run([], input: "greet.hello\n\ngreet.exclaim\n");
        Assert.Equal(0, exit);
        Assert.Equal(
            """
            > greet.hello
            HELLO, WORLD! HELLO, WORLD!
            > greet.exclaim
            greet.exclaim is on, off by default.
            settings.txt unchanged

            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public void A_Value_The_File_Holds_And_A_Setting_Refuses_Is_Said_And_Left_Out_Of_The_File()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Settings, "greet.repeat 9\nmod.rain heavy\n");

        var (exit, output) = Run(["greet.hello"]);
        Assert.Equal(0, exit);
        Assert.Equal(
            """
            ! settings.txt line 1 is refused. greet.repeat takes 1 to 5, not 9.
            > greet.hello
            Hello, world.
            settings.txt written

            """.ReplaceLineEndings("\n"),
            output);
        // The name no setting has is kept for the run that has one.
        Assert.Equal("mod.rain heavy\n", File.ReadAllText(Settings));
    }

    [Fact]
    public void A_Setting_The_Environment_Overrides_Says_So_And_Keeps_The_Set_Value_For_The_File()
    {
        var (exit, output) = Run(["greet.hello", "greet.style", "greet.style shout", "greet.hello"], style: "whisper");
        Assert.Equal(0, exit);
        Assert.Equal(
            """
            > greet.hello
            hello, world.
            > greet.style
            greet.style is Whisper from the environment's GREETING_STYLE, Plain by default.
            > greet.style shout
            greet.style is kept as Shout, and the environment's GREETING_STYLE holds it at Whisper.
            > greet.hello
            hello, world.
            settings.txt written

            """.ReplaceLineEndings("\n"),
            output);
        Assert.Equal("greet.style Shout\n", File.ReadAllText(Settings));
    }
}
