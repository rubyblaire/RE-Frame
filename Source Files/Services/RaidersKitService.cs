using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace REFrameXIV.Services;

public readonly record struct RaidersKitItem(
    uint ItemId,
    uint IconId,
    string Name,
    int Quantity,
    int ItemLevel,
    uint ActionItemId);
public readonly record struct RaidersKitSnapshot(RaidersKitItem? Food, RaidersKitItem? Potion, float FoodRemaining, float PotionCooldownRemaining);


public static unsafe class RaidersKitService
{

    private const int MealFilterGroup = 5;
    private const int MedicineFilterGroup = 6;


    private const uint WellFedStatusId = 48;

    private static readonly InventoryType[] PlayerBags =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    public static RaidersKitSnapshot Snapshot(Configuration configuration, IBattleChara? actor)
    {
        var foodRemaining = FindStatusRemaining(actor, WellFedStatusId);
        var candidates = ReadInventoryCandidates();


        var food = Select(candidates, configuration.RaidersKitFoodOverride, static candidate => candidate.IsFood);
        var potion = Select(candidates, configuration.RaidersKitPotionOverride, static candidate => candidate.IsPotion);
        var potionRemaining = ReadItemRecastRemaining(potion?.Item.ItemId ?? 0u);
        return new RaidersKitSnapshot(food?.Item, potion?.Item, foodRemaining, potionRemaining);
    }

    public static bool TryUse(RaidersKitItem? item)
    {
        if (item is not { } resolved || resolved.ItemId == 0 || resolved.ActionItemId == 0)
            return false;

        try
        {
            var manager = ActionManager.Instance();
            if (manager == null)
                return false;


            var acceptedImmediately = manager->UseAction(
                ActionType.Item,
                resolved.ActionItemId,
                0xE000_0000,
                0xFFFFu);

            if (!acceptedImmediately)
            {
                Plugin.Log.Debug(
                    "RE:Frame native consumable dispatch returned false immediately for item action {ActionItemId}; completion remains client-authoritative.",
                    resolved.ActionItemId);
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not use the selected raid consumable through ActionManager.");
            return false;
        }
    }

    private static float ReadItemRecastRemaining(uint itemId)
    {
        if (itemId == 0)
            return 0f;

        try
        {
            var manager = ActionManager.Instance();
            if (manager == null)
                return 0f;


            var remaining = 0f;
            foreach (var actionId in new[] { itemId, itemId + 1_000_000u })
            {
                if (!manager->IsRecastTimerActive(ActionType.Item, actionId))
                    continue;

                var total = manager->GetRecastTime(ActionType.Item, actionId);
                var elapsed = manager->GetRecastTimeElapsed(ActionType.Item, actionId);
                if (!float.IsFinite(total) || !float.IsFinite(elapsed) || total <= 0f)
                    continue;

                remaining = MathF.Max(remaining, MathF.Max(0f, total - elapsed));
            }

            return remaining;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not read the potion item recast.");
            return 0f;
        }
    }

    private static Candidate? Select(
        IEnumerable<Candidate> source,
        string manualName,
        Func<Candidate, bool> automaticPredicate)
    {
        var list = source.ToArray();
        var preferredName = manualName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(preferredName))
        {


            var manual = list
                .Where(x => x.Item.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Item.ItemLevel)
                .ThenByDescending(x => x.Item.ItemId)
                .FirstOrDefault();

            if (manual.Item.ItemId == 0)
            {
                manual = list
                    .Where(x => x.Item.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Item.ItemLevel)
                    .ThenByDescending(x => x.Item.ItemId)
                    .FirstOrDefault();
            }

            if (manual.Item.ItemId != 0)
                return manual;
        }

        return list
            .Where(automaticPredicate)
            .OrderByDescending(x => x.Item.ItemLevel)
            .ThenByDescending(x => x.Item.ItemId)
            .FirstOrDefault();
    }

    private static List<Candidate> ReadInventoryCandidates()
    {
        var result = new List<Candidate>();
        try
        {
            var manager = InventoryManager.Instance();
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (manager == null || sheet is null)
                return result;

            foreach (var bagType in PlayerBags)
            {
                var bag = manager->GetInventoryContainer(bagType);
                if (bag == null || !bag->IsLoaded || bag->Items == null)
                    continue;

                for (var i = 0; i < bag->Size; i++)
                {
                    var slot = bag->GetInventorySlot(i);
                    if (slot == null || slot->ItemId == 0 || slot->Quantity <= 0 || !sheet.TryGetRow(slot->ItemId, out var row))
                        continue;

                    var name = row.Name.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var filterGroup = ReadInt(row, "FilterGroup");
                    var isFood = filterGroup == MealFilterGroup;
                    var isPotion = filterGroup == MedicineFilterGroup;
                    var itemLevel = ReadInt(row, "LevelItem");


                    var actionItemId = slot->GetItemId();
                    if (actionItemId == 0)
                    {
                        actionItemId = slot->ItemId;
                        if ((slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0)
                            actionItemId += 1_000_000u;
                    }

                    result.Add(new Candidate(
                        new RaidersKitItem(
                            slot->ItemId,
                            row.Icon,
                            name,
                            slot->Quantity,
                            itemLevel,
                            actionItemId),
                        isFood,
                        isPotion));
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve raid consumable inventory items.");
        }

        return result;
    }

    private static float FindStatusRemaining(IBattleChara? actor, uint statusId)
    {
        if (actor is null)
            return 0f;

        try
        {
            foreach (var status in actor.StatusList)
            {
                if (status.StatusId == statusId)
                    return MathF.Max(0f, status.RemainingTime);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not read the food status duration.");
        }

        return 0f;
    }

    private static int ReadInt<T>(T row, string property)
    {
        try
        {
            var value = typeof(T).GetProperty(property)?.GetValue(row);
            if (value is null)
                return 0;
            if (value is IConvertible convertible)
                return convertible.ToInt32(null);

            var rowId = value.GetType().GetProperty("RowId")?.GetValue(value);
            return rowId is IConvertible rowIdValue ? rowIdValue.ToInt32(null) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private readonly record struct Candidate(RaidersKitItem Item, bool IsFood, bool IsPotion);
}
