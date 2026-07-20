using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Services;


public sealed class HudPresetService : IDisposable
{
    private readonly Plugin plugin;
    private readonly IFramework framework;
    private DateTime nextPollUtc;
    private string lastJob = string.Empty;
    private bool disposed;
    private bool applying;

    public HudPresetService(Plugin plugin, IFramework framework)
    {
        this.plugin = plugin;
        this.framework = framework;
        EnsureCollections();
        lastJob = plugin.GetJobAbbreviation();
        nextPollUtc = DateTime.UtcNow.AddSeconds(1);
        framework.Update += OnFrameworkUpdate;
    }

    public IReadOnlyDictionary<string, HudPresetData> GeneralPresets => plugin.Configuration.GeneralHudPresets;

    public IReadOnlyDictionary<string, HudPresetData> GetJobPresets(string job)
    {
        EnsureCollections();
        job = NormalizeJob(job);
        return plugin.Configuration.JobHudPresets.TryGetValue(job, out var presets)
            ? presets
            : EmptyPresets;
    }

    public bool SaveGeneral(string name, out string message)
    {
        name = NormalizeName(name);
        var job = NormalizeJob(plugin.GetJobAbbreviation());
        plugin.Configuration.GeneralHudPresets[name] = HudPresetData.Capture(plugin.Configuration, name, job);
        plugin.Configuration.ActiveGeneralHudPreset = name;
        SaveAndRefresh();
        message = $"Saved general preset “{name}”.";
        return true;
    }

    public bool SaveForCurrentJob(string name, out string message)
    {
        name = NormalizeName(name);
        var job = NormalizeJob(plugin.GetJobAbbreviation());
        var presets = GetOrCreateJob(job);
        if (presets.TryGetValue(name, out var existingPreset))
            plugin.LayoutRecovery.CreateFromPreset("Before Overwriting Job Preset", existingPreset, job);
        presets[name] = HudPresetData.Capture(plugin.Configuration, name, job);
        plugin.Configuration.ActiveJobHudPresets[job] = name;
        SaveAndRefresh();
        message = $"Saved and activated “{name}” for {job}.";
        return true;
    }

    public bool TryGetActiveForCurrentJob(out string job, out string name)
    {
        EnsureCollections();
        job = NormalizeJob(plugin.GetJobAbbreviation());
        name = string.Empty;

        if (!plugin.Configuration.ActiveJobHudPresets.TryGetValue(job, out var activeName) ||
            string.IsNullOrWhiteSpace(activeName) ||
            !plugin.Configuration.JobHudPresets.TryGetValue(job, out var presets) ||
            !presets.ContainsKey(activeName))
            return false;

        name = activeName;
        return true;
    }

    public bool UpdateActiveForCurrentJob(out string message)
    {
        if (!TryGetActiveForCurrentJob(out var job, out var name))
        {
            message = $"No active job preset is available for {NormalizeJob(plugin.GetJobAbbreviation())}.";
            return false;
        }

        var presets = GetOrCreateJob(job);
        plugin.LayoutRecovery.CreateFromPreset("Before Overwriting Job Preset", presets[name], job);
        presets[name] = HudPresetData.Capture(plugin.Configuration, name, job);
        plugin.Configuration.ActiveJobHudPresets[job] = name;
        SaveAndRefresh();
        message = $"Updated active {job} preset “{name}” from the current HUD.";
        return true;
    }

    public bool ApplyGeneral(string name, bool activate, out string message)
    {
        EnsureCollections();
        if (!plugin.Configuration.GeneralHudPresets.TryGetValue(name, out var preset))
        {
            message = $"General preset “{name}” was not found.";
            return false;
        }

        plugin.LayoutRecovery.Create("Before Applying Preset");
        ApplyPreset(preset, NormalizeJob(plugin.GetJobAbbreviation()));
        if (activate)
            plugin.Configuration.ActiveGeneralHudPreset = name;
        SaveAndRefresh();
        message = $"Applied general preset “{name}”.";
        return true;
    }

    public bool ApplyJob(string sourceJob, string name, string? targetJob, bool activate, out string message)
    {
        sourceJob = NormalizeJob(sourceJob);
        targetJob = NormalizeJob(targetJob ?? plugin.GetJobAbbreviation());
        if (!plugin.Configuration.JobHudPresets.TryGetValue(sourceJob, out var presets) || !presets.TryGetValue(name, out var preset))
        {
            message = $"Preset “{name}” was not found for {sourceJob}.";
            return false;
        }

        plugin.LayoutRecovery.Create("Before Applying Preset");
        ApplyPreset(preset, targetJob);
        if (activate)
            plugin.Configuration.ActiveJobHudPresets[targetJob] = name;
        SaveAndRefresh();
        message = sourceJob == targetJob
            ? $"Applied {targetJob} preset “{name}”."
            : $"Applied {sourceJob} preset “{name}” to {targetJob}.";
        return true;
    }

    public bool CopyJobPreset(string sourceJob, string name, string targetJob, string? newName, out string message)
    {
        sourceJob = NormalizeJob(sourceJob);
        targetJob = NormalizeJob(targetJob);
        if (!plugin.Configuration.JobHudPresets.TryGetValue(sourceJob, out var source) || !source.TryGetValue(name, out var preset))
        {
            message = $"Preset “{name}” was not found for {sourceJob}.";
            return false;
        }

        var destinationName = NormalizeName(string.IsNullOrWhiteSpace(newName) ? name : newName!);
        var clone = preset.Clone(destinationName);
        clone.SourceJob = targetJob;
        GetOrCreateJob(targetJob)[destinationName] = clone;
        plugin.Configuration.ActiveJobHudPresets[targetJob] = destinationName;
        if (string.Equals(targetJob, NormalizeJob(plugin.GetJobAbbreviation()), StringComparison.OrdinalIgnoreCase))
        {
            plugin.LayoutRecovery.Create("Before Applying Preset");
            ApplyPreset(clone, targetJob);
        }
        SaveAndRefresh();
        message = $"Copied and activated “{name}” from {sourceJob} to {targetJob} as “{destinationName}”.";
        return true;
    }

    public bool CopyGeneralToJob(string generalName, string targetJob, string? newName, out string message)
    {
        targetJob = NormalizeJob(targetJob);
        if (!plugin.Configuration.GeneralHudPresets.TryGetValue(generalName, out var preset))
        {
            message = $"General preset “{generalName}” was not found.";
            return false;
        }

        var destinationName = NormalizeName(string.IsNullOrWhiteSpace(newName) ? generalName : newName!);
        var clone = preset.Clone(destinationName);
        clone.SourceJob = targetJob;
        GetOrCreateJob(targetJob)[destinationName] = clone;
        plugin.Configuration.ActiveJobHudPresets[targetJob] = destinationName;
        if (string.Equals(targetJob, NormalizeJob(plugin.GetJobAbbreviation()), StringComparison.OrdinalIgnoreCase))
        {
            plugin.LayoutRecovery.Create("Before Applying Preset");
            ApplyPreset(clone, targetJob);
        }
        SaveAndRefresh();
        message = $"Copied, applied, and activated general preset “{generalName}” for {targetJob}.";
        return true;
    }

    public bool DeleteGeneral(string name, out string message)
    {
        if (!plugin.Configuration.GeneralHudPresets.Remove(name))
        {
            message = $"General preset “{name}” was not found.";
            return false;
        }
        if (string.Equals(plugin.Configuration.ActiveGeneralHudPreset, name, StringComparison.OrdinalIgnoreCase))
            plugin.Configuration.ActiveGeneralHudPreset = string.Empty;
        SaveAndRefresh();
        message = $"Deleted general preset “{name}”.";
        return true;
    }

    public bool DeleteJob(string job, string name, out string message)
    {
        job = NormalizeJob(job);
        if (!plugin.Configuration.JobHudPresets.TryGetValue(job, out var presets) || !presets.Remove(name))
        {
            message = $"Preset “{name}” was not found for {job}.";
            return false;
        }
        if (plugin.Configuration.ActiveJobHudPresets.TryGetValue(job, out var active) && string.Equals(active, name, StringComparison.OrdinalIgnoreCase))
            plugin.Configuration.ActiveJobHudPresets.Remove(job);
        SaveAndRefresh();
        message = $"Deleted {job} preset “{name}”.";
        return true;
    }

    public bool TryExportCurrent(string name, out string code, out string message)
    {
        var job = NormalizeJob(plugin.GetJobAbbreviation());
        var preset = HudPresetData.Capture(plugin.Configuration, NormalizeName(name), job);
        code = HudPresetShareCodec.Encode(preset);
        message = $"Created portable RF3 backup for the current {job} HUD as “{preset.Name}” ({code.Length} characters).";
        return true;
    }

    public bool TryConvertToRf3(string code, out string convertedCode, out string message)
    {
        convertedCode = string.Empty;
        if (!HudPresetShareCodec.TryDecode(code, out var preset, out message))
            return false;

        convertedCode = HudPresetShareCodec.Encode(preset);
        var sourceFormat = code.TrimStart().StartsWith("RF3:", StringComparison.OrdinalIgnoreCase)
            ? "RF3"
            : code.TrimStart().StartsWith("RF2:", StringComparison.OrdinalIgnoreCase)
                ? "RF2"
                : "legacy RFHUD1";
        message = $"Converted {sourceFormat} to a portable RF3 backup ({convertedCode.Length} characters). The preset was not applied or saved.";
        return true;
    }

    public bool TryExportGeneral(string name, out string code, out string message)
    {
        if (!plugin.Configuration.GeneralHudPresets.TryGetValue(name, out var preset))
        {
            code = string.Empty;
            message = $"General preset “{name}” was not found.";
            return false;
        }
        code = HudPresetShareCodec.Encode(preset);
        message = $"Created portable RF3 backup for “{name}” ({code.Length} characters).";
        return true;
    }

    public bool TryExportJob(string job, string name, out string code, out string message)
    {
        job = NormalizeJob(job);
        if (!plugin.Configuration.JobHudPresets.TryGetValue(job, out var presets) || !presets.TryGetValue(name, out var preset))
        {
            code = string.Empty;
            message = $"Preset “{name}” was not found for {job}.";
            return false;
        }
        code = HudPresetShareCodec.Encode(preset);
        message = $"Created portable RF3 backup for {job} “{name}” ({code.Length} characters).";
        return true;
    }

    public bool ImportGeneral(string code, string? nameOverride, out string message)
    {
        if (!HudPresetShareCodec.TryDecode(code, out var preset, out message))
            return false;
        plugin.LayoutRecovery.Create("Before Share Code Import");
        var name = NormalizeName(string.IsNullOrWhiteSpace(nameOverride) ? preset.Name : nameOverride!);
        preset = preset.Clone(name);
        plugin.Configuration.GeneralHudPresets[name] = preset;
        SaveAndRefresh();
        message = $"Imported “{name}” into General Presets.";
        return true;
    }

    public bool ImportForCurrentJob(string code, string? nameOverride, out string message)
    {
        if (!HudPresetShareCodec.TryDecode(code, out var preset, out message))
            return false;
        plugin.LayoutRecovery.Create("Before Share Code Import");
        var job = NormalizeJob(plugin.GetJobAbbreviation());
        var name = NormalizeName(string.IsNullOrWhiteSpace(nameOverride) ? preset.Name : nameOverride!);
        preset = preset.Clone(name);
        preset.SourceJob = job;
        GetOrCreateJob(job)[name] = preset;
        plugin.Configuration.ActiveJobHudPresets[job] = name;
        ApplyPreset(preset, job);
        SaveAndRefresh();
        message = $"Imported, applied, and activated “{name}” for {job}.";
        return true;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed || applying || !plugin.Configuration.AutoApplyHudPresets || DateTime.UtcNow < nextPollUtc)
            return;
        nextPollUtc = DateTime.UtcNow.AddMilliseconds(350);

        var job = NormalizeJob(plugin.GetJobAbbreviation());
        if (string.Equals(job, lastJob, StringComparison.OrdinalIgnoreCase))
            return;
        lastJob = job;

        if (plugin.Configuration.ActiveJobHudPresets.TryGetValue(job, out var activeJobName) &&
            plugin.Configuration.JobHudPresets.TryGetValue(job, out var jobPresets) &&
            jobPresets.TryGetValue(activeJobName, out var jobPreset))
        {
            ApplyPreset(jobPreset, job);
            SaveAndRefresh();
            return;
        }

        if (!string.IsNullOrWhiteSpace(plugin.Configuration.ActiveGeneralHudPreset) &&
            plugin.Configuration.GeneralHudPresets.TryGetValue(plugin.Configuration.ActiveGeneralHudPreset, out var general))
        {
            ApplyPreset(general, job);
            SaveAndRefresh();
        }
    }

    private void ApplyPreset(HudPresetData preset, string targetJob)
    {
        applying = true;
        try
        {
            plugin.NativeHudVisibility.PrepareForNativePlacementReplacement();
            preset.ApplyTo(plugin.Configuration, targetJob);
            HudLayout.EnsureDefaults(plugin.Configuration);
            plugin.LayoutHistory.Clear();
        }
        finally
        {
            applying = false;
        }
    }

    private void SaveAndRefresh()
    {
        plugin.SaveConfiguration();
        plugin.NativeHudVisibility.RefreshNow();
        plugin.NativeWindows.ApplyConfigurationChange();
    }

    private Dictionary<string, HudPresetData> GetOrCreateJob(string job)
    {
        EnsureCollections();
        if (!plugin.Configuration.JobHudPresets.TryGetValue(job, out var presets))
        {
            presets = new Dictionary<string, HudPresetData>(StringComparer.OrdinalIgnoreCase);
            plugin.Configuration.JobHudPresets[job] = presets;
        }
        return presets;
    }

    private void EnsureCollections()
    {
        plugin.Configuration.GeneralHudPresets ??= new Dictionary<string, HudPresetData>(StringComparer.OrdinalIgnoreCase);
        plugin.Configuration.JobHudPresets ??= new Dictionary<string, Dictionary<string, HudPresetData>>(StringComparer.OrdinalIgnoreCase);
        plugin.Configuration.ActiveJobHudPresets ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeJob(string job)
        => string.IsNullOrWhiteSpace(job) ? "XIV" : job.Trim().ToUpperInvariant();

    private static string NormalizeName(string name)
        => string.IsNullOrWhiteSpace(name) ? "HUD Preset" : name.Trim();

    private static readonly IReadOnlyDictionary<string, HudPresetData> EmptyPresets =
        new Dictionary<string, HudPresetData>(StringComparer.OrdinalIgnoreCase);
}
