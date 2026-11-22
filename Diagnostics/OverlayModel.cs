using System.Globalization;

namespace Diagnostics;

/// <summary>Where on the screen a section of the overlay's lines stands.</summary>
public enum OverlayPlace
{
    /// <summary>The first lines at the top left, the version and the frame's rate.</summary>
    Head,

    /// <summary>Beside the head, to its right on the same rows.</summary>
    Beside,

    /// <summary>The left column under the head.</summary>
    Left,

    /// <summary>The right column.</summary>
    Right,
}

/// <summary>What the kept frame times say of the frame's rate.</summary>
/// <param name="Milliseconds">The last frame's time in milliseconds.</param>
/// <param name="AverageMilliseconds">The mean time of the kept frames in milliseconds.</param>
/// <param name="LowFramesPerSecond">
/// The 1% low, the rate in frames a second of the mean time of the slowest hundredth of the kept
/// frames, one frame at least.
/// </param>
public readonly record struct FrameStats(double Milliseconds, double AverageMilliseconds, double LowFramesPerSecond);

/// <summary>
/// The debug overlay's content with no drawing, named sections of lines a host fills, the last
/// frames' times and their statistics.
/// </summary>
/// <remarks>
/// <para>
/// A host adds a section for each group of lines it has numbers for, calls <see cref="Frame"/>
/// once a frame, and draws <see cref="Lines"/> of each place and <see cref="FrameTimes"/> as a
/// strip while <see cref="Shown"/>. The sections are asked for their lines four times a second
/// and not each frame, so a number can be read while it changes. The model has one section of its
/// own, <c>frame</c> at the head, the rate and the statistics in a line.
/// </para>
/// <para>Every member is called on the host's thread.</para>
/// </remarks>
public sealed class OverlayModel
{
    /// <summary>How many frame times are kept, the strip's width in frames.</summary>
    public const int StripFrames = 240;

    /// <summary>How many seconds pass between two askings of the sections.</summary>
    public const double RefreshSeconds = 0.25;

    private readonly List<(string Name, OverlayPlace Place, Func<IEnumerable<string>> Lines, bool Detail)> _sections = [];
    private readonly Dictionary<OverlayPlace, List<string>> _lines = [];
    private readonly float[] _times = new float[StripFrames];
    private bool _shown;
    private int _count;
    private int _next;
    private double _since = double.PositiveInfinity;

    /// <summary>Makes an overlay holding its own section, <c>frame</c>.</summary>
    public OverlayModel() => AddSection("frame", OverlayPlace.Head, () => [FrameLine()]);

    /// <summary>
    /// Whether the overlay is shown, which the overlay's key turns over.
    /// Showing it closes the other panels where
    /// <see cref="DiagnosticKeys"/> holds them, see <see cref="OpenPanels"/>.
    /// </summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            var opened = value && !_shown;
            _shown = value;
            if (opened) WhenOpened?.Invoke();
        }
    }

    internal Action? WhenOpened { get; set; }

    /// <summary>
    /// Whether the sections added as detail are shown too, which Shift with the overlay's key turns
    /// over.
    /// </summary>
    public bool Detailed { get; set; }

    /// <summary>The kept frame times in milliseconds, oldest first, 240 at most.</summary>
    public IReadOnlyList<float> FrameTimes
    {
        get
        {
            var times = new float[_count];
            for (var i = 0; i < _count; i++) times[i] = _times[(_next - _count + i + StripFrames) % StripFrames];
            return times;
        }
    }

    /// <summary>The statistics of the kept frame times, zeros before the first frame.</summary>
    public FrameStats Stats
    {
        get
        {
            if (_count == 0) return default;
            var times = FrameTimes.Order().ToArray();
            var slowest = Math.Max(1, _count / 100);
            var low = times[^slowest..].Average();
            return new FrameStats(_times[(_next - 1 + StripFrames) % StripFrames], times.Average(), low > 0 ? 1000 / low : 0);
        }
    }

    /// <summary>
    /// Adds a section of lines, in the place of one of the same name where there is one.
    /// </summary>
    /// <param name="name">The section's name, which removes it.</param>
    /// <param name="place">Where its lines stand, after the place's earlier sections.</param>
    /// <param name="lines">Gives the section's lines when asked, four times a second.</param>
    /// <param name="detail">Whether the section shows only while <see cref="Detailed"/>.</param>
    public void AddSection(string name, OverlayPlace place, Func<IEnumerable<string>> lines, bool detail = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(lines);
        var at = _sections.FindIndex(section => section.Name == name);
        if (at < 0) _sections.Add((name, place, lines, detail));
        else _sections[at] = (name, place, lines, detail);
        _since = double.PositiveInfinity;
    }

    /// <summary>Takes a section out, and says whether there was one.</summary>
    /// <param name="name">The section's name.</param>
    public bool RemoveSection(string name)
    {
        _since = double.PositiveInfinity;
        return _sections.RemoveAll(section => section.Name == name) > 0;
    }

    /// <summary>
    /// Keeps a frame's time and asks the sections for their lines again where a quarter of a
    /// second has passed since they were asked.
    /// </summary>
    /// <param name="seconds">The frame's time in seconds.</param>
    public void Frame(double seconds)
    {
        _times[_next] = (float)(seconds * 1000);
        _next = (_next + 1) % StripFrames;
        _count = Math.Min(_count + 1, StripFrames);
        _since += seconds;
        if (Shown && _since >= RefreshSeconds) Refresh();
    }

    /// <summary>Asks the sections for their lines at once.</summary>
    /// <remarks>
    /// A section that throws gives one line naming it and the exception's message, so one fault
    /// hides no other section.
    /// </remarks>
    public void Refresh()
    {
        _since = 0;
        _lines.Clear();
        foreach (var (name, place, lines, detail) in _sections)
        {
            if (detail && !Detailed) continue;
            if (!_lines.TryGetValue(place, out var held)) _lines[place] = held = [];
            try
            {
                held.AddRange(lines());
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                held.Add($"{name} failed. {error.Message}");
            }
        }
    }

    /// <summary>
    /// The lines of a place as the sections last gave them, in the sections' order.
    /// </summary>
    /// <param name="place">The place.</param>
    public IReadOnlyList<string> Lines(OverlayPlace place)
    {
        if (double.IsPositiveInfinity(_since)) Refresh();
        return _lines.TryGetValue(place, out var lines) ? lines : [];
    }

    private string FrameLine()
    {
        var stats = Stats;
        var rate = stats.AverageMilliseconds > 0 ? 1000 / stats.AverageMilliseconds : 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"{rate:0} fps, {stats.Milliseconds:0.0} ms, {stats.AverageMilliseconds:0.0} ms average, {stats.LowFramesPerSecond:0} fps 1% low");
    }
}
