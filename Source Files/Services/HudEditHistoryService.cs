using System;
using System.Collections.Generic;
using System.Text.Json;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Services;


public sealed class HudEditHistoryService
{
    private const int MaximumEntries = 50;
    private static readonly JsonSerializerOptions FingerprintOptions = new() { WriteIndented = false };

    private readonly Plugin plugin;
    private readonly List<HistoryEntry> undo = new();
    private readonly List<HistoryEntry> redo = new();
    private HudLayoutState? transactionBefore;
    private string transactionLabel = string.Empty;

    public HudEditHistoryService(Plugin plugin) => this.plugin = plugin;

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string UndoLabel => CanUndo ? undo[^1].Label : string.Empty;
    public string RedoLabel => CanRedo ? redo[^1].Label : string.Empty;
    public bool HasActiveTransaction => transactionBefore is not null;

    public void BeginTransaction(string label)
    {
        if (transactionBefore is not null)
            return;

        transactionLabel = string.IsNullOrWhiteSpace(label) ? "Edit HUD" : label.Trim();
        transactionBefore = Capture();
    }

    public bool CommitTransaction()
    {
        if (transactionBefore is null)
            return false;

        var before = transactionBefore;
        var after = Capture();
        var label = transactionLabel;
        transactionBefore = null;
        transactionLabel = string.Empty;

        if (Equivalent(before, after))
            return false;

        PushUndo(new HistoryEntry(label, before, after));
        redo.Clear();
        return true;
    }

    public void CancelTransaction()
    {
        transactionBefore = null;
        transactionLabel = string.Empty;
    }

    public bool Record(string label, Action action)
    {
        if (action is null)
            return false;

        var before = Capture();
        action();
        var after = Capture();
        if (Equivalent(before, after))
            return false;

        PushUndo(new HistoryEntry(label, before, after));
        redo.Clear();
        return true;
    }

    public bool Undo(out string message)
    {
        if (!CanUndo)
        {
            message = "Nothing to undo.";
            return false;
        }

        CancelTransaction();
        var entry = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        Apply(entry.Before);
        redo.Add(entry);
        message = $"Undid {entry.Label}.";
        return true;
    }

    public bool Redo(out string message)
    {
        if (!CanRedo)
        {
            message = "Nothing to redo.";
            return false;
        }

        CancelTransaction();
        var entry = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        Apply(entry.After);
        PushUndo(entry);
        message = $"Redid {entry.Label}.";
        return true;
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
        CancelTransaction();
    }

    private HudLayoutState Capture()
        => HudLayoutState.Capture(plugin.Configuration, plugin.GetJobAbbreviation());

    private void Apply(HudLayoutState state)
    {
        plugin.NativeHudVisibility.PrepareForNativePlacementReplacement();
        state.ApplyTo(plugin.Configuration, plugin.GetJobAbbreviation());
        HudLayout.EnsureDefaults(plugin.Configuration);
        plugin.SaveConfiguration();
        plugin.NativeHudVisibility.RefreshNow();
        plugin.NativeWindows.ApplyConfigurationChange();
    }

    private void PushUndo(HistoryEntry entry)
    {
        undo.Add(entry);
        if (undo.Count > MaximumEntries)
            undo.RemoveAt(0);
    }

    private static bool Equivalent(HudLayoutState left, HudLayoutState right)
        => string.Equals(
            JsonSerializer.Serialize(left, FingerprintOptions),
            JsonSerializer.Serialize(right, FingerprintOptions),
            StringComparison.Ordinal);

    private sealed record HistoryEntry(string Label, HudLayoutState Before, HudLayoutState After);
}
