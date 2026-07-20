using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class PocketRibbonInteractionWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private float scale;

    public PocketRibbonInteractionWindow(Plugin plugin)
        : base("RE:Frame Pocket Ribbon Input###REFramePocketRibbonInput",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        IsOpen = true;
        IsClickthrough = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0f;
    }

    public override bool DrawConditions()
    {
        if (!plugin.Configuration.ShowHudOverlay ||
            plugin.IsHudEditMode ||
            plugin.HotbarEditing.IsEnabled ||
            !Plugin.ClientState.IsLoggedIn ||
            Plugin.GameGui.GameUiHidden ||
            Plugin.ClientState.IsGPosing ||
            plugin.NativeWindows.HasProtectedDutyWindowOpen ||
            plugin.NativeContextMenus.IsAnyMenuOpen ||
            !plugin.IsHudElementVisible(HudElementIds.PocketRibbon, plugin.CurrentHudMode))
            return false;

        var canvas = HudCanvas.Current();
        var ribbon = HudLayout.Resolve(
            plugin.Configuration,
            HudElementIds.PocketRibbon,
            canvas.Origin,
            canvas.Size,
            plugin.CurrentHudMode);
        var ribbonMin = ribbon.Position;
        var ribbonMax = ribbon.Position + ribbon.Size;
        foreach (var native in plugin.NativeWindows.HudOcclusionWindowBounds)
        {
            if (ribbonMin.X < native.Max.X && ribbonMax.X > native.Min.X &&
                ribbonMin.Y < native.Max.Y && ribbonMax.Y > native.Min.Y)
                return false;
        }

        return true;
    }

    public override void PreDraw()
    {
        var canvas = HudCanvas.Current();
        scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var bounds = HudLayout.Resolve(
            plugin.Configuration,
            HudElementIds.PocketRibbon,
            canvas.Origin,
            canvas.Size,
            plugin.CurrentHudMode);

        IsClickthrough = false;
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
        var size = ImGui.GetWindowSize();
        var clicked = ImGui.InvisibleButton("##pocket-ribbon-toggle", size);
        var hovered = ImGui.IsItemHovered();

        if (hovered)
        {
            var theme = plugin.CurrentTheme;
            var min = ImGui.GetWindowPos();
            var max = min + size;
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(new Vector4(
                    theme.Accent.X,
                    theme.Accent.Y,
                    theme.Accent.Z,
                    plugin.IsPocketDeckOpen ? 0.31f : 0.20f)),
                5f * scale);
            draw.AddRect(
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(new Vector4(
                    theme.AccentStrong.X,
                    theme.AccentStrong.Y,
                    theme.AccentStrong.Z,
                    0.92f)),
                5f * scale,
                ImDrawFlags.None,
                MathF.Max(1f, 1.2f * scale));
            ImGui.SetTooltip(plugin.IsPocketDeckOpen ? "Close Pocket" : "Open Pocket");
        }

        if (clicked)
            plugin.TogglePocketDeck();
    }

    public void Dispose() { }
}
