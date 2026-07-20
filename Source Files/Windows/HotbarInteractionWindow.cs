using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class HotbarInteractionWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly bool utility;
    private readonly uint hotbarId;
    private readonly string elementId;
    private readonly string diagnosticLabel;
    private float scale;
    private Vector2 contentOffset;
    private float slotSize;
    private float gap;
    private int columns;
    private int rows;

    public string ElementId => elementId;

    public HotbarInteractionWindow(Plugin plugin, uint hotbarId)
        : this(
            plugin,
            false,
            hotbarId,
            hotbarId switch
            {
                0u => HudElementIds.ActionBarOne,
                1u => HudElementIds.ActionBarTwo,
                _ => HudElementIds.ActionBarThree,
            },
            $"Combat Hotbar {hotbarId + 1}",
            $"REFrameActionInputs{hotbarId}")
    {
    }

    public HotbarInteractionWindow(Plugin plugin)
        : this(
            plugin,
            true,
            5u,
            HudElementIds.UtilityBars,
            "Utility Hotbar 1",
            "REFrameUtilityInputs")
    {
    }

    public HotbarInteractionWindow(Plugin plugin, bool secondUtility)
        : this(
            plugin,
            true,
            secondUtility ? ReframeHotbarIds.SecondUtility : 5u,
            secondUtility ? HudElementIds.UtilityBarsTwo : HudElementIds.UtilityBars,
            secondUtility ? "Utility Hotbar 2" : "Utility Hotbar 1",
            secondUtility ? "REFrameUtilityInputsTwo" : "REFrameUtilityInputs")
    {
    }

    public HotbarInteractionWindow(Plugin plugin, ReframeAdditionalHotbar bar)
        : this(
            plugin,
            false,
            bar.RuntimeHotbarId,
            bar.ElementId,
            bar.Name,
            $"REFrameAdditionalActionInputs{bar.Id}")
    {
    }

    private HotbarInteractionWindow(
        Plugin plugin,
        bool utility,
        uint hotbarId,
        string elementId,
        string diagnosticLabel,
        string windowId)
        : base(
            $"RE:Frame {diagnosticLabel} Inputs###{windowId}",
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
        this.utility = utility;
        this.hotbarId = hotbarId;
        this.elementId = elementId;
        this.diagnosticLabel = diagnosticLabel;

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
            !Plugin.ClientState.IsLoggedIn ||
            Plugin.GameGui.GameUiHidden ||
            Plugin.ClientState.IsGPosing ||
            plugin.HotbarEditing.IsEnabled)
            return false;


        if (HudElementIds.IsAdditionalCombatHotbar(elementId) &&
            !plugin.AdditionalHotbars.TryGetByElementId(elementId, out _))
            return false;

        var mode = plugin.CurrentHudMode;
        var roleplayHotbarSurface = mode == UiMode.Roleplay;
        return (!HudModeProfileService.IsCalmMode(mode) || roleplayHotbarSurface) &&
               plugin.IsHudElementVisible(elementId, mode);
    }

    public override void PreDraw()
    {
        var mouse = ImGui.GetMousePos();
        IsClickthrough = plugin.NativeWindows.IsPointInsideHudOcclusion(mouse) ||
                         plugin.NativeContextMenus.IsAnyMenuOpen;

        var canvas = plugin.GetRenderedHudCanvas();
        scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var bounds = HudLayout.Resolve(
            plugin.Configuration,
            elementId,
            canvas.Origin,
            canvas.Size,
            plugin.CurrentHudMode);

        Position = bounds.Position;
        PositionCondition = ImGuiCond.Always;
        Size = bounds.Size;
        SizeCondition = ImGuiCond.Always;

        if (utility)
        {
            columns = 4;
            rows = 3;
        }
        else
        {
            var shape = HotbarGridLayouts.Resolve(plugin.Configuration, elementId);
            columns = shape.Columns;
            rows = shape.Rows;
        }

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
        if (IsClickthrough)
            return;

        plugin.HotbarEditing.BeginInputFrame();
        plugin.BarInputDiagnostics.RecordNormalInputFrame(
            ImGui.GetFrameCount(),
            $"HotbarInteractionWindow ({diagnosticLabel})",
            ImGui.GetMousePos());

        for (var slot = 0; slot < ReframeHotbarIds.SlotCount; slot++)
        {
            var row = slot / columns;
            var col = slot % columns;
            var local = contentOffset + new Vector2(col * (slotSize + gap), row * (slotSize + gap));
            DrawSlotButton((uint)slot, local);
        }
    }

    private void DrawSlotButton(uint slotId, Vector2 local)
    {
        var slotReference = new HotbarSlotReference(hotbarId, slotId);
        ImGui.SetCursorPos(local);
        ImGui.InvisibleButton($"##reframe-hotbar-{hotbarId}-{slotId}", new Vector2(slotSize));

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var active = ImGui.IsItemActive();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (hovered)
            plugin.HotbarEditing.RegisterHoveredSlot(slotReference);

        if (clicked)
        {
            plugin.BarInputDiagnostics.RecordInputEvent($"Normal-mode slot click reached {slotReference.Label}");
            if (plugin.AdditionalHotbars.IsVirtualReference(slotReference))
                plugin.AdditionalHotbars.ExecuteVirtual(slotReference);
            else
                plugin.HotbarInput.Execute(hotbarId, slotId);
        }

        DrawFeedback(slotReference, hovered, active);
    }

    private void DrawFeedback(HotbarSlotReference slot, bool hovered, bool active)
    {
        if (!hovered && !active)
            return;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var theme = plugin.CurrentTheme;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                active ? 0.40f : 0.22f)),
            5f * scale);
        draw.AddRect(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                0.88f)),
            5f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, 1.4f * scale));

        if (!hovered)
            return;

        ImGui.BeginTooltip();
        if (plugin.HotbarEditing.TryGetSnapshot(slot, out var snapshot))
            ImGui.TextUnformatted(snapshot.IsEmpty ? "Empty slot" : snapshot.DisplayName);
        ImGui.TextDisabled(slot.Label);
        ImGui.EndTooltip();
    }

    public void Dispose() { }
}
