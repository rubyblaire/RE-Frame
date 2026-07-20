using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using REFrameXIV.Theme;

namespace REFrameXIV.UI;

public static class UiStyles
{
    public const int StyleVarCount = 5;
    public const int StyleColorCount = 18;

    public static void PushWindowStyle(ThemePalette theme, ForgeStyleSettings? forgeStyle = null)
    {
        var style = forgeStyle ?? ForgeStyleSettings.Default;
        style.Normalize();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, style.WindowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, style.ChildRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, style.FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 16f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 10f));

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WithAlpha(theme.Panel, 0.90f));


        ImGui.PushStyleColor(ImGuiCol.TitleBg, WithAlpha(theme.Panel, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, WithAlpha(theme.PanelAlt, 0.99f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, WithAlpha(theme.Panel, 0.94f));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithAlpha(theme.PanelAlt, 0.72f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, WithAlpha(theme.Panel, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(theme.AccentStrong, style.BorderOpacity));
        ImGui.PushStyleColor(ImGuiCol.Text, theme.Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, theme.Muted);


        ImGui.PushStyleColor(ImGuiCol.FrameBg, WithAlpha(theme.ResolvedInput, style.InputOpacity));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, WithAlpha(theme.ResolvedInputHovered, style.InputHoverOpacity));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, WithAlpha(theme.ResolvedInputActive, style.InputActiveOpacity));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, theme.Text);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, WithAlpha(theme.AccentStrong, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, theme.AccentStrong);
        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(theme.ResolvedButton, style.ButtonOpacity));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(theme.ResolvedButtonHovered, style.ButtonHoverOpacity));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(theme.ResolvedButtonActive, style.ButtonActiveOpacity));
    }

    public static void PopWindowStyle()
    {
        ImGui.PopStyleColor(StyleColorCount);
        ImGui.PopStyleVar(StyleVarCount);
    }

    public static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, Math.Clamp(color.W * alpha, 0f, 1f));

    public static void SectionLabel(string text, ThemePalette theme)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, theme.AccentStrong);
        ImGui.TextUnformatted(text.ToUpperInvariant());
        ImGui.PopStyleColor();
    }

    public static bool NavButton(string label, bool selected, ThemePalette theme, float width)
    {
        var resting = selected
            ? theme.ResolvedNavigationSelected
            : theme.ResolvedNavigation;
        ImGui.PushStyleColor(ImGuiCol.Button, resting);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.ResolvedNavigationHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ResolvedNavigationActive);
        var clicked = ImGui.Button(label, new Vector2(width, 38f));
        ImGui.PopStyleColor(3);
        return clicked;
    }

    public static void Divider(ThemePalette theme)
    {
        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        draw.AddLine(start, start + new Vector2(width, 0), ImGui.GetColorU32(WithAlpha(theme.Accent, 0.28f)), 1f);
        ImGui.Dummy(new Vector2(0, 1));
    }

    public static void StatusPill(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(color, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(color, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(color, 0.22f));
        ImGui.Button(text, new Vector2(0, 27f));
        ImGui.PopStyleColor(3);
    }

    public static void Progress(string label, float value, ThemePalette theme, Vector2 size)
    {
        ImGui.TextUnformatted(label);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, theme.AccentStrong);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, WithAlpha(theme.Panel, 0.92f));
        ImGui.ProgressBar(value, size, string.Empty);
        ImGui.PopStyleColor(2);
    }
}
