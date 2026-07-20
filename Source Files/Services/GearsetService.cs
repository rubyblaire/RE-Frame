using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using REFrameXIV.Models;

namespace REFrameXIV.Services;


public sealed unsafe class GearsetService
{
    private const long SuccessfulSwitchCooldownMilliseconds = 2500;
    private const long FailedSwitchCooldownMilliseconds = 750;
    private const long PendingSwitchTimeoutMilliseconds = 8000;

    private readonly IDataManager dataManager;
    private readonly IPlayerState playerState;
    private IReadOnlyList<JobGearsetOverview> cachedOverview = Array.Empty<JobGearsetOverview>();
    private DateTime nextOverviewRefreshUtc = DateTime.MinValue;
    private long nextGearsetRequestAllowedAtMs;
    private long pendingSwitchExpiresAtMs;
    private int pendingGearsetId = -1;
    private byte pendingClassJobId;

    public GearsetService(IDataManager dataManager, IPlayerState playerState)
    {
        this.dataManager = dataManager;
        this.playerState = playerState;
    }


    public IReadOnlyList<SavedJobGearset> GetSavedJobs()
    {
        var results = new List<SavedJobGearset>();
        var seenClassJobs = new HashSet<byte>();
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return results;

        var sheet = dataManager.GetExcelSheet<ClassJob>();
        for (var gearsetId = 0; gearsetId < 100; gearsetId++)
        {
            var entry = module->GetGearset(gearsetId);
            if (entry == null ||
                (entry->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0 ||
                entry->ClassJob == 0 ||
                !seenClassJobs.Add(entry->ClassJob))
                continue;

            if (!sheet.TryGetRow(entry->ClassJob, out var classJob))
                continue;

            var abbreviation = classJob.Abbreviation.ToString();
            var name = classJob.Name.ToString();
            if (string.IsNullOrWhiteSpace(abbreviation))
                abbreviation = $"JOB {entry->ClassJob}";
            if (string.IsNullOrWhiteSpace(name))
                name = abbreviation;
            else
                name = FormatJobName(name);

            results.Add(new SavedJobGearset(gearsetId, entry->ClassJob, name, abbreviation));
        }

        results.Sort((a, b) => string.Compare(a.JobName, b.JobName, StringComparison.OrdinalIgnoreCase));
        return results;
    }


    public IReadOnlyList<JobGearsetOverview> GetGearsetOverview()
    {
        if (DateTime.UtcNow < nextOverviewRefreshUtc)
            return cachedOverview;

        var results = new List<JobGearsetOverview>();
        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            cachedOverview = results;
            nextOverviewRefreshUtc = DateTime.UtcNow.AddMilliseconds(250);
            return cachedOverview;
        }

        var classJobSheet = dataManager.GetExcelSheet<ClassJob>();
        var currentGearsetId = module->CurrentGearsetIndex;

        for (var gearsetId = 0; gearsetId < 100; gearsetId++)
        {
            var entry = module->GetGearset(gearsetId);
            if (entry == null ||
                (entry->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0 ||
                entry->ClassJob == 0)
                continue;

            if (!classJobSheet.TryGetRow(entry->ClassJob, out var classJob))
                continue;

            var abbreviation = classJob.Abbreviation.ToString().Trim();
            var name = classJob.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(abbreviation))
                abbreviation = $"JOB {entry->ClassJob}";
            if (string.IsNullOrWhiteSpace(name))
                name = abbreviation;
            else
                name = FormatJobName(name);

            var level = playerState.IsLoaded ? playerState.GetClassJobLevel(classJob) : (short)0;
            var currentExperience = playerState.IsLoaded ? Math.Max(0, playerState.GetClassJobExperience(classJob)) : 0;
            var experienceToNext = ResolveExperienceToNext(level, out var experienceAvailable);
            if (level <= 0)
            {
                currentExperience = 0;
                experienceToNext = 0;
                experienceAvailable = false;
            }

            results.Add(new JobGearsetOverview(
                gearsetId,
                entry->ClassJob,
                name,
                abbreviation,
                Math.Max((short)0, entry->ItemLevel),
                Math.Max((short)0, level),
                currentExperience,
                experienceToNext,
                experienceAvailable,
                ResolveRoleGroup(entry->ClassJob, classJob),
                gearsetId == currentGearsetId));
        }

        if (!results.Any(gearset => gearset.IsCurrent) &&
            playerState.IsLoaded &&
            playerState.ClassJob.IsValid &&
            playerState.ClassJob.RowId is > 0 and <= byte.MaxValue)
        {
            var currentClassJob = playerState.ClassJob.Value;
            var currentClassJobId = (byte)playerState.ClassJob.RowId;
            var abbreviation = currentClassJob.Abbreviation.ToString().Trim();
            var name = currentClassJob.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(abbreviation))
                abbreviation = $"JOB {currentClassJobId}";
            if (string.IsNullOrWhiteSpace(name))
                name = abbreviation;
            else
                name = FormatJobName(name);

            var level = Math.Max((short)0, playerState.GetClassJobLevel(currentClassJob));
            var currentExperience = Math.Max(0, playerState.GetClassJobExperience(currentClassJob));
            var experienceToNext = ResolveExperienceToNext(level, out var experienceAvailable);
            results.Add(new JobGearsetOverview(
                -1,
                currentClassJobId,
                name,
                abbreviation,
                0,
                level,
                currentExperience,
                experienceToNext,
                experienceAvailable,
                ResolveRoleGroup(currentClassJobId, currentClassJob),
                true));
        }

        results.Sort(static (a, b) =>
        {
            if (a.IsCurrent != b.IsCurrent)
                return a.IsCurrent ? -1 : 1;

            var role = a.RoleGroup.CompareTo(b.RoleGroup);
            if (role != 0)
                return role;

            var job = string.Compare(a.JobName, b.JobName, StringComparison.OrdinalIgnoreCase);
            return job != 0 ? job : a.GearsetId.CompareTo(b.GearsetId);
        });

        cachedOverview = results;
        nextOverviewRefreshUtc = DateTime.UtcNow.AddMilliseconds(500);
        return cachedOverview;
    }

    public bool TryEquip(SavedJobGearset gearset, out string message)
        => TryEquip(gearset.GearsetId, gearset.JobName, gearset.Abbreviation, out message);

    public bool TryEquip(JobGearsetOverview gearset, out string message)
        => TryEquip(gearset.GearsetId, gearset.JobName, gearset.Abbreviation, out message);


    public void ResetSwitchGuard()
    {
        nextGearsetRequestAllowedAtMs = 0;
        pendingSwitchExpiresAtMs = 0;
        pendingGearsetId = -1;
        pendingClassJobId = 0;
    }

    private bool TryEquip(int gearsetId, string jobName, string abbreviation, out string message)
    {
        var now = Environment.TickCount64;
        if (now < nextGearsetRequestAllowedAtMs)
        {
            var remainingSeconds = Math.Max(0.1d, (nextGearsetRequestAllowedAtMs - now) / 1000d);
            message = $"Please wait {remainingSeconds:0.0} seconds for the previous job change to settle.";
            return false;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            message = "FFXIV's gearset system is not ready yet.";
            return false;
        }

        RefreshPendingSwitch(module, now);
        if (pendingGearsetId >= 0)
        {
            message = "The previous job change is still being applied by FFXIV. Please try again in a moment.";
            return false;
        }

        var entry = module->GetGearset(gearsetId);
        if (entry == null || (entry->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0)
        {
            message = $"The saved {abbreviation} gearset is no longer available.";
            return false;
        }

        if (module->CurrentGearsetIndex == gearsetId)
        {
            message = $"{jobName} ({abbreviation}) is already active.";
            return true;
        }

        var result = module->EquipGearset(gearsetId);
        if (result == 0)
        {
            pendingGearsetId = gearsetId;
            pendingClassJobId = entry->ClassJob;
            pendingSwitchExpiresAtMs = now + PendingSwitchTimeoutMilliseconds;
            nextGearsetRequestAllowedAtMs = now + SuccessfulSwitchCooldownMilliseconds;
            nextOverviewRefreshUtc = DateTime.MinValue;
            message = $"Switched to {jobName} ({abbreviation}).";
            return true;
        }


        nextGearsetRequestAllowedAtMs = now + FailedSwitchCooldownMilliseconds;
        message = $"FFXIV could not switch to {jobName} right now.";
        return false;
    }

    private void RefreshPendingSwitch(RaptureGearsetModule* module, long now)
    {
        if (pendingGearsetId < 0)
            return;

        var gearsetSettled = module->CurrentGearsetIndex == pendingGearsetId;
        var jobSettled = pendingClassJobId == 0 ||
                         (playerState.IsLoaded &&
                          playerState.ClassJob.IsValid &&
                          playerState.ClassJob.RowId == pendingClassJobId);

        if ((gearsetSettled && jobSettled) || now >= pendingSwitchExpiresAtMs)
        {
            pendingGearsetId = -1;
            pendingClassJobId = 0;
            pendingSwitchExpiresAtMs = 0;
        }
    }

    private uint ResolveExperienceToNext(short level, out bool available)
    {
        available = false;
        if (level <= 0)
            return 0;

        try
        {
            var sheet = dataManager.GetExcelSheet<ParamGrow>();
            if (!sheet.TryGetRow((uint)level, out var row))
                return 0;

            available = TryReadUnsignedProperty(
                row,
                out var value,
                "ExpToNext",
                "ExperienceToNext",
                "ExpToNextLevel");
            return value;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve experience required for level {Level}.", level);
            return 0;
        }
    }

    private static string FormatJobName(string name)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Trim().ToLowerInvariant());

    private static JobRoleGroup ResolveRoleGroup(byte classJobId, ClassJob classJob)
    {
        if (classJobId is >= 8 and <= 15)
            return JobRoleGroup.Crafter;
        if (classJobId is >= 16 and <= 18)
            return JobRoleGroup.Gatherer;

        var role = ReadUnsignedProperty(classJob, "Role");
        return role switch
        {
            1 => JobRoleGroup.Tank,
            5 => JobRoleGroup.Healer,
            2 => JobRoleGroup.MeleeDps,
            3 => JobRoleGroup.PhysicalRangedDps,
            4 => JobRoleGroup.MagicalRangedDps,
            _ => JobRoleGroup.Other,
        };
    }

    private static uint ReadUnsignedProperty<T>(T source, params string[] propertyNames)
        => TryReadUnsignedProperty(source, out var value, propertyNames) ? value : 0;

    private static bool TryReadUnsignedProperty<T>(T source, out uint result, params string[] propertyNames)
    {
        result = 0;
        object boxed = source!;
        var sourceType = boxed.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = sourceType.GetProperty(propertyName);
            if (property?.GetValue(boxed) is not { } value)
                continue;

            try
            {
                result = Convert.ToUInt32(value);
                return true;
            }
            catch
            {

            }
        }

        return false;
    }
}

public sealed record SavedJobGearset(int GearsetId, byte ClassJobId, string JobName, string Abbreviation);

public sealed record JobGearsetOverview(
    int GearsetId,
    byte ClassJobId,
    string JobName,
    string Abbreviation,
    short ItemLevel,
    short Level,
    int CurrentExperience,
    uint ExperienceToNext,
    bool ExperienceAvailable,
    JobRoleGroup RoleGroup,
    bool IsCurrent)
{
    public float ExperienceProgress
        => ExperienceAvailable && ExperienceToNext > 0
            ? Math.Clamp(CurrentExperience / (float)ExperienceToNext, 0f, 1f)
            : IsLevelCapped ? 1f : 0f;

    public int ExperiencePercent
        => ExperienceAvailable && ExperienceToNext > 0
            ? Math.Clamp((int)MathF.Round(ExperienceProgress * 100f), 0, 100)
            : IsLevelCapped ? 100 : 0;

    public bool IsLevelCapped => ExperienceAvailable && ExperienceToNext == 0 && Level > 0;
}
