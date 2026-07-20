using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;

namespace REFrameXIV.Services;


public static class WorkStatusService
{
    public static IReadOnlyList<WorkStatusEntry> Snapshot(IBattleChara actor, int maximum = 8)
    {
        maximum = Math.Clamp(maximum, 1, 16);
        var results = new List<WorkStatusEntry>();

        try
        {
            foreach (var status in actor.StatusList)
            {
                if (status.StatusId == 0 ||
                    !StatusDisplayService.TryResolve(status.StatusId, status.Param, out var display) ||
                    display.IsDebuff)
                    continue;

                var priority = ResolvePriority(display.Name, display.Description, display.IsFreeCompanyAction);

                results.Add(new WorkStatusEntry(
                    status.StatusId,
                    display.IconId,
                    display.Name,
                    display.Description,
                    status.RemainingTime,
                    priority));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve Work-frame status effects.");
        }

        return results
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.RemainingTime <= 0f ? float.MaxValue : entry.RemainingTime)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maximum)
            .ToArray();
    }

    private static int ResolvePriority(string name, string description, bool isFreeCompanyAction)
    {
        var text = $"{name} {description}".ToLowerInvariant();
        if (ContainsAny(text, "well fed", "food", "meal"))
            return 0;
        if (ContainsAny(text,
                "craftsmanship", "crafting", "control", "cp ", "cp.",
                "medicine", "medicated", "draught", "tea"))
            return 1;
        if (isFreeCompanyAction)
            return 2;
        if (ContainsAny(text, "spiritbond", "spirit bond"))
            return 2;
        return 3;
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));
}

public readonly record struct WorkStatusEntry(
    uint StatusId,
    uint IconId,
    string Name,
    string Description,
    float RemainingTime,
    int Priority);
