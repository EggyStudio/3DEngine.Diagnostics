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
}
