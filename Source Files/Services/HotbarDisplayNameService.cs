using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaCraftAction = Lumina.Excel.Sheets.CraftAction;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;

namespace REFrameXIV.Services;


public static unsafe class HotbarDisplayNameService
{
    private static readonly Regex GeneratedIdLabel = new(
        @"^(?:unknown\s+)?(?:action|craft\s*action|macro|item|event\s*item|emote|marker|general\s*action|buddy\s*action|main\s*command|companion|gear\s*set|pet\s*action|mount|field\s*marker|recipe|extra\s*command|pvp\s*quick\s*chat|pvp\s*combo|performance\s*instrument|ornament|phantom\s*action|quick\s*panel)?\s*(?:id\s*)?#?\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ResolveNativeSlot(RaptureHotbarModule.HotbarSlot* slot)
    {
        if (slot == null || slot->CommandType == HotbarSlotType.Empty || slot->CommandId == 0)
            return "Empty slot";

        var commandType = slot->CommandType;
        var commandId = slot->CommandId;
        var apparentType = slot->ApparentSlotType;
        var apparentId = slot->ApparentActionId;
        var originalType = slot->OriginalApparentSlotType;
        var originalId = slot->OriginalApparentActionId;

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module != null && module->ModuleReady)
            {
                ushort appearanceState = 0;
                RaptureHotbarModule.GetSlotAppearance(
                    &apparentType,
                    &apparentId,
                    &appearanceState,
                    module,
                    slot);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not refresh hotbar slot appearance while resolving its name.");
        }

        if (apparentType == HotbarSlotType.Empty || apparentId == 0)
        {
            apparentType = commandType;
            apparentId = commandId;
        }

        var apparentNativeName = TryGetNativeDisplayName(slot, apparentType, apparentId);
        var storedNativeName = apparentType == commandType && apparentId == commandId
            ? apparentNativeName
            : TryGetNativeDisplayName(slot, commandType, commandId);
        var popupName = CleanNativeText(slot->PopUpHelp.ToString());


        if (commandType == HotbarSlotType.Macro)
        {
            if (TryResolveMacroName(commandId, storedNativeName, popupName, out var macroName))
                return macroName;

            return FirstReadable(
                       storedNativeName,
                       popupName,
                       apparentType == HotbarSlotType.Macro ? apparentNativeName : string.Empty)
                   ?? "Unnamed macro";
        }

        if (commandType is HotbarSlotType.Action or HotbarSlotType.CraftAction)
        {
            return FirstReadable(
                       apparentNativeName,
                       ResolveActionNameOrEmpty(apparentType, apparentId),
                       storedNativeName,
                       ResolveActionNameOrEmpty(originalType, originalId),
                       ResolveActionNameOrEmpty(commandType, commandId),
                       popupName)
                   ?? FriendlyFallback(commandType);
        }


        return FirstReadable(apparentNativeName, storedNativeName, popupName)
               ?? FriendlyFallback(commandType);
    }

    public static string ResolveActionName(HotbarSlotType commandType, uint actionId)
    {
        if (actionId == 0)
            return FriendlyFallback(commandType);

        var resolved = ResolveActionNameOrEmpty(commandType, actionId);
        return IsReadable(resolved) ? resolved : FriendlyFallback(commandType);
    }

    public static string ResolveStoredActionName(HotbarSlotType commandType, uint actionId, string? storedName)
    {
        var cleaned = CleanNativeText(storedName);
        if (IsReadable(cleaned))
            return cleaned;
        return ResolveActionName(commandType, actionId);
    }

    private static string ResolveActionNameOrEmpty(HotbarSlotType commandType, uint actionId)
    {
        if (actionId == 0)
            return string.Empty;

        try
        {
            if (commandType == HotbarSlotType.CraftAction)
            {
                var craftSheet = Plugin.DataManager.GetExcelSheet<LuminaCraftAction>();
                if (craftSheet.TryGetRow(actionId, out var craftAction))
                    return CleanNativeText(craftAction.Name.ToString());
            }
            else if (commandType == HotbarSlotType.Action)
            {
                var actionSheet = Plugin.DataManager.GetExcelSheet<LuminaAction>();
                if (actionSheet.TryGetRow(actionId, out var action))
                    return CleanNativeText(action.Name.ToString());
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve localized {Type} name for row {Id}.", commandType, actionId);
        }

        return string.Empty;
    }

    private static string TryGetNativeDisplayName(
        RaptureHotbarModule.HotbarSlot* slot,
        HotbarSlotType type,
        uint id)
    {
        if (slot == null || type == HotbarSlotType.Empty || id == 0)
            return string.Empty;

        try
        {
            return CleanNativeText(slot->GetDisplayNameForSlot(type, id).ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve native display text for {Type} {Id}.", type, id);
            return string.Empty;
        }
    }

    private static bool TryResolveMacroName(
        uint commandId,
        string storedNativeName,
        string popupName,
        out string name)
    {
        name = string.Empty;
        try
        {
            var module = RaptureMacroModule.Instance();
            if (module == null)
                return false;

            var candidates = BuildMacroCandidates(commandId);
            if (candidates.Count == 0)
                return false;

            var expectedNames = new[] { storedNativeName, popupName }
                .Where(IsReadable)
                .ToArray();
            var configuredNames = new List<string>(candidates.Count);

            foreach (var candidate in candidates)
            {
                var macro = module->GetMacro(candidate.Set, candidate.Index);
                if (macro == null)
                    continue;

                var candidateName = CleanNativeText(macro->Name.ToString());
                if (string.IsNullOrWhiteSpace(candidateName))
                    continue;

                if (expectedNames.Any(expected => string.Equals(expected, candidateName, StringComparison.OrdinalIgnoreCase)))
                {
                    name = candidateName;
                    return true;
                }

                if (!configuredNames.Any(existing => string.Equals(existing, candidateName, StringComparison.Ordinal)))
                    configuredNames.Add(candidateName);
            }


            if (configuredNames.Count == 1)
            {
                name = configuredNames[0];
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve macro name for native macro command {Id}.", commandId);
        }

        return false;
    }

    private static List<MacroReference> BuildMacroCandidates(uint commandId)
    {
        var result = new List<MacroReference>(4);

        void Add(uint set, uint index)
        {
            if (set > 1 || index >= 100 || result.Contains(new MacroReference(set, index)))
                return;
            result.Add(new MacroReference(set, index));
        }


        var packedSet = commandId >> 8;
        var packedNumber = commandId & 0xFFu;
        if (packedSet <= 1 && packedNumber is >= 1 and <= 100)
            Add(packedSet, packedNumber - 1);
        if (packedSet == 1 && packedNumber < 100)
            Add(1, packedNumber);

        if (commandId is >= 1 and <= 100)
            Add(0, commandId - 1);
        else if (commandId is >= 101 and <= 200)
            Add(1, commandId - 101);

        return result;
    }

    private static string? FirstReadable(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var cleaned = CleanNativeText(candidate);
            if (IsReadable(cleaned))
                return cleaned;
        }
        return null;
    }

    private static bool IsReadable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var cleaned = value.Trim();
        if (uint.TryParse(cleaned, out _))
            return false;
        return !GeneratedIdLabel.IsMatch(cleaned);
    }

    private static string CleanNativeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var firstLine = value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;


        var bracket = firstLine.LastIndexOf(" [", StringComparison.Ordinal);
        if (bracket > 0 && firstLine.EndsWith(']'))
            firstLine = firstLine[..bracket].TrimEnd();

        return firstLine;
    }

    private static string FriendlyFallback(HotbarSlotType type)
        => type switch
        {
            HotbarSlotType.Action => "Unknown action",
            HotbarSlotType.CraftAction => "Unknown crafting action",
            HotbarSlotType.Macro => "Unnamed macro",
            HotbarSlotType.Item or HotbarSlotType.InventoryItem => "Unknown item",
            HotbarSlotType.EventItem or HotbarSlotType.KeyItem => "Unknown key item",
            HotbarSlotType.Emote => "Unknown emote",
            HotbarSlotType.GeneralAction => "Unknown general action",
            HotbarSlotType.BuddyAction => "Unknown companion action",
            HotbarSlotType.MainCommand => "Unknown main command",
            HotbarSlotType.Companion => "Unknown minion",
            HotbarSlotType.GearSet => "Unnamed gearset",
            HotbarSlotType.PetAction => "Unknown pet action",
            HotbarSlotType.Mount => "Unknown mount",
            HotbarSlotType.FieldMarker or HotbarSlotType.Marker => "Unknown marker",
            HotbarSlotType.Recipe => "Unknown recipe",
            HotbarSlotType.PerformanceInstrument => "Unknown instrument",
            HotbarSlotType.Ornament => "Unknown fashion accessory",
            _ => "Unknown command",
        };

    private readonly record struct MacroReference(uint Set, uint Index);
}
