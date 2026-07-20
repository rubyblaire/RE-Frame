using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed class LeisureDockInteractionWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private float scale;

    public LeisureDockInteractionWindow(Plugin plugin)
        : base("RE:Frame Leisure Dock Inputs###REFrameLeisureDockInputs",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        IsClickthrough = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0f;
    }

    public override bool DrawConditions()
    {
        var mode = plugin.CurrentHudMode;
        return plugin.Configuration.ShowHudOverlay &&
               plugin.Configuration.EnableHudMouseInteraction &&
               !plugin.IsHudEditMode &&
               !plugin.HotbarEditing.IsEnabled &&
               Plugin.ClientState.IsLoggedIn &&
               !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing &&
               mode is UiMode.Leisure or UiMode.Roleplay or UiMode.Work &&
               !plugin.ShouldUseWorkstationDock(mode) &&
               plugin.IsHudElementVisible(HudElementIds.LeisureDock, mode);
    }

    public override void PreDraw()
    {
        IsClickthrough = true;
        var canvas = HudCanvas.Current();
        scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var bounds = HudLayout.LeisureDock(plugin.Configuration, canvas.Origin, canvas.Size, plugin.CurrentHudMode);
        Position = bounds.Position;
        PositionCondition = ImGuiCond.Always;
        Size = bounds.Size;
        SizeCondition = ImGuiCond.Always;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw()
    {
        if (plugin.NativeWindows.IsPointInsideHudOcclusion(ImGui.GetMousePos()))
            return;

        var width = ImGui.GetWindowSize().X / 4f;
        DrawButton(0, "COMMAND", () =>
        {
            plugin.CloseLeisureDockPopup();
            plugin.OpenCommandPalette();
        });
        DrawButton(1, "TRAVEL", () => plugin.ToggleLeisureDockPopup(LeisureDockPopup.Travel));
        DrawButton(2, "APPEARANCE", () => plugin.ToggleLeisureDockPopup(LeisureDockPopup.Appearance));
        DrawButton(3, "DOCKS", () => plugin.ToggleLeisureDockPopup(LeisureDockPopup.Docks));

        void DrawButton(int index, string label, Action action)
        {
            var local = new Vector2(index * width, 0f);
            var buttonSize = new Vector2(width, ImGui.GetWindowSize().Y);
            var hovered = HudInput.HitTest(local, buttonSize, out var min, out var max);
            var held = HudInput.LeftHeld(hovered);
            if (hovered || held)
            {
                var theme = plugin.CurrentTheme;
                var stateColor = held ? theme.ResolvedDockButtonActive : theme.ResolvedDockButtonHovered;
                var hudOpacity = Math.Clamp(plugin.Configuration.HudOpacity, 0.35f, 1f);
                var fill = new Vector4(
                    stateColor.X,
                    stateColor.Y,
                    stateColor.Z,
                    Math.Clamp(stateColor.W * hudOpacity, 0f, 1f));
                var borderColor = theme.HasExtendedColors
                    ? theme.ResolvedDockBorder
                    : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.94f);
                var border = new Vector4(
                    borderColor.X,
                    borderColor.Y,
                    borderColor.Z,
                    Math.Clamp(borderColor.W * hudOpacity, 0f, 1f));
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(fill), 5f * scale);
                draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(border), 5f * scale, ImDrawFlags.None, 1f);
            }
            if (HudInput.LeftClicked(hovered)) action();
            if (hovered) ImGui.SetTooltip(label);
        }
    }

    public void Dispose() { }
}
