using System.Numerics;
using ImGuiNET;

namespace Diagnostics;

/// <summary>Draws a <see cref="ConsoleModel"/> with Dear ImGui, in a host's ImGui frame.</summary>
/// <remarks>
/// <para>
/// The console is a window across the top of the host's window, a plain list of every line, a line
/// written again in a row shown once with its count in brackets, the field the line is entered in
/// and the hint under it where there is one. While the model's popup shows, the candidates stand in
/// a box above the field, as wide as the field, six rows at most with the selected one banded, and
/// the ghost text follows what is typed in a dimmed color. Up and down move the selection or walk
/// the history, Tab fills the selected candidate and Enter runs the line, all by the model, while
/// the field has the keyboard.
/// </para>
/// <para>
/// The box and the ghost are drawn on ImGui's foreground list, so they stand over the console
/// whichever window has the focus. The console draws in the neutral look of the debug overlay,
/// light gray text on black at half alpha, in one palette of its own pushed around its drawing and
/// popped after, and takes no color from the host's theme. The font is the host's.
/// <see cref="Draw"/> is called on the host's thread between the host's <c>NewFrame</c> and
/// <c>Render</c>.
/// </para>
/// </remarks>
/// <param name="model">The console drawn.</param>
public sealed class ImGuiConsole(ConsoleModel model)
{
    private const ImGuiWindowFlags Window = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;

    // Enter submits, and the callbacks let Tab and the arrows change the field's own text, which
    // a field that has the keyboard otherwise keeps over the string given to it.
    private const ImGuiInputTextFlags Field = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackCompletion
        | ImGuiInputTextFlags.CallbackHistory | ImGuiInputTextFlags.CallbackAlways;

    private readonly ConsoleModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private long _seen;
    private string _given = "";
    private bool _pushed;
    private bool _wasOpen;

    /// <summary>
    /// The share of the host's window's height the console takes, 0.4 by default.
    /// </summary>
    public float Height { get; set; } = 0.4f;

    /// <summary>
    /// Whether the console's field had the keyboard when it was last drawn, which a host gives its
    /// key reading as typing.
    /// </summary>
    public bool Typing { get; private set; }

    /// <summary>Draws the console where its model is open, and nothing where it is not.</summary>
    /// <param name="window">The host's window's size in ImGui's units.</param>
    public void Draw(Vector2 window)
    {
        if (!_model.Open)
        {
            (Typing, _wasOpen) = (false, false);
            return;
        }

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(window.X, MathF.Round(window.Y * Math.Clamp(Height, 0.1f, 1f))), ImGuiCond.Always);
        using var look = NeutralLook.Push();
        if (ImGui.Begin("Console##diagnostics", Window))
        {
            // The hint's row is the window's only where there is a hint, so a console with none
            // has no empty strip under the field.
            var hint = _model.Hint;
            Lines(hint.Length > 0);
            Entry(hint);
        }

        ImGui.End();
        _wasOpen = true;
    }

    private void Lines(bool hinted)
    {
        // The lines take what the window has left over the field and the hint under it.
        var room = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() - (hinted ? ImGui.GetTextLineHeightWithSpacing() : 0f);
        if (ImGui.BeginChild("##lines", new Vector2(0f, MathF.Max(room, ImGui.GetTextLineHeight())), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var line in _model.Lines())
            {
                // A line written again in a row shows its count after its text.
                var text = line.Count > 1 ? $"{line.Text} (x{line.Count})" : line.Text;
                if (ColorOf(line.Level) is { } color)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TextUnformatted(text);
                    ImGui.PopStyleColor();
                }
                else
                    ImGui.TextUnformatted(text);
            }

            // A new line scrolls the view to the end where the view stood at the end, so a
            // reader who scrolled up keeps the place.
            var written = _model.Written;
            if (_seen != written && (!_wasOpen || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - ImGui.GetTextLineHeightWithSpacing() * 2))
                ImGui.SetScrollHereY(1f);
            _seen = written;
        }

        ImGui.EndChild();
    }

    private unsafe void Entry(string hint)
    {
        ImGui.SetNextItemWidth(-1f);
        if (!_wasOpen) ImGui.SetKeyboardFocusHere();
        var typed = _model.Typed;
        _pushed = typed != _given;
        if (ImGui.InputTextWithHint("##typed", "Enter a command or a setting", ref typed, 512, Field, Edit))
        {
            _model.Enter();
            // Enter takes the keyboard from the field, which the next line needs again.
            ImGui.SetKeyboardFocusHere(-1);
        }
        else if (typed != _model.Typed)
            _model.Edit(typed);

        _given = _model.Typed;
        Typing = ImGui.IsItemActive();
        var (min, max) = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (hint.Length > 0) ImGui.TextDisabled(hint);

        if (!_model.PopupShown) return;
        var list = ImGui.GetForegroundDrawList();
        Ghost(list, min, max);
        Popup(list, min, max);
    }

    // The rest of the selected candidate, in the dimmed color, where the typed text ends, as long
    // as it fits in the field.
    private void Ghost(ImDrawListPtr list, Vector2 min, Vector2 max)
    {
        if (_model.Ghost is not { Length: > 0 } ghost) return;
        var style = ImGui.GetStyle();
        var at = new Vector2(min.X + style.FramePadding.X + ImGui.CalcTextSize(_model.Typed).X, min.Y + style.FramePadding.Y);
        if (at.X + ImGui.CalcTextSize(ghost).X <= max.X - style.FramePadding.X)
            list.AddText(at, NeutralLook.U32(NeutralLook.Dim), ghost);
    }

    // The candidates in a box whose lower edge is the field's upper edge and whose sides are the
    // field's, the selected row banded, their text starting where the typed text does, and one
    // border running round the box and the field together, so the two read as one.
    private void Popup(ImDrawListPtr list, Vector2 min, Vector2 max)
    {
        var style = ImGui.GetStyle();
        var candidates = _model.Candidates;
        var rows = Math.Min(ConsoleModel.PopupRows, candidates.Count - _model.FirstVisible);
        var rowHeight = ImGui.GetTextLineHeight() + style.ItemSpacing.Y;
        var height = rows * rowHeight + style.WindowPadding.Y;
        var box = (From: new Vector2(min.X, min.Y - height), To: new Vector2(max.X, min.Y));

        list.AddRectFilled(box.From, box.To, NeutralLook.U32(NeutralLook.Popup));
        for (var row = 0; row < rows; row++)
        {
            var index = _model.FirstVisible + row;
            var top = box.From.Y + style.WindowPadding.Y / 2 + row * rowHeight;
            if (index == _model.SelectedIndex)
                list.AddRectFilled(new Vector2(box.From.X + 1f, top), new Vector2(box.To.X - 1f, top + rowHeight), NeutralLook.U32(NeutralLook.Chosen));
            list.AddText(new Vector2(box.From.X + style.FramePadding.X, top + style.ItemSpacing.Y / 2), NeutralLook.U32(NeutralLook.Text), candidates[index]);
        }

        list.AddRect(box.From, max, NeutralLook.U32(NeutralLook.Border));
    }

    // The field's own text is the model's line, which Tab and the arrows change through the model
    // and write back.
    private unsafe int Edit(ImGuiInputTextCallbackData* data)
    {
        var field = new ImGuiInputTextCallbackDataPtr(data);
        var current = System.Text.Encoding.UTF8.GetString(data->Buf, data->BufTextLen);
        if (field.EventFlag == ImGuiInputTextFlags.CallbackAlways)
        {
            // A line the host set since the field last gave its text is the model's to keep, and
            // the field takes it where it differs, while the keys of the frame go on from it.
            if (_pushed) Take(field, current);
            else if (current != _model.Typed) _model.Edit(current);
            return 0;
        }

        if (current != _model.Typed) _model.Edit(current);
        if (field.EventFlag == ImGuiInputTextFlags.CallbackCompletion) _model.Accept();
        else _model.Navigate(field.EventKey == ImGuiKey.UpArrow ? -1 : 1);

        Take(field, current);
        return 0;
    }

    private void Take(ImGuiInputTextCallbackDataPtr field, string current)
    {
        if (_model.Typed == current) return;
        field.DeleteChars(0, field.BufTextLen);
        field.InsertChars(0, _model.Typed);
    }

    // An entered line takes the style's dimmed text, a warning and an error their own colors, and
    // a plain line the style's.
    private static Vector4? ColorOf(ConsoleLevel level) => level switch
    {
        ConsoleLevel.Input => NeutralLook.Dim,
        ConsoleLevel.Warning => NeutralLook.Warning,
        ConsoleLevel.Error => NeutralLook.Error,
        _ => null,
    };
}
