using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using REFrameXIV.Models;
using REFrameXIV.UI;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;

namespace REFrameXIV.Services;


public sealed unsafe class AdditionalHotbarService
{


    private static readonly int[] NativeBackingCandidates = { 3, 4, 6, 7, 8, 9 };

    private readonly Configuration configuration;

    public AdditionalHotbarService(Configuration configuration)
    {
        this.configuration = configuration;
        EnsureValid();
    }

    public IReadOnlyList<ReframeAdditionalHotbar> CombatBars
        => configuration.AdditionalCombatHotbars;

    public int RemainingNativeCombatBars
    {
        get
        {
            var used = configuration.AdditionalCombatHotbars
                .Where(bar => bar.IsNativeBacked)
                .Select(bar => bar.NativeHotbarId)
                .ToHashSet();
            return NativeBackingCandidates.Count(candidate => !used.Contains(candidate));
        }
    }

    public IEnumerable<string> ElementIds
        => configuration.AdditionalCombatHotbars.Select(bar => bar.ElementId);

    public void EnsureValid()
    {
        configuration.AdditionalCombatHotbars ??= new List<ReframeAdditionalHotbar>();
        configuration.SecondUtilityBarSlots ??= new List<ReframeVirtualHotbarSlot>();
        configuration.ReframeHotbarKeybinds ??= new List<ReframeHotbarKeybind>();

        var usedIds = new HashSet<uint>();
        var usedNativeBacking = new HashSet<int>();
        var nextId = Math.Max(1u, configuration.NextAdditionalCombatHotbarId);
        foreach (var bar in configuration.AdditionalCombatHotbars.Where(bar => bar is not null))
        {
            if (bar.Id == 0 || !usedIds.Add(bar.Id))
            {
                while (!usedIds.Add(nextId))
                    nextId++;
                bar.Id = nextId++;
            }

            bar.EnsureValid();
            if (bar.IsNativeBacked &&
                (!NativeBackingCandidates.Contains(bar.NativeHotbarId) ||
                 !usedNativeBacking.Add(bar.NativeHotbarId)))
            {
                bar.NativeHotbarId = -1;
            }
            if (!bar.IsNativeBacked)
                RepairStoredNames(bar.Slots);

            if (string.IsNullOrWhiteSpace(bar.Name))
                bar.Name = bar.IsNativeBacked
                    ? $"Combat Hotbar {bar.NativeHotbarId + 1}"
                    : $"Overflow Hotbar {bar.Id}";
        }

        configuration.AdditionalCombatHotbars = configuration.AdditionalCombatHotbars
            .Where(bar => bar is not null)
            .ToList();
        configuration.NextAdditionalCombatHotbarId = Math.Max(nextId, usedIds.Count == 0 ? 1u : usedIds.Max() + 1u);

        EnsureVirtualSlotList(configuration.SecondUtilityBarSlots);
        RepairStoredNames(configuration.SecondUtilityBarSlots);
        EnsureLayoutDefaults(configuration);
    }

    public ReframeAdditionalHotbar AddCombatHotbar()
    {
        EnsureValid();
        var usedNative = configuration.AdditionalCombatHotbars
            .Where(bar => bar.IsNativeBacked)
            .Select(bar => bar.NativeHotbarId)
            .ToHashSet();
        var nativeId = NativeBackingCandidates.FirstOrDefault(candidate => !usedNative.Contains(candidate), -1);
        var id = Math.Max(1u, configuration.NextAdditionalCombatHotbarId);
        configuration.NextAdditionalCombatHotbarId = id + 1u;
        var overflowOrdinal = configuration.AdditionalCombatHotbars.Count(bar => !bar.IsNativeBacked) + 1;
        var bar = new ReframeAdditionalHotbar
        {
            Id = id,
            Name = nativeId >= 0
                ? $"Combat Hotbar {nativeId + 1}"
                : $"Overflow Hotbar {overflowOrdinal}",
            Enabled = true,
            Columns = 12,
            NativeHotbarId = nativeId,
        };
        bar.EnsureValid();
        configuration.AdditionalCombatHotbars.Add(bar);
        EnsureLayoutDefault(configuration, bar, configuration.AdditionalCombatHotbars.Count - 1);
        HudModeProfileService.SetVisibility(configuration, UiMode.Roleplay, bar.ElementId, true);
        return bar;
    }


    public int AddAllRemainingNativeCombatHotbars()
    {
        var added = 0;
        while (RemainingNativeCombatBars > 0)
        {
            AddCombatHotbar();
            added++;
        }
        return added;
    }

    public bool RemoveCombatHotbar(uint id)
    {
        var bar = configuration.AdditionalCombatHotbars.FirstOrDefault(candidate => candidate.Id == id);
        if (bar is null)
            return false;

        configuration.AdditionalCombatHotbars.Remove(bar);
        RemoveElementState(configuration, bar.ElementId);
        configuration.ReframeHotbarKeybinds.RemoveAll(binding => binding.HotbarId == bar.RuntimeHotbarId);
        return true;
    }

    public bool TryGetByElementId(string elementId, out ReframeAdditionalHotbar bar)
    {
        bar = null!;
        if (!HudElementIds.TryParseAdditionalCombatHotbar(elementId, out var id))
            return false;
        bar = configuration.AdditionalCombatHotbars.FirstOrDefault(candidate => candidate.Id == id)!;
        return bar is not null;
    }

    public bool TryGetByRuntimeHotbarId(uint hotbarId, out ReframeAdditionalHotbar bar)
    {
        bar = configuration.AdditionalCombatHotbars.FirstOrDefault(candidate => candidate.RuntimeHotbarId == hotbarId)!;
        return bar is not null;
    }

    public string GetElementLabel(string elementId)
        => TryGetByElementId(elementId, out var bar)
            ? bar.Name
            : HudElementIds.Label(elementId);

    public bool GetSharedVisibility(string elementId, bool fallback = true)
        => TryGetByElementId(elementId, out var bar) ? bar.Enabled : fallback;

    public bool IsVirtualReference(HotbarSlotReference reference)
        => reference.SlotId < ReframeHotbarIds.SlotCount &&
           (reference.HotbarId == ReframeHotbarIds.SecondUtility ||
            (reference.HotbarId >= ReframeHotbarIds.AdditionalCombatBase &&
             TryGetByRuntimeHotbarId(reference.HotbarId, out var bar) && !bar.IsNativeBacked));

    public bool IsKnownReference(HotbarSlotReference reference)
        => reference.SlotId < ReframeHotbarIds.SlotCount &&
           (reference.HotbarId == ReframeHotbarIds.SecondUtility ||
            TryGetByRuntimeHotbarId(reference.HotbarId, out _));

    public string GetSlotLabel(HotbarSlotReference reference)
    {
        if (reference.HotbarId == ReframeHotbarIds.SecondUtility)
            return $"Utility Hotbar 2 • Slot {reference.SlotId + 1u}";
        return TryGetByRuntimeHotbarId(reference.HotbarId, out var bar)
            ? $"{bar.Name} • Slot {reference.SlotId + 1u}"
            : $"RE:Frame Hotbar {reference.HotbarId} • Slot {reference.SlotId + 1u}";
    }

    public bool TryGetVirtualSlot(HotbarSlotReference reference, out ReframeVirtualHotbarSlot slot)
    {
        slot = null!;
        if (reference.SlotId >= ReframeHotbarIds.SlotCount)
            return false;

        if (reference.HotbarId == ReframeHotbarIds.SecondUtility)
        {
            EnsureVirtualSlotList(configuration.SecondUtilityBarSlots);
            slot = configuration.SecondUtilityBarSlots[(int)reference.SlotId];
            return true;
        }

        if (!TryGetByRuntimeHotbarId(reference.HotbarId, out var bar) || bar.IsNativeBacked)
            return false;
        bar.EnsureValid();
        slot = bar.Slots[(int)reference.SlotId];
        return true;
    }

    public bool TryGetSnapshot(HotbarSlotReference reference, out HotbarSlotSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetVirtualSlot(reference, out var slot))
            return false;
        var type = (HotbarSlotType)slot.CommandType;
        snapshot = new HotbarSlotSnapshot(
            reference,
            type,
            slot.CommandId,
            slot.IsEmpty
                ? "Empty slot"
                : HotbarDisplayNameService.ResolveStoredActionName(type, slot.CommandId, slot.DisplayName));
        return true;
    }

    public bool SetVirtualSlot(
        HotbarSlotReference reference,
        HotbarSlotType commandType,
        uint commandId,
        string? displayName = null,
        uint iconId = 0)
    {
        if (!TryGetVirtualSlot(reference, out var slot))
            return false;
        if (commandType == HotbarSlotType.Empty || commandId == 0)
        {
            slot.Clear();
            return true;
        }


        if (!IsPlayerAssignableCommand(commandType))
            return false;

        slot.CommandType = (int)commandType;
        slot.CommandId = commandId;
        slot.DisplayName = HotbarDisplayNameService.ResolveStoredActionName(
            commandType,
            commandId,
            displayName);
        slot.IconId = iconId != 0 ? iconId : ResolveCommandIcon(commandType, commandId);
        return true;
    }

    public bool ClearVirtualSlot(HotbarSlotReference reference)
    {
        if (!TryGetVirtualSlot(reference, out var slot))
            return false;
        slot.Clear();
        return true;
    }

    public bool ExecuteVirtual(HotbarSlotReference reference)
    {
        if (!TryGetVirtualSlot(reference, out var stored) || stored.IsEmpty)
            return false;

        var commandType = (HotbarSlotType)stored.CommandType;
        if (!IsPlayerAssignableCommand(commandType))
            return false;

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
                return false;


            var scratch = &module->ScratchSlot;
            try
            {
                scratch->Set(commandType, stored.CommandId);
                scratch->LoadIconId();
                module->ExecuteSlot(scratch);
                return true;
            }
            finally
            {
                scratch->Set(HotbarSlotType.Empty, 0u);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not execute virtual hotbar command {Type} {Id}.", commandType, stored.CommandId);
            return false;
        }
    }

    public uint ResolveIconId(HotbarSlotReference reference)
        => TryGetVirtualSlot(reference, out var slot) ? slot.IconId : 0u;

    public static void EnsureLayoutDefaults(Configuration configuration)
    {
        configuration.HudLayouts ??= new Dictionary<string, HudElementLayout>();
        configuration.AdditionalCombatHotbars ??= new List<ReframeAdditionalHotbar>();
        for (var index = 0; index < configuration.AdditionalCombatHotbars.Count; index++)
        {
            var bar = configuration.AdditionalCombatHotbars[index];
            if (bar is not null)
                EnsureLayoutDefault(configuration, bar, index);
        }
    }

    private static void EnsureLayoutDefault(Configuration configuration, ReframeAdditionalHotbar bar, int index)
    {
        if (configuration.HudLayouts.ContainsKey(bar.ElementId))
            return;


        var group = index / 8;
        var row = index % 8;
        var columnOrder = new[] { 1, 0, 2 };
        var column = columnOrder[group % columnOrder.Length];
        var cascade = group / columnOrder.Length;
        var x = 80f + column * 624f + (cascade % 5) * 8f;
        var y = 879f - row * 43f - (cascade % 5) * 6f;
        configuration.HudLayouts[bar.ElementId] = new HudElementLayout
        {
            X = x / 1920f,
            Y = y / 1080f,
            Width = 513f / 1920f,
            Height = 40f / 1080f,
        };
    }

    private static void RemoveElementState(Configuration configuration, string elementId)
    {
        configuration.HudLayouts?.Remove(elementId);
        if (configuration.HudModeProfiles is null)
            return;
        foreach (var profile in configuration.HudModeProfiles.Values)
        {
            if (profile is null)
                continue;
            profile.EnsureValid();
            profile.HudLayouts.Remove(elementId);
            profile.ElementVisibility.Remove(elementId);
            profile.ElementLocks.Remove(elementId);
        }
    }

    private static void RepairStoredNames(IEnumerable<ReframeVirtualHotbarSlot> slots)
    {
        foreach (var slot in slots)
        {
            if (slot is null || slot.IsEmpty)
                continue;

            var type = (HotbarSlotType)slot.CommandType;
            slot.DisplayName = HotbarDisplayNameService.ResolveStoredActionName(
                type,
                slot.CommandId,
                slot.DisplayName);
        }
    }

    private static void EnsureVirtualSlotList(List<ReframeVirtualHotbarSlot> slots)
    {
        while (slots.Count < ReframeHotbarIds.SlotCount)
            slots.Add(new ReframeVirtualHotbarSlot());
        if (slots.Count > ReframeHotbarIds.SlotCount)
            slots.RemoveRange(ReframeHotbarIds.SlotCount, slots.Count - ReframeHotbarIds.SlotCount);
        for (var index = 0; index < slots.Count; index++)
        {
            slots[index] ??= new ReframeVirtualHotbarSlot();
            slots[index].EnsureValid();
        }
    }

    public static bool IsPlayerAssignableCommand(HotbarSlotType commandType)
        => commandType is HotbarSlotType.Action or
            HotbarSlotType.CraftAction or
            HotbarSlotType.Emote or
            HotbarSlotType.Mount or
            HotbarSlotType.Companion or
            HotbarSlotType.Macro;

    private static uint ResolveCommandIcon(HotbarSlotType commandType, uint commandId)
    {
        if (!IsPlayerAssignableCommand(commandType) || commandId == 0)
            return 0;

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
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve virtual hotbar icon {Type} {Id}.", commandType, commandId);
            return 0;
        }
    }

}
