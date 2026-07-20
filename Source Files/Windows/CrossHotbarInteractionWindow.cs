using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class CrossHotbarInteractionWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly List<CrossSlot> slots = new(16);
    private CrossHotbarState state;
    private float scale;

    public CrossHotbarInteractionWindow(Plugin plugin)
        : base("RE:Frame Cross Hotbar Inputs###REFrameCrossHotbarInputs",
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
            !Plugin.ClientState.IsLoggedIn ||
            (Plugin.GameGui.GameUiHidden || Plugin.ClientState.IsGPosing))
            return false;

        if (plugin.HotbarEditing.IsEnabled)
            return plugin.CrossHotbarState.IsControllerUser;

        var mode = plugin.CurrentHudMode;
        return plugin.Configuration.ReplaceNativeCrossHotbar &&
               (!HudModeProfileService.IsCalmMode(mode) || mode == UiMode.Roleplay) &&
               plugin.IsHudElementVisible(HudElementIds.CrossHotbar, mode) &&
               plugin.CrossHotbarState.TryGetState(out state) &&
               !state.PetHotbarActive;
    }

    public override void PreDraw()
    {
        var mouse = ImGui.GetMousePos();
        var paletteOwnsPointer = plugin.HotbarEditing.IsEnabled &&
                                 plugin.IsPointInsideActionPalette(mouse);
        IsClickthrough = paletteOwnsPointer ||
                         plugin.NativeWindows.IsPointInsideHudOcclusion(mouse) ||
                         plugin.NativeContextMenus.IsAnyMenuOpen;

        var canvas = plugin.GetRenderedHudCanvas();
        scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var mode = plugin.HotbarEditing.IsEnabled && HudModeProfileService.IsCalmMode(plugin.CurrentHudMode)
            ? UiMode.RaidReady
            : plugin.CurrentHudMode;
        var bounds = HudLayout.Resolve(plugin.Configuration, HudElementIds.CrossHotbar, canvas.Origin, canvas.Size, mode);
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
        if (IsClickthrough)
            return;

        plugin.HotbarEditing.BeginInputFrame();
        plugin.BarInputDiagnostics.RecordNormalInputFrame(
            ImGui.GetFrameCount(),
            "CrossHotbarInteractionWindow",
            ImGui.GetMousePos());
        if (plugin.HotbarEditing.IsEnabled)
        {
            var set = Math.Clamp(plugin.HotbarEditing.CrossHotbarSet, 1, 8);
            if (!plugin.HotbarEditing.IsDraggingSlot &&
                !plugin.HotbarEditing.IsDraggingAction &&
                plugin.CrossHotbarState.TryGetState(out var live))
            {
                set = live.SetNumber;
                plugin.HotbarEditing.SetCrossHotbarSet(set);
            }

            state = new CrossHotbarState((uint)(9 + set), set, false, false, false);
        }

        BuildSlots(ImGui.GetWindowSize());
        foreach (var slot in slots)
            DrawSlot(slot);
    }

    private void BuildSlots(Vector2 boundsSize)
    {
        slots.Clear();
        var padding = MathF.Max(2f, 4f * scale);
        var slotGap = MathF.Max(1f, 2f * scale);
        var clusterGap = MathF.Max(7f, 10f * scale);
        var centerGap = MathF.Max(20f, 34f * scale);
        var triggerHeight = Math.Clamp(18f * scale, 14f, MathF.Max(14f, boundsSize.Y * 0.16f));
        var footerHeight = Math.Clamp(22f * scale, 18f, MathF.Max(18f, boundsSize.Y * 0.18f));
        var availableHeight = MathF.Max(1f, boundsSize.Y - triggerHeight - footerHeight);
        var slotFromWidth = (boundsSize.X - padding * 2f - slotGap * 8f - clusterGap * 2f - centerGap) / 12f;
        var slotFromHeight = (availableHeight - slotGap * 2f) / 3f;
        var slotSize = MathF.Max(14f, MathF.Min(slotFromWidth, slotFromHeight));
        var clusterSize = slotSize * 3f + slotGap * 2f;
        var halfWidth = clusterSize * 2f + clusterGap;
        var contentWidth = halfWidth * 2f + centerGap;
        var contentStart = new Vector2(
            (boundsSize.X - contentWidth) * 0.5f,
            triggerHeight + MathF.Max(0f, (availableHeight - clusterSize) * 0.5f));

        AddHalf(0u, contentStart, slotSize, slotGap, clusterSize, clusterGap);
        AddHalf(8u, contentStart + new Vector2(halfWidth + centerGap, 0f), slotSize, slotGap, clusterSize, clusterGap);
    }

    private void AddHalf(uint firstSlot, Vector2 start, float slotSize, float slotGap, float clusterSize, float clusterGap)
    {
        AddCluster(firstSlot, start, slotSize, slotGap);
        AddCluster(firstSlot + 4u, start + new Vector2(clusterSize + clusterGap, 0f), slotSize, slotGap);
    }

    private void AddCluster(uint firstSlot, Vector2 start, float slotSize, float gap)
    {
        var step = slotSize + gap;
        for (var index = 0; index < 4; index++)
        {
            var offset = index switch
            {


                0 => new Vector2(0f, step),
                1 => new Vector2(step, 0f),
                2 => new Vector2(step * 2f, step),
                _ => new Vector2(step, step * 2f),
            };
            slots.Add(new CrossSlot(firstSlot + (uint)index, start + offset, slotSize));
        }
    }

    private void DrawSlot(CrossSlot slot)
    {
        var reference = new HotbarSlotReference(state.HotbarId, slot.SlotId);
        ImGui.SetCursorPos(slot.LocalPosition);
        ImGui.InvisibleButton($"##reframe-cross-{state.HotbarId}-{slot.SlotId}", new Vector2(slot.Size));

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var active = ImGui.IsItemActive();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var released = hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        var editing = plugin.HotbarEditing.IsEnabled;

        if (hovered)
            plugin.HotbarEditing.RegisterHoveredSlot(reference);

        if (editing)
        {
            if (clicked)
            {
                if (plugin.HotbarEditing.IsDraggingAction)
                    AssignPaletteAction(reference);
                else
                    plugin.HotbarEditing.Select(reference);
            }

            if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, MathF.Max(3f, 4f * scale)))
                plugin.HotbarEditing.BeginSlotDrag(reference);

            if (released)
                HandleEditingRelease(reference);
        }
        else if (clicked)
        {
            plugin.BarInputDiagnostics.RecordInputEvent($"Normal-mode XHB click reached {reference.Label}");
            plugin.HotbarInput.Execute(state.HotbarId, slot.SlotId);
        }

        DrawFeedback(reference, hovered, active, editing, slot.Size);

        if (hovered && !editing)
        {
            plugin.HotbarEditing.TryGetSnapshot(reference, out var snapshot);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(snapshot.IsEmpty ? "Empty slot" : snapshot.DisplayName);
            ImGui.TextDisabled(reference.Label);
            ImGui.TextDisabled(snapshot.IsEmpty
                ? "Empty native Cross Hotbar slot."
                : "Left-click to execute this native Cross Hotbar command.");
            ImGui.EndTooltip();
        }
    }

    private void AssignPaletteAction(HotbarSlotReference destination)
    {
        if (!plugin.HotbarEditing.AssignAction(
                destination,
                plugin.HotbarEditing.DraggedActionType,
                plugin.HotbarEditing.DraggedActionId,
                out var message))
        {
            if (!string.IsNullOrWhiteSpace(message))
                Plugin.ChatGui.PrintError(message);
            return;
        }

        plugin.HotbarEditing.Select(destination);
        plugin.HotbarEditing.CancelActionDrag();
        plugin.HotbarEditing.MarkDropHandled();
    }

    private void HandleEditingRelease(HotbarSlotReference destination)
    {
        if (plugin.HotbarEditing.IsDraggingAction)
        {
            AssignPaletteAction(destination);
            return;
        }

        if (plugin.HotbarEditing.DraggedSlot is not { } source)
            return;

        if (source == destination)
        {
            plugin.HotbarEditing.MarkDropHandled();
            return;
        }

        if (!plugin.HotbarEditing.Transfer(
                source,
                destination,
                ImGui.GetIO().KeyCtrl,
                out var message))
        {
            if (!string.IsNullOrWhiteSpace(message))
                Plugin.ChatGui.PrintError(message);
            return;
        }

        plugin.HotbarEditing.MarkDropHandled();
    }

    private void DrawFeedback(HotbarSlotReference slot, bool hovered, bool active, bool editing, float size)
    {
        var selected = editing && plugin.HotbarEditing.SelectedSlot == slot;
        var source = editing && plugin.HotbarEditing.DraggedSlot == slot;
        if (!hovered && !active && !selected && !source)
            return;

        var min = ImGui.GetItemRectMin();
        var max = min + new Vector2(size);
        var theme = plugin.CurrentTheme;
        var draw = ImGui.GetWindowDrawList();
        if (hovered || active)
        {
            draw.AddRectFilled(
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(new Vector4(
                    theme.AccentStrong.X,
                    theme.AccentStrong.Y,
                    theme.AccentStrong.Z,
                    active ? 0.40f : 0.22f)),
                5f * scale);
        }

        draw.AddRect(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                selected || source ? 1f : 0.88f)),
            5f * scale,
            ImDrawFlags.None,
            selected || source ? MathF.Max(2f, 2.3f * scale) : MathF.Max(1f, 1.4f * scale));
    }

    public void Dispose() { }

    private readonly record struct CrossSlot(uint SlotId, Vector2 LocalPosition, float Size);
}
