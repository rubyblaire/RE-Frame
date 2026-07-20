using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaClassJob = Lumina.Excel.Sheets.ClassJob;
using LuminaCraftAction = Lumina.Excel.Sheets.CraftAction;
using LuminaCompanion = Lumina.Excel.Sheets.Companion;
using LuminaEmote = Lumina.Excel.Sheets.Emote;
using LuminaMount = Lumina.Excel.Sheets.Mount;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Services;

public readonly record struct HotbarSlotReference(uint HotbarId, uint SlotId)
{
    public bool IsCrossHotbar => HotbarId is >= 10u and <= 17u;
    public bool IsReframeVirtual => ReframeHotbarIds.IsVirtual(HotbarId);

    public string Label
    {
        get
        {
            if (IsReframeVirtual && Plugin.Instance?.AdditionalHotbars is { } additional)
                return additional.GetSlotLabel(this);
            if (HotbarId == ReframeHotbarIds.PetBar)
                return $"Pet Bar • Slot {SlotId + 1u}";
            return IsCrossHotbar
                ? $"Cross Hotbar {HotbarId - 9u} • Slot {SlotId + 1u}"
                : $"Hotbar {HotbarId + 1u} • Slot {SlotId + 1u}";
        }
    }
}

public readonly record struct HotbarSlotSnapshot(
    HotbarSlotReference Reference,
    HotbarSlotType CommandType,
    uint CommandId,
    string DisplayName)
{
    public bool IsEmpty => CommandType == HotbarSlotType.Empty || CommandId == 0;
}

public readonly record struct HotbarActionOption(
    HotbarSlotType CommandType,
    uint ActionId,
    string Name,
    uint IconId,
    uint RequiredLevel,
    bool IsRoleAction);

public enum HotbarPaletteCategory
{
    Actions,
    Emotes,
    Mounts,
    Minions,
    Macros,
}


public sealed unsafe class HotbarEditingService
{
    private static readonly HashSet<string> DiscipleOfHandAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL",
    };


    private static readonly HashSet<string> RetiredCraftActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Finishing Touches",
    };

    private readonly BarInputDiagnostics diagnostics;
    private readonly List<HotbarActionOption> currentJobActions = new();
    private readonly List<HotbarActionOption> emoteCommands = new();
    private readonly List<HotbarActionOption> mountCommands = new();
    private readonly List<HotbarActionOption> minionCommands = new();
    private readonly List<HotbarActionOption> macroCommands = new();
    private readonly Dictionary<(HotbarSlotType Type, uint Id), string> actionNames = new();
    private bool generalCommandCacheInitialized;

    private uint cachedJobId = uint.MaxValue;
    private int cachedLevel = -1;
    private bool actionCacheInitialized;
    private string cachedJobLabel = "Current Job";
    private string activeElementId = HudElementIds.ActionBarOne;
    private int inputFrame = -1;
    private HotbarSlotReference? pendingAssignmentReadback;
    private HotbarSlotType pendingAssignmentCommandType;
    private uint pendingAssignmentActionId;
    private int pendingAssignmentReadbackFrame = -1;

    public HotbarEditingService(BarInputDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    public bool IsEnabled { get; private set; }
    public string ActiveElementId => activeElementId;
    public HotbarSlotReference? SelectedSlot { get; private set; }
    public int CrossHotbarSet { get; private set; } = 1;
    public uint DraggedActionId { get; private set; }
    public HotbarSlotType DraggedActionType { get; private set; } = HotbarSlotType.Action;
    public uint DraggedActionIconId { get; private set; }
    public string DraggedActionName { get; private set; } = string.Empty;
    public bool IsDraggingAction => DraggedActionId != 0;
    public HotbarSlotReference? DraggedSlot { get; private set; }
    public HotbarSlotReference? HoveredSlotThisFrame { get; private set; }
    public bool DropHandledThisFrame { get; private set; }
    public bool IsDraggingSlot => DraggedSlot is not null;
    public string CurrentJobLabel
    {
        get
        {
            EnsureActionCache();
            return cachedJobLabel;
        }
    }

    public static bool IsSupportedElement(string id)
        => id is HudElementIds.ActionBarOne or
            HudElementIds.ActionBarTwo or
            HudElementIds.ActionBarThree or
            HudElementIds.CrossHotbar or
            HudElementIds.PetBar or
            HudElementIds.UtilityBars or
            HudElementIds.UtilityBarsTwo ||
            HudElementIds.IsAdditionalCombatHotbar(id);

    public void SetEnabled(bool enabled)
    {
        if (enabled && !IsEnabled)
        {


            generalCommandCacheInitialized = false;
            emoteCommands.Clear();
            mountCommands.Clear();
            minionCommands.Clear();
            macroCommands.Clear();
        }

        IsEnabled = enabled;
        if (!enabled)
        {
            SelectedSlot = null;
            CancelActionDrag();
            CancelSlotDrag();
            HoveredSlotThisFrame = null;
            DropHandledThisFrame = false;
            inputFrame = -1;
            pendingAssignmentReadback = null;
            pendingAssignmentCommandType = HotbarSlotType.Empty;
            pendingAssignmentActionId = 0;
            pendingAssignmentReadbackFrame = -1;
        }
    }

    public void SetActiveElement(string id)
    {
        if (!IsSupportedElement(id))
        {
            activeElementId = id;
            SelectedSlot = null;
            CancelActionDrag();
            CancelSlotDrag();
            return;
        }

        if (!string.Equals(activeElementId, id, StringComparison.Ordinal))
        {
            activeElementId = id;
            SelectedSlot = null;
            CancelActionDrag();
            CancelSlotDrag();
        }
    }

    public void SetCrossHotbarSet(int setNumber)
    {
        var clamped = Math.Clamp(setNumber, 1, 8);
        if (CrossHotbarSet == clamped)
            return;

        CrossHotbarSet = clamped;
        CancelActionDrag();
        CancelSlotDrag();
        if (activeElementId == HudElementIds.CrossHotbar)
            SelectedSlot = null;
    }

    public void Select(HotbarSlotReference slot)
    {
        SelectedSlot = slot;
        diagnostics.RecordInputEvent($"Selected {slot.Label}");
    }


    public void BeginInputFrame()
    {
        var frame = ImGui.GetFrameCount();
        if (inputFrame == frame)
            return;

        inputFrame = frame;
        HoveredSlotThisFrame = null;
        DropHandledThisFrame = false;
        ProcessPendingAssignmentReadback(frame);
    }

    public void RegisterHoveredSlot(HotbarSlotReference slot)
        => HoveredSlotThisFrame = slot;

    public bool BeginSlotDrag(HotbarSlotReference slot)
    {
        if (!TryGetSnapshot(slot, out var snapshot) || snapshot.IsEmpty)
            return false;

        DraggedSlot = slot;
        SelectedSlot = slot;
        diagnostics.RecordInputEvent($"Slot drag started from {slot.Label}");
        return true;
    }

    public void CancelSlotDrag() => DraggedSlot = null;

    public void MarkDropHandled()
    {
        DropHandledThisFrame = true;
        DraggedSlot = null;
    }


    public void FinalizePointerRelease(bool pointerInsidePalette)
    {
        if (!IsEnabled)
            return;

        if (!DropHandledThisFrame && DraggedSlot is { } source &&
            HoveredSlotThisFrame is null && !pointerInsidePalette)
        {
            if (!Clear(source, out var clearMessage) && !string.IsNullOrWhiteSpace(clearMessage))
                Plugin.ChatGui.PrintError(clearMessage);
        }

        if (!DropHandledThisFrame && IsDraggingAction && !pointerInsidePalette)
            CancelActionDrag();

        DraggedSlot = null;
    }

    public string GetActionDisplayName(uint actionId) => ResolveActionName(HotbarSlotType.Action, actionId);


    public void SetNativeCrossEditMode(bool enabled)
    {
        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
                return;

            if (enabled)
                module->CrossHotbarFlags |= CrossHotbarFlags.EditMode;
            else
                module->CrossHotbarFlags &= ~CrossHotbarFlags.EditMode;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not update the native cross-hotbar edit flag.");
        }
    }

    public void BeginActionDrag(HotbarActionOption action)
        => BeginActionDrag(action.CommandType, action.ActionId, action.Name, action.IconId);

    public void BeginActionDrag(HotbarSlotType commandType, uint actionId, string actionName, uint iconId = 0)
    {
        if (actionId == 0)
            return;

        SelectedSlot = null;
        CancelSlotDrag();
        DraggedActionType = commandType;
        DraggedActionId = actionId;
        DraggedActionIconId = iconId;
        DraggedActionName = HotbarDisplayNameService.ResolveStoredActionName(
            commandType,
            actionId,
            actionName);
        diagnostics.RecordInputEvent($"Palette action armed: {DraggedActionName} ({commandType} {actionId})");
    }

    public void CancelActionDrag()
    {
        DraggedActionType = HotbarSlotType.Action;
        DraggedActionId = 0;
        DraggedActionIconId = 0;
        DraggedActionName = string.Empty;
    }

    public bool TryGetSnapshot(HotbarSlotReference slotReference, out HotbarSlotSnapshot snapshot)
    {
        snapshot = default;
        if (Plugin.Instance.AdditionalHotbars.IsVirtualReference(slotReference))
            return Plugin.Instance.AdditionalHotbars.TryGetSnapshot(slotReference, out snapshot);

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady || !IsValidSlot(slotReference))
                return false;

            var slot = module->GetSlotById(slotReference.HotbarId, slotReference.SlotId);
            if (slot == null)
                return false;

            var type = slot->CommandType;
            var id = slot->CommandId;
            snapshot = new HotbarSlotSnapshot(
                slotReference,
                type,
                id,
                HotbarDisplayNameService.ResolveNativeSlot(slot));
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not read hotbar {Hotbar}, slot {Slot} for editing.", slotReference.HotbarId + 1u, slotReference.SlotId + 1u);
            return false;
        }
    }

    public bool AssignAction(
        HotbarSlotReference destination,
        HotbarSlotType commandType,
        uint actionId,
        out string message)
    {
        message = string.Empty;
        if (actionId == 0 ||
            !AdditionalHotbarService.IsPlayerAssignableCommand(commandType) ||
            !IsValidSlot(destination))
        {
            message = "That command or destination slot is invalid.";
            diagnostics.RecordNativeWrite($"REJECTED — assign {commandType} {actionId} to {destination.Label}: invalid command or slot");
            return false;
        }

        if (Plugin.Instance.AdditionalHotbars.IsVirtualReference(destination))
        {
            var actionName = ResolveActionName(commandType, actionId);
            if (!Plugin.Instance.AdditionalHotbars.SetVirtualSlot(destination, commandType, actionId, actionName, DraggedActionIconId))
            {
                message = "RE:Frame could not assign that command to the overflow slot.";
                return false;
            }

            SelectedSlot = destination;
            Plugin.Instance.SaveConfiguration();
            diagnostics.RecordNativeWrite($"SUCCESS — assigned {commandType} {actionId} to RE:Frame-owned {destination.Label}");
            message = $"Assigned {actionName} to {destination.Label}.";
            return true;
        }

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
            {
                message = "FFXIV's hotbar data is not ready yet.";
                diagnostics.RecordNativeWrite($"FAILED — assign action {actionId} to {destination.Label}: RaptureHotbarModule not ready");
                return false;
            }

            var actionName = ResolveActionName(commandType, actionId);
            var activeClassJobId = module->ActiveHotbarClassJobId;
            var sharedHotbar = module->IsHotbarShared(destination.HotbarId);
            diagnostics.BeginAssignmentAudit(
                destination,
                commandType.ToString(),
                actionId,
                actionName,
                ImGui.GetFrameCount(),
                activeClassJobId,
                sharedHotbar);

            if (!TrySetAndPersistSlot(
                    module,
                    destination,
                    commandType,
                    actionId,
                    out var mutationDetail))
            {
                message = $"FFXIV rejected the native slot update for {destination.Label}.";
                diagnostics.RecordImmediateReadback($"FAILED — {mutationDetail}");
                diagnostics.RecordNativeWrite($"FAILED — {mutationDetail}");
                return false;
            }

            SelectedSlot = destination;

            var immediateMatch = TryReadNativeSlotState(
                module,
                destination,
                commandType,
                actionId,
                refreshIcon: true,
                out var immediateState);
            diagnostics.RecordImmediateReadback($"{immediateState}; path={mutationDetail}");
            diagnostics.RecordNativeWrite(immediateMatch
                ? $"VERIFIED IMMEDIATE — native slot Set + WriteSavedSlot({commandType} {actionId}) → {destination.Label}"
                : $"WRITE CALLED — {mutationDetail}; immediate readback did not match {commandType} {actionId} at {destination.Label}");

            pendingAssignmentReadback = destination;
            pendingAssignmentCommandType = commandType;
            pendingAssignmentActionId = actionId;
            pendingAssignmentReadbackFrame = ImGui.GetFrameCount() + 1;

            message = immediateMatch
                ? $"Assigned {actionName} to {destination.Label}."
                : $"FFXIV received the write request for {destination.Label}; awaiting next-frame verification.";
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not assign action {ActionId} to hotbar {Hotbar}, slot {Slot}.", actionId, destination.HotbarId + 1u, destination.SlotId + 1u);
            message = "FFXIV could not save that action to the selected slot.";
            diagnostics.RecordNativeWrite($"EXCEPTION — assign action {actionId} to {destination.Label}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool Clear(HotbarSlotReference slotReference, out string message)
    {
        message = string.Empty;
        if (!IsValidSlot(slotReference))
        {
            message = "That hotbar slot is invalid.";
            diagnostics.RecordNativeWrite($"REJECTED — clear {slotReference.Label}: invalid slot");
            return false;
        }

        if (Plugin.Instance.AdditionalHotbars.IsVirtualReference(slotReference))
        {
            if (!Plugin.Instance.AdditionalHotbars.ClearVirtualSlot(slotReference))
            {
                message = $"RE:Frame could not clear {slotReference.Label}.";
                return false;
            }

            SelectedSlot = slotReference;
            Plugin.Instance.SaveConfiguration();
            diagnostics.RecordNativeWrite($"SUCCESS — cleared RE:Frame-owned {slotReference.Label}");
            message = $"Cleared {slotReference.Label}.";
            return true;
        }

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
            {
                message = "FFXIV's hotbar data is not ready yet.";
                diagnostics.RecordNativeWrite($"FAILED — clear {slotReference.Label}: RaptureHotbarModule not ready");
                return false;
            }

            if (!TrySetAndPersistSlot(
                    module,
                    slotReference,
                    HotbarSlotType.Empty,
                    0u,
                    out var mutationDetail))
            {
                message = $"FFXIV could not clear {slotReference.Label}.";
                diagnostics.RecordNativeWrite($"FAILED — clear {slotReference.Label}: {mutationDetail}");
                return false;
            }

            SelectedSlot = slotReference;
            message = $"Cleared {slotReference.Label}.";
            diagnostics.RecordNativeWrite($"SUCCESS — cleared {slotReference.Label} through {mutationDetail}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not clear hotbar {Hotbar}, slot {Slot}.", slotReference.HotbarId + 1u, slotReference.SlotId + 1u);
            message = "FFXIV could not clear that hotbar slot.";
            diagnostics.RecordNativeWrite($"EXCEPTION — clear {slotReference.Label}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool Transfer(HotbarSlotReference source, HotbarSlotReference destination, bool copy, out string message)
    {
        message = string.Empty;
        if (source == destination)
        {
            diagnostics.RecordNativeWrite($"NO-OP — source and destination are both {source.Label}");
            return true;
        }
        if (!TryGetSnapshot(source, out var sourceSnapshot) || sourceSnapshot.IsEmpty)
        {
            message = "The source slot is empty.";
            diagnostics.RecordNativeWrite($"REJECTED — transfer from {source.Label}: source slot empty or unreadable");
            return false;
        }
        if (!IsValidSlot(destination))
        {
            message = "The destination slot is invalid.";
            diagnostics.RecordNativeWrite($"REJECTED — transfer {source.Label} → {destination.Label}: invalid destination");
            return false;
        }

        if (Plugin.Instance.AdditionalHotbars.IsVirtualReference(source) ||
            Plugin.Instance.AdditionalHotbars.IsVirtualReference(destination))
            return TransferWithVirtual(source, destination, sourceSnapshot, copy, out message);

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
            {
                message = "FFXIV's hotbar data is not ready yet.";
                diagnostics.RecordNativeWrite($"FAILED — transfer {source.Label} → {destination.Label}: RaptureHotbarModule not ready");
                return false;
            }

            if (!TryGetSnapshot(destination, out var destinationSnapshot))
            {
                message = $"FFXIV could not read {destination.Label} before the transfer.";
                diagnostics.RecordNativeWrite($"FAILED — transfer {source.Label} → {destination.Label}: destination unreadable");
                return false;
            }

            if (!TrySetAndPersistSlot(
                    module,
                    destination,
                    sourceSnapshot.CommandType,
                    sourceSnapshot.CommandId,
                    out var destinationMutation))
            {
                message = $"FFXIV could not update {destination.Label}.";
                diagnostics.RecordNativeWrite($"FAILED — transfer {source.Label} → {destination.Label}: {destinationMutation}");
                return false;
            }

            if (!copy)
            {
                var sourceReplacementType = destinationSnapshot.IsEmpty
                    ? HotbarSlotType.Empty
                    : destinationSnapshot.CommandType;
                var sourceReplacementId = destinationSnapshot.IsEmpty
                    ? 0u
                    : destinationSnapshot.CommandId;

                if (!TrySetAndPersistSlot(
                        module,
                        source,
                        sourceReplacementType,
                        sourceReplacementId,
                        out var sourceMutation))
                {


                    var rollbackType = destinationSnapshot.IsEmpty
                        ? HotbarSlotType.Empty
                        : destinationSnapshot.CommandType;
                    var rollbackId = destinationSnapshot.IsEmpty
                        ? 0u
                        : destinationSnapshot.CommandId;
                    var rolledBack = TrySetAndPersistSlot(
                        module,
                        destination,
                        rollbackType,
                        rollbackId,
                        out var rollbackDetail);

                    message = rolledBack
                        ? "FFXIV could not complete that move; the destination was restored."
                        : "FFXIV could not complete that move and the destination rollback also failed.";
                    diagnostics.RecordNativeWrite(
                        $"FAILED — source update {source.Label}: {sourceMutation}; rollback={(rolledBack ? "SUCCESS" : "FAILED")} ({rollbackDetail})");
                    return false;
                }

                diagnostics.RecordNativeWrite(
                    $"SUCCESS — moved/swapped {source.Label} → {destination.Label}; destination={destinationMutation}; source={sourceMutation}");
            }
            else
            {
                diagnostics.RecordNativeWrite(
                    $"SUCCESS — copied {source.Label} → {destination.Label}; destination={destinationMutation}");
            }

            SelectedSlot = destination;
            message = copy
                ? $"Copied {sourceSnapshot.DisplayName} to {destination.Label}."
                : $"Moved {sourceSnapshot.DisplayName} to {destination.Label}.";
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not transfer hotbar {SourceHotbar}:{SourceSlot} to {DestinationHotbar}:{DestinationSlot}.", source.HotbarId, source.SlotId, destination.HotbarId, destination.SlotId);
            message = "FFXIV could not save that hotbar change.";
            diagnostics.RecordNativeWrite($"EXCEPTION — transfer {source.Label} → {destination.Label}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }


    private bool TransferWithVirtual(
        HotbarSlotReference source,
        HotbarSlotReference destination,
        HotbarSlotSnapshot sourceSnapshot,
        bool copy,
        out string message)
    {
        message = string.Empty;
        if (!TryGetSnapshot(destination, out var destinationSnapshot))
        {
            message = $"RE:Frame could not read {destination.Label} before the transfer.";
            return false;
        }

        if (!CanStoreSnapshot(destination, sourceSnapshot) || (!copy && !CanStoreSnapshot(source, destinationSnapshot)))
        {
            message = "Overflow hotbars can contain actions, crafting actions, emotes, mounts, minions, and macros.";
            return false;
        }

        if (!WriteSnapshot(destination, sourceSnapshot, out var destinationError))
        {
            message = destinationError;
            return false;
        }

        if (!copy && !WriteSnapshot(source, destinationSnapshot, out var sourceError))
        {
            WriteSnapshot(destination, destinationSnapshot, out _);
            message = string.IsNullOrWhiteSpace(sourceError)
                ? "RE:Frame could not complete that move; the destination was restored."
                : sourceError;
            return false;
        }

        Plugin.Instance.SaveConfiguration();
        SelectedSlot = destination;
        diagnostics.RecordNativeWrite($"SUCCESS — {(copy ? "copied" : "moved/swapped")} {source.Label} → {destination.Label} with RE:Frame overflow support");
        message = copy
            ? $"Copied {sourceSnapshot.DisplayName} to {destination.Label}."
            : $"Moved {sourceSnapshot.DisplayName} to {destination.Label}.";
        return true;
    }

    private static bool CanStoreSnapshot(HotbarSlotReference destination, HotbarSlotSnapshot snapshot)
    {
        if (!Plugin.Instance.AdditionalHotbars.IsVirtualReference(destination) || snapshot.IsEmpty)
            return true;
        return AdditionalHotbarService.IsPlayerAssignableCommand(snapshot.CommandType);
    }

    private static bool WriteSnapshot(
        HotbarSlotReference destination,
        HotbarSlotSnapshot snapshot,
        out string message)
    {
        message = string.Empty;
        if (Plugin.Instance.AdditionalHotbars.IsVirtualReference(destination))
        {
            if (snapshot.IsEmpty)
                return Plugin.Instance.AdditionalHotbars.ClearVirtualSlot(destination);
            var success = Plugin.Instance.AdditionalHotbars.SetVirtualSlot(
                destination,
                snapshot.CommandType,
                snapshot.CommandId,
                snapshot.DisplayName);
            if (!success)
                message = $"RE:Frame could not update {destination.Label}.";
            return success;
        }

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
            {
                message = "FFXIV's hotbar data is not ready yet.";
                return false;
            }

            var type = snapshot.IsEmpty ? HotbarSlotType.Empty : snapshot.CommandType;
            var id = snapshot.IsEmpty ? 0u : snapshot.CommandId;
            if (!TrySetAndPersistSlot(module, destination, type, id, out var detail))
            {
                message = $"FFXIV could not update {destination.Label}: {detail}";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not write a mixed native/overflow hotbar transfer.");
            message = $"FFXIV could not update {destination.Label}.";
            return false;
        }
    }


    private void ProcessPendingAssignmentReadback(int frame)
    {
        if (pendingAssignmentReadback is not { } destination ||
            pendingAssignmentCommandType == HotbarSlotType.Empty ||
            pendingAssignmentActionId == 0 ||
            frame < pendingAssignmentReadbackFrame)
            return;

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
            {
                diagnostics.RecordNextFrameReadback("FAILED — RaptureHotbarModule was not ready on the verification frame");
                diagnostics.RecordNativeWrite($"FAILED READBACK — module not ready for {destination.Label}");
                return;
            }

            var matched = TryReadNativeSlotState(
                module,
                destination,
                pendingAssignmentCommandType,
                pendingAssignmentActionId,
                refreshIcon: true,
                out var state);
            diagnostics.RecordNextFrameReadback(state);
            diagnostics.RecordNativeWrite(matched
                ? $"VERIFIED NEXT FRAME — {pendingAssignmentCommandType} {pendingAssignmentActionId} persisted at {destination.Label}"
                : $"FAILED READBACK — expected {pendingAssignmentCommandType} {pendingAssignmentActionId} at {destination.Label}");
        }
        catch (Exception ex)
        {
            diagnostics.RecordNextFrameReadback($"EXCEPTION — {ex.GetType().Name}: {ex.Message}");
            diagnostics.RecordNativeWrite($"EXCEPTION DURING READBACK — {destination.Label}: {ex.GetType().Name}");
        }
        finally
        {
            pendingAssignmentReadback = null;
            pendingAssignmentCommandType = HotbarSlotType.Empty;
            pendingAssignmentActionId = 0;
            pendingAssignmentReadbackFrame = -1;
        }
    }


    private static bool TrySetAndPersistSlot(
        RaptureHotbarModule* module,
        HotbarSlotReference destination,
        HotbarSlotType commandType,
        uint commandId,
        out string detail)
    {
        detail = string.Empty;
        if (module == null || !module->ModuleReady || !IsValidSlot(destination))
        {
            detail = "module not ready or destination invalid";
            return false;
        }

        var slot = module->GetSlotById(destination.HotbarId, destination.SlotId);
        if (slot == null)
        {
            detail = $"{destination.Label} returned a null native slot pointer";
            return false;
        }

        slot->Set(commandType, commandId);
        if (commandType != HotbarSlotType.Empty && commandId != 0)
            slot->LoadIconId();

        var liveMatches = commandType == HotbarSlotType.Empty || commandId == 0
            ? slot->CommandType == HotbarSlotType.Empty && slot->CommandId == 0
            : slot->CommandType == commandType && slot->CommandId == commandId;
        if (!liveMatches)
        {
            detail = $"HotbarSlot.Set did not take (expected {commandType}:{commandId}, read {slot->CommandType}:{slot->CommandId})";
            return false;
        }

        var activeClassJobId = (uint)module->ActiveHotbarClassJobId;
        module->WriteSavedSlot(
            activeClassJobId,
            destination.HotbarId,
            destination.SlotId,
            slot,
            ignoreSharedHotbars: false,
            isPvpSlot: module->PvPHotbarsActive);

        detail = $"HotbarSlot.Set + WriteSavedSlot(job {activeClassJobId}, shared={module->IsHotbarShared(destination.HotbarId)}, pvp={module->PvPHotbarsActive})";
        return true;
    }

    private static bool TryReadNativeSlotState(
        RaptureHotbarModule* module,
        HotbarSlotReference destination,
        HotbarSlotType expectedCommandType,
        uint expectedActionId,
        bool refreshIcon,
        out string state)
    {
        var slot = module->GetSlotById(destination.HotbarId, destination.SlotId);
        if (slot == null)
        {
            state = $"FAILED — {destination.Label} returned a null native slot pointer";
            return false;
        }

        var commandType = slot->CommandType;
        var commandId = slot->CommandId;
        var iconBefore = slot->IconId;
        if (refreshIcon && commandType == expectedCommandType && commandId == expectedActionId)
            slot->LoadIconId();
        var iconAfter = slot->IconId;
        var matches = commandType == expectedCommandType && commandId == expectedActionId;
        state = $"{(matches ? "MATCH" : "MISMATCH")} — type {commandType}; id {commandId}; icon {iconBefore}→{iconAfter}; empty={slot->IsEmpty}";
        return matches;
    }

    public IReadOnlyList<HotbarActionOption> SearchActions(string query, int maximumResults = 60)
        => SearchCommands(HotbarPaletteCategory.Actions, query, maximumResults);

    public IReadOnlyList<HotbarActionOption> SearchCommands(
        HotbarPaletteCategory category,
        string query,
        int maximumResults = 60)
    {
        EnsureActionCache();
        if (category != HotbarPaletteCategory.Actions)
            EnsureGeneralCommandCache();

        var normalized = query?.Trim() ?? string.Empty;
        IEnumerable<HotbarActionOption> results = category switch
        {
            HotbarPaletteCategory.Emotes => emoteCommands,
            HotbarPaletteCategory.Mounts => mountCommands,
            HotbarPaletteCategory.Minions => minionCommands,
            HotbarPaletteCategory.Macros => macroCommands,
            _ => currentJobActions,
        };

        if (!string.IsNullOrWhiteSpace(normalized))
            results = results.Where(command => command.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase));

        return results
            .OrderBy(command => string.IsNullOrWhiteSpace(normalized) || !command.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ThenBy(command => command.IsRoleAction)
            .ThenBy(command => command.RequiredLevel)
            .ThenBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maximumResults, 1, 300))
            .ToArray();
    }

    private void EnsureGeneralCommandCache()
    {
        if (generalCommandCacheInitialized)
            return;

        generalCommandCacheInitialized = true;
        emoteCommands.Clear();
        mountCommands.Clear();
        minionCommands.Clear();
        macroCommands.Clear();

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaEmote>();
            foreach (var row in sheet)
            {
                if (row.RowId != 0 && Plugin.UnlockState.IsEmoteUnlocked(row))
                    AddGeneralCommand(emoteCommands, HotbarSlotType.Emote, row.RowId, row, "Name", "Singular");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not build the Emote hotbar palette.");
        }

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaMount>();
            foreach (var row in sheet)
            {
                if (row.RowId != 0 && Plugin.UnlockState.IsMountUnlocked(row))
                    AddGeneralCommand(mountCommands, HotbarSlotType.Mount, row.RowId, row, "Singular", "Name");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not build the Mount hotbar palette.");
        }

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaCompanion>();
            foreach (var row in sheet)
            {
                if (row.RowId != 0 && Plugin.UnlockState.IsCompanionUnlocked(row))
                    AddGeneralCommand(minionCommands, HotbarSlotType.Companion, row.RowId, row, "Singular", "Name");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not build the Minion hotbar palette.");
        }

        DeduplicateAndSort(emoteCommands);
        DeduplicateAndSort(mountCommands);
        DeduplicateAndSort(minionCommands);
        macroCommands.AddRange(BuildMacroCommands());
    }

    private void AddGeneralCommand<T>(
        List<HotbarActionOption> destination,
        HotbarSlotType commandType,
        uint rowId,
        T row,
        params string[] nameProperties)
        where T : struct
    {
        if (rowId == 0)
            return;

        var boxed = (object)row;
        var name = ReadTextProperty(boxed, nameProperties);
        if (string.IsNullOrWhiteSpace(name) || !IsUnlockedIfKnown(boxed))
            return;

        var iconId = ReadUnsignedProperty(boxed, "Icon", "IconId");
        if (iconId == 0)
            iconId = ResolveNativeCommandIcon(commandType, rowId);
        if (iconId == 0)
            return;

        destination.Add(new HotbarActionOption(commandType, rowId, name, iconId, 0u, false));
        actionNames[(commandType, rowId)] = name;
    }

    private IReadOnlyList<HotbarActionOption> BuildMacroCommands()
    {
        var macros = new List<HotbarActionOption>(200);
        try
        {
            var module = RaptureMacroModule.Instance();
            if (module == null)
                return macros;

            for (uint set = 0; set <= 1; set++)
            {
                for (uint index = 0; index < 100; index++)
                {
                    var macro = module->GetMacro(set, index);
                    if (macro == null)
                        continue;

                    var name = macro->Name.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;


                    var commandId = (set << 8) | (index + 1u);
                    var iconId = ResolveNativeCommandIcon(HotbarSlotType.Macro, commandId);
                    macros.Add(new HotbarActionOption(
                        HotbarSlotType.Macro,
                        commandId,
                        name,
                        iconId,
                        0u,
                        false));
                    actionNames[(HotbarSlotType.Macro, commandId)] = name;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not build the Macro hotbar palette.");
        }

        return macros
            .OrderBy(macro => macro.ActionId >> 8)
            .ThenBy(macro => macro.ActionId & 0xFFu)
            .ToArray();
    }

    private static bool IsUnlockedIfKnown(object row)
    {
        if (!TryReadReferencedRowId(row, "UnlockLink", out var unlockLink) || unlockLink == 0)
            return true;

        try
        {
            var uiState = UIState.Instance();
            return uiState == null || uiState->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink);
        }
        catch
        {

            return true;
        }
    }

    private static uint ResolveNativeCommandIcon(HotbarSlotType commandType, uint commandId)
    {
        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
                return 0;

            var scratch = &module->ScratchSlot;
            try
            {
                scratch->Set(commandType, commandId);
                scratch->LoadIconId();
                return scratch->IconId;
            }
            finally
            {
                scratch->Set(HotbarSlotType.Empty, 0u);
            }
        }
        catch
        {
            return 0;
        }
    }

    private void EnsureActionCache()
    {
        var jobId = Plugin.PlayerState.IsLoaded && Plugin.PlayerState.ClassJob.IsValid
            ? Plugin.PlayerState.ClassJob.RowId
            : 0u;
        var level = Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.Level : 0;
        if (cachedJobId == jobId && cachedLevel == level && actionCacheInitialized)
            return;

        cachedJobId = jobId;
        cachedLevel = level;
        actionCacheInitialized = true;
        currentJobActions.Clear();
        actionNames.Clear();
        generalCommandCacheInitialized = false;
        emoteCommands.Clear();
        mountCommands.Clear();
        minionCommands.Clear();
        macroCommands.Clear();
        cachedJobLabel = "Current Job";

        var jobIds = new HashSet<uint>();
        var jobAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isDiscipleOfHand = false;
        if (jobId != 0)
            jobIds.Add(jobId);

        if (Plugin.PlayerState.IsLoaded && Plugin.PlayerState.ClassJob.IsValid)
        {
            var currentJob = Plugin.PlayerState.ClassJob.Value;
            var abbreviation = currentJob.Abbreviation.ToString();
            var jobName = currentJob.Name.ToString().Trim();
            cachedJobLabel = !string.IsNullOrWhiteSpace(abbreviation)
                ? $"{abbreviation} • Level {Math.Max(0, level)}"
                : !string.IsNullOrWhiteSpace(jobName)
                    ? $"{jobName} • Level {Math.Max(0, level)}"
                    : $"Current Job • Level {Math.Max(0, level)}";
            if (!string.IsNullOrWhiteSpace(abbreviation))
            {
                jobAbbreviations.Add(abbreviation);
                isDiscipleOfHand = DiscipleOfHandAbbreviations.Contains(abbreviation);
            }

            if (isDiscipleOfHand)
                cachedJobLabel = $"{abbreviation} • Level {Math.Max(0, level)} • Shared Crafting Actions";

            if (TryReadReferencedRowId(currentJob, "ClassJobParent", out var parentJobId) && parentJobId != 0)
            {
                jobIds.Add(parentJobId);
                try
                {
                    var classJobs = Plugin.DataManager.GetExcelSheet<LuminaClassJob>();
                    if (classJobs.TryGetRow(parentJobId, out var parentJob))
                    {
                        var parentAbbreviation = parentJob.Abbreviation.ToString();
                        if (!string.IsNullOrWhiteSpace(parentAbbreviation))
                            jobAbbreviations.Add(parentAbbreviation);
                    }
                }
                catch
                {

                }
            }
        }

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaAction>();
            foreach (var action in sheet)
            {
                if (action.RowId == 0)
                    continue;

                var name = action.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name) || action.Icon == 0)
                    continue;

                var boxedAction = (object)action;


                if (TryReadBooleanProperty(boxedAction, "IsPlayerAction", out var isPlayerAction) && !isPlayerAction)
                    continue;
                if (TryReadBooleanProperty(boxedAction, "IsPvP", out var isPvp) && isPvp)
                    continue;
                if (TryReadBooleanProperty(boxedAction, "IsPvPAction", out var isPvpAction) && isPvpAction)
                    continue;

                var hasPlayerJobMetadata = TryMatchJob(boxedAction, jobIds, jobAbbreviations, out var matchesCurrentJob);
                if (!hasPlayerJobMetadata || !matchesCurrentJob)
                    continue;

                var requiredLevel = ReadUnsignedProperty(boxedAction, "ClassJobLevel", "Level");
                if (requiredLevel == 0 || requiredLevel > (uint)Math.Max(0, level))
                    continue;


                if (!isDiscipleOfHand &&
                    TryReadReferencedRowId(boxedAction, "UnlockLink", out var unlockLink) &&
                    unlockLink != 0)
                {
                    try
                    {
                        var uiState = UIState.Instance();
                        if (uiState != null && !uiState->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink))
                            continue;
                    }
                    catch
                    {


                    }
                }

                var isRoleAction = TryReadBooleanProperty(boxedAction, "IsRoleAction", out var roleAction) && roleAction;
                var option = new HotbarActionOption(
                    HotbarSlotType.Action,
                    action.RowId,
                    name,
                    action.Icon,
                    requiredLevel,
                    isRoleAction);
                currentJobActions.Add(option);
                actionNames[(HotbarSlotType.Action, action.RowId)] = name;
            }

            if (isDiscipleOfHand)
                AddCraftActions(jobIds, jobAbbreviations, level);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not build the action list for hotbar editing.");
        }

        DeduplicateAndSort(currentJobActions);
    }

    private static void DeduplicateAndSort(List<HotbarActionOption> actions)
    {
        var unique = actions
            .GroupBy(action => (action.CommandType, action.ActionId))
            .Select(group => group.First())
            .OrderBy(action => action.IsRoleAction)
            .ThenBy(action => action.RequiredLevel)
            .ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        actions.Clear();
        actions.AddRange(unique);
    }

    private string ResolveActionName(HotbarSlotType commandType, uint actionId)
    {
        EnsureActionCache();
        if (commandType is not (HotbarSlotType.Action or HotbarSlotType.CraftAction))
            EnsureGeneralCommandCache();

        if (actionNames.TryGetValue((commandType, actionId), out var cached) &&
            !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var resolved = commandType is HotbarSlotType.Action or HotbarSlotType.CraftAction
            ? HotbarDisplayNameService.ResolveActionName(commandType, actionId)
            : ResolveNativeCommandName(commandType, actionId);
        actionNames[(commandType, actionId)] = resolved;
        return resolved;
    }

    private static string ResolveNativeCommandName(HotbarSlotType commandType, uint commandId)
    {
        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
                return HotbarDisplayNameService.ResolveActionName(commandType, commandId);

            var scratch = &module->ScratchSlot;
            try
            {
                scratch->Set(commandType, commandId);
                scratch->LoadIconId();
                return HotbarDisplayNameService.ResolveNativeSlot(scratch);
            }
            finally
            {
                scratch->Set(HotbarSlotType.Empty, 0u);
            }
        }
        catch
        {
            return HotbarDisplayNameService.ResolveActionName(commandType, commandId);
        }
    }

    private void AddCraftActions(
        HashSet<uint> currentJobIds,
        HashSet<string> currentJobAbbreviations,
        int level)
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaCraftAction>();
            foreach (var craftAction in sheet)
            {
                if (craftAction.RowId == 0)
                    continue;

                var name = craftAction.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name) || craftAction.Icon == 0)
                    continue;

                var boxedCraftAction = (object)craftAction;
                if (IsRetiredCraftAction(boxedCraftAction, name))
                    continue;

                var hasJobMetadata = TryMatchJob(
                    boxedCraftAction,
                    currentJobIds,
                    currentJobAbbreviations,
                    out var matchesCurrentJob);
                if (!hasJobMetadata || !matchesCurrentJob)
                    continue;

                var requiredLevel = ReadUnsignedProperty(
                    boxedCraftAction,
                    "ClassJobLevel",
                    "Level");
                if (requiredLevel == 0 || requiredLevel > (uint)Math.Max(0, level))
                    continue;

                var option = new HotbarActionOption(
                    HotbarSlotType.CraftAction,
                    craftAction.RowId,
                    name,
                    craftAction.Icon,
                    requiredLevel,
                    false);
                currentJobActions.Add(option);
                actionNames[(HotbarSlotType.CraftAction, craftAction.RowId)] = name;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not build the CraftAction list for hotbar editing.");
        }
    }

    private static bool IsRetiredCraftAction(object craftAction, string name)
    {
        if (RetiredCraftActionNames.Contains(name))
            return true;


        if (TryReadBooleanProperty(craftAction, "IsPlayerAction", out var isPlayerAction) && !isPlayerAction)
            return true;
        if (TryReadBooleanProperty(craftAction, "IsDeprecated", out var isDeprecated) && isDeprecated)
            return true;
        if (TryReadBooleanProperty(craftAction, "IsRemoved", out var isRemoved) && isRemoved)
            return true;
        if (TryReadBooleanProperty(craftAction, "IsObsolete", out var isObsolete) && isObsolete)
            return true;
        if (TryReadBooleanProperty(craftAction, "IsDisabled", out var isDisabled) && isDisabled)
            return true;

        return false;
    }

    private static bool IsValidSlot(HotbarSlotReference slot)
        => Plugin.Instance.AdditionalHotbars.IsKnownReference(slot) ||
           (slot.HotbarId <= 17u && slot.SlotId <= 15u);

    private static bool TryMatchJob(
        object row,
        HashSet<uint> currentJobIds,
        HashSet<string> currentJobAbbreviations,
        out bool matchesCurrentJob)
    {
        matchesCurrentJob = false;
        var hasMetadata = false;

        if (TryReadReferencedRowId(row, "ClassJob", out var classJobId) && classJobId != 0)
        {
            hasMetadata = true;
            matchesCurrentJob |= currentJobIds.Contains(classJobId);
        }

        if (TryReadReferencedValue(row, "ClassJobCategory", out var category) && category is not null)
        {
            hasMetadata = true;
            foreach (var abbreviation in currentJobAbbreviations)
            {
                var property = category.GetType().GetProperty(
                    abbreviation,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property?.PropertyType == typeof(bool) && property.GetValue(category) is true)
                {
                    matchesCurrentJob = true;
                    break;
                }
            }
        }

        return hasMetadata;
    }

    private static bool TryReadReferencedRowId(object source, string propertyName, out uint rowId)
    {
        rowId = 0;
        try
        {
            var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var reference = property?.GetValue(source);
            if (reference is null)
                return false;

            if (TryConvertUnsigned(reference, out rowId))
                return true;

            var rowIdProperty = reference.GetType().GetProperty("RowId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return TryConvertUnsigned(rowIdProperty?.GetValue(reference), out rowId);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadReferencedValue(object source, string propertyName, out object? value)
    {
        value = null;
        try
        {
            var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var reference = property?.GetValue(source);
            if (reference is null)
                return false;

            var rowIdProperty = reference.GetType().GetProperty("RowId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (!TryConvertUnsigned(rowIdProperty?.GetValue(reference), out var rowId) || rowId == 0)
                return false;

            var valueProperty = reference.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            value = valueProperty?.GetValue(reference);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadBooleanProperty(object source, string propertyName, out bool value)
    {
        value = false;
        try
        {
            var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null)
                return false;

            var raw = property.GetValue(source);
            if (raw is bool boolean)
            {
                value = boolean;
                return true;
            }

            if (raw is not null)
            {
                value = Convert.ToBoolean(raw);
                return true;
            }
        }
        catch
        {

        }

        return false;
    }

    private static string ReadTextProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            try
            {
                var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var text = property?.GetValue(source)?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            catch
            {

            }
        }

        return string.Empty;
    }

    private static uint ReadUnsignedProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            try
            {
                var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (TryConvertUnsigned(property?.GetValue(source), out var value))
                    return value;
            }
            catch
            {

            }
        }

        return 0;
    }

    private static bool TryConvertUnsigned(object? value, out uint result)
    {
        result = 0;
        try
        {
            if (value is null)
                return false;
            result = Convert.ToUInt32(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
