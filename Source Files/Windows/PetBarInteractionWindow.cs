using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed unsafe class PetBarInteractionWindow : Window, IDisposable
{
    private const uint PetHotbarId = ReframeHotbarIds.PetBar;
    private const int SlotCount = ReframeHotbarIds.SlotCount;
    private readonly Plugin plugin;
    private float scale;
    private Vector2 contentOffset;
    private float slotSize;
    private float gap;
    private int columns;
    private int rows;

    public PetBarInteractionWindow(Plugin plugin)
        : base("RE:Frame Pet Bar Inputs###REFramePetBarInputs",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground)
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
           plugin.IsHudElementVisible(HudElementIds.PetBar, plugin.CurrentHudMode) &&
           plugin.PetBarState.IsActive &&
           !plugin.IsHudEditMode &&
           !plugin.HotbarEditing.IsEnabled &&
           Plugin.ClientState.IsLoggedIn &&
           !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing &&
           (!HudModeProfileService.IsCalmMode(plugin.CurrentHudMode) || plugin.CurrentHudMode == UiMode.Roleplay);

    public override void PreDraw()
    {
        IsClickthrough = true;
        var canvas = HudCanvas.Current();
        scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var bounds = HudLayout.PetBar(plugin.Configuration, canvas.Origin, canvas.Size, plugin.CurrentHudMode);
        Position = bounds.Position;
        PositionCondition = ImGuiCond.Always;
        Size = bounds.Size;
        SizeCondition = ImGuiCond.Always;

        var shape = HotbarGridLayouts.Resolve(plugin.Configuration, HudElementIds.PetBar);
        columns = shape.Columns;
        rows = shape.Rows;
        gap = MathF.Max(1f, 3f * scale);
        slotSize = MathF.Max(18f, MathF.Min(
            (bounds.Size.X - gap * (columns - 1)) / columns,
            (bounds.Size.Y - gap * (rows - 1)) / rows));
        var content = new Vector2(
            columns * slotSize + (columns - 1) * gap,
            rows * slotSize + (rows - 1) * gap);
        contentOffset = (bounds.Size - content) * 0.5f;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw()
    {
        if (plugin.NativeWindows.IsPointInsideHudOcclusion(ImGui.GetMousePos()))
            return;

        for (var slot = 0; slot < SlotCount; slot++)
        {
            var row = slot / columns;
            var col = slot % columns;
            var local = contentOffset + new Vector2(col * (slotSize + gap), row * (slotSize + gap));
            var hovered = HudInput.HitTest(local, new Vector2(slotSize), out var min, out var max);
            var held = HudInput.LeftHeld(hovered);
            if (hovered || held)
            {
                var theme = plugin.CurrentTheme;
                var alpha = held ? 0.42f : 0.24f;
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, alpha)), 5f * scale);
                draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.92f)), 5f * scale, ImDrawFlags.None, MathF.Max(1f, 1.5f * scale));
            }
            if (HudInput.LeftClicked(hovered))
                plugin.HotbarInput.Execute(PetHotbarId, (uint)slot);
            if (hovered && plugin.AdaptiveState.EffectiveMode != UiMode.RaidReady)
            {
                var reference = new HotbarSlotReference(PetHotbarId, (uint)slot);
                plugin.HotbarEditing.TryGetSnapshot(reference, out var snapshot);
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(snapshot.IsEmpty ? "Empty slot" : snapshot.DisplayName);
                ImGui.TextDisabled(reference.Label);
                ImGui.TextDisabled(snapshot.IsEmpty
                    ? "Empty native pet slot."
                    : "Left-click to execute this native pet command.");
                ImGui.EndTooltip();
            }
        }
    }

    public void Dispose() { }
}
