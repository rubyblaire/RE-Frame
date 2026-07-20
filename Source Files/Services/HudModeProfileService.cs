using System;
using System.Collections.Generic;
using System.Linq;
using REFrameXIV.Models;
using REFrameXIV.Localization;

namespace REFrameXIV.Services;

public static class HudModeProfileService
{
    public static readonly UiMode[] EditableModes =
    {
        UiMode.Leisure,
        UiMode.Roleplay,
        UiMode.Quest,
        UiMode.RaidReady,
        UiMode.Work,
    };


    public static readonly UiMode[] LegacySerializedModes =
    {
        UiMode.Leisure,
        UiMode.Quest,
        UiMode.RaidReady,
        UiMode.Combat,
    };


    public static readonly UiMode[] SerializedModes =
    {
        UiMode.Leisure,
        UiMode.Quest,
        UiMode.RaidReady,
        UiMode.Combat,
        UiMode.Work,
    };

    public static UiMode Normalize(UiMode mode) => mode switch
    {
        UiMode.Leisure => UiMode.Leisure,
        UiMode.Quest => UiMode.Quest,
        UiMode.RaidReady => UiMode.RaidReady,
        UiMode.Combat => UiMode.RaidReady,
        UiMode.Work => UiMode.Work,
        UiMode.Roleplay => UiMode.Roleplay,
        _ => UiMode.Leisure,
    };

    public static string Label(UiMode mode) => Normalize(mode) switch
    {
        UiMode.Quest => Localizer.Text("mode.quest", "Quest"),
        UiMode.RaidReady => Localizer.Text("mode.raid", "Raid"),
        UiMode.Work => Localizer.Text("mode.work", "Work"),
        UiMode.Roleplay => Localizer.Text("mode.roleplay", "Roleplay"),
        _ => Localizer.Text("mode.leisure", "Leisure"),
    };

    public static string Key(UiMode mode) => Normalize(mode).ToString();

    public static string SerializedKey(UiMode mode) => mode.ToString();

    public static bool IsCalmMode(UiMode mode)
    {
        mode = Normalize(mode);
        return mode is UiMode.Leisure or UiMode.Roleplay;
    }

    public static void RemoveRetiredMapWidgets(Configuration configuration)
    {
        const string retiredWeather = "forge-weather";
        const string retiredSundial = "forge-sundial";

        configuration.HudLayouts?.Remove(retiredWeather);
        configuration.HudLayouts?.Remove(retiredSundial);
        if (configuration.HudModeProfiles is null)
            return;

        foreach (var profile in configuration.HudModeProfiles.Values)
        {
            profile?.HudLayouts?.Remove(retiredWeather);
            profile?.HudLayouts?.Remove(retiredSundial);
            profile?.ElementVisibility?.Remove(retiredWeather);
            profile?.ElementVisibility?.Remove(retiredSundial);
            profile?.ElementLocks?.Remove(retiredWeather);
            profile?.ElementLocks?.Remove(retiredSundial);
        }
    }

    public static void EnsureCollections(Configuration configuration)
    {
        if (configuration.HudModeProfiles is null)
            configuration.HudModeProfiles = new Dictionary<string, HudModeProfile>(StringComparer.OrdinalIgnoreCase);
        else if (!configuration.HudModeProfiles.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
            configuration.HudModeProfiles = new Dictionary<string, HudModeProfile>(configuration.HudModeProfiles, StringComparer.OrdinalIgnoreCase);
        foreach (var key in new List<string>(configuration.HudModeProfiles.Keys))
        {
            var profile = configuration.HudModeProfiles[key];
            if (profile is null)
            {
                configuration.HudModeProfiles.Remove(key);
                continue;
            }

            profile.EnsureValid();
        }


        var raidKey = Key(UiMode.RaidReady);
        var combatKey = SerializedKey(UiMode.Combat);
        if (!configuration.HudModeProfiles.ContainsKey(raidKey) &&
            configuration.HudModeProfiles.TryGetValue(combatKey, out var legacyCombat) &&
            legacyCombat is not null)
        {
            configuration.HudModeProfiles[raidKey] = legacyCombat.Clone();
        }


        var workKey = Key(UiMode.Work);
        if (!configuration.HudModeProfiles.ContainsKey(workKey))
            configuration.HudModeProfiles[workKey] = CreateDefaultWorkProfile();

        var roleplayKey = Key(UiMode.Roleplay);
        if (!configuration.HudModeProfiles.ContainsKey(roleplayKey))
            configuration.HudModeProfiles[roleplayKey] = CreateDefaultRoleplayProfile();


        RemoveRetiredMapWidgets(configuration);
    }

    public static bool TryGetProfile(Configuration configuration, UiMode mode, out HudModeProfile profile)
    {
        if (configuration.HudModeProfiles is not null &&
            configuration.HudModeProfiles.TryGetValue(Key(mode), out var resolved) &&
            resolved is not null)
        {
            resolved.EnsureValid();
            profile = resolved;
            return true;
        }

        profile = null!;
        return false;
    }

    public static HudModeProfile GetOrCreate(Configuration configuration, UiMode mode)
    {
        EnsureCollections(configuration);
        var key = Key(mode);
        if (!configuration.HudModeProfiles.TryGetValue(key, out var profile) || profile is null)
        {
            profile = new HudModeProfile();
            configuration.HudModeProfiles[key] = profile;
        }

        profile.EnsureValid();
        return profile;
    }

    public static bool ResolveVisibility(
        Configuration configuration,
        UiMode mode,
        string elementId,
        bool sharedFallback)
    {
        if (mode == UiMode.Auto)
            return sharedFallback;

        return TryGetProfile(configuration, mode, out var profile) &&
               profile.ElementVisibility.TryGetValue(elementId, out var visible)
            ? visible
            : sharedFallback;
    }

    public static void SetVisibility(Configuration configuration, UiMode mode, string elementId, bool visible)
        => GetOrCreate(configuration, mode).ElementVisibility[elementId] = visible;

    public static bool TryGetLayout(
        Configuration configuration,
        UiMode mode,
        string elementId,
        out HudElementLayout layout)
    {
        if (mode != UiMode.Auto &&
            TryGetProfile(configuration, mode, out var profile) &&
            profile.HudLayouts.TryGetValue(elementId, out var resolved) &&
            resolved is not null)
        {
            layout = resolved;
            return true;
        }

        layout = null!;
        return false;
    }

    public static void SetLayout(Configuration configuration, UiMode mode, string elementId, HudElementLayout layout)
        => GetOrCreate(configuration, mode).HudLayouts[elementId] = layout;

    public static void ResetLayout(Configuration configuration, UiMode mode, string elementId)
    {
        if (mode == UiMode.Auto || !TryGetProfile(configuration, mode, out var profile))
            return;

        profile.HudLayouts.Remove(elementId);
        RemoveProfileIfEmpty(configuration, mode, profile);
    }

    public static void ResetElement(Configuration configuration, UiMode mode, string elementId)
    {
        if (mode == UiMode.Auto || !TryGetProfile(configuration, mode, out var profile))
            return;

        profile.HudLayouts.Remove(elementId);
        profile.ElementVisibility.Remove(elementId);
        profile.ElementLocks.Remove(elementId);
        RemoveProfileIfEmpty(configuration, mode, profile);
    }

    public static bool IsLocked(Configuration configuration, UiMode mode, string elementId)
    {
        mode = Normalize(mode);
        return TryGetProfile(configuration, mode, out var profile) &&
               profile.ElementLocks.TryGetValue(elementId, out var locked) &&
               locked;
    }

    public static void SetLocked(Configuration configuration, UiMode mode, string elementId, bool locked)
    {
        mode = Normalize(mode);
        var profile = GetOrCreate(configuration, mode);
        if (locked)
            profile.ElementLocks[elementId] = true;
        else
            profile.ElementLocks.Remove(elementId);
        RemoveProfileIfEmpty(configuration, mode, profile);
    }

    public static void CopyElement(Configuration configuration, UiMode sourceMode, UiMode destinationMode, string elementId)
    {
        sourceMode = Normalize(sourceMode);
        destinationMode = Normalize(destinationMode);
        if (sourceMode == destinationMode)
            return;

        var sourceLayout = TryGetLayout(configuration, sourceMode, elementId, out var resolvedLayout)
            ? resolvedLayout
            : configuration.HudLayouts.TryGetValue(elementId, out var sharedLayout)
                ? sharedLayout
                : null;

        if (sourceLayout is not null)
        {
            SetLayout(configuration, destinationMode, elementId, new HudElementLayout
            {
                X = sourceLayout.X,
                Y = sourceLayout.Y,
                Width = sourceLayout.Width,
                Height = sourceLayout.Height,
            });
        }

        var sharedVisibility = ResolveSharedVisibility(configuration, elementId);
        SetVisibility(configuration, destinationMode, elementId,
            ResolveVisibility(configuration, sourceMode, elementId, sharedVisibility));
        SetLocked(configuration, destinationMode, elementId, IsLocked(configuration, sourceMode, elementId));
    }

    public static void ResetMode(Configuration configuration, UiMode mode)
    {
        EnsureCollections(configuration);
        mode = Normalize(mode);
        if (mode == UiMode.Work)
            configuration.HudModeProfiles[Key(mode)] = CreateDefaultWorkProfile();
        else if (mode == UiMode.Roleplay)
            configuration.HudModeProfiles[Key(mode)] = CreateDefaultRoleplayProfile();
        else
            configuration.HudModeProfiles.Remove(Key(mode));
    }

    public static void CopyMode(Configuration configuration, UiMode sourceMode, UiMode destinationMode)
    {
        sourceMode = Normalize(sourceMode);
        destinationMode = Normalize(destinationMode);
        if (sourceMode == destinationMode)
            return;

        EnsureCollections(configuration);
        if (TryGetProfile(configuration, sourceMode, out var sourceProfile))
            configuration.HudModeProfiles[Key(destinationMode)] = sourceProfile.Clone();
        else
            configuration.HudModeProfiles.Remove(Key(destinationMode));
    }

    public static void ClearAllModeLayouts(Configuration configuration)
    {
        EnsureCollections(configuration);
        foreach (var profile in configuration.HudModeProfiles.Values)
            profile?.HudLayouts.Clear();

        RemoveEmptyProfiles(configuration);
    }

    public static Dictionary<string, HudModeProfile> CloneProfiles(
        Dictionary<string, HudModeProfile>? source)
    {
        var clone = new Dictionary<string, HudModeProfile>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return clone;

        foreach (var (key, profile) in source)
        {
            if (profile is not null)
                clone[key] = profile.Clone();
        }

        return clone;
    }

    private static bool ResolveSharedVisibility(Configuration configuration, string id)
    {
        if (HudElementIds.TryParseAdditionalCombatHotbar(id, out var hotbarId))
        {
            var bar = configuration.AdditionalCombatHotbars?.FirstOrDefault(candidate => candidate.Id == hotbarId);
            return bar?.Enabled ?? true;
        }

        return id switch
        {
        HudElementIds.Location => configuration.ShowLocationFrame,
        HudElementIds.JobRibbon => configuration.ShowJobRibbon,
        HudElementIds.PocketRibbon => configuration.ShowPocketRibbon,
        HudElementIds.Minimap => configuration.ShowMinimapFrame,
        HudElementIds.ForgeCoordinates
            => configuration.ForgeSquareMinimapEnabled && configuration.ShowMinimapFrame,
        HudElementIds.Chat => configuration.ShowChatFrame,
        HudElementIds.Party => configuration.ShowPartyFrames,
        HudElementIds.AllianceOne => configuration.ShowAllianceFrames && configuration.ShowAllianceFrameOne,
        HudElementIds.AllianceTwo => configuration.ShowAllianceFrames && configuration.ShowAllianceFrameTwo,
        HudElementIds.Player => configuration.ShowPlayerFrame,
        HudElementIds.Target => configuration.ShowTargetFrame,
        HudElementIds.TargetOfTarget => configuration.ShowTargetOfTargetFrame,
        HudElementIds.CastBar => configuration.ShowCastBar,
        HudElementIds.PlayerCastBar => configuration.ShowPlayerCastBar,
        HudElementIds.Focus => configuration.ShowFocusFrame,
        HudElementIds.EnemyList => configuration.ShowEnemyList,
        HudElementIds.ActionBarOne => configuration.ShowActionBarFrames && configuration.ShowActionBarOne,
        HudElementIds.ActionBarTwo => configuration.ShowActionBarFrames && configuration.ShowActionBarTwo,
        HudElementIds.ActionBarThree => configuration.ShowActionBarFrames && configuration.ShowActionBarThree,
        HudElementIds.CrossHotbar => configuration.ShowCrossHotbar,
        HudElementIds.PetBar => configuration.ShowPetBar,
        HudElementIds.UtilityBars => configuration.ShowUtilityBarFrames,
        HudElementIds.UtilityBarsTwo => configuration.ShowSecondUtilityBarFrames,
        HudElementIds.RaidTools => configuration.ShowRaidTools,
        HudElementIds.RaidBuffs => configuration.ShowRaidBuffs,
        HudElementIds.RaidDebuffs => configuration.ShowRaidDebuffs,
        HudElementIds.RaidersKit => configuration.ShowRaidersKit,
        HudElementIds.LimitBreak => configuration.ShowLimitBreakGauge,
        HudElementIds.CombatHalo => configuration.ShowCombatHalo,
        HudElementIds.Greeting => configuration.ShowLoginGreeting,
        HudElementIds.LeisureDock => configuration.ShowLeisureDock,
        _ => true,
        };
    }


    private static HudModeProfile CreateDefaultRoleplayProfile()
    {
        var profile = new HudModeProfile();


        profile.ElementVisibility[HudElementIds.Chat] = true;
        profile.ElementVisibility[HudElementIds.Party] = false;
        profile.ElementVisibility[HudElementIds.AllianceOne] = false;
        profile.ElementVisibility[HudElementIds.AllianceTwo] = false;
        profile.ElementVisibility[HudElementIds.Target] = false;
        profile.ElementVisibility[HudElementIds.TargetOfTarget] = false;
        profile.ElementVisibility[HudElementIds.CastBar] = false;
        profile.ElementVisibility[HudElementIds.PlayerCastBar] = false;
        profile.ElementVisibility[HudElementIds.Focus] = false;
        profile.ElementVisibility[HudElementIds.EnemyList] = false;


        profile.ElementVisibility[HudElementIds.ActionBarOne] = true;
        profile.ElementVisibility[HudElementIds.ActionBarTwo] = true;
        profile.ElementVisibility[HudElementIds.ActionBarThree] = true;
        profile.ElementVisibility[HudElementIds.CrossHotbar] = false;
        profile.ElementVisibility[HudElementIds.PetBar] = false;


        profile.ElementVisibility[HudElementIds.UtilityBars] = true;
        profile.ElementVisibility[HudElementIds.UtilityBarsTwo] = true;
        profile.ElementVisibility[HudElementIds.RaidTools] = false;
        profile.ElementVisibility[HudElementIds.RaidBuffs] = false;
        profile.ElementVisibility[HudElementIds.RaidDebuffs] = false;
        profile.ElementVisibility[HudElementIds.RaidersKit] = false;
        profile.ElementVisibility[HudElementIds.LimitBreak] = false;
        profile.ElementVisibility[HudElementIds.CombatHalo] = false;
        profile.ElementVisibility[HudElementIds.Greeting] = false;
        profile.ElementVisibility[HudElementIds.LeisureDock] = true;
        return profile;
    }

    private static HudModeProfile CreateDefaultWorkProfile()
    {
        var profile = new HudModeProfile();


        profile.ElementVisibility[HudElementIds.Party] = false;
        profile.ElementVisibility[HudElementIds.AllianceOne] = false;
        profile.ElementVisibility[HudElementIds.AllianceTwo] = false;
        profile.ElementVisibility[HudElementIds.Target] = false;
        profile.ElementVisibility[HudElementIds.TargetOfTarget] = false;
        profile.ElementVisibility[HudElementIds.CastBar] = false;
        profile.ElementVisibility[HudElementIds.Focus] = false;
        profile.ElementVisibility[HudElementIds.EnemyList] = false;
        profile.ElementVisibility[HudElementIds.PetBar] = false;
        profile.ElementVisibility[HudElementIds.RaidTools] = false;
        profile.ElementVisibility[HudElementIds.LimitBreak] = false;
        profile.ElementVisibility[HudElementIds.CombatHalo] = false;
        profile.ElementVisibility[HudElementIds.LeisureDock] = true;
        return profile;
    }

    private static void RemoveProfileIfEmpty(Configuration configuration, UiMode mode, HudModeProfile profile)
    {
        if (profile.HudLayouts.Count == 0 && profile.ElementVisibility.Count == 0 && profile.ElementLocks.Count == 0)
            configuration.HudModeProfiles.Remove(Key(mode));
    }

    private static void RemoveEmptyProfiles(Configuration configuration)
    {
        foreach (var mode in EditableModes)
        {
            if (TryGetProfile(configuration, mode, out var profile) &&
                profile.HudLayouts.Count == 0 &&
                profile.ElementVisibility.Count == 0 &&
                profile.ElementLocks.Count == 0)
                configuration.HudModeProfiles.Remove(Key(mode));
        }
    }
}
