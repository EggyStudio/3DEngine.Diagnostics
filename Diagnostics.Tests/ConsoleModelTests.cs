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
        registry.Add(new Setting<bool>("sound.vsync", "Whether sound waits for the display.", false));
        registry.Add(new Setting<string>("gfx.render", "Which renderer draws.", "main"));
        return (new ConsoleModel(registry), registry);
    }

    private static string[] Eight() => Enumerable.Range(0, 8).Select(i => "a.n" + i).ToArray();

    private static ConsoleModel MakeEight()
    {
        var registry = new Registry();
        foreach (var name in Eight()) registry.Add(new Setting<int>(name, "A number.", 0));
        return new ConsoleModel(registry);
    }

    [Fact]
    public void A_Line_In_Gives_Its_Echo_And_Its_Answers_Lines_Out_As_One_Plain_List()
    {
        var (console, _) = Make();
        Assert.True(console.Submit(" render.shot ").Done);
        Assert.False(console.Submit("render.show x").Done);
        console.Write(ConsoleLevel.Warning, "The shader cache is cold.");
        Assert.Equal(
            [
                new("> render.shot", ConsoleLevel.Input, Index: 0), new("written", ConsoleLevel.Info, Index: 1),
                new("two lines", ConsoleLevel.Info, Index: 2), new("> render.show x", ConsoleLevel.Input, Index: 3),
                new("No such pass.", ConsoleLevel.Error, Index: 4), new("The shader cache is cold.", ConsoleLevel.Warning, Index: 5),
            ],
            console.Lines());
        Assert.Equal(6, console.Written);
        console.Clear();
        Assert.Empty(console.Lines());
    }

    [Fact]
    public void A_Line_A_Host_Writes_Again_Is_One_Line_With_A_Count_And_Written_Counts_Each()
    {
        var console = new ConsoleModel(new Registry());
        for (var i = 0; i < 3; i++) console.Write(ConsoleLevel.Warning, "The shader cache is cold.");
        console.Write(ConsoleLevel.Error, "The shader cache is cold.");
        console.Write(ConsoleLevel.Info, "Ready.");
        console.Write(ConsoleLevel.Info, "Ready.");
        console.Write(ConsoleLevel.Warning, "The shader cache is cold.");

        // The same text at another level is another line, and a line that came between breaks the
        // run.
        Assert.Equal(
            [
                new ConsoleLine("The shader cache is cold.", ConsoleLevel.Warning, Count: 3, Index: 0),
                new ConsoleLine("The shader cache is cold.", ConsoleLevel.Error, Count: 1, Index: 3),
                new ConsoleLine("Ready.", ConsoleLevel.Info, Count: 2, Index: 4),
                new ConsoleLine("The shader cache is cold.", ConsoleLevel.Warning, Count: 1, Index: 6),
            ],
            console.Lines());
        Assert.Equal(7, console.Written);
    }

    [Fact]
    public void A_Line_Entered_And_Its_Answer_Never_Collapse_Nor_Does_A_Written_Line_Onto_An_Answer()
    {
        var (console, _) = Make();
        console.Submit("render.shot");
        console.Submit("render.shot");
        console.Write(ConsoleLevel.Info, "two lines");
        console.Write(ConsoleLevel.Input, "> render.shot");
        console.Write(ConsoleLevel.Input, "> render.shot");

        Assert.Equal(9, console.Lines().Count);
        Assert.All(console.Lines(), line => Assert.Equal(1, line.Count));
        Assert.Equal(Enumerable.Range(0, 9).Select(i => (long)i), console.Lines().Select(line => line.Index));
        Assert.Equal(9, console.Written);
    }

    [Fact]
    public void A_Collapsed_Line_Keeps_Its_Index_When_Older_Lines_Are_Dropped()
    {
        var console = new ConsoleModel(new Registry(), depth: 2);
        console.Write(ConsoleLevel.Info, "a");
        console.Write(ConsoleLevel.Info, "b");
        console.Write(ConsoleLevel.Info, "b");
        console.Write(ConsoleLevel.Info, "c");
        Assert.Equal([("b", 2, 1L), ("c", 1, 3L)], console.Lines().Select(line => (line.Text, line.Count, line.Index)));
    }

    [Fact]
    public void Each_Line_Kept_Is_Told_To_A_Subscriber_With_Whether_It_Repeated_The_Last_Entered_And_Answers_Too()
    {
        var (console, _) = Make();
        var told = new List<(string Text, int Count, bool Repeated)>();
        console.LineWritten += (line, repeated) => told.Add((line.Text, line.Count, repeated));

        console.Write(ConsoleLevel.Warning, "cold");
        console.Write(ConsoleLevel.Warning, "cold");
        console.Write(ConsoleLevel.Info, "a\nb");
        console.Submit("render.shot");

        Assert.Equal(
            [
                ("cold", 1, false), ("cold", 2, true), ("a", 1, false), ("b", 1, false),
                ("> render.shot", 1, false), ("written", 1, false), ("two lines", 1, false),
            ],
            told);
    }

    [Fact]
    public void A_Subscriber_Is_Called_Outside_The_Lock_On_The_Writing_Thread()
    {
        var console = new ConsoleModel(new Registry());
        var (called, answered, thread) = (0, false, -1);
        console.LineWritten += (_, _) =>
        {
            called++;
            thread = Environment.CurrentManagedThreadId;
            // Another thread reads the lines while the subscriber runs, which waits for the lock if
            // it is held.
            answered = Task.Run(() => console.Lines().Count).Wait(TimeSpan.FromSeconds(5));
        };

        console.Write(ConsoleLevel.Info, "a");
        Assert.True(answered);
        Assert.Equal((1, Environment.CurrentManagedThreadId), (called, thread));
    }

    [Fact]
    public void A_Subscribers_Exception_Reaches_The_Writer_After_The_Line_Is_Kept()
    {
        var console = new ConsoleModel(new Registry());
        console.LineWritten += (_, _) => throw new InvalidOperationException("The log file is full.");

        Assert.Throws<InvalidOperationException>(() => console.Write(ConsoleLevel.Info, "a"));
        Assert.Equal(["a"], console.Lines().Select(line => line.Text));
        Assert.Equal(1, console.Written);
    }

    [Fact]
    public void A_Line_Is_Stamped_By_The_Hosts_Function_And_A_Repeat_Takes_The_Latest_Stamp()
    {
        long frame = 10;
        var console = new ConsoleModel(new Registry(), stamp: () => frame);
        var told = new List<long>();
        console.LineWritten += (line, _) => told.Add(line.Stamp);

        console.Write(ConsoleLevel.Warning, "cold");
        frame = 12;
        console.Write(ConsoleLevel.Warning, "cold");
        console.Write(ConsoleLevel.Info, "a\nb");

        // The first writing keeps its index and the repeat moves the stamp, and both lines of one
        // writing share a stamp.
        Assert.Equal([(0L, 12L), (2L, 12L), (3L, 12L)], console.Lines().Select(line => (line.Index, line.Stamp)));
        Assert.Equal([10L, 12L, 12L, 12L], told);
    }

    [Fact]
    public void A_Console_With_No_Stamp_Function_Stamps_Zero_And_The_Function_Is_Called_Outside_The_Lock()
    {
        var plain = new ConsoleModel(new Registry());
        plain.Write(ConsoleLevel.Info, "a");
        Assert.Equal(0, plain.Lines()[0].Stamp);

        ConsoleModel? console = null;
        var answered = false;
        console = new ConsoleModel(new Registry(), stamp: () =>
        {
            answered = Task.Run(() => console!.Lines().Count).Wait(TimeSpan.FromSeconds(5));
            return 1;
        });
        console.Write(ConsoleLevel.Info, "a");
        Assert.True(answered);
        Assert.Equal(1, console.Lines()[0].Stamp);
    }

    [Fact]
    public void A_Line_Run_Is_Told_With_Its_Answer_Whether_Submitted_Or_Entered_And_Failures_Carry_Their_Exception()
    {
        var (console, registry) = Make();
        registry.Add(new Command("render.boom", "Throws.", "render.boom", _ => throw new InvalidOperationException("No world is lent.")));
        var run = new List<(string Line, string? Code, bool Thrown)>();
        var order = new List<string>();
        console.LineRun += (line, answer) =>
        {
            run.Add((line, answer.Code, answer.Error is InvalidOperationException));
            order.Add(console.Lines()[^1].Text);
        };

        console.Submit("  render.shot ");
        console.Typed = "render.boom";
        console.Enter();
        console.Submit("nothing");
        console.Submit("   ");

        // The entered line is told as it was trimmed, the blank line is no line run, and each is
        // told after its echo and its answer are written.
        Assert.Equal(
            [("render.shot", null, false), ("render.boom", Answer.Failed, true), ("nothing", Answer.NoSuchCommand, false)],
            run);
        Assert.Equal(["two lines", "render.boom failed. No world is lent.", "No command and no setting is named nothing."], order);
    }

    [Fact]
    public void A_Subscriber_Of_A_Line_Run_Is_Called_Outside_The_Lock_And_Its_Exception_Reaches_The_Caller()
    {
        var (console, _) = Make();
        var answered = false;
        console.LineRun += (_, _) =>
        {
            answered = Task.Run(() => console.Lines().Count).Wait(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("The log file is full.");
        };

        Assert.Throws<InvalidOperationException>(() => console.Submit("render.shot"));
        Assert.True(answered);
        Assert.Equal(3, console.Lines().Count);
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
    public void The_Candidates_Are_The_Names_That_Begin_With_The_Word_And_Then_Those_At_A_Later_Part()
    {
        var (console, _) = Make();

        console.Edit("re");
        // The names that begin with re, in order, then the one whose later part does.
        Assert.Equal(
            ["render.quality", "render.shot", "render.show", "render.vsync", "reset", "gfx.render"],
            console.Candidates);

        console.Edit("vsync");
        Assert.Equal(["render.vsync", "sound.vsync"], console.Candidates);

        console.Edit("render.s");
        Assert.Equal(["render.shot", "render.show"], console.Candidates);

        // A name the word spells out is complete, and a blank line has none.
        console.Edit("render.vsync");
        Assert.Empty(console.Candidates);
        console.Edit("RENDER.VS");
        Assert.Equal(["render.vsync"], console.Candidates);
        console.Edit("  ");
        Assert.Empty(console.Candidates);
        Assert.Equal(-1, console.SelectedIndex);
    }

    [Fact]
    public void A_Settings_Choices_And_A_Command_Argument_Of_The_Kind_Name_Complete_Too()
    {
        var (console, registry) = Make();
        registry.Add(new Command("explain", "Says what a name does.", "explain <name>", _ => new Answer("")) { Arguments = [new("name", "name")] });

        console.Edit("render.quality ");
        Assert.Equal(["Low", "Medium", "High"], console.Candidates);
        console.Edit("render.quality m");
        Assert.Equal(["Medium"], console.Candidates);
        console.Edit("render.vsync o");
        Assert.Equal(["off", "on"], console.Candidates);
        // A choice spelled out is complete, a second value has none and a command with no name
        // argument has none.
        console.Edit("render.vsync on");
        Assert.Empty(console.Candidates);
        console.Edit("render.vsync on o");
        Assert.Empty(console.Candidates);
        console.Edit("render.shot re");
        Assert.Empty(console.Candidates);

        console.Edit("explain vs");
        Assert.Equal(["render.vsync", "sound.vsync"], console.Candidates);
    }

    [Fact]
    public void The_Popup_Shows_Once_The_Player_Edited_And_Not_For_A_Line_Set_By_The_Program()
    {
        var (console, _) = Make();

        console.Typed = "render.s";
        Assert.Equal(["render.shot", "render.show"], console.Candidates);
        Assert.False(console.PopupShown);
        Assert.Equal("", console.Ghost);

        console.Edit("render.sh");
        Assert.True(console.PopupShown);
        console.Edit("zzz");
        Assert.False(console.PopupShown);

        // The same text again is no edit.
        console.Edit("render.sh");
        console.Edit("render.sh");
        Assert.Equal(0, console.SelectedIndex);
        console.Navigate(1);
        console.Edit("render.sh");
        Assert.Equal(1, console.SelectedIndex);
    }

    [Fact]
    public void The_Selection_Moves_With_Up_And_Down_Wraps_And_Keeps_Six_Rows_In_View()
    {
        var console = MakeEight();
        console.Edit("a.n");
        Assert.Equal((8, 0, 0), (console.Candidates.Count, console.SelectedIndex, console.FirstVisible));

        // Up from the first wraps to the last, which scrolls the six rows to the end.
        console.Navigate(-1);
        Assert.Equal((7, 2), (console.SelectedIndex, console.FirstVisible));
        console.Navigate(-1);
        Assert.Equal((6, 2), (console.SelectedIndex, console.FirstVisible));

        // Down from the last wraps to the first, which scrolls back.
        console.Navigate(1);
        console.Navigate(1);
        Assert.Equal((0, 0), (console.SelectedIndex, console.FirstVisible));

        // Down through the rows moves the window one row at a time past the sixth.
        for (var i = 0; i < 5; i++) console.Navigate(1);
        Assert.Equal((5, 0), (console.SelectedIndex, console.FirstVisible));
        console.Navigate(1);
        Assert.Equal((6, 1), (console.SelectedIndex, console.FirstVisible));
        console.Navigate(-1);
        console.Navigate(-1);
        Assert.Equal((4, 1), (console.SelectedIndex, console.FirstVisible));
        Assert.Equal(6, ConsoleModel.PopupRows);

        // A new edit starts again at the first.
        console.Edit("a.");
        Assert.Equal((0, 0), (console.SelectedIndex, console.FirstVisible));
    }

    [Fact]
    public void The_Ghost_Is_The_Rest_Of_The_Selected_Candidate_Where_It_Begins_With_The_Word()
    {
        var (console, _) = Make();

        console.Edit("render.s");
        Assert.Equal("hot", console.Ghost);
        console.Navigate(1);
        Assert.Equal("how", console.Ghost);
        console.Navigate(1);
        Assert.Equal("hot", console.Ghost);

        // The case the player typed stays and the rest is the candidate's.
        console.Edit("RENDER.V");
        Assert.Equal("sync", console.Ghost);

        // A candidate that matches at a later part begins with no word, so it has no ghost.
        console.Edit("vsync");
        Assert.Equal("render.vsync", console.Candidates[0]);
        Assert.Equal("", console.Ghost);

        // After a setting's name the ghost is the first choice.
        console.Edit("render.quality ");
        Assert.Equal("Low", console.Ghost);
        console.Edit("render.quality h");
        Assert.Equal("igh", console.Ghost);
    }

    [Fact]
    public void Tab_Fills_The_Selected_Candidate_Whether_Or_Not_It_Begins_With_The_Word()
    {
        var (console, _) = Make();

        console.Edit("vsync");
        Assert.True(console.Accept());
        Assert.Equal("render.vsync ", console.Typed);
        Assert.False(console.PopupShown);

        console.Edit("sound.v");
        console.Navigate(1);
        console.Edit("vsync");
        console.Navigate(1);
        Assert.Equal(1, console.SelectedIndex);
        console.Accept();
        Assert.Equal("sound.vsync ", console.Typed);

        // The word being typed is the last one, and what comes before it stays.
        console.Edit("render.quality m");
        console.Accept();
        Assert.Equal("render.quality Medium ", console.Typed);
        console.Edit("render.vsync ");
        Assert.Equal("off", console.Ghost);

        // Nothing to fill, nothing changes.
        console.Edit("zzz");
        Assert.False(console.Accept());
        Assert.Equal("zzz", console.Typed);
    }

    [Fact]
    public void Tab_Fills_A_Line_Recalled_From_The_History_Whose_Popup_Is_Hidden()
    {
        var (console, _) = Make();
        console.Typed = "render.s";
        Assert.False(console.PopupShown);
        Assert.True(console.Accept());
        Assert.Equal("render.shot ", console.Typed);
    }

    [Fact]
    public void Up_And_Down_Walk_The_History_Where_No_Popup_Shows_And_Keep_The_Line_Being_Entered()
    {
        var (console, _) = Make();
        console.Navigate(-1);
        Assert.Equal("", console.Typed);

        console.Typed = "render.vsync";
        console.Enter();
        console.Typed = "render.shot";
        console.Enter();
        console.Typed = "render.shot";
        console.Enter();
        Assert.Equal(["render.vsync", "render.shot"], console.History);
        Assert.Equal("", console.Typed);

        // The unfinished line comes back past the last entry, and no popup shows on the way.
        console.Edit("sound");
        Assert.True(console.PopupShown);
        console.Navigate(1);
        console.Navigate(1);
        Assert.Equal("sound", console.Typed);
        console.Typed = "sou";
        console.Navigate(-1);
        Assert.Equal(("render.shot", false), (console.Typed, console.PopupShown));
        console.Navigate(-1);
        Assert.Equal("render.vsync", console.Typed);
        console.Navigate(-1);
        Assert.Equal("render.vsync", console.Typed);
        console.Navigate(1);
        Assert.Equal("render.shot", console.Typed);
        console.Navigate(1);
        Assert.Equal("sou", console.Typed);
        console.Navigate(1);
        Assert.Equal("sou", console.Typed);
    }

    [Fact]
    public void While_The_Popup_Shows_Up_And_Down_Leave_The_History_Alone()
    {
        var (console, _) = Make();
        console.Typed = "render.vsync";
        console.Enter();

        console.Edit("render.s");
        console.Navigate(-1);
        Assert.Equal(("render.s", 1), (console.Typed, console.SelectedIndex));
        console.Navigate(1);
        Assert.Equal(("render.s", 0), (console.Typed, console.SelectedIndex));

        // Once the popup is gone, up recalls.
        console.Edit("zzz");
        console.Navigate(-1);
        Assert.Equal("render.vsync", console.Typed);
    }

    [Fact]
    public void Enter_Runs_The_Typed_Line_Empties_It_And_Hides_The_Popup()
    {
        var (console, _) = Make();
        console.Edit("render.vsync off");
        var answer = console.Enter();
        Assert.True(answer.Done);
        Assert.Equal(("", false, -1), (console.Typed, console.PopupShown, console.SelectedIndex));
        Assert.Equal("> render.vsync off", console.Lines()[0].Text);

        // A line handed over from elsewhere leaves the typing alone.
        console.Edit("render.sh");
        console.Submit("render.shot");
        Assert.Equal(("render.sh", true), (console.Typed, console.PopupShown));
    }

    [Fact]
    public void The_Hint_Is_A_Commands_Usage_Or_A_Setting_In_A_Sentence()
    {
        var (console, _) = Make();
        console.Typed = "render.shot a.png";
        Assert.Equal("render.shot [<path>]", console.Hint);
        console.Typed = "render.vsync";
        Assert.Equal("render.vsync is on.", console.Hint);
        console.Typed = "render.q";
        Assert.Equal("render.quality is Medium.", console.Hint);
        console.Typed = "render.s";
        Assert.Equal("", console.Hint);
        console.Typed = "";
        Assert.Equal("", console.Hint);
    }
}
