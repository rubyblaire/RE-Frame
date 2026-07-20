using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace REFrameXIV.Models;


[Serializable]
public sealed class ReframeAdditionalHotbar
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Columns { get; set; } = 12;


    public int NativeHotbarId { get; set; } = -1;

    public List<ReframeVirtualHotbarSlot> Slots { get; set; } = new();

    public string ElementId => HudElementIds.AdditionalCombatHotbar(Id);
    public bool IsNativeBacked => NativeHotbarId is >= 0 and <= 9;
    public uint RuntimeHotbarId => IsNativeBacked
        ? (uint)NativeHotbarId
        : ReframeHotbarIds.AdditionalCombatBase + Id;

    public void EnsureValid()
    {
        Name ??= string.Empty;
        Columns = HotbarGridLayouts.NormalizeColumns(Columns);
        if (NativeHotbarId is < -1 or > 9)
            NativeHotbarId = -1;

        Slots ??= new List<ReframeVirtualHotbarSlot>();
        while (Slots.Count < ReframeHotbarIds.SlotCount)
            Slots.Add(new ReframeVirtualHotbarSlot());
        if (Slots.Count > ReframeHotbarIds.SlotCount)
            Slots = Slots.Take(ReframeHotbarIds.SlotCount).ToList();
        for (var index = 0; index < Slots.Count; index++)
        {
            Slots[index] ??= new ReframeVirtualHotbarSlot();
            Slots[index].EnsureValid();
        }
    }
}

[Serializable]
public sealed class ReframeVirtualHotbarSlot
{
    public int CommandType { get; set; }
    public uint CommandId { get; set; }
    public uint IconId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public bool IsEmpty => CommandId == 0 || CommandType == (int)RaptureHotbarModule.HotbarSlotType.Empty;

    public void Clear()
    {
        CommandType = (int)RaptureHotbarModule.HotbarSlotType.Empty;
        CommandId = 0;
        IconId = 0;
        DisplayName = string.Empty;
    }

    public void EnsureValid()
    {
        DisplayName ??= string.Empty;
        if (CommandId == 0)
            Clear();
    }

    public ReframeVirtualHotbarSlot Clone()
        => new()
        {
            CommandType = CommandType,
            CommandId = CommandId,
            IconId = IconId,
            DisplayName = DisplayName,
        };
}

public static class ReframeHotbarIds
{
    public const int SlotCount = 12;
    public const uint PetBar = 18u;
    public const uint SecondUtility = 1000u;
    public const uint AdditionalCombatBase = 2000u;

    public static bool IsVirtual(uint hotbarId)
        => hotbarId == SecondUtility || hotbarId >= AdditionalCombatBase;
}
