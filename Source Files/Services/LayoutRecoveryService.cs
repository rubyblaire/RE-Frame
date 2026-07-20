using System;
using System.Collections.Generic;
using System.Linq;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Services;


public sealed class LayoutRecoveryService
{
    public const int MaximumSnapshots = 10;
    private readonly Plugin plugin;

    public LayoutRecoveryService(Plugin plugin)
    {
        this.plugin = plugin;
        EnsureValid();
    }

    public IReadOnlyList<LayoutRecoverySnapshot> Snapshots
        => plugin.Configuration.RecentLayoutHistory;

    public LayoutRecoverySnapshot Create(string reason, bool save = true)
        => Add(reason, HudLayoutState.Capture(plugin.Configuration, plugin.GetJobAbbreviation()), save);


    public LayoutRecoverySnapshot CreateFromPreset(string reason, HudPresetData preset, string targetJob, bool save = true)
    {
        var state = HudLayoutState.Capture(plugin.Configuration, targetJob);
        state.Preset = preset.Clone("Layout State");
        state.Preset.CreatedUtc = DateTime.UnixEpoch;

        targetJob = string.IsNullOrWhiteSpace(targetJob) ? "XIV" : targetJob.Trim().ToUpperInvariant();
        if (preset.JobGaugePlacement is null)
            state.NativeJobGaugePlacements.Remove(targetJob);
        else
            state.NativeJobGaugePlacements[targetJob] = ClonePlacement(preset.JobGaugePlacement);

        state.NativeStatusEffectsPlacement = ClonePlacement(preset.NativeStatusEffectsPlacement ?? new NativeJobGaugePlacement());
        CopyQuestPlacement(state, HudElementIds.NativeScenarioGuide, preset.NativeScenarioGuidePlacement);
        CopyQuestPlacement(state, HudElementIds.NativeQuestList, preset.NativeQuestListPlacement);
        CopyQuestPlacement(state, HudElementIds.NativeDutyInfo, preset.NativeDutyInfoPlacement);
        return Add(reason, state, save);
    }

    private LayoutRecoverySnapshot Add(string reason, HudLayoutState state, bool save)
    {
        EnsureValid();
        var snapshot = new LayoutRecoverySnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Layout restore point" : reason.Trim(),
            State = state,
        };

        plugin.Configuration.RecentLayoutHistory.Insert(0, snapshot);
        Trim();
        if (save)
            plugin.SaveConfiguration();
        return snapshot;
    }

    public bool Restore(string id, out string message)
    {
        EnsureValid();
        var snapshot = plugin.Configuration.RecentLayoutHistory.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (snapshot?.State is null)
        {
            message = "That layout restore point is no longer available.";
            return false;
        }

        var restoreState = snapshot.State.Clone();
        var restoredReason = snapshot.Reason;
        Create("Before Restoring Layout History", save: false);
        plugin.NativeHudVisibility.PrepareForNativePlacementReplacement();
        restoreState.ApplyTo(plugin.Configuration, plugin.GetJobAbbreviation());
        HudLayout.EnsureDefaults(plugin.Configuration);
        plugin.LayoutHistory.Clear();
        plugin.SaveConfiguration();
        plugin.NativeHudVisibility.RefreshNow();
        plugin.NativeWindows.ApplyConfigurationChange();
        message = $"Restored layout snapshot: {restoredReason}.";
        return true;
    }

    public bool Delete(string id)
    {
        EnsureValid();
        var removed = plugin.Configuration.RecentLayoutHistory.RemoveAll(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
            plugin.SaveConfiguration();
        return removed;
    }

    public void EnsureValid()
    {
        plugin.Configuration.RecentLayoutHistory ??= new List<LayoutRecoverySnapshot>();
        plugin.Configuration.RecentLayoutHistory.RemoveAll(item =>
            item is null || item.State is null || item.State.Preset is null || string.IsNullOrWhiteSpace(item.Id));
        plugin.Configuration.RecentLayoutHistory.Sort((left, right) => right.CreatedUtc.CompareTo(left.CreatedUtc));
        Trim();
    }

    private static void CopyQuestPlacement(HudLayoutState state, string id, NativeJobGaugePlacement? placement)
    {
        if (placement is null)
            state.NativeQuestElementPlacements.Remove(id);
        else
            state.NativeQuestElementPlacements[id] = ClonePlacement(placement);
    }

    private static NativeJobGaugePlacement ClonePlacement(NativeJobGaugePlacement source)
        => new()
        {
            X = source.X,
            Y = source.Y,
            OriginalX = source.OriginalX,
            OriginalY = source.OriginalY,
            Scale = source.Scale,
            OriginalScale = source.OriginalScale,
            HasOriginal = source.HasOriginal,
            Components = CloneComponents(source.Components),
        };

    private static Dictionary<string, NativeJobGaugePlacement> CloneComponents(Dictionary<string, NativeJobGaugePlacement>? source)
    {
        var clone = new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return clone;

        foreach (var (key, placement) in source)
        {
            if (placement is not null)
                clone[key] = ClonePlacement(placement);
        }

        return clone;
    }

    private void Trim()
    {
        while (plugin.Configuration.RecentLayoutHistory.Count > MaximumSnapshots)
            plugin.Configuration.RecentLayoutHistory.RemoveAt(plugin.Configuration.RecentLayoutHistory.Count - 1);
    }
}
