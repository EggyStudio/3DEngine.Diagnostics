using Xunit;

namespace Diagnostics.Tests;

public class RegistryTests
{
    private static Command Echo(string name = "echo") =>
        new(name, "Answers with its words.", name + " <words>...", line => new Answer(string.Join("|", line.Words)));

    [Fact]
    public void A_Name_Twice_Is_Refused_Whether_Command_Or_Setting()
    {
        var registry = new Registry();
        registry.Add(Echo());
        registry.Add(new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true));

        var twice = Assert.Throws<ArgumentException>(() => registry.Add(Echo()));
        Assert.Contains("echo is taken by a command", twice.Message);
        var asSetting = Assert.Throws<ArgumentException>(() => registry.Add(new Setting<int>("echo", "A number.", 1)));
        Assert.Contains("echo is taken by a command", asSetting.Message);
        var asCommand = Assert.Throws<ArgumentException>(() => registry.Add(Echo("render.vsync")));
        Assert.Contains("render.vsync is taken by a setting", asCommand.Message);
    }

    [Theory]
    [InlineData("render.vsync", true)]
    [InlineData("a", true)]
    [InlineData("shadow_2.far", true)]
    [InlineData("", false)]
    [InlineData("Render.vsync", false)]
    [InlineData("render..vsync", false)]
    [InlineData("render.", false)]
    [InlineData(".render", false)]
    [InlineData("2d.on", false)]
    [InlineData("render vsync", false)]
    public void A_Name_Is_Lowercase_Words_Joined_By_Dots(string name, bool isName)
    {
        Assert.Equal(isName, Registry.IsName(name));
        if (!isName) Assert.Throws<ArgumentException>(() => new Registry().Add(Echo(name)));
    }

    [Theory]
    [InlineData("a b  c", new[] { "a", "b", "c" })]
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData("say \"a \\\"b\\\" c\"", new[] { "say", "a \"b\" c" })]
    [InlineData("path \"C:\\\\dir\\\\\" x", new[] { "path", "C:\\dir\\", "x" })]
    [InlineData("a\\b", new[] { "a\\b" })]
    [InlineData("\"\" x", new[] { "", "x" })]
    [InlineData("pre\"fix ed\"", new[] { "prefix ed" })]
    [InlineData("  \t ", new string[0])]
    public void A_Line_Is_Split_At_Blanks_With_Quotes_Kept_Whole(string text, string[] words)
    {
        Assert.Equal(words, CommandLine.Split(text));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("two words")]
    [InlineData("")]
    [InlineData("a \"quoted\" word")]
    [InlineData("back\\slash\\")]
    [InlineData("tab\there")]
    public void A_Quoted_Word_Reads_Back_As_One_Word(string word)
    {
        Assert.Equal([word], CommandLine.Split(CommandLine.Quote(word)));
    }

    [Fact]
    public void A_Line_Runs_Its_Command_With_Its_Words_And_Its_Rest_As_Typed()
    {
        var registry = new Registry();
        registry.Add(Echo());
        registry.Add(new Command("whole", "Answers with its line.", "whole <line>", line => new Answer(line.Rest)));

        Assert.Equal("a b|c", registry.Run("  echo \"a b\"   c ").Text);
        Assert.Equal("\"a b\"   c", registry.Run("whole \"a b\"   c").Text);
        Assert.True(registry.Run("echo").Done);
        Assert.Equal("", registry.Run("   ").Text);
    }

    [Theory]
    [InlineData("script.run \"1 + 2\"", "1 + 2")]
    [InlineData("script.run 1 + 2", "1 + 2")]
    [InlineData("script.run \"Print(\\\"a b\\\")\"", "Print(\"a b\")")]
    [InlineData("script.run \"a\\\\b\"", "a\\b")]
    [InlineData("script.run \"C:\\dir x\"", "C:\\dir x")]
    [InlineData("script.run \"a\" b", "\"a\" b")]
    [InlineData("script.run \"a \"b\" c\"", "\"a \"b\" c\"")]
    [InlineData("script.run \"abc", "\"abc")]
    [InlineData("script.run \"abc\\\"", "\"abc\\\"")]
    [InlineData("script.run abc\"", "abc\"")]
    [InlineData("script.run \"\"", "")]
    [InlineData("script.run", "")]
    [InlineData("script.run   ", "")]
    public void The_Rest_As_One_Text_Loses_One_Pair_Of_Quotes_Around_It_And_Is_Else_As_Typed(string line, string text)
    {
        Assert.Equal(text, CommandLine.Parse(line).Text);
    }

    [Fact]
    public void An_Unknown_Name_And_A_Throwing_Command_Answer_With_Their_Codes()
    {
        var registry = new Registry();
        registry.Add(new Command("boom", "Throws.", "boom", _ => throw new InvalidOperationException("No world is lent.")));
        registry.Add(new Command("mine", "Fails with the host's code.", "mine", _ => Answer.Fail("NO_SUCH_ENTITY", "No entity 7.")));

        var unknown = registry.Run("nothing here");
        Assert.Equal((Answer.NoSuchCommand, "No command and no setting is named nothing."), (unknown.Code, unknown.Text));
        var thrown = registry.Run("boom");
        Assert.Equal((Answer.Failed, "boom failed. No world is lent."), (thrown.Code, thrown.Text));
        Assert.Equal("NO_SUCH_ENTITY", registry.Run("mine").Code);
        Assert.False(registry.Run("mine").Done);
    }

    [Fact]
    public void A_Throwing_Commands_Answer_Carries_The_Exception_And_No_Other_Answer_Does()
    {
        var registry = new Registry();
        var fault = new InvalidOperationException("No world is lent.");
        registry.Add(new Command("boom", "Throws.", "boom", _ => throw fault));
        registry.Add(new Command("mine", "Fails with the host's code.", "mine", _ => Answer.Fail("NO_SUCH_ENTITY", "No entity 7.")));
        registry.Add(new Setting<int>("a.n", "A number.", 1) { Low = 0, High = 3 });

        Assert.Same(fault, registry.Run("boom").Error);
        Assert.Null(registry.Run("mine").Error);
        Assert.Null(registry.Run("nothing").Error);
        Assert.Null(registry.Run("a.n 9").Error);
        Assert.Null(registry.Run("a.n 2").Error);
        Assert.Null(registry.Run("help").Error);

        // Two answers of one text and code differ by their exceptions, which compare by reference.
        Assert.NotEqual(registry.Run("boom"), new Answer("boom failed. No world is lent.", Answer.Failed, new InvalidOperationException("No world is lent.")));
        Assert.Equal(registry.Run("boom"), new Answer("boom failed. No world is lent.", Answer.Failed, fault));
    }

    [Fact]
    public void A_Settings_Name_Reads_It_And_A_Value_After_It_Sets_It()
    {
        var registry = new Registry();
        var scale = new Setting<float>("render.scale", "The share of the window's size a frame is drawn at.", 1) { Low = 0.5, High = 2 };
        var title = new Setting<string>("window.title", "The window's title.", "Game");
        registry.Add(scale);
        registry.Add(title);

        Assert.Equal("render.scale is 1.", registry.Run("render.scale").Text);
        Assert.True(registry.Run("render.scale 1.5").Done);
        Assert.Equal(1.5f, scale.Value);
        Assert.Equal("render.scale is 1.5, 1 by default.", registry.Run("render.scale").Text);

        registry.Run("window.title My game");
        Assert.Equal("My game", title.Value);
        registry.Run("window.title \"quoted  twice\"");
        Assert.Equal("quoted  twice", title.Value);

        Assert.True(registry.Run("reset render.scale").Done);
        Assert.Equal(1f, scale.Value);
        Assert.Equal(Answer.NoSuchCommand, registry.Run("reset render.none").Code);
    }

    [Fact]
    public void Help_And_List_Name_Commands_And_Settings()
    {
        var registry = new Registry();
        registry.Add(Echo("entity.echo"));
        registry.Add(new Setting<int>("entity.limit", "How many entities a scene holds.", 100) { Low = 1, High = 5000, AtNextStart = true });

        Assert.Equal("entity.echo\nentity.limit 100", registry.Run("list entity.").Text);
        Assert.Equal("entity.echo <words>...\nAnswers with its words.", registry.Run("help entity.echo").Text);
        Assert.Equal(
            "entity.limit is 100.\nHow many entities a scene holds.\nIt takes 1 to 5000.\nA set takes effect at the next start.",
            registry.Run("help entity.limit").Text);
        Assert.Contains("help  Lists the commands", registry.Run("help").Text);
        Assert.Equal(["entity.echo", "entity.limit"], registry.Starting("entity"));
        Assert.Equal(["entity.echo", "help", "list", "reset"], registry.Commands.Select(command => command.Name));
    }

    [Fact]
    public void A_Removed_Name_Is_Free_Again()
    {
        var registry = new Registry();
        Assert.True(registry.Remove("help"));
        Assert.False(registry.Remove("help"));
        registry.Add(new Command("help", "The host's own help.", "help", _ => new Answer("mine")));
        Assert.Equal("mine", registry.Run("help").Text);
    }

    [Fact]
    public async Task Commands_Are_Added_From_Many_Threads_At_Once()
    {
        var registry = new Registry();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                registry.Add(Echo($"t{thread}.c{i}"));
                Assert.NotNull(registry.FindCommand($"t{thread}.c{i}"));
                _ = registry.Starting("t");
            }
        })));
        Assert.Equal(1600, registry.Starting("t").Count);
    }

    [Fact]
    public void An_Event_Is_Raised_As_A_Setting_Is_Added_And_Not_As_A_Command_Is()
    {
        var registry = new Registry();
        var seen = new List<(string Name, bool Found)>();
        registry.SettingAdded += setting => seen.Add((setting.Name, registry.FindSetting(setting.Name) == setting));
        registry.Add(Echo("game.quit"));
        registry.Add(new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true));
        Assert.Throws<ArgumentException>(() => registry.Add(new Setting<bool>("render.vsync", "Again.", true)));
        Assert.Equal([("render.vsync", true)], seen);
    }
}
