using System.Numerics;
using ImGuiNET;

namespace Diagnostics;

/// <summary>
/// The one palette the console and the panel draw with, the look of the debug overlay, light gray
/// text on black at half alpha with no hue anywhere, pushed around their drawing and popped after,
/// so a host's own windows keep the host's theme.
/// </summary>
/// <remarks>
/// Nothing the drawers draw reads a color from the host's theme. The colors of the lines that
/// failed alone have a hue, since a failure should stand out. The values are internal until a host
/// needs to set them.
/// </remarks>
internal static class NeutralLook
{
    /// <summary>The text, a light gray.</summary>
    public static readonly Vector4 Text = Gray(224);

    /// <summary>The echo, the ghost text, the hint and the placeholder, a dim gray.</summary>
    public static readonly Vector4 Dim = Gray(172);

    /// <summary>A warning line, an amber that stands out on black.</summary>
    public static readonly Vector4 Warning = new(1.00f, 0.72f, 0.25f, 1f);

    /// <summary>An error line, a red that stands out on black.</summary>
    public static readonly Vector4 Error = new(1.00f, 0.40f, 0.38f, 1f);

    /// <summary>The console's window and its field, black at half alpha.</summary>
    public static readonly Vector4 Ground = new(0f, 0f, 0f, 0.5f);

    /// <summary>
    /// The popup of candidates, the field while the popup shows and the panel's window, plain
    /// black, so the box they make is one shade over whatever lies behind it and the panel reads
    /// over a bright scene.
    /// </summary>
    public static readonly Vector4 Popup = new(0f, 0f, 0f, 1f);

    /// <summary>The chosen row, a neutral gray.</summary>
    public static readonly Vector4 Chosen = Gray(88);

    /// <summary>The borders, a gray.</summary>
    public static readonly Vector4 Border = new(0.45f, 0.45f, 0.45f, 0.7f);

    private static readonly (ImGuiCol Slot, Vector4 Color)[] Colors =
    [
        (ImGuiCol.Text, Text),
        (ImGuiCol.TextDisabled, Dim),
        (ImGuiCol.WindowBg, Ground),
        (ImGuiCol.ChildBg, new(0f, 0f, 0f, 0f)),
        (ImGuiCol.PopupBg, Popup),
        (ImGuiCol.Border, Border),
        (ImGuiCol.BorderShadow, new(0f, 0f, 0f, 0f)),
        (ImGuiCol.FrameBg, Ground),
        (ImGuiCol.FrameBgHovered, new(0f, 0f, 0f, 0.6f)),
        (ImGuiCol.FrameBgActive, new(0f, 0f, 0f, 0.7f)),
        (ImGuiCol.Header, Chosen),
        (ImGuiCol.HeaderHovered, Chosen),
        (ImGuiCol.HeaderActive, Chosen),
        (ImGuiCol.ScrollbarBg, new(0f, 0f, 0f, 0.3f)),
        (ImGuiCol.ScrollbarGrab, Gray(110, 0.8f)),
        (ImGuiCol.ScrollbarGrabHovered, Gray(150, 0.9f)),
        (ImGuiCol.ScrollbarGrabActive, Gray(180)),
        (ImGuiCol.Separator, Border),
        (ImGuiCol.TextSelectedBg, Gray(128, 0.45f)),
        (ImGuiCol.NavCursor, new(0f, 0f, 0f, 0f)),
    ];

    /// <summary>Pushes the palette, and gives what pops it.</summary>
    public static Pushed Push()
    {
        foreach (var (slot, color) in Colors) ImGui.PushStyleColor(slot, color);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        return new Pushed();
    }

    /// <summary>
    /// Pushes the popup's black as a window's ground, which the panel stands on so it reads over a
    /// bright scene as the console's candidates do. Its text, selected band and border are the
    /// palette's own, which the popup draws with too.
    /// </summary>
    public static void PushPopupGround() => ImGui.PushStyleColor(ImGuiCol.WindowBg, Popup);

    /// <summary>Pops what <see cref="PushPopupGround"/> pushed.</summary>
    public static void PopPopupGround() => ImGui.PopStyleColor();

    /// <summary>Pushes the popup's black for a field's ground, in all its states.</summary>
    public static void PushJoined()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Popup);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Popup);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Popup);
    }

    /// <summary>Pops what <see cref="PushJoined"/> pushed.</summary>
    public static void PopJoined() => ImGui.PopStyleColor(3);

    /// <summary>A color as a drawing list takes it.</summary>
    /// <param name="color">The color.</param>
    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    private static Vector4 Gray(int level, float alpha = 1f) => new(level / 255f, level / 255f, level / 255f, alpha);

    /// <summary>What pops the palette a push made.</summary>
    public readonly struct Pushed : IDisposable
    {
        /// <summary>Pops the colors and the style values pushed.</summary>
        public void Dispose()
        {
            ImGui.PopStyleVar(6);
            ImGui.PopStyleColor(Colors.Length);
        }
    }
}
