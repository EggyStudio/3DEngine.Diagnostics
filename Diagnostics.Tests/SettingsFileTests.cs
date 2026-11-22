using Xunit;

namespace Diagnostics.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "diagnostics-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string File_ => Path.Combine(_folder, "player", "settings.txt");

    private static (Registry Registry, Setting<float> Scale, Setting<bool> Vsync, Setting<string> Name, Setting<bool> Meshlets) Make()
    {
        var registry = new Registry();
        var scale = new Setting<float>("render.scale", "The share of the window's size a frame is drawn at.", 1) { Low = 0.5, High = 2 };
        var vsync = new Setting<bool>("render.vsync", "Whether a frame waits for the display.", true);
        var name = new Setting<string>("player.name", "The player's name.", "Player");
        var meshlets = new Setting<bool>("render.meshlets", "Whether meshes are drawn as meshlets.", false) { AtNextStart = true };
        var session = new Setting<int>("debug.seed", "The run's seed.", 0) { Saved = false };
        foreach (var setting in new Setting[] { scale, vsync, name, meshlets, session }) registry.Add(setting);
        return (registry, scale, vsync, name, meshlets);
    }

    [Fact]
    public void The_File_Is_Written_And_Read_Back_With_Only_Changed_Values()
    {
        var (registry, scale, _, name, meshlets) = Make();
        var file = new SettingsFile(File_);
        Assert.Empty(file.Read(registry));
        Assert.False(file.Write(registry));
        Assert.False(File.Exists(File_));

        scale.TrySet(1.5f);
        name.TrySet("Ada \"the\" Great");
        meshlets.TrySet(true);
        registry.Run("debug.seed 7");
        Assert.True(file.Write(registry));
        Assert.Equal(
            "player.name \"Ada \\\"the\\\" Great\"\nrender.meshlets on\nrender.scale 1.5\n",
            File.ReadAllText(File_));
        Assert.False(file.Write(registry));
        Assert.False(File.Exists(File_ + ".tmp"));

        // A second run reads the three back, the one for the next start as its running value.
        var (again, scaleAgain, vsyncAgain, nameAgain, meshletsAgain) = Make();
        Assert.Empty(new SettingsFile(File_).Read(again));
        Assert.Equal(1.5f, scaleAgain.Value);
        Assert.True(vsyncAgain.Value);
        Assert.Equal("Ada \"the\" Great", nameAgain.Value);
        Assert.True(meshletsAgain.Value);
        Assert.Null(meshletsAgain.PendingText);

        // A value set back to its default leaves the file.
        scale.Reset();
        Assert.True(file.Write(registry));
        Assert.DoesNotContain("render.scale", File.ReadAllText(File_));
    }

    [Fact]
    public void Unknown_Names_And_Comments_Are_Kept_As_They_Stand()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, "# kept by hand\r\nmod.rain  heavy  \nrender.scale 2\n\nmod.fog \"thick fog\"\n");
        var (registry, scale, vsync, _, _) = Make();
        var file = new SettingsFile(File_);
        Assert.Empty(file.Read(registry));
        Assert.Equal(2f, scale.Value);

        vsync.TrySet(false);
        Assert.True(file.Write(registry));
        Assert.Equal(
            "render.scale 2\nrender.vsync off\n# kept by hand\nmod.rain  heavy  \n\nmod.fog \"thick fog\"\n",
            File.ReadAllText(File_));

        // A setting added after the read takes its name's line over, value and all, and the line
        // is then written once, by the setting.
        var rain = new Setting<string>("mod.rain", "How hard it rains.", "none");
        registry.Add(rain);
        Assert.Equal("heavy", rain.Value);
        Assert.Contains("mod.fog \"thick fog\"", file.Text(registry));
        Assert.Equal(1, file.Text(registry).Split("mod.rain").Length - 1);
    }

    [Fact]
    public void A_Refused_Value_Is_Reported_And_Its_Setting_Stays_At_Its_Default()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, "render.scale 9\nrender.vsync\nplayer.name Ada\n");
        var (registry, scale, vsync, name, _) = Make();
        var file = new SettingsFile(File_);

        Assert.Equal(
            [
                "settings.txt line 1 is refused. render.scale takes 0.5 to 2, not 9.",
                "settings.txt line 2 is refused. render.vsync has no value.",
            ],
            file.Read(registry));
        Assert.Equal((1f, true, "Ada"), (scale.Value, vsync.Value, name.Value));
        Assert.True(file.Write(registry));
        Assert.Equal("player.name Ada\n", File.ReadAllText(File_));
    }

    [Fact]
    public void A_Value_The_Environment_Overrides_Is_Not_Written_In_The_Stored_Ones_Place()
    {
        const string variable = "DIAGNOSTICS_TEST_FILE_SCALE";
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, "render.scale 1.5\n");
        Environment.SetEnvironmentVariable(variable, "2");
        try
        {
            var registry = new Registry();
            var scale = new Setting<float>("render.scale", "The share of the window's size a frame is drawn at.", 1) { Low = 0.5, High = 2, EnvironmentVariable = variable };
            registry.Add(scale);
            var file = new SettingsFile(File_);
            Assert.Empty(file.Read(registry));
            Assert.Equal(2f, scale.Value);
            Assert.Equal("1.5", scale.StoredText);
            Assert.False(file.Write(registry));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void A_Games_File_Stands_In_A_Folder_Of_Its_Name()
    {
        var path = SettingsFile.For("my-game").Path;
        Assert.Equal("settings.txt", Path.GetFileName(path));
        Assert.Equal("my-game", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Throws<ArgumentException>(() => SettingsFile.For(" "));
    }

    [Fact]
    public void The_File_Keeps_What_A_Player_Set_And_Never_A_Value_The_Program_Wrote_Through_Its_Getter()
    {
        var width = 1280;
        var registry = new Registry();
        var setting = new Setting<int>("window.width", "The window's width.", 1280, () => width, value => width = value);
        registry.Add(setting);
        var file = new SettingsFile(File_);
        Assert.Empty(file.Read(registry));

        // The program changes its own variable, as a window that was resized does.
        width = 1920;
        Assert.Equal("", file.Text(registry));
        Assert.False(file.Write(registry));

        registry.Run("window.width 1600");
        Assert.Equal("window.width 1600\n", file.Text(registry));
        width = 800;
        Assert.Equal("window.width 1600\n", file.Text(registry));
        Assert.Equal("1600", setting.SavedText);
        Assert.True(file.Write(registry));

        // What a start reads from the file counts as the player's, whatever the program does after.
        var again = new Registry();
        var width2 = 1280;
        again.Add(new Setting<int>("window.width", "The window's width.", 1280, () => width2, value => width2 = value));
        var second = new SettingsFile(File_);
        Assert.Empty(second.Read(again));
        width2 = 640;
        Assert.False(second.Write(again));
        Assert.Equal("window.width 1600\n", File.ReadAllText(File_));
    }

    [Fact]
    public void A_Setting_Added_After_The_Start_Takes_Its_Line_And_Is_Written_When_It_Is_Set()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, "late.volume 7\nlate.refused 500\nother.mode fast\n");
        var registry = new Registry();
        var file = new SettingsFile(File_);
        var trouble = new List<string>();
        file.Trouble += trouble.Add;
        Assert.Empty(file.Read(registry));

        var volume = new Setting<int>("late.volume", "The volume.", 5) { Low = 0, High = 10 };
        registry.Add(volume);
        Assert.Equal(7, volume.Value);
        registry.Add(new Setting<int>("late.refused", "Refuses.", 1) { Low = 0, High = 10 });
        Assert.Equal(["settings.txt line 2 is refused. late.refused takes 0 to 10, not 500."], trouble);
        Assert.Equal(1, registry.FindSetting("late.refused") is Setting<int> refused ? refused.Value : -1);

        // The line belongs to the setting from then on, and the file is written at its next set.
        Assert.Equal("late.volume 7\nother.mode fast\n", file.Text(registry));
        volume.TrySet(9);
        Assert.Equal("late.volume 9\nother.mode fast\n", File.ReadAllText(File_));

        // A late setting with no line is written when it is set, too.
        var mute = new Setting<bool>("late.mute", "Mute.", false);
        registry.Add(mute);
        mute.TrySet(true);
        Assert.Equal("late.mute on\nlate.volume 9\nother.mode fast\n", File.ReadAllText(File_));
        Assert.Single(trouble);
    }

    [Fact]
    public void A_Write_That_Fails_After_A_Late_Set_Is_Told_And_Leaves_The_Set_Done()
    {
        Directory.CreateDirectory(_folder);
        var blocked = Path.Combine(_folder, "blocked");
        File.WriteAllText(blocked, "a file where a folder should be");
        var registry = new Registry();
        var file = new SettingsFile(Path.Combine(blocked, "settings.txt"));
        var trouble = new List<string>();
        file.Trouble += trouble.Add;
        Assert.Empty(file.Read(registry));

        var late = new Setting<bool>("late.mute", "Mute.", false);
        registry.Add(late);
        Assert.True(late.TrySet(true).Done);
        Assert.True(late.Value);
        Assert.Single(trouble);
        Assert.StartsWith("settings.txt is not written. ", trouble[0]);
    }
}
