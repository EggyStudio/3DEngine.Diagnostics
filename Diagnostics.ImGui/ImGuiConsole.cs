using System.Numerics;
using ImGuiNET;

namespace Diagnostics;

/// <summary>Draws a <see cref="ConsoleModel"/> with Dear ImGui, in a host's ImGui frame.</summary>
/// <remarks>
/// The console is a window across the top of the host's window, its filters and search, the
/// lines, the line being entered and the hint under it. Up and down go through the history, Tab
/// completes and Enter submits, while the field has the keyboard. It sets no style and no font,
/// which are the host's. <see cref="Draw"/> is called on the host's thread between the host's
/// <c>NewFrame</c> and <c>Render</c>.
/// </remarks>
/// <param name="model">The console drawn.</param>
public sealed class ImGuiConsole(ConsoleModel model)
{
    private const ImGuiWindowFlags Window = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;

    // Enter submits, and the callbacks let Tab and the arrows change the field's own text, which
    // a field that has the keyboard otherwise keeps over the string given to it.
    private const ImGuiInputTextFlags Field = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackCompletion
        | ImGuiInputTextFlags.CallbackHistory;

    private readonly ConsoleModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private string _typed = "";
    private long _seen;
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

    /// <summary>The line being entered.</summary>
    public string Typed
    {
        get => _typed;
        set => _typed = value ?? "";
    }

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
        if (ImGui.Begin("Console##diagnostics", Window))
        {
            Filters();
            Lines();
            Entry();
        }

        ImGui.End();
        _wasOpen = true;
    }

    private void Filters()
    {
        var search = _model.Search;
        ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X * 0.4f));
        if (ImGui.InputTextWithHint("##search", "Search the lines", ref search, 128)) _model.Search = search;

        var (info, warnings, errors) = (_model.ShowInfo, _model.ShowWarnings, _model.ShowErrors);
        ImGui.SameLine();
        if (ImGui.Checkbox("info", ref info)) _model.ShowInfo = info;
        ImGui.SameLine();
        if (ImGui.Checkbox("warnings", ref warnings)) _model.ShowWarnings = warnings;
        ImGui.SameLine();
        if (ImGui.Checkbox("errors", ref errors)) _model.ShowErrors = errors;
    }

    private void Lines()
    {
        // The lines take what the window has left over the field and the hint under it.
        var room = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() - ImGui.GetTextLineHeightWithSpacing();
        if (ImGui.BeginChild("##lines", new Vector2(0f, MathF.Max(room, ImGui.GetTextLineHeight())), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var line in _model.Lines())
            {
                if (ColorOf(line.Level) is { } color)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TextUnformatted(line.Text);
                    ImGui.PopStyleColor();
                }
                else
                    ImGui.TextUnformatted(line.Text);
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

    private unsafe void Entry()
    {
        ImGui.SetNextItemWidth(-1f);
        if (!_wasOpen) ImGui.SetKeyboardFocusHere();
        if (ImGui.InputTextWithHint("##typed", "Enter a command or a setting", ref _typed, 512, Field, Edit))
        {
            _model.Submit(_typed);
            _typed = "";
            // Enter takes the keyboard from the field, which the next line needs again.
            ImGui.SetKeyboardFocusHere(-1);
        }

        Typing = ImGui.IsItemActive();
        ImGui.TextDisabled(_model.Hint(_typed) is { Length: > 0 } hint ? hint : " ");
    }

    private unsafe int Edit(ImGuiInputTextCallbackData* data)
    {
        var field = new ImGuiInputTextCallbackDataPtr(data);
        var current = System.Text.Encoding.UTF8.GetString(data->Buf, data->BufTextLen);
        var next = field.EventFlag == ImGuiInputTextFlags.CallbackCompletion
            ? _model.Complete(current).Text
            : field.EventKey == ImGuiKey.UpArrow ? _model.Back(current) : _model.Forward(current);
        if (next != current)
        {
            field.DeleteChars(0, field.BufTextLen);
            field.InsertChars(0, next);
        }

        return 0;
    }

    // An entered line takes the style's dimmed text, a warning and an error their own colors, and
    // a plain line the style's.
    private static Vector4? ColorOf(ConsoleLevel level) => level switch
    {
        ConsoleLevel.Input => ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled],
        ConsoleLevel.Warning => new Vector4(0.80f, 0.52f, 0.05f, 1f),
        ConsoleLevel.Error => new Vector4(0.85f, 0.20f, 0.18f, 1f),
        _ => null,
    };
}
