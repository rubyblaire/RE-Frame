using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed class RaidToolsInteractionWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public RaidToolsInteractionWindow(Plugin plugin)
        : base("RE:Frame Raid Tools###REFrameRaidTools",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.plugin = plugin;
        IsClickthrough = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0f;
    }

    public override bool DrawConditions()
        => plugin.Configuration.ShowHudOverlay &&
           plugin.Configuration.EnableHudMouseInteraction &&
           plugin.IsHudElementVisible(HudElementIds.RaidTools, plugin.CurrentHudMode) &&
           !plugin.IsHudEditMode &&
           !plugin.HotbarEditing.IsEnabled &&
           Plugin.ClientState.IsLoggedIn &&
           !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing &&
           plugin.CurrentHudMode == UiMode.RaidReady;

    public override void PreDraw()
    {
        IsClickthrough = true;
        var canvas = HudCanvas.Current();
        var bounds = HudLayout.RaidTools(plugin.Configuration, canvas.Origin, canvas.Size, plugin.CurrentHudMode);
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
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, .75f, 1.35f);
        var gap = 6f * scale;
        var width = MathF.Max(1f, (size.X - gap * 5f) / 4f);
        var height = MathF.Max(1f, size.Y - gap * 2f);
        var theme = plugin.CurrentTheme;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(theme.PanelAlt.X, theme.PanelAlt.Y, theme.PanelAlt.Z, .94f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, .32f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, .48f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, .85f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);

        DrawButton("REPAIR", NativeRaidTool.Repair, string.Empty, new Vector2(gap, gap), new Vector2(width, height));
        DrawButton("WAYMARK", NativeRaidTool.Waymarks, plugin.Configuration.RaidWaymarkCommand, new Vector2(gap * 2f + width, gap), new Vector2(width, height));
        DrawButton("COUNTDOWN", NativeRaidTool.Countdown, plugin.Configuration.RaidCountdownCommand, new Vector2(gap * 3f + width * 2f, gap), new Vector2(width, height));
        DrawButton("STRATEGY", NativeRaidTool.StrategyBoard, plugin.Configuration.RaidStrategyCommand, new Vector2(gap * 4f + width * 3f, gap), new Vector2(width, height));

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
    }

    private void DrawButton(string label, NativeRaidTool tool, string fallbackCommand, Vector2 position, Vector2 size)
    {
        var hovered = HudInput.HitTest(position, size, out _, out _);
        var held = HudInput.LeftHeld(hovered);
        if (hovered)
        {
            var theme = plugin.CurrentTheme;
            var alpha = held ? 0.48f : 0.32f;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, alpha));
        }

        ImGui.SetCursorPos(position);
        ImGui.Button($"{label}##raid-{label}", size);

        if (hovered)
            ImGui.PopStyleColor();

        if (HudInput.LeftClicked(hovered))
        {
            var opened = NativeRaidToolService.TryOpen(tool);
            if (!opened && tool != NativeRaidTool.Waymarks && !string.IsNullOrWhiteSpace(fallbackCommand))
                opened = NativeChatCommandService.TryExecute(fallbackCommand);

            if (!opened)
                Plugin.ChatGui.PrintError($"RE:Frame could not open the native {label.ToLowerInvariant()} window.");
        }

        if (hovered && plugin.AdaptiveState.EffectiveMode != UiMode.RaidReady)
            ImGui.SetTooltip($"Open FFXIV's native {label.ToLowerInvariant()} window.");
    }

    public void Dispose() { }
}
