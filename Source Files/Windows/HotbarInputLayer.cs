using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class HotbarInputLayer
{
    private readonly Plugin plugin;

    public HotbarInputLayer(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {


        if (plugin.HotbarEditing.IsEnabled)
            return;

        if (!plugin.Configuration.ShowHudOverlay ||
            plugin.IsHudEditMode ||
            !Plugin.ClientState.IsLoggedIn ||
            (Plugin.GameGui.GameUiHidden || Plugin.ClientState.IsGPosing))
            return;

        try
        {
            var editing = plugin.HotbarEditing.IsEnabled;
            var currentMode = plugin.CurrentHudMode;
            var layoutMode = editing && HudModeProfileService.IsCalmMode(currentMode)
                ? UiMode.RaidReady
                : currentMode;


            var canvas = plugin.GetRenderedHudCanvas();
            var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);

            plugin.HotbarEditing.BeginInputFrame();


            var keyboardEditing = editing && !plugin.CrossHotbarState.IsControllerUser;
            if (keyboardEditing || (!editing && !HudModeProfileService.IsCalmMode(currentMode) && plugin.IsHudElementVisible(HudElementIds.ActionBarOne, currentMode)))
                DrawConfiguredCombatBar("REFrameDirectBar1", 0u, HudLayout.ActionBarOne(plugin.Configuration, canvas.Origin, canvas.Size, layoutMode), scale, editing);

            if (keyboardEditing || (!editing && !HudModeProfileService.IsCalmMode(currentMode) && plugin.IsHudElementVisible(HudElementIds.ActionBarTwo, currentMode)))
                DrawConfiguredCombatBar("REFrameDirectBar2", 1u, HudLayout.ActionBarTwo(plugin.Configuration, canvas.Origin, canvas.Size, layoutMode), scale, editing);

            if (keyboardEditing || (!editing && !HudModeProfileService.IsCalmMode(currentMode) && plugin.IsHudElementVisible(HudElementIds.ActionBarThree, currentMode)))
                DrawConfiguredCombatBar("REFrameDirectBar3", 2u, HudLayout.ActionBarThree(plugin.Configuration, canvas.Origin, canvas.Size, layoutMode), scale, editing);


            if (!editing && !HudModeProfileService.IsCalmMode(currentMode) && plugin.IsHudElementVisible(HudElementIds.UtilityBars, currentMode))
                DrawLinearBar("REFrameDirectUtility", 5u, HudLayout.UtilityBars(plugin.Configuration, canvas.Origin, canvas.Size, currentMode), 4, 3, scale, false);

            DrawCrossHotbarIfVisible(canvas, currentMode, layoutMode, scale, editing);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame direct hotbar input layer failed for this frame.");
        }
    }

    private void DrawCrossHotbarIfVisible(HudCanvasInfo canvas, UiMode currentMode, UiMode layoutMode, float scale, bool editing)
    {
        var editingCross = editing && plugin.CrossHotbarState.IsControllerUser;
        var hasLiveCross = plugin.CrossHotbarState.TryGetState(out var state);
        var visibleByProfile = plugin.Configuration.ReplaceNativeCrossHotbar &&
                               !HudModeProfileService.IsCalmMode(currentMode) &&
                               plugin.IsHudElementVisible(HudElementIds.CrossHotbar, currentMode);

        if (!editingCross && !visibleByProfile)
            return;

        if (editingCross)
        {
            var set = Math.Clamp(plugin.HotbarEditing.CrossHotbarSet, 1, 8);
            if (!plugin.HotbarEditing.IsDraggingAction &&
                !plugin.HotbarEditing.IsDraggingSlot &&
                hasLiveCross)
            {
                set = state.SetNumber;
                plugin.HotbarEditing.SetCrossHotbarSet(set);
            }

            state = new CrossHotbarState((uint)(9 + set), set, false, false, false);
            hasLiveCross = true;
        }

        if (!hasLiveCross || state.PetHotbarActive)
            return;

        var bounds = HudLayout.Resolve(plugin.Configuration, HudElementIds.CrossHotbar, canvas.Origin, canvas.Size, layoutMode);
        DrawCrossBar("REFrameDirectCross", state.HotbarId, bounds, scale, editing);
    }

    private void DrawConfiguredCombatBar(
        string windowId,
        uint hotbarId,
        HudBounds bounds,
        float scale,
        bool editing)
    {
        var shape = HotbarGridLayouts.Resolve(plugin.Configuration, hotbarId);
        DrawLinearBar(windowId, hotbarId, bounds, shape.Columns, shape.Rows, scale, editing);
    }

    private void DrawLinearBar(
        string windowId,
        uint hotbarId,
        HudBounds bounds,
        int columns,
        int rows,
        float scale,
        bool editing)
    {
        var suppressInput = ShouldSuppressInput(editing);
        BeginExactWindow(windowId, bounds, suppressInput);
        try
        {
            if (suppressInput)
                return;

            var gap = MathF.Max(1f, 3f * scale);
            var slotSize = MathF.Max(18f, MathF.Min(
                (bounds.Size.X - gap * (columns - 1)) / columns,
                (bounds.Size.Y - gap * (rows - 1)) / rows));
            var content = new Vector2(
                columns * slotSize + (columns - 1) * gap,
                rows * slotSize + (rows - 1) * gap);
            var contentOffset = (bounds.Size - content) * 0.5f;

            for (var slotIndex = 0; slotIndex < columns * rows; slotIndex++)
            {
                var row = slotIndex / columns;
                var column = slotIndex % columns;
                var local = contentOffset + new Vector2(
                    column * (slotSize + gap),
                    row * (slotSize + gap));
                DrawSlotButton(
                    $"##{windowId}-slot-{slotIndex}",
                    new HotbarSlotReference(hotbarId, (uint)slotIndex),
                    local,
                    slotSize,
                    scale,
                    editing);
            }
        }
        finally
        {
            EndExactWindow();
        }
    }

    private void DrawCrossBar(string windowId, uint hotbarId, HudBounds bounds, float scale, bool editing)
    {
        var suppressInput = ShouldSuppressInput(editing);
        BeginExactWindow(windowId, bounds, suppressInput);
        try
        {
            if (suppressInput)
                return;

            var padding = MathF.Max(2f, 4f * scale);
            var slotGap = MathF.Max(1f, 2f * scale);
            var clusterGap = MathF.Max(7f, 10f * scale);
            var centerGap = MathF.Max(20f, 34f * scale);
            var triggerHeight = Math.Clamp(18f * scale, 14f, MathF.Max(14f, bounds.Size.Y * 0.16f));
            var footerHeight = Math.Clamp(22f * scale, 18f, MathF.Max(18f, bounds.Size.Y * 0.18f));
            var availableHeight = MathF.Max(1f, bounds.Size.Y - triggerHeight - footerHeight);
            var slotFromWidth = (bounds.Size.X - padding * 2f - slotGap * 8f - clusterGap * 2f - centerGap) / 12f;
            var slotFromHeight = (availableHeight - slotGap * 2f) / 3f;
            var slotSize = MathF.Max(14f, MathF.Min(slotFromWidth, slotFromHeight));
            var clusterSize = slotSize * 3f + slotGap * 2f;
            var halfWidth = clusterSize * 2f + clusterGap;
            var contentWidth = halfWidth * 2f + centerGap;
            var contentStart = new Vector2(
                (bounds.Size.X - contentWidth) * 0.5f,
                triggerHeight + MathF.Max(0f, (availableHeight - clusterSize) * 0.5f));

            DrawCrossHalf(windowId, hotbarId, 0u, contentStart, slotSize, slotGap, clusterSize, clusterGap, scale, editing);
            DrawCrossHalf(windowId, hotbarId, 8u, contentStart + new Vector2(halfWidth + centerGap, 0f), slotSize, slotGap, clusterSize, clusterGap, scale, editing);
        }
        finally
        {
            EndExactWindow();
        }
    }

    private void DrawCrossHalf(
        string windowId,
        uint hotbarId,
        uint firstSlot,
        Vector2 start,
        float slotSize,
        float slotGap,
        float clusterSize,
        float clusterGap,
        float scale,
        bool editing)
    {
        DrawCrossCluster(windowId, hotbarId, firstSlot, start, slotSize, slotGap, scale, editing);
        DrawCrossCluster(windowId, hotbarId, firstSlot + 4u, start + new Vector2(clusterSize + clusterGap, 0f), slotSize, slotGap, scale, editing);
    }

    private void DrawCrossCluster(
        string windowId,
        uint hotbarId,
        uint firstSlot,
        Vector2 start,
        float slotSize,
        float gap,
        float scale,
        bool editing)
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

            var slotId = firstSlot + (uint)index;
            DrawSlotButton(
                $"##{windowId}-slot-{slotId}",
                new HotbarSlotReference(hotbarId, slotId),
                start + offset,
                slotSize,
                scale,
                editing);
        }
    }

    private void DrawSlotButton(
        string id,
        HotbarSlotReference slot,
        Vector2 localPosition,
        float slotSize,
        float scale,
        bool editing)
    {
        ImGui.SetCursorPos(localPosition);
        ImGui.InvisibleButton(id, new Vector2(slotSize));

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var active = ImGui.IsItemActive();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var released = hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        if (hovered)
            plugin.HotbarEditing.RegisterHoveredSlot(slot);

        if (editing)
        {
            if (clicked)
            {
                if (plugin.HotbarEditing.IsDraggingAction)
                    AssignPaletteAction(slot);
                else
                    plugin.HotbarEditing.Select(slot);
            }

            if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, MathF.Max(3f, 4f * scale)))
                plugin.HotbarEditing.BeginSlotDrag(slot);

            if (released)
                HandleEditingRelease(slot);
        }
        else if (clicked)
        {
            plugin.HotbarInput.Execute(slot.HotbarId, slot.SlotId);
        }

        DrawFeedback(slot, hovered, active, editing, scale);
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

        if (!plugin.HotbarEditing.Transfer(source, destination, ImGui.GetIO().KeyCtrl, out var message))
        {
            if (!string.IsNullOrWhiteSpace(message))
                Plugin.ChatGui.PrintError(message);
            return;
        }

        plugin.HotbarEditing.MarkDropHandled();
    }

    private void DrawFeedback(HotbarSlotReference slot, bool hovered, bool active, bool editing, float scale)
    {
        var selected = editing && plugin.HotbarEditing.SelectedSlot == slot;
        var source = editing && plugin.HotbarEditing.DraggedSlot == slot;
        if (!hovered && !active && !selected && !source)
            return;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var theme = plugin.CurrentTheme;
        var draw = ImGui.GetForegroundDrawList();

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
                selected || source ? 1f : 0.92f)),
            5f * scale,
            ImDrawFlags.None,
            selected || source ? MathF.Max(2f, 2.4f * scale) : MathF.Max(1f, 1.6f * scale));

        if (hovered && editing)
        {
            ImGui.BeginTooltip();
            plugin.HotbarEditing.TryGetSnapshot(slot, out var snapshot);
            ImGui.TextUnformatted(snapshot.IsEmpty ? "Empty slot" : snapshot.DisplayName);
            ImGui.TextDisabled(slot.Label);
            ImGui.TextDisabled(plugin.HotbarEditing.IsDraggingAction
                ? $"Release to place {plugin.HotbarEditing.DraggedActionName}."
                : "Click to select. Drag to move or swap. Ctrl-drag copies.");
            ImGui.EndTooltip();
        }
    }

    private bool ShouldSuppressInput(bool editing)
    {
        var mouse = ImGui.GetMousePos();


        if (editing)
            return plugin.IsPointInsideActionPalette(mouse) || plugin.NativeContextMenus.IsAnyMenuOpen;

        return plugin.NativeContextMenus.IsAnyMenuOpen ||
               plugin.NativeWindows.IsPointInsideHudOcclusion(mouse);
    }

    private static void BeginExactWindow(string id, HudBounds bounds, bool suppressInput)
    {
        ImGui.SetNextWindowPos(bounds.Position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(bounds.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        var flags = ImGuiWindowFlags.NoDecoration |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoBringToFrontOnFocus |
                    ImGuiWindowFlags.NoNav |
                    ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse |
                    ImGuiWindowFlags.NoBackground;

        if (suppressInput)
            flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMouseInputs;

        ImGui.Begin($"##{id}", flags);
    }

    private static void EndExactWindow()
    {
        ImGui.End();
        ImGui.PopStyleVar(2);
    }
}
