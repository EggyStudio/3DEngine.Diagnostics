using Xunit;

namespace Diagnostics.Tests;

public class OverlayModelTests
{
    [Fact]
    public void Named_Sections_Give_Their_Places_Lines_In_Their_Order()
    {
        var overlay = new OverlayModel { Shown = true };
        overlay.AddSection("camera", OverlayPlace.Left, () => ["camera 1 2 3"]);
        overlay.AddSection("scene", OverlayPlace.Left, () => ["entities 7", "meshes 3"]);
        overlay.AddSection("gpu", OverlayPlace.Right, () => ["a card"]);

        Assert.Equal(["camera 1 2 3", "entities 7", "meshes 3"], overlay.Lines(OverlayPlace.Left));
        Assert.Equal(["a card"], overlay.Lines(OverlayPlace.Right));
        Assert.Empty(overlay.Lines(OverlayPlace.Beside));
        Assert.Single(overlay.Lines(OverlayPlace.Head));

        // A section added again under its name takes the first one's place, and a removed one goes.
        overlay.AddSection("camera", OverlayPlace.Left, () => ["camera 4 5 6"]);
        Assert.True(overlay.RemoveSection("scene"));
        Assert.False(overlay.RemoveSection("scene"));
        Assert.Equal(["camera 4 5 6"], overlay.Lines(OverlayPlace.Left));
    }

    [Fact]
    public void The_Sections_Are_Asked_Four_Times_A_Second()
    {
        var asked = 0;
        var overlay = new OverlayModel { Shown = true };
        overlay.AddSection("count", OverlayPlace.Left, () => [$"asked {++asked}"]);

        Assert.Equal(["asked 1"], overlay.Lines(OverlayPlace.Left));
        for (var i = 0; i < 14; i++) overlay.Frame(1 / 60.0);
        Assert.Equal(["asked 1"], overlay.Lines(OverlayPlace.Left));
        overlay.Frame(1 / 60.0);
        overlay.Frame(1 / 60.0);
        Assert.Equal(["asked 2"], overlay.Lines(OverlayPlace.Left));

        // A hidden overlay asks nothing.
        overlay.Shown = false;
        for (var i = 0; i < 120; i++) overlay.Frame(1 / 60.0);
        Assert.Equal(2, asked);
    }

    [Fact]
    public void A_Detail_Section_Shows_Only_With_The_Detail()
    {
        var overlay = new OverlayModel();
        overlay.AddSection("passes", OverlayPlace.Right, () => ["shadow 1.2 ms"], detail: true);
        Assert.Empty(overlay.Lines(OverlayPlace.Right));
        overlay.Detailed = true;
        overlay.Refresh();
        Assert.Equal(["shadow 1.2 ms"], overlay.Lines(OverlayPlace.Right));
    }

    [Fact]
    public void A_Section_That_Throws_Gives_A_Line_And_Hides_No_Other()
    {
        var overlay = new OverlayModel();
        overlay.AddSection("gpu", OverlayPlace.Left, () => throw new InvalidOperationException("No device."));
        overlay.AddSection("scene", OverlayPlace.Left, () => ["entities 7"]);
        Assert.Equal(["gpu failed. No device.", "entities 7"], overlay.Lines(OverlayPlace.Left));
    }

    [Fact]
    public void The_Strip_Keeps_The_Last_240_Frames_And_Their_Statistics()
    {
        var overlay = new OverlayModel();
        Assert.Equal(default, overlay.Stats);
        Assert.Empty(overlay.FrameTimes);

        // 300 frames of 10 ms, the last three of 40 ms, of which the strip keeps 240.
        for (var i = 0; i < 297; i++) overlay.Frame(0.010);
        for (var i = 0; i < 3; i++) overlay.Frame(0.040);
        var times = overlay.FrameTimes;
        Assert.Equal(OverlayModel.StripFrames, times.Count);
        Assert.Equal((10f, 40f), (times[0], times[^1]));

        var stats = overlay.Stats;
        Assert.Equal(40, stats.Milliseconds, 3);
        Assert.Equal((237 * 10 + 3 * 40) / 240.0, stats.AverageMilliseconds, 3);
        // The slowest hundredth of 240 frames is two frames, both of 40 ms, so 25 frames a second.
        Assert.Equal(25, stats.LowFramesPerSecond, 3);
        Assert.Equal(["96 fps, 40.0 ms, 10.4 ms average, 25 fps 1% low"], overlay.Lines(OverlayPlace.Head));
    }
}
