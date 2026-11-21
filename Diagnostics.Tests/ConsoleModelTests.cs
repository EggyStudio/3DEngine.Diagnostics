using Xunit;

namespace Diagnostics.Tests;

public class ConsoleModelTests
{
    private enum Quality { Low, Medium, High }

    private static (ConsoleModel Console, Registry Registry) Make()
    {
        var registry = new Registry();
        registry.Add(new Command("render.shot", "Writes a picture of the frame.", "render.shot [<path>]", line => new Answer("written\ntwo lines")));
        registry.Add(new Command("render.show", "Shows a pass.", "render.show <pass>", _ => Answer.Fail(Answer.BadArgument, "No such pass.")));
        registry.Add(new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true));
        registry.Add(new Setting<Quality>("render.quality", "How fine the frame is drawn.", Quality.Medium));
        return (new ConsoleModel(registry), registry);
    }

    [Fact]
    public void A_Line_In_Gives_Its_Echo_And_Its_Answers_Lines_Out()
    {
        var (console, _) = Make();
        Assert.True(console.Submit(" render.shot ").Done);
        Assert.False(console.Submit("render.show x").Done);
        Assert.Equal(
            [
                new("> render.shot", ConsoleLevel.Input), new("written", ConsoleLevel.Info), new("two lines", ConsoleLevel.Info),
                new("> render.show x", ConsoleLevel.Input), new("No such pass.", ConsoleLevel.Error),
            ],
            console.Lines());
        Assert.Equal(5, console.Written);

        console.ShowInfo = false;
        Assert.Equal([new ConsoleLine("No such pass.", ConsoleLevel.Error)], console.Lines());
        console.ShowInfo = true;
        console.Search = "WRIT";
        Assert.Equal([new ConsoleLine("written", ConsoleLevel.Info)], console.Lines());
        console.Clear();
        Assert.Empty(console.Lines());
    }

    [Fact]
    public void The_Oldest_Lines_Are_Dropped_Past_The_Depth()
    {
        var console = new ConsoleModel(new Registry(), depth: 3);
        for (var i = 0; i < 5; i++) console.Write(ConsoleLevel.Info, $"line {i}");
        Assert.Equal(["line 2", "line 3", "line 4"], console.Lines().Select(line => line.Text));
        Assert.Equal(5, console.Written);
    }

    [Fact]
    public async Task Lines_Are_Written_From_Many_Threads_At_Once()
    {
        var console = new ConsoleModel(new Registry());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                console.Write(ConsoleLevel.Warning, $"{thread} {i}");
                _ = console.Lines();
            }
        })));
        Assert.Equal(800, console.Lines().Count);
        Assert.Equal(800, console.Written);
    }

    [Fact]
    public void The_History_Goes_Back_And_Comes_Forward_To_The_Line_Being_Entered()
    {
        var (console, _) = Make();
        Assert.Equal("draft", console.Back("draft"));
        console.Submit("render.vsync");
        console.Submit("render.shot");
        console.Submit("render.shot");
        Assert.Equal(["render.vsync", "render.shot"], console.History);

        Assert.Equal("render.shot", console.Back("ren"));
        Assert.Equal("render.vsync", console.Back("render.shot"));
        Assert.Equal("render.vsync", console.Back("render.vsync"));
        Assert.Equal("render.shot", console.Forward("render.vsync"));
        Assert.Equal("ren", console.Forward("render.shot"));
        Assert.Equal("ren", console.Forward("ren"));
    }

    [Fact]
    public void A_Typed_Start_Completes_By_Prefix()
    {
        var (console, _) = Make();

        var several = console.Complete("render.s");
        Assert.Equal("render.sho", several.Text);
        Assert.Equal(["render.shot", "render.show"], several.Candidates);

        var one = console.Complete("render.v");
        Assert.Equal(("render.vsync ", 1), (one.Text, one.Candidates.Count));

        var none = console.Complete("zzz");
        Assert.Equal(("zzz", 0), (none.Text, none.Candidates.Count));

        Assert.Equal(["help", "list", "render.quality", "render.shot", "render.show", "render.vsync", "reset"], console.Complete("").Candidates);
    }

    [Fact]
    public void A_Settings_Value_And_A_Name_After_Help_Complete_Too()
    {
        var (console, _) = Make();

        var choice = console.Complete("render.quality m");
        Assert.Equal(("render.quality Medium ", "Medium"), (choice.Text, Assert.Single(choice.Candidates)));
        Assert.Equal(["off", "on"], console.Complete("render.vsync o").Candidates);
        Assert.Equal("render.vsync o", console.Complete("render.vsync o").Text);
        Assert.Empty(console.Complete("render.vsync on o").Candidates);

        var named = console.Complete("help render.q");
        Assert.Equal("help render.quality ", named.Text);
        Assert.Empty(console.Complete("render.shot re").Candidates);
    }

    [Fact]
    public void The_Hint_Is_A_Commands_Usage_Or_A_Setting_In_A_Sentence()
    {
        var (console, _) = Make();
        Assert.Equal("render.shot [<path>]", console.Hint("render.shot a.png"));
        Assert.Equal("render.vsync is on.", console.Hint("render.vsync"));
        Assert.Equal("render.quality is Medium.", console.Hint("render.q"));
        Assert.Equal("", console.Hint("render.s"));
        Assert.Equal("", console.Hint(""));
    }
}
