using System.Numerics;
using ImGuiNET;

namespace Diagnostics;

/// <summary>Draws a <see cref="PanelModel"/> with Dear ImGui, in a host's ImGui frame.</summary>
/// <remarks>
/// The panel is a window in the middle of the host's window, the page's title, its rows with the
/// selected one marked, and under them what the last input answered where it was refused. It takes
/// no mouse. With <see cref="ReadsKeys"/> it reads the arrows, Enter and Escape and a pad's
/// directions, lower face button and right face button from ImGui's own keys, repeating a held
/// direction, so a host that feeds ImGui its input feeds the panel, a pad's where its backend sets
/// <c>ImGuiBackendFlags.HasGamepad</c>. The window is sized to its rows, which ImGui measures in
/// the first frame and draws from the second. It draws in the neutral look of the console, its
/// palette pushed around the window and popped after, and takes no color from the host's theme.
/// <see cref="Draw"/> is called on the host's thread between the host's <c>NewFrame</c> and
/// <c>Render</c>.
/// </remarks>
/// <param name="model">The panel drawn.</param>
public sealed class ImGuiPanel(PanelModel model)
{
    private const ImGuiWindowFlags Window = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav;

    private static readonly (ImGuiKey Key, ImGuiKey Pad, PanelInput Input, bool Repeats)[] Keys =
    [
        (ImGuiKey.UpArrow, ImGuiKey.GamepadDpadUp, PanelInput.Up, true),
        (ImGuiKey.DownArrow, ImGuiKey.GamepadDpadDown, PanelInput.Down, true),
        (ImGuiKey.LeftArrow, ImGuiKey.GamepadDpadLeft, PanelInput.Left, true),
        (ImGuiKey.RightArrow, ImGuiKey.GamepadDpadRight, PanelInput.Right, true),
        (ImGuiKey.Enter, ImGuiKey.GamepadFaceDown, PanelInput.Accept, false),
        (ImGuiKey.Escape, ImGuiKey.GamepadFaceRight, PanelInput.Back, false),
    ];

    private readonly PanelModel _model = model ?? throw new ArgumentNullException(nameof(model));

    /// <summary>
    /// Whether the panel reads its six inputs from ImGui's keys while it is open, which it does by
    /// default. A host that steers the model itself turns it off.
    /// </summary>
    public bool ReadsKeys { get; set; } = true;

    /// <summary>The panel's width in ImGui's units, 520 by default.</summary>
    public float Width { get; set; } = 520f;

    /// <summary>Draws the panel where its model is open, and nothing where it is not.</summary>
    /// <param name="window">The host's window's size in ImGui's units.</param>
    public void Draw(Vector2 window)
    {
        if (!_model.Open) return;
        if (ReadsKeys && !ImGui.GetIO().WantTextInput)
            foreach (var (key, pad, input, repeats) in Keys)
                if (ImGui.IsKeyPressed(key, repeats) || ImGui.IsKeyPressed(pad, repeats))
                    _model.Press(input);

        // Back on the first page closes the panel, which then draws nothing this frame.
        if (!_model.Open) return;

        var rows = _model.Rows;
        ImGui.SetNextWindowPos(window / 2, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(MathF.Min(Width, window.X), 0f), ImGuiCond.Always);
        using var look = NeutralLook.Push();
        if (ImGui.Begin("Panel##diagnostics", Window))
        {
            ImGui.TextUnformatted(_model.Title);
            ImGui.Separator();
            foreach (var row in rows) Row(row);
            if (_model.Answer is { Done: false } refused)
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextDisabled(refused.Text);
                ImGui.PopTextWrapPos();
            }
        }

        ImGui.End();
    }

    private static void Row(PanelRow row)
    {
        if (row.Kind == PanelRowKind.Text)
        {
            ImGui.TextDisabled(row.Label);
            return;
        }

        var value = row.Kind switch
        {
            PanelRowKind.Page => ">",
            PanelRowKind.Setting => row.Selected ? $"< {row.Value} >" : row.Value,
            _ => "",
        };
        if (row.Pending) value += "  at the next start";
        if (row.Overridden) value += "  kept, the environment holds it";

        // The selected row is a filled band of the style's header color, the same a list's
        // selected item takes.
        var from = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        if (row.Selected)
            ImGui.GetWindowDrawList().AddRectFilled(
                from - new Vector2(4f, 1f),
                from + new Vector2(width + 4f, ImGui.GetTextLineHeight() + 1f),
                NeutralLook.U32(NeutralLook.Chosen));
        ImGui.TextUnformatted(row.Label);
        if (value.Length > 0)
        {
            ImGui.SameLine(MathF.Max(0f, width - ImGui.CalcTextSize(value).X));
            ImGui.TextUnformatted(value);
        }
    }
}
