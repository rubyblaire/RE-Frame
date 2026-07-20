using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using REFrameXIV.Models;
using REFrameXIV.Services;

namespace REFrameXIV.UI;


internal static unsafe class HudActorInputRouter
{
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;

    private static bool previousLeftDown;
    private static bool previousRightDown;
    private static bool leftPressedThisFrame;
    private static bool leftReleasedThisFrame;
    private static bool rightClickedThisFrame;
    private static bool leftDownThisFrame;
    private static ulong pressedActorId;
    private static IGameObject? hoveredActorThisFrame;
    private static readonly Dictionary<(uint HotbarId, uint SlotId), bool> PreviousBindingStates = new();
    private static PendingAltExecution? pendingAltExecution;
    private static NativeRaidTool? pressedRaidTool;
    private static LeisureDockAction? pressedLeisureDockAction;
    private static LeisureDockPopupAction? pressedLeisureDockPopupAction;
    private static WorkstationDockAction? pressedWorkstationDockAction;
    private static int? pressedWorkstationDockPopupIndex;
    private static RoleplayDockAction? pressedRoleplayDockAction;
    private static int? pressedRoleplayDockPopupIndex;
    private static QuestDockAction? pressedQuestDockAction;
    private static bool ardynChantPressed;
    private static bool clockPressed;
    private static HotbarSlotReference? pressedHotbarSlot;
    private static Vector2 hotbarPressPosition;
    private static bool draggingHotbarSlot;

    public static void Process(Plugin plugin, ImDrawListPtr draw, Vector2 origin, Vector2 viewportSize, bool pointerReserved = false)
    {


        if (!UpdateNativePointer(out var mouse))
            return;


        mouse += origin;

        hoveredActorThisFrame = null;

        if (pointerReserved)
        {
            UpdateHotbarBindingFallback(plugin, null);
            FinishPointerFrame();
            return;
        }

        if (!plugin.Configuration.ShowHudOverlay ||
            plugin.IsHudEditMode ||
            !Plugin.ClientState.IsLoggedIn ||
            Plugin.GameGui.GameUiHidden ||
            plugin.NativeContextMenus.IsAnyMenuOpen)
        {
            UpdateHotbarBindingFallback(plugin, null);
            FinishPointerFrame();
            return;
        }


        if (plugin.NativeWindows.IsPointInsideHudOcclusion(mouse))
        {
            UpdateHotbarBindingFallback(plugin, null);
            FinishPointerFrame();
            return;
        }


        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var raidStatusScale = HudRenderer.ResolveRaidStatusDisplayScale(
            plugin.Configuration.InterfaceScale,
            viewportSize);
        var mode = plugin.CurrentHudMode;
        var workstationDockActive = plugin.ShouldUseWorkstationDock(mode);


        if (plugin.HotbarEditing.IsEnabled)
        {
            UpdateHotbarBindingFallback(plugin, null);
            FinishPointerFrame();
            return;
        }


        if (plugin.IsHudElementVisible(HudElementIds.LeisureDock, mode))
        {
            var dockBounds = HudLayout.LeisureDock(plugin.Configuration, origin, viewportSize, mode);

            if (plugin.ActiveLeisureDockPopup != LeisureDockPopup.None)
            {
                if (ProcessLeisureDockPopup(plugin, draw, dockBounds, origin, viewportSize, scale, mouse))
                {
                    UpdateHotbarBindingFallback(plugin, null);
                    FinishPointerFrame();
                    return;
                }

                if (leftPressedThisFrame && !Contains(dockBounds, mouse))
                    plugin.CloseLeisureDockPopup();
            }

            if (plugin.ActiveRoleplayDockPopup != RoleplayDockPopup.None)
            {
                if (ProcessRoleplayDockPopup(plugin, draw, dockBounds, origin, viewportSize, scale, mouse))
                {
                    UpdateHotbarBindingFallback(plugin, null);
                    FinishPointerFrame();
                    return;
                }

                if (leftPressedThisFrame && !Contains(dockBounds, mouse))
                    plugin.CloseRoleplayDockPopup();
            }

            if (plugin.ActiveWorkstationDockPopup != WorkstationDockPopup.None)
            {
                if (ProcessWorkstationDockPopup(plugin, draw, dockBounds, origin, viewportSize, scale, mouse))
                {
                    UpdateHotbarBindingFallback(plugin, null);
                    FinishPointerFrame();
                    return;
                }

                if (leftPressedThisFrame && !Contains(dockBounds, mouse))
                    plugin.CloseWorkstationDockPopup();
            }
        }


        if (plugin.IsHudElementVisible(HudElementIds.Location, mode))
        {
            var locationBounds = HudLayout.Resolve(plugin.Configuration, HudElementIds.Location, origin, viewportSize, mode);
            if (ProcessArdynChantButton(plugin, draw, locationBounds, scale, mouse) ||
                ProcessClock(plugin, draw, locationBounds, scale, mouse))
            {
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }
        }

        var jobRibbonBounds = HudLayout.Resolve(
            plugin.Configuration,
            HudElementIds.JobRibbon,
            origin,
            viewportSize,
            mode);
        var pocketRibbonBounds = HudLayout.Resolve(
            plugin.Configuration,
            HudElementIds.PocketRibbon,
            origin,
            viewportSize,
            mode);

        if (plugin.IsPocketDeckOpen)
        {


            if (plugin.IsPointInsidePocketDeck(mouse))
            {
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }

            if (leftPressedThisFrame && !Contains(pocketRibbonBounds, mouse))
            {
                plugin.ClosePocketDeck();
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }
        }

        if (plugin.IsJobSwitcherOpen)
        {


            if (plugin.IsPointInsideJobSwitcher(mouse))
            {
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }

            if (leftPressedThisFrame && !Contains(jobRibbonBounds, mouse))
            {
                plugin.CloseJobSwitcher();
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }
        }

        if (plugin.IsHudElementVisible(HudElementIds.JobRibbon, mode) &&
            Contains(jobRibbonBounds, mouse))
        {


            UpdateHotbarBindingFallback(plugin, null);
            FinishPointerFrame();
            return;
        }

        if (plugin.IsHudElementVisible(HudElementIds.PocketRibbon, mode) &&
            Contains(pocketRibbonBounds, mouse))
        {

            UpdateHotbarBindingFallback(plugin, null);
            FinishPointerFrame();
            return;
        }


        if (plugin.IsHudElementVisible(HudElementIds.Minimap, mode))
        {
            var minimapBounds = HudLayout.Resolve(plugin.Configuration, HudElementIds.Minimap, origin, viewportSize, mode);
            if (Contains(minimapBounds, mouse) || plugin.NativeHudVisibility.IsPointInsideIntegratedMinimap(mouse))
            {
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }
        }


        if (mode == UiMode.RaidReady && Plugin.ObjectTable.LocalPlayer is IBattleChara statusOwner)
        {
            if (plugin.IsHudElementVisible(HudElementIds.RaidBuffs, mode) &&
                ProcessStandaloneStatusPanel(
                    plugin,
                    statusOwner,
                    HudLayout.Resolve(plugin.Configuration, HudElementIds.RaidBuffs, origin, viewportSize, mode),
                    false,
                    raidStatusScale,
                    mouse))
            {
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }

            if (plugin.IsHudElementVisible(HudElementIds.RaidDebuffs, mode) &&
                ProcessStandaloneStatusPanel(
                    plugin,
                    statusOwner,
                    HudLayout.Resolve(plugin.Configuration, HudElementIds.RaidDebuffs, origin, viewportSize, mode),
                    true,
                    raidStatusScale,
                    mouse))
            {
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }
        }


        if (plugin.IsHudElementVisible(HudElementIds.Party, mode) && Plugin.PartyList.Length > 1)
            ProcessParty(plugin, draw, HudLayout.Resolve(plugin.Configuration, HudElementIds.Party, origin, viewportSize, mode), scale, mouse);

        if (plugin.AllianceFrames.IsAlliance)
        {
            if (plugin.IsHudElementVisible(HudElementIds.AllianceOne, mode))
                ProcessAllianceGroup(plugin, draw, HudLayout.Resolve(plugin.Configuration, HudElementIds.AllianceOne, origin, viewportSize, mode), 0, scale, mouse);
            if (plugin.IsHudElementVisible(HudElementIds.AllianceTwo, mode))
                ProcessAllianceGroup(plugin, draw, HudLayout.Resolve(plugin.Configuration, HudElementIds.AllianceTwo, origin, viewportSize, mode), 1, scale, mouse);
        }

        if (!HudModeProfileService.IsCalmMode(mode) &&
            Plugin.TargetManager.Target is { } target && target.IsValid())
        {
            if (plugin.IsHudElementVisible(HudElementIds.Target, mode))
                ProcessTarget(plugin, draw, HudLayout.Resolve(plugin.Configuration, HudElementIds.Target, origin, viewportSize, mode), target, scale, mouse);

            if (plugin.IsHudElementVisible(HudElementIds.TargetOfTarget, mode) &&
                target is IBattleChara targetBattle &&
                ResolveTargetsTarget(targetBattle) is { } targetOfTarget && targetOfTarget.IsValid())
            {
                ProcessActorRegion(
                    plugin,
                    draw,
                    targetOfTarget,
                    HudLayout.Resolve(plugin.Configuration, HudElementIds.TargetOfTarget, origin, viewportSize, mode),
                    scale,
                    mouse,
                    "Target's target");
            }
        }

        if (plugin.IsHudElementVisible(HudElementIds.Focus, mode) &&
            !HudModeProfileService.IsCalmMode(mode) &&
            Plugin.TargetManager.FocusTarget is { } focus && focus.IsValid())
        {
            ProcessActorRegion(plugin, draw, focus,
                HudLayout.Resolve(plugin.Configuration, HudElementIds.Focus, origin, viewportSize, mode),
                scale, mouse, "Focus target");
        }

        if (plugin.IsHudElementVisible(HudElementIds.EnemyList, mode) && !HudModeProfileService.IsCalmMode(mode))
            ProcessEnemyList(plugin, draw, HudLayout.Resolve(plugin.Configuration, HudElementIds.EnemyList, origin, viewportSize, mode), scale, mouse);

        if (mode == UiMode.Work &&
            plugin.IsHudElementVisible(HudElementIds.Player, mode) &&
            Plugin.ObjectTable.LocalPlayer is IBattleChara workPlayer)
        {
            if (ProcessWorkPlayerStatuses(
                plugin,
                workPlayer,
                HudLayout.Resolve(plugin.Configuration, HudElementIds.Player, origin, viewportSize, mode),
                scale,
                mouse))
            {
                UpdateHotbarBindingFallback(plugin, null);
                FinishPointerFrame();
                return;
            }
        }

        if (plugin.IsHudElementVisible(HudElementIds.RaidTools, mode) && mode == UiMode.RaidReady)
            ProcessRaidTools(plugin, draw, HudLayout.RaidTools(plugin.Configuration, origin, viewportSize, mode), scale, mouse);
        if (plugin.IsHudElementVisible(HudElementIds.RaidersKit, mode) && mode == UiMode.RaidReady)
            ProcessRaidersKit(plugin, draw, HudLayout.Resolve(plugin.Configuration, HudElementIds.RaidersKit, origin, viewportSize, mode), scale, mouse);

        if (plugin.Configuration.EnableHudMouseInteraction &&
            plugin.IsHudElementVisible(HudElementIds.LeisureDock, mode) &&
            ((mode is UiMode.Leisure or UiMode.Roleplay or UiMode.Quest or UiMode.Work) || workstationDockActive))
        {
            var dockBounds = HudLayout.LeisureDock(plugin.Configuration, origin, viewportSize, mode);
            if (mode == UiMode.Quest)
                ProcessQuestDock(plugin, draw, dockBounds, scale, mouse);
            else if (workstationDockActive)
                ProcessWorkstationDock(plugin, draw, dockBounds, scale, mouse);
            else if (mode == UiMode.Roleplay)
                ProcessRoleplayDock(plugin, draw, dockBounds, scale, mouse);
            else
                ProcessLeisureDock(plugin, draw, dockBounds, scale, mouse);
        }


        UpdateHotbarBindingFallback(plugin, hoveredActorThisFrame);
        FinishPointerFrame();
    }

    private static bool ProcessClock(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {


        var horizontalInset = MathF.Max(8f, 10f * scale);
        var hitPosition = new Vector2(
            bounds.Position.X + horizontalInset,
            bounds.Position.Y + bounds.Size.Y * 0.52f);
        var hitSize = new Vector2(
            MathF.Max(1f, bounds.Size.X - horizontalInset * 2f),
            MathF.Max(1f, bounds.Position.Y + bounds.Size.Y - hitPosition.Y));
        var hitBounds = new HudBounds(hitPosition, hitSize);

        if (!Contains(hitBounds, mouse))
            return false;

        if (leftPressedThisFrame)
        {
            clockPressed = true;
            plugin.CycleClockMode();
        }

        var theme = plugin.CurrentTheme;
        var held = leftDownThisFrame && clockPressed;
        var alpha = held ? 0.42f : 0.22f;
        draw.AddRectFilled(
            hitBounds.Position,
            hitBounds.Position + hitBounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                alpha)),
            4f * scale);
        draw.AddRect(
            hitBounds.Position,
            hitBounds.Position + hitBounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                0.88f)),
            4f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, 1.2f * scale));

        ImGui.SetTooltip("Clock: click the world/time row to cycle ET → ST → LT.");
        return true;
    }

    private static bool ProcessArdynChantButton(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds locationBounds,
        float scale,
        Vector2 mouse)
    {
        if (!plugin.Configuration.ShowArdynChantButton)
            return false;

        var button = HudLayout.LocationChantButton(locationBounds, scale);
        if (!Contains(button, mouse))
            return false;

        if (leftPressedThisFrame)
        {
            ardynChantPressed = true;
            plugin.PlayArdynChant();
        }

        var theme = plugin.CurrentTheme;
        var held = leftDownThisFrame && ardynChantPressed;
        draw.AddRectFilled(
            button.Position,
            button.Position + button.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                held ? 0.42f : 0.22f)),
            6f * scale);
        draw.AddRect(
            button.Position,
            button.Position + button.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                0.92f)),
            6f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, 1.3f * scale));

        ImGui.SetTooltip("Play Ardyn's Chant once.");
        return true;
    }

    private static bool ProcessHotbars(
        Plugin plugin,
        ImDrawListPtr draw,
        Vector2 origin,
        Vector2 viewportSize,
        float scale,
        Vector2 mouse)
    {
        var mode = plugin.CurrentHudMode;
        if (plugin.IsHudElementVisible(HudElementIds.ActionBarOne, mode) &&
            ProcessHotbarGrid(plugin, draw, HudLayout.ActionBarOne(plugin.Configuration, origin, viewportSize, mode), 0u, HotbarGridLayouts.Resolve(plugin.Configuration, 0u).Columns, HotbarGridLayouts.Resolve(plugin.Configuration, 0u).Rows, scale, mouse, false))
            return true;
        if (plugin.IsHudElementVisible(HudElementIds.ActionBarTwo, mode) &&
            ProcessHotbarGrid(plugin, draw, HudLayout.ActionBarTwo(plugin.Configuration, origin, viewportSize, mode), 1u, HotbarGridLayouts.Resolve(plugin.Configuration, 1u).Columns, HotbarGridLayouts.Resolve(plugin.Configuration, 1u).Rows, scale, mouse, false))
            return true;
        if (plugin.IsHudElementVisible(HudElementIds.ActionBarThree, mode) &&
            ProcessHotbarGrid(plugin, draw, HudLayout.ActionBarThree(plugin.Configuration, origin, viewportSize, mode), 2u, HotbarGridLayouts.Resolve(plugin.Configuration, 2u).Columns, HotbarGridLayouts.Resolve(plugin.Configuration, 2u).Rows, scale, mouse, false))
            return true;

        return plugin.IsHudElementVisible(HudElementIds.UtilityBars, mode) &&
               ProcessHotbarGrid(plugin, draw, HudLayout.UtilityBars(plugin.Configuration, origin, viewportSize, mode), 5u, 4, 3, scale, mouse, false);
    }

    private static void ProcessHotbarEditing(
        Plugin plugin,
        ImDrawListPtr draw,
        Vector2 origin,
        Vector2 viewportSize,
        float scale,
        Vector2 mouse)
    {
        var mode = HudModeProfileService.IsCalmMode(plugin.CurrentHudMode)
            ? UiMode.RaidReady
            : plugin.CurrentHudMode;

        HotbarSlotHit? hovered = null;
        var insideAnyBar = false;
        var pointerInsidePalette = plugin.IsPointInsideActionPalette(mouse);

        if (!pointerInsidePalette && plugin.CrossHotbarState.IsControllerUser)
        {
            var setNumber = Math.Clamp(plugin.HotbarEditing.CrossHotbarSet, 1, 8);
            if (!draggingHotbarSlot && pressedHotbarSlot is null &&
                !plugin.HotbarEditing.IsDraggingAction &&
                plugin.CrossHotbarState.TryGetState(out var liveCross))
            {
                setNumber = liveCross.SetNumber;
                plugin.HotbarEditing.SetCrossHotbarSet(setNumber);
            }

            var bounds = HudLayout.Resolve(plugin.Configuration, HudElementIds.CrossHotbar, origin, viewportSize, mode);
            insideAnyBar = Contains(bounds, mouse);
            hovered = HitTestCrossHotbar(bounds, (uint)(9 + setNumber), scale, mouse);
        }
        else if (!pointerInsidePalette)
        {
            var barOne = HudLayout.ActionBarOne(plugin.Configuration, origin, viewportSize, mode);
            var barTwo = HudLayout.ActionBarTwo(plugin.Configuration, origin, viewportSize, mode);
            var barThree = HudLayout.ActionBarThree(plugin.Configuration, origin, viewportSize, mode);
            insideAnyBar = Contains(barOne, mouse) || Contains(barTwo, mouse) || Contains(barThree, mouse);
            var shapeOne = HotbarGridLayouts.Resolve(plugin.Configuration, 0u);
            var shapeTwo = HotbarGridLayouts.Resolve(plugin.Configuration, 1u);
            var shapeThree = HotbarGridLayouts.Resolve(plugin.Configuration, 2u);
            hovered = HitTestHotbarGrid(barOne, 0u, shapeOne.Columns, shapeOne.Rows, scale, mouse)
                      ?? HitTestHotbarGrid(barTwo, 1u, shapeTwo.Columns, shapeTwo.Rows, scale, mouse)
                      ?? HitTestHotbarGrid(barThree, 2u, shapeThree.Columns, shapeThree.Rows, scale, mouse);
        }

        if (hovered is { } hit)
        {
            plugin.HotbarEditing.RegisterHoveredSlot(hit.Slot);
            DrawHotbarHover(plugin, draw, hit.Bounds, scale, true);
        }

        if (leftPressedThisFrame)
        {
            if (hovered is { } pressed)
            {
                if (plugin.HotbarEditing.IsDraggingAction)
                {
                    AssignPaletteAction(plugin, pressed.Slot);
                    ResetHotbarPointerState();
                    return;
                }

                plugin.HotbarEditing.Select(pressed.Slot);
                pressedHotbarSlot = pressed.Slot;
                hotbarPressPosition = mouse;
                draggingHotbarSlot = false;
            }
            else
            {
                pressedHotbarSlot = null;
                draggingHotbarSlot = false;
            }
        }

        if (pressedHotbarSlot is { } source && leftDownThisFrame)
        {
            if (!draggingHotbarSlot && Vector2.Distance(mouse, hotbarPressPosition) >= MathF.Max(3f, 4f * scale))
                draggingHotbarSlot = plugin.HotbarEditing.BeginSlotDrag(source);
        }

        if (!leftReleasedThisFrame)
            return;

        if (plugin.HotbarEditing.IsDraggingAction)
        {
            if (hovered is { } destination)
                AssignPaletteAction(plugin, destination.Slot);
            else if (!plugin.IsPointInsideActionPalette(mouse))
                plugin.HotbarEditing.CancelActionDrag();

            ResetHotbarPointerState();
            return;
        }

        if (draggingHotbarSlot && pressedHotbarSlot is { } dragSource)
        {
            if (hovered is { } destination && destination.Slot != dragSource)
            {
                if (!plugin.HotbarEditing.Transfer(
                        dragSource,
                        destination.Slot,
                        IsVirtualKeyDown(VkControl),
                        out var transferMessage) &&
                    !string.IsNullOrWhiteSpace(transferMessage))
                {
                    Plugin.ChatGui.PrintError(transferMessage);
                }
            }
            else if (!insideAnyBar && !plugin.IsPointInsideActionPalette(mouse))
            {
                if (!plugin.HotbarEditing.Clear(dragSource, out var clearMessage) &&
                    !string.IsNullOrWhiteSpace(clearMessage))
                {
                    Plugin.ChatGui.PrintError(clearMessage);
                }
            }
        }

        plugin.HotbarEditing.CancelSlotDrag();
        ResetHotbarPointerState();
    }

    private static void AssignPaletteAction(Plugin plugin, HotbarSlotReference destination)
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

    private static bool ProcessHotbarGrid(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        uint hotbarId,
        int columns,
        int rows,
        float scale,
        Vector2 mouse,
        bool editing)
    {
        if (!Contains(bounds, mouse))
            return false;

        var hit = HitTestHotbarGrid(bounds, hotbarId, columns, rows, scale, mouse);
        if (hit is { } slot)
        {
            DrawHotbarHover(plugin, draw, slot.Bounds, scale, editing);
            if (leftPressedThisFrame && !editing)
                plugin.HotbarInput.Execute(slot.Slot.HotbarId, slot.Slot.SlotId);
        }


        return true;
    }

    private static HotbarSlotHit? HitTestHotbarGrid(
        HudBounds bounds,
        uint hotbarId,
        int columns,
        int rows,
        float scale,
        Vector2 mouse)
    {
        if (!Contains(bounds, mouse))
            return null;

        var gap = MathF.Max(1f, 3f * scale);
        var slotSize = MathF.Max(18f, MathF.Min(
            (bounds.Size.X - gap * (columns - 1)) / columns,
            (bounds.Size.Y - gap * (rows - 1)) / rows));
        var content = new Vector2(
            columns * slotSize + (columns - 1) * gap,
            rows * slotSize + (rows - 1) * gap);
        var start = bounds.Position + (bounds.Size - content) * 0.5f;

        for (var slot = 0; slot < columns * rows; slot++)
        {
            var row = slot / columns;
            var column = slot % columns;
            var slotBounds = new HudBounds(
                start + new Vector2(column * (slotSize + gap), row * (slotSize + gap)),
                new Vector2(slotSize));
            if (Contains(slotBounds, mouse))
                return new HotbarSlotHit(new HotbarSlotReference(hotbarId, (uint)slot), slotBounds);
        }

        return null;
    }

    private static HotbarSlotHit? HitTestCrossHotbar(
        HudBounds bounds,
        uint hotbarId,
        float scale,
        Vector2 mouse)
    {
        if (!Contains(bounds, mouse))
            return null;

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
            bounds.Position.X + (bounds.Size.X - contentWidth) * 0.5f,
            bounds.Position.Y + triggerHeight + MathF.Max(0f, (availableHeight - clusterSize) * 0.5f));

        return HitTestCrossHalf(hotbarId, 0u, contentStart, slotSize, slotGap, clusterSize, clusterGap, mouse)
               ?? HitTestCrossHalf(hotbarId, 8u, contentStart + new Vector2(halfWidth + centerGap, 0f), slotSize, slotGap, clusterSize, clusterGap, mouse);
    }

    private static HotbarSlotHit? HitTestCrossHalf(
        uint hotbarId,
        uint firstSlot,
        Vector2 start,
        float slotSize,
        float slotGap,
        float clusterSize,
        float clusterGap,
        Vector2 mouse)
        => HitTestCrossCluster(hotbarId, firstSlot, start, slotSize, slotGap, mouse)
           ?? HitTestCrossCluster(hotbarId, firstSlot + 4u, start + new Vector2(clusterSize + clusterGap, 0f), slotSize, slotGap, mouse);

    private static HotbarSlotHit? HitTestCrossCluster(
        uint hotbarId,
        uint firstSlot,
        Vector2 start,
        float slotSize,
        float gap,
        Vector2 mouse)
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
            var slotBounds = new HudBounds(start + offset, new Vector2(slotSize));
            if (Contains(slotBounds, mouse))
                return new HotbarSlotHit(new HotbarSlotReference(hotbarId, firstSlot + (uint)index), slotBounds);
        }

        return null;
    }

    private static void DrawHotbarHover(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, bool editing)
    {
        var accent = plugin.CurrentTheme.AccentStrong;
        draw.AddRectFilled(
            bounds.Position,
            bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, editing ? 0.30f : 0.22f)),
            5f * scale);
        draw.AddRect(
            bounds.Position,
            bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.96f)),
            5f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, editing ? 2.2f * scale : 1.5f * scale));
    }

    private static void ResetHotbarPointerState()
    {
        pressedHotbarSlot = null;
        draggingHotbarSlot = false;
    }

    private static void ProcessParty(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {
        var mode = plugin.CurrentHudMode;
        var sortByRole = plugin.AllianceFrames.IsAlliance || mode == UiMode.RaidReady || mode == UiMode.Quest;
        var orderedMembers = HudRenderer.GetOrderedPartyMembers(sortByRole);
        var count = Math.Min(8, orderedMembers.Count);
        if (count <= 0)
            return;

        var isAlliance = plugin.AllianceFrames.IsAlliance;
        var contentPosition = bounds.Position;
        var contentHeight = bounds.Size.Y;
        if (isAlliance)
        {
            var headerHeight = Math.Clamp(22f * scale, 19f, 29f);
            contentPosition = bounds.Position + new Vector2(0f, headerHeight + 2f * scale);
            contentHeight = MathF.Max(1f, bounds.Size.Y - headerHeight - 2f * scale);
        }

        var gap = (isAlliance ? 2f : 4f) * scale;
        var minimumRowHeight = isAlliance ? 15f : 24f;
        var rowHeight = MathF.Max(minimumRowHeight, (contentHeight - gap * (count - 1)) / count);
        for (var i = 0; i < count; i++)
        {
            var actor = orderedMembers[i]?.GameObject;
            if (actor is null || !actor.IsValid())
                continue;

            var rowPosition = contentPosition + new Vector2(0f, i * (rowHeight + gap));
            var statusBandHeight = actor is IBattleChara battle
                ? ProcessPartyStatuses(plugin, battle, rowPosition, bounds.Size.X, rowHeight, scale, mouse, i)
                : 0f;

            var actorHeight = MathF.Max(0f, rowHeight - statusBandHeight);
            if (actorHeight > 1f)
            {
                ProcessActorRegion(
                    plugin,
                    draw,
                    actor,
                    new HudBounds(rowPosition + new Vector2(0f, statusBandHeight), new Vector2(bounds.Size.X, actorHeight)),
                    scale,
                    mouse,
                    null,
                    suppressTooltip: true);
            }
        }
    }

    private static void ProcessAllianceGroup(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        int groupIndex,
        float scale,
        Vector2 mouse)
    {
        var headerHeight = Math.Clamp(22f * scale, 19f, 29f);
        var rowGap = MathF.Max(1f, 2f * scale);
        var bodyHeight = MathF.Max(1f, bounds.Size.Y - headerHeight - 4f * scale);
        var rowHeight = MathF.Max(15f, (bodyHeight - rowGap * 7f) / 8f);
        var bodyTop = bounds.Position.Y + headerHeight + 2f * scale;

        for (var slotIndex = 0; slotIndex < 8; slotIndex++)
        {
            var actor = plugin.AllianceFrames.GetActor(groupIndex, slotIndex);
            if (actor is null || !actor.IsValid())
                continue;

            var rowPosition = new Vector2(
                bounds.Position.X + 3f * scale,
                bodyTop + slotIndex * (rowHeight + rowGap));
            var rowBounds = new HudBounds(
                rowPosition,
                new Vector2(bounds.Size.X - 6f * scale, rowHeight));

            ProcessActorRegion(
                plugin,
                draw,
                actor,
                rowBounds,
                scale,
                mouse,
                null,
                suppressTooltip: true);
        }
    }

    private static float ProcessPartyStatuses(
        Plugin plugin,
        IBattleChara actor,
        Vector2 rowPosition,
        float rowWidth,
        float rowHeight,
        float scale,
        Vector2 mouse,
        int rowIndex)
    {
        if (rowHeight < 30f * scale)
            return 0f;

        var iconSize = Math.Clamp(rowHeight * 0.25f, 10f * scale, 18f * scale);
        var gap = MathF.Max(1f, 2f * scale);
        var top = rowPosition.Y + 3f * scale;
        var left = rowPosition.X + 6f * scale;
        var right = rowPosition.X + rowWidth - 6f * scale;
        var maxPerSide = Math.Clamp((int)((rowWidth * 0.43f + gap) / (iconSize + gap)), 1, 7);
        var buffCount = 0;
        var debuffCount = 0;
        var statusIndex = 0;

        foreach (var status in actor.StatusList)
        {
            if (status.StatusId == 0 || !TryGetStatusTooltipData(status.StatusId, status.Param, out var data))
                continue;

            Vector2 iconPosition;
            if (data.IsDebuff)
            {
                if (debuffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(right - iconSize - debuffCount * (iconSize + gap), top);
                debuffCount++;
            }
            else
            {
                if (buffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(left + buffCount * (iconSize + gap), top);
                buffCount++;
            }

            ProcessStatusRegion(plugin, actor, status.StatusId, status.RemainingTime, data,
                new HudBounds(iconPosition, new Vector2(iconSize)), mouse, $"party-{rowIndex}-{statusIndex++}");
        }

        return buffCount > 0 || debuffCount > 0 ? iconSize + 4f * scale : 0f;
    }

    private static void ProcessTarget(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        IGameObject primaryTarget,
        float scale,
        Vector2 mouse)
    {
        var statusBandHeight = primaryTarget is IBattleChara battleTarget
            ? ProcessTargetStatuses(plugin, battleTarget, bounds, scale, mouse)
            : 0f;

        var actorHeight = MathF.Max(0f, bounds.Size.Y - statusBandHeight);
        if (actorHeight > 1f)
        {
            ProcessActorRegion(
                plugin,
                draw,
                primaryTarget,
                new HudBounds(bounds.Position + new Vector2(0f, statusBandHeight), new Vector2(bounds.Size.X, actorHeight)),
                scale,
                mouse);
        }
    }

    private static float ProcessTargetStatuses(Plugin plugin, IBattleChara actor, HudBounds bounds, float scale, Vector2 mouse)
    {
        var iconSize = Math.Clamp(bounds.Size.Y * 0.19f, 14f * scale, 21f * scale);
        var gap = MathF.Max(1f, 2f * scale);
        var top = bounds.Position.Y + 4f * scale;
        var left = bounds.Position.X + 12f * scale;
        var right = bounds.Position.X + bounds.Size.X - 12f * scale;
        var maxPerSide = Math.Clamp((int)((bounds.Size.X * 0.44f + gap) / (iconSize + gap)), 1, 12);
        var buffCount = 0;
        var debuffCount = 0;
        var statusIndex = 0;

        foreach (var status in actor.StatusList)
        {
            if (status.StatusId == 0 || !TryGetStatusTooltipData(status.StatusId, status.Param, out var data))
                continue;

            Vector2 iconPosition;
            if (data.IsDebuff)
            {
                if (debuffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(right - iconSize - debuffCount * (iconSize + gap), top);
                debuffCount++;
            }
            else
            {
                if (buffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(left + buffCount * (iconSize + gap), top);
                buffCount++;
            }

            ProcessStatusRegion(plugin, actor, status.StatusId, status.RemainingTime, data,
                new HudBounds(iconPosition, new Vector2(iconSize)), mouse, $"target-{statusIndex++}");
        }

        return buffCount > 0 || debuffCount > 0 ? iconSize + 6f * scale : 0f;
    }

    private static bool ProcessStandaloneStatusPanel(
        Plugin plugin,
        IBattleChara actor,
        HudBounds panelBounds,
        bool debuffs,
        float scale,
        Vector2 mouse)
    {
        var entries = new List<(float RemainingTime, StatusTooltipData Data)>();
        foreach (var status in actor.StatusList)
        {
            if (status.StatusId == 0 ||
                !TryGetStatusTooltipData(status.StatusId, status.Param, out var data) ||
                data.IsDebuff != debuffs)
                continue;

            entries.Add((status.RemainingTime, data));
        }

        if (entries.Count == 0)
            return false;

        var livePanelBounds = HudRenderer.ResolveRaidStatusPanelBounds(panelBounds, scale, entries.Count);
        if (!Contains(livePanelBounds, mouse))
            return false;

        var slots = HudRenderer.ResolveRaidStatusIconBounds(panelBounds, scale, entries.Count);
        for (var index = 0; index < Math.Min(entries.Count, slots.Count); index++)
        {
            var slot = slots[index];
            if (!Contains(slot, mouse))
                continue;

            var entry = entries[index];
            ImGui.BeginTooltip();
            ImGui.TextColored(entry.Data.IsDebuff ? plugin.CurrentTheme.Danger : plugin.CurrentTheme.Success, entry.Data.Name);
            if (!string.IsNullOrWhiteSpace(entry.Data.Description))
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 360f * scale);
                ImGui.TextWrapped(entry.Data.Description);
                ImGui.PopTextWrapPos();
            }
            if (entry.RemainingTime > 0.05f)
                ImGui.TextDisabled($"Remaining: {FormatStatusDuration(entry.RemainingTime)}");
            ImGui.EndTooltip();
            return true;
        }

        return false;
    }

    private static bool ProcessWorkPlayerStatuses(
        Plugin plugin,
        IBattleChara actor,
        HudBounds playerBounds,
        float scale,
        Vector2 mouse)
    {
        if (!Contains(playerBounds, mouse))
            return false;

        var entries = WorkStatusService.Snapshot(actor, 8);
        if (entries.Count == 0)
            return false;

        var slots = HudRenderer.ResolveWorkStatusSlots(playerBounds, scale, entries.Count);
        for (var index = 0; index < Math.Min(entries.Count, slots.Count); index++)
        {
            var entry = entries[index];
            var bounds = slots[index].IconBounds;
            if (!Contains(bounds, mouse))
                continue;

            ImGui.BeginTooltip();
            ImGui.TextColored(plugin.CurrentTheme.Success, entry.Name);
            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 360f * scale);
                ImGui.TextWrapped(entry.Description);
                ImGui.PopTextWrapPos();
            }
            if (entry.RemainingTime > 0.05f)
                ImGui.TextDisabled($"Remaining: {FormatStatusDuration(entry.RemainingTime)}");
            ImGui.EndTooltip();
            return true;
        }

        return false;
    }

    private static bool ProcessRoleplayDockPopup(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds dockBounds,
        Vector2 viewportOrigin,
        Vector2 viewportSize,
        float scale,
        Vector2 mouse)
    {
        var popup = plugin.ActiveRoleplayDockPopup;
        if (popup == RoleplayDockPopup.None)
            return false;

        var blend = plugin.GetRoleplayDockPopupBlend();
        var bounds = HudLayout.ResolveRoleplayDockPopup(
            dockBounds,
            popup,
            viewportOrigin,
            viewportSize,
            scale,
            blend);
        if (!Contains(bounds, mouse))
            return false;

        var segmentCount = popup == RoleplayDockPopup.ChatChannels ? 4 : 5;
        var segmentWidth = bounds.Size.X / segmentCount;
        var index = Math.Clamp(
            (int)((mouse.X - bounds.Position.X) / MathF.Max(1f, segmentWidth)),
            0,
            segmentCount - 1);
        var segmentBounds = new HudBounds(
            bounds.Position + new Vector2(segmentWidth * index, 0f),
            new Vector2(index == segmentCount - 1 ? bounds.Size.X - segmentWidth * index : segmentWidth, bounds.Size.Y));

        if (leftPressedThisFrame)
            pressedRoleplayDockPopupIndex = index;

        var held = leftDownThisFrame && pressedRoleplayDockPopupIndex == index;
        DrawDockPopupInteractionState(plugin, draw, segmentBounds, scale, held, blend);

        var label = popup == RoleplayDockPopup.ChatChannels
            ? index switch
            {
                0 => "SAY",
                1 => "PARTY",
                2 => "FC",
                _ => "LS1",
            }
            : index switch
            {
                0 => "LEISURE",
                1 => "QUEST",
                2 => "RAID",
                3 => "WORK",
                _ => "AUTO",
            };

        if (leftReleasedThisFrame && pressedRoleplayDockPopupIndex == index)
        {
            plugin.CloseRoleplayDockPopup();
            if (popup == RoleplayDockPopup.ChatChannels)
            {
                var channel = index switch
                {
                    0 => (Command: "/say", Label: "Say"),
                    1 => (Command: "/party", Label: "Party"),
                    2 => (Command: "/freecompany", Label: "Free Company"),
                    _ => (Command: "/linkshell1", Label: "Linkshell 1"),
                };
                plugin.SetRoleplayChatMode(channel.Command, channel.Label);
            }
            else
            {
                plugin.SetMode(index switch
                {
                    0 => UiMode.Leisure,
                    1 => UiMode.Quest,
                    2 => UiMode.RaidReady,
                    3 => UiMode.Work,
                    _ => UiMode.Auto,
                });
            }
        }

        ImGui.SetTooltip(popup == RoleplayDockPopup.ChatChannels
            ? $"{label}: make this the active chat channel."
            : $"{label}: switch the active RE:Frame dock and layout.");
        return true;
    }

    private static bool ProcessWorkstationDockPopup(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds dockBounds,
        Vector2 viewportOrigin,
        Vector2 viewportSize,
        float scale,
        Vector2 mouse)
    {
        if (plugin.ActiveWorkstationDockPopup == WorkstationDockPopup.None)
            return false;

        var blend = plugin.GetWorkstationDockPopupBlend();
        var bounds = HudLayout.ResolveWorkstationDockPopup(
            dockBounds,
            plugin.ActiveWorkstationDockPopup,
            viewportOrigin,
            viewportSize,
            scale,
            blend);
        if (!Contains(bounds, mouse))
            return false;

        var resourcePopup = plugin.ActiveWorkstationDockPopup == WorkstationDockPopup.Resources;
        var segmentCount = resourcePopup ? 3 : 5;
        var segmentWidth = bounds.Size.X / segmentCount;
        var index = Math.Clamp((int)((mouse.X - bounds.Position.X) / MathF.Max(1f, segmentWidth)), 0, segmentCount - 1);
        var segmentBounds = new HudBounds(
            bounds.Position + new Vector2(segmentWidth * index, 0f),
            new Vector2(index == segmentCount - 1 ? bounds.Size.X - segmentWidth * index : segmentWidth, bounds.Size.Y));

        if (leftPressedThisFrame)
            pressedWorkstationDockPopupIndex = index;

        var held = leftDownThisFrame && pressedWorkstationDockPopupIndex == index;
        DrawDockPopupInteractionState(plugin, draw, segmentBounds, scale, held, blend);

        if (leftReleasedThisFrame && pressedWorkstationDockPopupIndex == index)
        {
            plugin.CloseWorkstationDockPopup();
            if (resourcePopup)
            {
                var resource = index switch
                {
                    0 => (Url: "https://ffxivteamcraft.com/search", Label: "Teamcraft"),
                    1 => (Url: "https://garlandtools.org/", Label: "Garland"),
                    _ => (Url: "https://ffxivcrafting.com/", Label: "FFXIV Crafting"),
                };
                plugin.OpenExternalResource(resource.Url, resource.Label);
            }
            else
            {
                plugin.SetMode(index switch
                {
                    0 => UiMode.Leisure,
                    1 => UiMode.Roleplay,
                    2 => UiMode.Quest,
                    3 => UiMode.RaidReady,
                    _ => UiMode.Auto,
                });
            }
        }

        var label = resourcePopup
            ? index switch
            {
                0 => "TEAMCRAFT",
                1 => "GARLAND",
                _ => "FFXIV CRAFTING",
            }
            : index switch
        {
            0 => "LEISURE",
            1 => "ROLEPLAY",
            2 => "QUEST",
            3 => "RAID",
            _ => "AUTO",
        };
        ImGui.SetTooltip(resourcePopup
            ? $"{label}: open this crafting resource in your browser."
            : $"{label}: switch the active RE:Frame dock and layout.");
        return true;
    }


    private static bool ProcessLeisureDockPopup(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds dockBounds,
        Vector2 viewportOrigin,
        Vector2 viewportSize,
        float scale,
        Vector2 mouse)
    {
        var popup = plugin.ActiveLeisureDockPopup;
        if (popup == LeisureDockPopup.None)
            return false;

        var blend = plugin.GetLeisureDockPopupBlend();
        var bounds = HudLayout.ResolveLeisureDockPopup(
            dockBounds,
            popup,
            viewportOrigin,
            viewportSize,
            scale,
            blend);
        if (!Contains(bounds, mouse))
            return false;

        if (popup == LeisureDockPopup.Docks)
        {
            const int segmentCount = 5;
            var segmentWidth = bounds.Size.X / segmentCount;
            var index = Math.Clamp(
                (int)((mouse.X - bounds.Position.X) / MathF.Max(1f, segmentWidth)),
                0,
                segmentCount - 1);
            var segmentBounds = new HudBounds(
                bounds.Position + new Vector2(segmentWidth * index, 0f),
                new Vector2(
                    index == segmentCount - 1 ? bounds.Size.X - segmentWidth * index : segmentWidth,
                    bounds.Size.Y));
            var action = index switch
            {
                0 => LeisureDockPopupAction.DockRoleplay,
                1 => LeisureDockPopupAction.DockQuest,
                2 => LeisureDockPopupAction.DockRaid,
                3 => LeisureDockPopupAction.DockWork,
                _ => LeisureDockPopupAction.DockAuto,
            };
            var label = index switch
            {
                0 => "ROLEPLAY",
                1 => "QUEST",
                2 => "RAID",
                3 => "WORK",
                _ => "AUTO",
            };

            ProcessLeisureDockPopupButton(plugin, draw, action, label, segmentBounds, scale, blend);
            return true;
        }

        var halfWidth = bounds.Size.X * 0.5f;
        var leftBounds = new HudBounds(bounds.Position, new Vector2(halfWidth, bounds.Size.Y));
        var rightBounds = new HudBounds(
            bounds.Position + new Vector2(halfWidth, 0f),
            new Vector2(bounds.Size.X - halfWidth, bounds.Size.Y));

        var leftAction = popup == LeisureDockPopup.Travel
            ? LeisureDockPopupAction.Teleport
            : LeisureDockPopupAction.CharacterSelect;
        var rightAction = popup == LeisureDockPopup.Travel
            ? LeisureDockPopupAction.Lifestream
            : LeisureDockPopupAction.AppearanceWorkspace;

        if (Contains(leftBounds, mouse))
        {
            ProcessLeisureDockPopupButton(
                plugin,
                draw,
                leftAction,
                popup == LeisureDockPopup.Travel ? "TELEPORT" : "CHARACTER SELECT",
                leftBounds,
                scale,
                blend);
        }
        else
        {
            ProcessLeisureDockPopupButton(
                plugin,
                draw,
                rightAction,
                popup == LeisureDockPopup.Travel ? "LIFESTREAM" : "PENUMBRA/GLAMOURER",
                rightBounds,
                scale,
                blend);
        }

        return true;
    }

    private static void ProcessLeisureDockPopupButton(
        Plugin plugin,
        ImDrawListPtr draw,
        LeisureDockPopupAction action,
        string label,
        HudBounds bounds,
        float scale,
        float opacity)
    {
        if (leftPressedThisFrame)
            pressedLeisureDockPopupAction = action;

        var held = leftDownThisFrame && pressedLeisureDockPopupAction == action;
        DrawDockPopupInteractionState(plugin, draw, bounds, scale, held, opacity);

        if (leftReleasedThisFrame && pressedLeisureDockPopupAction == action)
        {
            plugin.CloseLeisureDockPopup();
            switch (action)
            {
                case LeisureDockPopupAction.Teleport:
                    plugin.OpenTeleportWindow();
                    break;
                case LeisureDockPopupAction.Lifestream:
                    plugin.RunIntegrationCommand(plugin.Configuration.LifestreamCommand, "Lifestream");
                    break;
                case LeisureDockPopupAction.CharacterSelect:
                    plugin.OpenCharacterSelect();
                    break;
                case LeisureDockPopupAction.AppearanceWorkspace:
                    plugin.OpenAppearanceWorkspace();
                    break;
                case LeisureDockPopupAction.DockRoleplay:
                    plugin.SetMode(UiMode.Roleplay);
                    break;
                case LeisureDockPopupAction.DockQuest:
                    plugin.SetMode(UiMode.Quest);
                    break;
                case LeisureDockPopupAction.DockRaid:
                    plugin.SetMode(UiMode.RaidReady);
                    break;
                case LeisureDockPopupAction.DockWork:
                    plugin.SetMode(UiMode.Work);
                    break;
                case LeisureDockPopupAction.DockAuto:
                    plugin.SetMode(UiMode.Auto);
                    break;
            }
        }

        ImGui.SetTooltip(label);
    }

    private static void ExecuteUniversalDockAction(Plugin plugin, DockButtonConfig button)
    {
        plugin.CloseRoleplayDockPopup();
        plugin.CloseLeisureDockPopup();
        plugin.CloseWorkstationDockPopup();

        switch (button.Action.Trim().ToLowerInvariant())
        {
            case "command":
                plugin.OpenCommandPalette();
                break;
            case "travel":
                plugin.ToggleLeisureDockPopup(LeisureDockPopup.Travel);
                break;
            case "appearance":
                plugin.ToggleLeisureDockPopup(LeisureDockPopup.Appearance);
                break;
            case "chat":
                plugin.ToggleRoleplayDockPopup(RoleplayDockPopup.ChatChannels);
                break;
            case "emotes":
                if (!NativeChatCommandService.TryExecute("/emotelist"))
                    Plugin.ChatGui.PrintError("RE:Frame could not open FFXIV's native Emote list.");
                break;
            case "scenes":
                plugin.OpenScenekeeper();
                break;
            case "questlog":
                plugin.OpenQuestLogWindow();
                break;
            case "dutyfinder":
                plugin.OpenDutyFinderWindow();
                break;
            case "resources":
                plugin.ToggleWorkstationDockPopup(WorkstationDockPopup.Resources);
                break;
            case "dock": 
            case "docks":
                plugin.ToggleLeisureDockPopup(LeisureDockPopup.Docks);
                break;
            case DockButtonCatalog.CustomCommand:
                plugin.ExecuteDockCommand(button.Command, button.Label);
                break;
            default:
                Plugin.ChatGui.PrintError($"RE:Frame does not recognize the dock action '{button.Action}'.");
                break;
        }
    }

    private static void DrawDockInteractionState(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        float scale,
        bool held)
    {
        var theme = plugin.CurrentTheme;
        var hudOpacity = Math.Clamp(plugin.Configuration.HudOpacity, 0.35f, 1f);
        var state = held ? theme.ResolvedDockButtonActive : theme.ResolvedDockButtonHovered;
        var border = theme.HasExtendedColors
            ? theme.ResolvedDockBorder
            : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.94f);
        var fillColor = new Vector4(
            state.X,
            state.Y,
            state.Z,
            Math.Clamp(state.W * hudOpacity, 0f, 1f));
        var borderColor = new Vector4(
            border.X,
            border.Y,
            border.Z,
            Math.Clamp(border.W * hudOpacity, 0f, 1f));

        draw.AddRectFilled(
            bounds.Position,
            bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(fillColor),
            5f * scale);
        draw.AddRect(
            bounds.Position,
            bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(borderColor),
            5f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, 1.5f * scale));
    }

    private static void DrawDockPopupInteractionState(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        float scale,
        bool held,
        float opacity)
    {
        var theme = plugin.CurrentTheme;
        var hudOpacity = Math.Clamp(plugin.Configuration.HudOpacity, 0.35f, 1f);
        var state = held ? theme.ResolvedDockButtonActive : theme.ResolvedDockButtonHovered;
        var border = theme.HasExtendedColors
            ? theme.ResolvedDockBorder
            : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.92f);
        var combinedOpacity = Math.Clamp(opacity * hudOpacity, 0f, 1f);
        var fillColor = new Vector4(
            state.X,
            state.Y,
            state.Z,
            Math.Clamp(state.W * combinedOpacity, 0f, 1f));
        var borderColor = new Vector4(
            border.X,
            border.Y,
            border.Z,
            Math.Clamp(border.W * combinedOpacity, 0f, 1f));

        draw.AddRectFilled(
            bounds.Position + new Vector2(3f * scale),
            bounds.Position + bounds.Size - new Vector2(3f * scale),
            ImGui.ColorConvertFloat4ToU32(fillColor),
            5f * scale);
        draw.AddRect(
            bounds.Position + new Vector2(2f * scale),
            bounds.Position + bounds.Size - new Vector2(2f * scale),
            ImGui.ColorConvertFloat4ToU32(borderColor),
            5f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, 1.35f * scale));
    }

    private static void ProcessQuestDock(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {
        var buttons = DockButtonCatalog.Visible(plugin.Configuration, DockButtonCatalog.Quest);
        if (buttons.Count == 0)
            return;

        var segmentWidth = bounds.Size.X / buttons.Count;
        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            var definition = DockButtonCatalog.Definition(DockButtonCatalog.Quest, button.Action);
            var x = segmentWidth * index;
            var width = index == buttons.Count - 1 ? bounds.Size.X - x : segmentWidth;
            var segment = new HudBounds(bounds.Position + new Vector2(x, 0f), new Vector2(width, bounds.Size.Y));
            switch (button.Action)
            {
                case "command":
                    ProcessQuestDockButton(plugin, draw, QuestDockAction.Command, button.Label, definition.Tooltip, segment, scale, mouse, plugin.OpenCommandPalette);
                    break;
                case "questlog":
                    ProcessQuestDockButton(plugin, draw, QuestDockAction.QuestLog, button.Label, definition.Tooltip, segment, scale, mouse, plugin.OpenQuestLogWindow);
                    break;
                case "dutyfinder":
                    ProcessQuestDockButton(plugin, draw, QuestDockAction.DutyFinder, button.Label, definition.Tooltip, segment, scale, mouse, plugin.OpenDutyFinderWindow);
                    break;
                default:
                    ProcessQuestDockButton(plugin, draw, QuestDockAction.Command, button.Label, definition.Tooltip, segment, scale, mouse, () => ExecuteUniversalDockAction(plugin, button));
                    break;
            }
        }
    }

    private static void ProcessQuestDockButton(
        Plugin plugin,
        ImDrawListPtr draw,
        QuestDockAction actionId,
        string label,
        string tooltip,
        HudBounds bounds,
        float scale,
        Vector2 mouse,
        Action action)
    {
        if (!Contains(bounds, mouse))
            return;

        if (leftPressedThisFrame)
            pressedQuestDockAction = actionId;

        var held = leftDownThisFrame && pressedQuestDockAction == actionId;
        DrawDockInteractionState(plugin, draw, bounds, scale, held);

        if (leftReleasedThisFrame && pressedQuestDockAction == actionId)
            action();

        ImGui.SetTooltip($"{label}: {tooltip}");
    }

    private static void ProcessRoleplayDock(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {
        var buttons = DockButtonCatalog.Visible(plugin.Configuration, DockButtonCatalog.Roleplay);
        if (buttons.Count == 0)
            return;

        var segmentWidth = bounds.Size.X / buttons.Count;
        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            var definition = DockButtonCatalog.Definition(DockButtonCatalog.Roleplay, button.Action);
            var x = segmentWidth * index;
            var width = index == buttons.Count - 1 ? bounds.Size.X - x : segmentWidth;
            var segment = new HudBounds(bounds.Position + new Vector2(x, 0f), new Vector2(width, bounds.Size.Y));
            switch (button.Action)
            {
                case "chat":
                    ProcessRoleplayDockButton(plugin, draw, RoleplayDockAction.Chat, button.Label, definition.Tooltip, segment, scale, mouse,
                        () => plugin.ToggleRoleplayDockPopup(RoleplayDockPopup.ChatChannels));
                    break;
                case "emotes":
                    ProcessRoleplayDockButton(plugin, draw, RoleplayDockAction.Emotes, button.Label, definition.Tooltip, segment, scale, mouse, () =>
                    {
                        plugin.CloseRoleplayDockPopup();
                        if (!NativeChatCommandService.TryExecute("/emotelist"))
                            Plugin.ChatGui.PrintError("RE:Frame could not open FFXIV's native Emote list.");
                    });
                    break;
                case "scenes":
                    ProcessRoleplayDockButton(plugin, draw, RoleplayDockAction.Scenes, button.Label, definition.Tooltip, segment, scale, mouse, () =>
                    {
                        plugin.CloseRoleplayDockPopup();
                        plugin.OpenScenekeeper();
                    });
                    break;
                case "docks":
                    ProcessRoleplayDockButton(plugin, draw, RoleplayDockAction.Docks, button.Label, definition.Tooltip, segment, scale, mouse,
                        () => plugin.ToggleRoleplayDockPopup(RoleplayDockPopup.Docks));
                    break;
                default:
                    ProcessRoleplayDockButton(plugin, draw, RoleplayDockAction.Scenes, button.Label, definition.Tooltip, segment, scale, mouse, () => ExecuteUniversalDockAction(plugin, button));
                    break;
            }
        }
    }

    private static void ProcessRoleplayDockButton(
        Plugin plugin,
        ImDrawListPtr draw,
        RoleplayDockAction actionId,
        string label,
        string tooltip,
        HudBounds bounds,
        float scale,
        Vector2 mouse,
        Action action)
    {
        if (!Contains(bounds, mouse))
            return;

        if (leftPressedThisFrame)
            pressedRoleplayDockAction = actionId;

        var held = leftDownThisFrame && pressedRoleplayDockAction == actionId;
        DrawDockInteractionState(plugin, draw, bounds, scale, held);

        if (leftReleasedThisFrame && pressedRoleplayDockAction == actionId)
            action();

        ImGui.SetTooltip($"{label}: {tooltip}");
    }

    private static void ProcessLeisureDock(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {
        var buttons = DockButtonCatalog.Visible(plugin.Configuration, DockButtonCatalog.Leisure);
        if (buttons.Count == 0)
            return;

        var segmentWidth = bounds.Size.X / buttons.Count;
        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            var definition = DockButtonCatalog.Definition(DockButtonCatalog.Leisure, button.Action);
            var x = segmentWidth * index;
            var width = index == buttons.Count - 1 ? bounds.Size.X - x : segmentWidth;
            var segment = new HudBounds(bounds.Position + new Vector2(x, 0f), new Vector2(width, bounds.Size.Y));
            switch (button.Action)
            {
                case "command":
                    ProcessLeisureDockButton(plugin, draw, LeisureDockAction.Command, button.Label, definition.Tooltip, segment, scale, mouse, () =>
                    {
                        plugin.CloseLeisureDockPopup();
                        plugin.OpenCommandPalette();
                    });
                    break;
                case "travel":
                    ProcessLeisureDockButton(plugin, draw, LeisureDockAction.Travel, button.Label, definition.Tooltip, segment, scale, mouse,
                        () => plugin.ToggleLeisureDockPopup(LeisureDockPopup.Travel));
                    break;
                case "appearance":
                    ProcessLeisureDockButton(plugin, draw, LeisureDockAction.Appearance, button.Label, definition.Tooltip, segment, scale, mouse,
                        () => plugin.ToggleLeisureDockPopup(LeisureDockPopup.Appearance));
                    break;
                case "docks":
                    ProcessLeisureDockButton(plugin, draw, LeisureDockAction.Docks, button.Label, definition.Tooltip, segment, scale, mouse,
                        () => plugin.ToggleLeisureDockPopup(LeisureDockPopup.Docks));
                    break;
                default:
                    ProcessLeisureDockButton(plugin, draw, LeisureDockAction.Command, button.Label, definition.Tooltip, segment, scale, mouse, () => ExecuteUniversalDockAction(plugin, button));
                    break;
            }
        }
    }

    private static void ProcessLeisureDockButton(
        Plugin plugin,
        ImDrawListPtr draw,
        LeisureDockAction actionId,
        string label,
        string tooltip,
        HudBounds bounds,
        float scale,
        Vector2 mouse,
        Action action)
    {
        if (!Contains(bounds, mouse))
            return;

        if (leftPressedThisFrame)
            pressedLeisureDockAction = actionId;

        var held = leftDownThisFrame && pressedLeisureDockAction == actionId;
        DrawDockInteractionState(plugin, draw, bounds, scale, held);

        if (leftReleasedThisFrame && pressedLeisureDockAction == actionId)
            action();

        ImGui.SetTooltip($"{label}: {tooltip}");
    }

    private static void ProcessWorkstationDock(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        float scale,
        Vector2 mouse)
    {
        var buttons = DockButtonCatalog.Visible(plugin.Configuration, DockButtonCatalog.Work);
        if (buttons.Count == 0)
            return;

        var segmentWidth = bounds.Size.X / buttons.Count;
        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            var definition = DockButtonCatalog.Definition(DockButtonCatalog.Work, button.Action);
            var x = segmentWidth * index;
            var width = index == buttons.Count - 1 ? bounds.Size.X - x : segmentWidth;
            var segment = new HudBounds(bounds.Position + new Vector2(x, 0f), new Vector2(width, bounds.Size.Y));
            switch (button.Action)
            {
                case "command":
                    ProcessWorkstationDockButton(plugin, draw, WorkstationDockAction.Command, button.Label, definition.Tooltip, segment, scale, mouse, () =>
                    {
                        plugin.CloseWorkstationDockPopup();
                        plugin.OpenCommandPalette();
                    });
                    break;
                case "resources":
                    ProcessWorkstationDockButton(plugin, draw, WorkstationDockAction.Resources, button.Label, definition.Tooltip, segment, scale, mouse,
                        () => plugin.ToggleWorkstationDockPopup(WorkstationDockPopup.Resources));
                    break;
                case "dock":
                case "docks":
                    ProcessWorkstationDockButton(plugin, draw, WorkstationDockAction.Dock, button.Label, definition.Tooltip, segment, scale, mouse,
                        () => plugin.ToggleLeisureDockPopup(LeisureDockPopup.Docks));
                    break;
                default:
                    ProcessWorkstationDockButton(plugin, draw, WorkstationDockAction.Command, button.Label, definition.Tooltip, segment, scale, mouse, () => ExecuteUniversalDockAction(plugin, button));
                    break;
            }
        }
    }

    private static void ProcessWorkstationDockButton(
        Plugin plugin,
        ImDrawListPtr draw,
        WorkstationDockAction actionId,
        string label,
        string tooltip,
        HudBounds bounds,
        float scale,
        Vector2 mouse,
        Action action)
    {
        if (!Contains(bounds, mouse))
            return;

        if (leftPressedThisFrame)
            pressedWorkstationDockAction = actionId;

        var held = leftDownThisFrame && pressedWorkstationDockAction == actionId;
        DrawDockInteractionState(plugin, draw, bounds, scale, held);

        if (leftReleasedThisFrame && pressedWorkstationDockAction == actionId)
            action();

        ImGui.SetTooltip($"{label}: {tooltip}");
    }

    private static void ProcessRaidTools(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {
        var gap = 6f * scale;
        var width = MathF.Max(1f, (bounds.Size.X - gap * 5f) / 4f);
        var height = MathF.Max(1f, bounds.Size.Y - gap * 2f);

        ProcessRaidToolButton(plugin, draw, NativeRaidTool.Repair, "REPAIR", string.Empty,
            new HudBounds(bounds.Position + new Vector2(gap, gap), new Vector2(width, height)), scale, mouse);
        ProcessRaidToolButton(plugin, draw, NativeRaidTool.Waymarks, "WAYMARK", plugin.Configuration.RaidWaymarkCommand,
            new HudBounds(bounds.Position + new Vector2(gap * 2f + width, gap), new Vector2(width, height)), scale, mouse);
        ProcessRaidToolButton(plugin, draw, NativeRaidTool.Countdown, "COUNTDOWN", plugin.Configuration.RaidCountdownCommand,
            new HudBounds(bounds.Position + new Vector2(gap * 3f + width * 2f, gap), new Vector2(width, height)), scale, mouse);
        ProcessRaidToolButton(plugin, draw, NativeRaidTool.StrategyBoard, "STRATEGY", plugin.Configuration.RaidStrategyCommand,
            new HudBounds(bounds.Position + new Vector2(gap * 4f + width * 3f, gap), new Vector2(width, height)), scale, mouse);
    }

    private static void ProcessRaidersKit(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {
        var gap = 7f * scale;
        var top = bounds.Position.Y + 24f * scale;
        var height = MathF.Max(28f * scale, bounds.Size.Y - 30f * scale);
        var width = MathF.Max(1f, (bounds.Size.X - gap * 3f) / 2f);
        var foodBounds = new HudBounds(new Vector2(bounds.Position.X + gap, top), new Vector2(width, height));
        var potionBounds = new HudBounds(new Vector2(bounds.Position.X + gap * 2f + width, top), new Vector2(width, height));
        var snapshot = RaidersKitService.Snapshot(plugin.Configuration, Plugin.ObjectTable.LocalPlayer as IBattleChara);

        if (Contains(foodBounds, mouse))
        {
            if (leftReleasedThisFrame && !RaidersKitService.TryUse(snapshot.Food))
                Plugin.ChatGui.PrintError("RE:Frame could not use the selected raid food.");
            ImGui.SetTooltip(snapshot.Food is { } food ? $"Use {food.Name} (x{food.Quantity})" : "No matching food was found in inventory.");
        }
        else if (Contains(potionBounds, mouse))
        {
            if (leftReleasedThisFrame && snapshot.PotionCooldownRemaining <= 0f && !RaidersKitService.TryUse(snapshot.Potion))
                Plugin.ChatGui.PrintError("RE:Frame could not use the selected raid potion.");
            ImGui.SetTooltip(snapshot.Potion is { } potion ? $"Use {potion.Name} (x{potion.Quantity})" : "No matching potion was found in inventory.");
        }
    }

    private static void ProcessRaidToolButton(
        Plugin plugin,
        ImDrawListPtr draw,
        NativeRaidTool tool,
        string label,
        string fallbackCommand,
        HudBounds bounds,
        float scale,
        Vector2 mouse)
    {
        if (!Contains(bounds, mouse))
            return;

        if (leftPressedThisFrame)
            pressedRaidTool = tool;

        var held = leftDownThisFrame && pressedRaidTool == tool;
        var theme = plugin.CurrentTheme;
        var alpha = held ? 0.44f : 0.25f;
        draw.AddRectFilled(bounds.Position, bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, alpha)),
            5f * scale);
        draw.AddRect(bounds.Position, bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.94f)),
            5f * scale, ImDrawFlags.None, MathF.Max(1f, 1.5f * scale));

        if (leftReleasedThisFrame && pressedRaidTool == tool)
        {
            var opened = NativeRaidToolService.TryOpen(tool);
            if (!opened && tool != NativeRaidTool.Waymarks && !string.IsNullOrWhiteSpace(fallbackCommand))
                opened = NativeChatCommandService.TryExecute(fallbackCommand);

            if (!opened)
                Plugin.ChatGui.PrintError($"RE:Frame could not open the native {label.ToLowerInvariant()} window.");
        }

        if (plugin.AdaptiveState.EffectiveMode != UiMode.RaidReady)
            ImGui.SetTooltip($"Open FFXIV's native {label.ToLowerInvariant()} window.");
    }

    private static void ProcessEnemyList(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, float scale, Vector2 mouse)
    {
        var enemies = plugin.EnemyList.Snapshot(
            8,
            excludeCurrentTarget: false,
            excludeFocusTarget: false,
            engagedOnly: true);
        if (enemies.Count == 0)
            return;

        var gap = 4f * scale;
        var rowHeight = HudLayout.EnemyListRowHeight(scale);
        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy is null || !enemy.IsValid())
                continue;

            ProcessActorRegion(plugin, draw, enemy,
                new HudBounds(bounds.Position + new Vector2(0f, i * (rowHeight + gap)), new Vector2(bounds.Size.X, rowHeight)),
                scale, mouse);
        }
    }

    private static void ProcessActorRegion(
        Plugin plugin,
        ImDrawListPtr draw,
        IGameObject actor,
        HudBounds bounds,
        float scale,
        Vector2 mouse,
        string? roleLabel = null,
        bool suppressTooltip = false)
    {
        if (!Contains(bounds, mouse))
            return;

        hoveredActorThisFrame ??= actor;

        if (plugin.Configuration.EnableMouseoverTargeting)
            plugin.HudTargeting.TouchMouseover(actor);


        if (leftPressedThisFrame)
        {
            pressedActorId = actor.GameObjectId;
        }

        if (leftReleasedThisFrame && pressedActorId == actor.GameObjectId)
            plugin.HudTargeting.SetTarget(actor);

        if (rightClickedThisFrame && plugin.Configuration.RightClickOpensNativeContextMenu)
            plugin.HudTargeting.OpenNativeContextMenu(actor);

        var theme = plugin.CurrentTheme;
        var held = leftDownThisFrame && pressedActorId == actor.GameObjectId;
        var alpha = held ? 0.34f : 0.18f;
        draw.AddRectFilled(bounds.Position, bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, alpha)),
            5f * scale);
        draw.AddRect(bounds.Position, bounds.Position + bounds.Size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.92f)),
            5f * scale, ImDrawFlags.None, MathF.Max(1f, 1.5f * scale));

        if (suppressTooltip || plugin.AdaptiveState.EffectiveMode == UiMode.RaidReady)
            return;

        ImGui.BeginTooltip();
        if (!string.IsNullOrWhiteSpace(roleLabel))
            ImGui.TextDisabled(roleLabel);
        ImGui.TextUnformatted(actor.Name.ToString());
        if (plugin.Configuration.EnableMouseoverTargeting)
            ImGui.TextDisabled("Hover: native <mo> target");
        ImGui.TextDisabled("Left-click: target");
        if (plugin.Configuration.RightClickOpensNativeContextMenu)
            ImGui.TextDisabled("Right-click: open FFXIV character menu");
        ImGui.EndTooltip();
    }

    private static void ProcessStatusRegion(
        Plugin plugin,
        IGameObject owner,
        uint statusId,
        float remainingTime,
        StatusTooltipData data,
        HudBounds bounds,
        Vector2 mouse,
        string uniqueSuffix)
    {
        _ = uniqueSuffix;
        if (!Contains(bounds, mouse))
            return;

        hoveredActorThisFrame ??= owner;

        if (plugin.Configuration.EnableMouseoverTargeting)
            plugin.HudTargeting.TouchMouseover(owner);

        if (leftPressedThisFrame)
        {
            pressedActorId = owner.GameObjectId;
        }

        if (leftReleasedThisFrame && pressedActorId == owner.GameObjectId)
            plugin.HudTargeting.SetTarget(owner);

        if (rightClickedThisFrame && plugin.Configuration.RightClickOpensNativeContextMenu)
            plugin.HudTargeting.OpenNativeContextMenu(owner);

        ImGui.BeginTooltip();
        var theme = plugin.CurrentTheme;
        ImGui.TextColored(data.IsDebuff ? theme.Danger : theme.Success, data.Name);
        if (!string.IsNullOrWhiteSpace(data.Description))
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 360f * Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f));
            ImGui.TextWrapped(data.Description);
            ImGui.PopTextWrapPos();
        }

        if (remainingTime > 0.05f)
            ImGui.TextDisabled($"Remaining: {FormatStatusDuration(remainingTime)}");
        ImGui.EndTooltip();
    }

    private static bool UpdateNativePointer(out Vector2 mouse)
    {
        mouse = default;
        var gameWindow = GetForegroundWindow();
        if (gameWindow == nint.Zero)
        {
            ResetPointerButtons();
            return false;
        }

        _ = GetWindowThreadProcessId(gameWindow, out var foregroundProcessId);
        if (foregroundProcessId != (uint)Environment.ProcessId)
        {
            ResetPointerButtons();
            return false;
        }

        if (!GetCursorPos(out var cursor) || !ScreenToClient(gameWindow, ref cursor))
            return false;

        var leftDown = IsVirtualKeyDown(VkLeftButton);
        var rightDown = IsVirtualKeyDown(VkRightButton);
        leftPressedThisFrame = leftDown && !previousLeftDown;
        leftReleasedThisFrame = !leftDown && previousLeftDown;
        rightClickedThisFrame = rightDown && !previousRightDown;
        leftDownThisFrame = leftDown;
        previousLeftDown = leftDown;
        previousRightDown = rightDown;


        mouse = new Vector2(cursor.X, cursor.Y);
        return true;
    }

    private static void ResetPointerButtons()
    {
        previousLeftDown = false;
        previousRightDown = false;
        leftPressedThisFrame = false;
        leftReleasedThisFrame = false;
        rightClickedThisFrame = false;
        leftDownThisFrame = false;
        pressedActorId = 0;
        hoveredActorThisFrame = null;
        PreviousBindingStates.Clear();
        pendingAltExecution = null;
        pressedRaidTool = null;
        pressedLeisureDockAction = null;
        pressedLeisureDockPopupAction = null;
        pressedWorkstationDockAction = null;
        pressedWorkstationDockPopupIndex = null;
        pressedRoleplayDockAction = null;
        pressedRoleplayDockPopupIndex = null;
        pressedQuestDockAction = null;
        ardynChantPressed = false;
        clockPressed = false;
        pressedHotbarSlot = null;
        draggingHotbarSlot = false;
    }

    private static void FinishPointerFrame()
    {
        if (!leftReleasedThisFrame)
            return;

        pressedActorId = 0;
        pressedRaidTool = null;
        pressedLeisureDockAction = null;
        pressedLeisureDockPopupAction = null;
        pressedWorkstationDockAction = null;
        pressedWorkstationDockPopupIndex = null;
        pressedRoleplayDockAction = null;
        pressedRoleplayDockPopupIndex = null;
        pressedQuestDockAction = null;
        ardynChantPressed = false;
        clockPressed = false;
    }

    private static void UpdateHotbarBindingFallback(Plugin plugin, IGameObject? hoveredActor)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null || !module->ModuleReady)
        {
            PreviousBindingStates.Clear();
            pendingAltExecution = null;
            return;
        }

        var now = Environment.TickCount64;


        if (pendingAltExecution is { } pending)
        {
            if (!pending.Actor.IsValid() || pending.Actor.GameObjectId != pending.ActorId)
            {
                pendingAltExecution = null;
            }
            else
            {
                plugin.HudTargeting.TouchMouseover(pending.Actor);
                var ageMs = now - pending.ArmedAtMs;
                var mainKeyStillDown = IsVirtualKeyDown(pending.Binding.VirtualKey);
                if ((!mainKeyStillDown && ageMs >= 12) || ageMs >= 650)
                {
                    plugin.HudTargeting.TouchMouseover(pending.Actor);
                    plugin.HotbarInput.ExecuteForMouseover(pending.HotbarId, pending.SlotId, pending.Actor);
                    pendingAltExecution = null;
                }
            }
        }

        for (uint hotbarId = 0; hotbarId < 3; hotbarId++)
        {
            for (uint slotId = 0; slotId < 12; slotId++)
            {
                var key = (hotbarId, slotId);
                var label = NativeHotbarKeybindService.GetLabel(module, hotbarId, slotId);
                var parsed = TryParseBinding(label, out var binding);
                var down = parsed && IsBindingDown(binding);
                var wasDown = PreviousBindingStates.TryGetValue(key, out var previous) && previous;
                PreviousBindingStates[key] = down;

                if (pendingAltExecution is not null || hoveredActor is null || !parsed || !binding.Alt || !down || wasDown)
                    continue;

                plugin.HudTargeting.TouchMouseover(hoveredActor);
                pendingAltExecution = new PendingAltExecution(
                    hotbarId,
                    slotId,
                    binding,
                    hoveredActor,
                    hoveredActor.GameObjectId,
                    now);
            }
        }
    }

    private static bool TryParseBinding(string label, out ParsedBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        var parts = label.Trim().ToUpperInvariant().Replace(" ", string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var alt = false;
        var control = false;
        var shift = false;
        string? keyToken = null;
        foreach (var part in parts)
        {
            switch (part)
            {
                case "A":
                case "ALT": alt = true; break;
                case "C":
                case "CTRL":
                case "CONTROL": control = true; break;
                case "S":
                case "SHIFT": shift = true; break;
                default: keyToken = part; break;
            }
        }

        if (keyToken is null || !TryMapVirtualKey(keyToken, out var virtualKey))
            return false;

        binding = new ParsedBinding(virtualKey, alt, control, shift);
        return true;
    }

    private static bool TryMapVirtualKey(string token, out int virtualKey)
    {
        virtualKey = 0;
        if (token.Length == 1)
        {
            var c = token[0];
            if (c is >= '0' and <= '9' || c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }
        }

        if (token.Length >= 2 && token[0] == 'F' && int.TryParse(token[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = 0x70 + functionKey - 1;
            return true;
        }

        if (token.Length == 2 && token[0] == 'N' && token[1] is >= '0' and <= '9')
        {
            virtualKey = 0x60 + token[1] - '0';
            return true;
        }

        virtualKey = token switch
        {
            "TAB" => 0x09,
            "ENTER" or "RETURN" => 0x0D,
            "SPACE" => 0x20,
            "PGUP" or "PAGEUP" => 0x21,
            "PGDN" or "PAGEDOWN" => 0x22,
            "END" => 0x23,
            "HOME" => 0x24,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "INS" or "INSERT" => 0x2D,
            "DEL" or "DELETE" => 0x2E,
            "N*" => 0x6A,
            "N+" => 0x6B,
            "N-" => 0x6D,
            "N." => 0x6E,
            "N/" => 0x6F,
            _ => 0,
        };
        return virtualKey != 0;
    }

    private static bool IsBindingDown(ParsedBinding binding)
    {
        var altDown = IsVirtualKeyDown(VkAlt);
        var controlDown = IsVirtualKeyDown(VkControl);
        var shiftDown = IsVirtualKeyDown(VkShift);
        return IsVirtualKeyDown(binding.VirtualKey) &&
               altDown == binding.Alt &&
               controlDown == binding.Control &&
               shiftDown == binding.Shift;
    }

    private static bool IsVirtualKeyDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);


    private readonly record struct HotbarSlotHit(HotbarSlotReference Slot, HudBounds Bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static bool Contains(HudBounds bounds, Vector2 point)
        => point.X >= bounds.Position.X && point.X < bounds.Position.X + bounds.Size.X &&
           point.Y >= bounds.Position.Y && point.Y < bounds.Position.Y + bounds.Size.Y;

    private static bool TryGetStatusTooltipData(uint statusId, uint statusParameter, out StatusTooltipData data)
    {
        data = new StatusTooltipData(string.Empty, string.Empty, false);
        if (!StatusDisplayService.TryResolve(statusId, statusParameter, out var display))
            return false;

        data = new StatusTooltipData(display.Name, display.Description, display.IsDebuff);
        return true;
    }

    private static string FormatStatusDuration(float seconds)
    {
        seconds = MathF.Max(0f, seconds);
        if (seconds >= 86400f)
        {
            var days = (int)(seconds / 86400f);
            var hours = (int)(seconds % 86400f / 3600f);
            return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        }
        if (seconds >= 3600f)
            return $"{(int)(seconds / 3600f)}h {(int)(seconds % 3600f / 60f)}m";
        if (seconds >= 60f)
            return $"{(int)(seconds / 60f)}m {(int)(seconds % 60f)}s";
        return $"{MathF.Ceiling(seconds):0}s";
    }

    private static IGameObject? ResolveTargetsTarget(IBattleChara? target)
    {
        if (target is null || target.Address == nint.Zero)
            return null;

        try
        {
            var nativeTarget = (NativeCharacter*)target.Address;
            var targetId = nativeTarget->GetTargetId().Id;
            if (targetId == 0UL)
                return null;

            return Plugin.ObjectTable.FirstOrDefault(actor => actor.GameObjectId == targetId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "RE:Frame could not resolve the target's target for direct HUD input.");
            return null;
        }
    }

    private enum LeisureDockAction
    {
        Command,
        Travel,
        Appearance,
        Docks,
    }

    private enum LeisureDockPopupAction
    {
        Teleport,
        Lifestream,
        CharacterSelect,
        AppearanceWorkspace,
        DockRoleplay,
        DockQuest,
        DockRaid,
        DockWork,
        DockAuto,
    }

    private enum QuestDockAction
    {
        Command,
        QuestLog,
        DutyFinder,
    }

    private enum RoleplayDockAction
    {
        Chat,
        Emotes,
        Scenes,
        Docks,
    }

    private enum WorkstationDockAction
    {
        Command,
        Resources,
        Dock,
    }

    private readonly record struct ParsedBinding(int VirtualKey, bool Alt, bool Control, bool Shift);
    private readonly record struct PendingAltExecution(
        uint HotbarId,
        uint SlotId,
        ParsedBinding Binding,
        IGameObject Actor,
        ulong ActorId,
        long ArmedAtMs);
    private readonly record struct StatusTooltipData(string Name, string Description, bool IsDebuff);
}
