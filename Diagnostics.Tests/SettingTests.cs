using Xunit;

namespace Diagnostics.Tests;

public class SettingTests
{
    private enum Quality { Low, Medium, High }

    [Fact]
    public void A_Number_Out_Of_Its_Range_Is_Refused_With_Its_Reason()
    {
        var scale = new Setting<float>("render.scale", "The share of the window's size a frame is drawn at.", 1) { Low = 0.5, High = 2 };

        var high = scale.TrySet("3");
        Assert.Equal((Answer.OutOfRange, "render.scale takes 0.5 to 2, not 3."), (high.Code, high.Text));
        Assert.Equal(Answer.OutOfRange, scale.TrySet(0.25f).Code);
        Assert.Equal(Answer.OutOfRange, scale.TrySet("NaN").Code);
        Assert.Equal(1f, scale.Value);

        var word = scale.TrySet("big");
        Assert.Equal((Answer.BadArgument, "render.scale takes 0.5 to 2, not big."), (word.Code, word.Text));
        Assert.True(scale.TrySet("2").Done);
        Assert.Equal(2f, scale.Value);

        var count = new Setting<int>("shadow.cascades", "How many cascades the sun's shadow has.", 4) { Low = 1 };
        Assert.Equal("shadow.cascades takes 1 or more, not 0.", count.TrySet("0").Text);
        Assert.Equal("shadow.cascades takes 1 or more, not 1.5.", count.TrySet("1.5").Text);
    }

    [Fact]
    public void A_Toggle_And_A_Choice_Read_Their_Words_Whatever_Their_Case()
    {
        var vsync = new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true);
        Assert.True(vsync.TrySet("OFF").Done);
        Assert.False(vsync.Value);
        Assert.Equal("off", vsync.Text);
        Assert.Equal("render.vsync takes one of off, on, not maybe.", vsync.TrySet("maybe").Text);

        var quality = new Setting<Quality>("render.quality", "How fine the frame is drawn.", Quality.Medium);
        Assert.Equal(SettingKind.Choice, quality.Kind);
        Assert.Equal(["Low", "Medium", "High"], quality.Choices);
        Assert.True(quality.TrySet("high").Done);
        Assert.Equal(Quality.High, quality.Value);
        Assert.Equal("High", quality.Text);
        // A number is no choice, though it is an enum's value.
        Assert.Equal(Answer.BadArgument, quality.TrySet("1").Code);
        Assert.Equal(Answer.BadArgument, quality.TrySet("7").Code);
    }

    [Fact]
    public void A_Number_Is_Text_In_The_Invariant_Culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var gamma = new Setting<double>("render.gamma", "The display's gamma.", 2.2);
            Assert.Equal("2.2", gamma.Text);
            Assert.True(gamma.TrySet("2.4").Done);
            Assert.Equal(2.4, gamma.Value);
            Assert.Equal(Answer.BadArgument, gamma.TrySet("2,4").Code);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_Hosts_Getter_And_Setter_Hold_The_Value()
    {
        var volume = 0.8f;
        var changes = 0;
        var setting = new Setting<float>("sound.volume", "How loud the game is.", 0.8f, () => volume, value => volume = value) { Low = 0, High = 1 };
        setting.Changed += _ => changes++;

        setting.TrySet("0.5");
        Assert.Equal(0.5f, volume);
        setting.TrySet("0.5");
        Assert.Equal(1, changes);

        // A value the host changes itself is the setting's, and the file's.
        volume = 0.25f;
        Assert.Equal("0.25", setting.Text);
        Assert.Equal("0.25", setting.StoredText);
    }

    [Fact]
    public void A_Setting_For_The_Next_Start_Keeps_Its_Running_Value()
    {
        var applied = new List<bool>();
        var meshlets = new Setting<bool>("render.meshlets", "Whether meshes are drawn as meshlets.", false, set: applied.Add) { AtNextStart = true };

        var answer = meshlets.TrySet("on");
        Assert.Equal("render.meshlets is on at the next start.", answer.Text);
        Assert.False(meshlets.Value);
        Assert.Equal("on", meshlets.PendingText);
        Assert.Equal("on", meshlets.StoredText);
        Assert.Empty(applied);
        Assert.Equal("render.meshlets is off, on at the next start.", meshlets.Describe());

        // Set back, nothing waits.
        meshlets.TrySet("off");
        Assert.Null(meshlets.PendingText);

        // As the program starts, the file's value is the running one.
        Assert.True(meshlets.TryStart("on").Done);
        Assert.True(meshlets.Value);
        Assert.Null(meshlets.PendingText);
        Assert.Equal([true], applied);
    }

    [Fact]
    public void A_Value_The_Environment_Overrides_Is_Shown_As_Such()
    {
        const string variable = "DIAGNOSTICS_TEST_VSYNC";
        Environment.SetEnvironmentVariable(variable, "off");
        try
        {
            var applied = new List<bool>();
            var vsync = new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true, set: applied.Add) { EnvironmentVariable = variable };
            var registry = new Registry();
            registry.Add(vsync);

            Assert.True(vsync.Overridden);
            Assert.False(vsync.Value);
            Assert.Equal([false], applied);
            Assert.Equal("render.vsync is off from the environment's DIAGNOSTICS_TEST_VSYNC, on by default.", registry.Run("render.vsync").Text);
            Assert.Equal("render.vsync off (environment)", registry.Run("list render.").Text);

            // A set is kept for the file and the running value stays the environment's.
            var answer = registry.Run("render.vsync on");
            Assert.True(answer.Done);
            Assert.Equal("render.vsync is kept as on, and the environment's DIAGNOSTICS_TEST_VSYNC holds it at off.", answer.Text);
            Assert.False(vsync.Value);
            Assert.Equal("on", vsync.StoredText);
            Assert.Equal([false], applied);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void An_Environments_Value_The_Setting_Refuses_Overrides_Nothing()
    {
        const string variable = "DIAGNOSTICS_TEST_SCALE";
        Environment.SetEnvironmentVariable(variable, "9");
        try
        {
            var scale = new Setting<float>("render.scale", "The share of the window's size a frame is drawn at.", 1) { Low = 0.5, High = 2, EnvironmentVariable = variable };
            Assert.False(scale.Overridden);
            Assert.Equal(1f, scale.Value);
            Assert.Equal("The environment's DIAGNOSTICS_TEST_SCALE is refused. render.scale takes 0.5 to 2, not 9.", scale.EnvironmentRefusal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void A_Type_No_Setting_Holds_Is_Refused()
    {
        Assert.Throws<NotSupportedException>(() => new Setting<DateTime>("clock.start", "When the clock starts.", default));
    }

    [Fact]
    public void A_Setting_That_Only_Reads_Is_Shown_With_Its_Value_And_Answers_A_Set_As_Not_Settable()
    {
        var registry = new Registry();
        var version = "1.4";
        var build = new Setting<string>("build.version", "The version the program was built as.", "dev", () => version)
        {
            ReadOnly = true,
            AtNextStart = true,
        };
        registry.Add(build);

        Assert.True(build.ReadOnly);
        Assert.Equal("build.version is 1.4, dev by default.", registry.Run("build.version").Text);
        Assert.Equal("1.4", build.StoredText);
        Assert.Null(build.PendingText);
        Assert.DoesNotContain("next start", registry.Run("help build.version").Text);
        Assert.Equal("build.version 1.4", registry.Run("list build").Text);

        var set = registry.Run("build.version 2.0");
        Assert.Equal((Answer.NotSettable, "build.version is read only."), (set.Code, set.Text));
        Assert.Equal(Answer.NotSettable, registry.Run("reset build.version").Code);
        Assert.Equal(Answer.NotSettable, build.TrySet("2.0").Code);
        Assert.Equal(Answer.NotSettable, build.TryStart("2.0").Code);
        Assert.Equal(Answer.NotSettable, build.Move(1).Code);
        Assert.Equal("1.4", build.Value);

        // The getter reads the program's own change, and no file keeps it.
        version = "1.5";
        Assert.Equal("1.5", build.Text);
    }

    [Fact]
    public void A_Text_Setting_Asks_Its_Offer_Afresh_Each_Time_And_Takes_A_Value_Outside_It()
    {
        var registry = new Registry();
        var sizes = new List<string> { "native", "1920x1080" };
        var asked = 0;
        var taken = new List<string>();
        var size = new Setting<string>("window.size", "The window's size.", "native", apply: value =>
        {
            if (value.Contains('!')) return "A size has no mark.";
            taken.Add(value);
            return null;
        })
        {
            Offer = () =>
            {
                asked++;
                return sizes;
            },
        };
        registry.Add(size);

        Assert.Equal(["native", "1920x1080"], size.Choices);
        sizes.Add("1280x720");
        Assert.Equal(["native", "1920x1080", "1280x720"], size.Choices);
        Assert.Equal(2, asked);
        Assert.Contains("any text, offered as native, 1920x1080, 1280x720", registry.Run("help window.size").Text);

        // A step goes to the next offered choice, wraps, and from a value outside the offer starts
        // at its first up or its last down.
        Assert.Equal("window.size is 1920x1080.", size.Move(1).Text);
        size.Move(1);
        Assert.Equal("1280x720", size.Text);
        size.Move(1);
        Assert.Equal("native", size.Text);
        size.Move(-1);
        Assert.Equal("1280x720", size.Text);
        Assert.True(registry.Run("window.size 1000x700").Done);
        Assert.Equal("1000x700", size.Text);
        size.Move(-1);
        Assert.Equal("1280x720", size.Text);
        registry.Run("window.size 1000x700");
        size.Move(1);
        Assert.Equal("native", size.Text);

        // A value outside the offer reaches apply, which refuses with its reason and leaves the
        // setting as it was.
        var refused = registry.Run("window.size big!");
        Assert.Equal((Answer.Refused, "A size has no mark."), (refused.Code, refused.Text));
        Assert.Equal("native", size.Text);
        Assert.Equal(["1920x1080", "1280x720", "native", "1280x720", "1000x700", "1280x720", "1000x700", "native"], taken);
    }

    [Fact]
    public void A_Text_Setting_With_No_Offer_Or_An_Empty_One_Has_No_Choices_And_No_Step()
    {
        var plain = new Setting<string>("player.name", "The name.", "Ada");
        var empty = new Setting<string>("player.title", "The title.", "", () => "") { Offer = () => [] };
        Assert.Empty(plain.Choices);
        Assert.Equal("", plain.Takes);
        Assert.Empty(empty.Choices);
        Assert.Equal("", empty.Takes);
        Assert.Equal(Answer.BadArgument, plain.Move(1).Code);
        Assert.Equal(Answer.BadArgument, empty.Move(1).Code);
        Assert.True(plain.InPanel);
    }

    [Fact]
    public void A_Locked_Setting_Answers_Each_Set_With_The_Locks_Reason_Until_The_Lock_Lets_Go()
    {
        var registry = new Registry();
        string? why = "The window is not shown.";
        var taken = new List<int>();
        var width = new Setting<int>("window.width", "The width.", 600, apply: value => { taken.Add(value); return null; })
        {
            Lock = () => why,
            Low = 0,
            High = 4096,
            Step = 16,
        };
        registry.Add(width);

        Assert.Equal("The window is not shown.", width.Locked);
        Assert.False(width.ReadOnly);
        var set = registry.Run("window.width 800");
        Assert.Equal((Answer.NotSettable, "The window is not shown."), (set.Code, set.Text));
        Assert.Equal(Answer.NotSettable, registry.Run("reset window.width").Code);
        Assert.Equal(Answer.NotSettable, width.TryStart("800").Code);
        Assert.Equal(Answer.NotSettable, width.Move(1).Code);
        Assert.Equal("window.width is 600.", registry.Run("window.width").Text);
        Assert.Empty(taken);

        why = null;
        Assert.Null(width.Locked);
        Assert.Equal("window.width is 800.", registry.Run("window.width 800").Text);
        Assert.Equal([800], taken);
    }

    [Fact]
    public void A_Setter_That_Refuses_Gives_Its_Reason_And_Leaves_The_Value_Where_It_Was()
    {
        var device = 32;
        var voices = new Setting<int>("sound.voices", "How many sounds play at once.", 32, () => device,
            apply: value =>
            {
                if (value == 13) return "The sound device has no 13 voices.";
                device = value;
                return null;
            });
        var registry = new Registry();
        registry.Add(voices);
        var raised = 0;
        voices.Changed += _ => raised++;

        var refused = registry.Run("sound.voices 13");
        Assert.Equal((Answer.Refused, "The sound device has no 13 voices."), (refused.Code, refused.Text));
        Assert.Equal((32, "32", "32", 0), (device, voices.Text, voices.StoredText, raised));

        Assert.True(registry.Run("sound.voices 16").Done);
        Assert.Equal((16, "16", 1), (device, voices.StoredText, raised));

        // A setting that holds its own value keeps it only where the setter took it.
        var held = new Setting<int>("sound.rate", "The rate.", 48, apply: value => value > 96 ? "Too fast." : null);
        Assert.Equal("Too fast.", held.TrySet(100).Text);
        Assert.Equal(48, held.Value);
        Assert.True(held.TrySet(44).Done);
        Assert.Equal(44, held.Value);

        Assert.Throws<ArgumentException>(() => new Setting<int>("sound.x", "x.", 1, set: _ => { }, apply: _ => null));
    }

    [Fact]
    public void A_Whole_Number_Without_A_Sign_Of_Four_Or_Eight_Bytes_Is_Range_Checked()
    {
        var seed = new Setting<uint>("debug.seed", "The run's seed.", 7);
        Assert.Equal((SettingKind.Whole, "0 to 4294967295"), (seed.Kind, seed.Takes));
        Assert.True(seed.TrySet("4294967295").Done);
        Assert.Equal(uint.MaxValue, seed.Value);
        var below = seed.TrySet("-1");
        Assert.Equal((Answer.OutOfRange, "debug.seed takes 0 to 4294967295, not -1."), (below.Code, below.Text));
        Assert.Equal(Answer.OutOfRange, seed.TrySet("4294967296").Code);
        Assert.Equal(Answer.BadArgument, seed.TrySet("1.5").Code);
        Assert.Equal(uint.MaxValue, seed.Value);
        Assert.True(seed.TrySet(0u).Done);
        Assert.Equal("0", seed.Move(-1).Done ? seed.StoredText : "");

        var ranged = new Setting<uint>("debug.count", "How many.", 4) { Low = 2, High = 9 };
        Assert.Equal("2 to 9", ranged.Takes);
        Assert.Equal(Answer.OutOfRange, ranged.TrySet("1").Code);
        Assert.Equal(Answer.OutOfRange, ranged.TrySet(10u).Code);
        Assert.True(ranged.TrySet("9").Done);

        // Eight bytes are held exactly, where a double would round the last digits away.
        var token = new Setting<ulong>("debug.token", "The run's token.", 0);
        Assert.True(token.TrySet("18446744073709551615").Done);
        Assert.Equal(ulong.MaxValue, token.Value);
        Assert.Equal("18446744073709551615", token.Text);
        Assert.Equal(Answer.OutOfRange, token.TrySet("18446744073709551616").Code);
        Assert.Equal(Answer.OutOfRange, token.TrySet("-1").Code);
        Assert.True(token.TrySet("9007199254740993").Done);
        Assert.Equal(9007199254740993UL, token.Value);
        Assert.True(token.Move(1).Done);
        Assert.Equal("9007199254740994", token.StoredText);
    }

    [Fact]
    public void A_Number_Refuses_Both_Infinities_Beside_Nan_With_Or_Without_A_High()
    {
        var open = new Setting<float>("render.gain", "The gain.", 1);
        var closed = new Setting<double>("render.bias", "The bias.", 1) { Low = -10, High = 10 };
        foreach (var text in new[] { "Infinity", "-Infinity", "NaN", "1e50" })
        {
            Assert.Equal(Answer.OutOfRange, open.TrySet(text).Code);
            Assert.Equal(Answer.OutOfRange, closed.TrySet(text).Code);
        }

        Assert.Equal("render.gain takes a number, not Infinity.", open.TrySet(float.PositiveInfinity).Text);
        Assert.Equal(Answer.OutOfRange, open.TrySet(float.NegativeInfinity).Code);
        Assert.Equal(Answer.OutOfRange, closed.TrySet(double.PositiveInfinity).Code);
        Assert.Equal((1f, 1.0), (open.Value, closed.Value));
        Assert.True(open.TrySet("1e30").Done);
    }
}
