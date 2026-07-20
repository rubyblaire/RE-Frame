using System;
using System.Collections.Generic;
using REFrameXIV.Services;
using REFrameXIV.Theme;

namespace REFrameXIV.Models;


[Serializable]
public sealed class HudPresetData
{
    public string Name { get; set; } = "HUD Preset";
    public string SourceJob { get; set; } = "XIV";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, HudElementLayout> HudLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HudModeProfile> HudModeProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowLocationFrame { get; set; } = true;
    public bool ShowJobRibbon { get; set; } = true;
    public bool ShowPocketRibbon { get; set; } = true;
    public bool ShowMinimapFrame { get; set; } = true;
    public bool ShowChatFrame { get; set; } = true;
    public bool ShowPartyFrames { get; set; } = true;
    public bool ShowAllianceFrames { get; set; } = true;
    public bool ShowAllianceFrameOne { get; set; } = true;
    public bool ShowAllianceFrameTwo { get; set; } = true;
    public bool ShowPlayerFrame { get; set; } = true;
    public bool ShowTargetFrame { get; set; } = true;
    public bool ShowTargetOfTargetFrame { get; set; } = true;
    public bool ShowCastBar { get; set; } = true;
    public bool ShowPlayerCastBar { get; set; } = true;
    public bool ShowFocusFrame { get; set; } = true;
    public bool ShowEnemyList { get; set; } = true;
    public bool ShowActionBarFrames { get; set; } = true;
    public bool ShowActionBarOne { get; set; } = true;
    public bool ShowActionBarTwo { get; set; } = true;
    public bool ShowActionBarThree { get; set; } = true;
    public int ActionBarOneColumns { get; set; } = 12;
    public int ActionBarTwoColumns { get; set; } = 12;
    public int ActionBarThreeColumns { get; set; } = 12;
    public int PetBarColumns { get; set; } = 12;
    public bool ShowCrossHotbar { get; set; } = true;
    public bool ShowPetBar { get; set; } = true;
    public bool ShowUtilityBarFrames { get; set; } = true;
    public bool ShowRaidTools { get; set; } = true;
    public bool ShowRaidBuffs { get; set; } = true;
    public bool ShowRaidDebuffs { get; set; } = true;
    public bool ShowRaidersKit { get; set; } = true;
    public bool ShowCombatHalo { get; set; } = true;
    public bool ShowCombatHaloInRaidReady { get; set; } = true;
    public bool ShowGreeting { get; set; } = true;
    public bool ShowLeisureDock { get; set; } = true;
    public bool HaloFollowsPlayer { get; set; } = true;
    public bool FrameNativeHoldouts { get; set; } = true;
    public bool FollowJobColors { get; set; } = true;
    public ThemePreset SelectedTheme { get; set; } = ThemePreset.CornflowerSeafoam;
    public float InterfaceScale { get; set; } = 1f;
    public float HudOpacity { get; set; } = 0.86f;
    public float HaloRadius { get; set; } = 118f;
    public float HaloThickness { get; set; } = 6f;
    public float HaloVerticalOffset { get; set; } = -20f;
    public NativeJobGaugePlacement? JobGaugePlacement { get; set; }
    public NativeJobGaugePlacement? NativeStatusEffectsPlacement { get; set; }
    public NativeJobGaugePlacement? NativeScenarioGuidePlacement { get; set; }
    public NativeJobGaugePlacement? NativeQuestListPlacement { get; set; }
    public NativeJobGaugePlacement? NativeDutyInfoPlacement { get; set; }

    public static HudPresetData Capture(Configuration configuration, string name, string sourceJob)
    {
        var preset = new HudPresetData
        {
            Name = string.IsNullOrWhiteSpace(name) ? "HUD Preset" : name.Trim(),
            SourceJob = string.IsNullOrWhiteSpace(sourceJob) ? "XIV" : sourceJob.Trim().ToUpperInvariant(),
            CreatedUtc = DateTime.UtcNow,
            ShowLocationFrame = configuration.ShowLocationFrame,
            ShowJobRibbon = configuration.ShowJobRibbon,
            ShowPocketRibbon = configuration.ShowPocketRibbon,
            ShowMinimapFrame = configuration.ShowMinimapFrame,
            ShowChatFrame = configuration.ShowChatFrame,
            ShowPartyFrames = configuration.ShowPartyFrames,
            ShowAllianceFrames = configuration.ShowAllianceFrames,
            ShowAllianceFrameOne = configuration.ShowAllianceFrameOne,
            ShowAllianceFrameTwo = configuration.ShowAllianceFrameTwo,
            ShowPlayerFrame = configuration.ShowPlayerFrame,
            ShowTargetFrame = configuration.ShowTargetFrame,
            ShowTargetOfTargetFrame = configuration.ShowTargetOfTargetFrame,
            ShowCastBar = configuration.ShowCastBar,
            ShowPlayerCastBar = configuration.ShowPlayerCastBar,
            ShowFocusFrame = configuration.ShowFocusFrame,
            ShowEnemyList = configuration.ShowEnemyList,
            ShowActionBarFrames = configuration.ShowActionBarFrames,
            ShowActionBarOne = configuration.ShowActionBarOne,
            ShowActionBarTwo = configuration.ShowActionBarTwo,
            ShowActionBarThree = configuration.ShowActionBarThree,
            ActionBarOneColumns = HotbarGridLayouts.NormalizeColumns(configuration.ActionBarOneColumns),
            ActionBarTwoColumns = HotbarGridLayouts.NormalizeColumns(configuration.ActionBarTwoColumns),
            ActionBarThreeColumns = HotbarGridLayouts.NormalizeColumns(configuration.ActionBarThreeColumns),
            PetBarColumns = HotbarGridLayouts.NormalizeColumns(configuration.PetBarColumns),
            ShowCrossHotbar = configuration.ShowCrossHotbar,
            ShowPetBar = configuration.ShowPetBar,
            ShowUtilityBarFrames = configuration.ShowUtilityBarFrames,
            ShowRaidTools = configuration.ShowRaidTools,
            ShowRaidBuffs = configuration.ShowRaidBuffs,
            ShowRaidDebuffs = configuration.ShowRaidDebuffs,
            ShowRaidersKit = configuration.ShowRaidersKit,
            ShowCombatHalo = configuration.ShowCombatHalo,
            ShowCombatHaloInRaidReady = configuration.ShowCombatHaloInRaidReady,
            ShowGreeting = configuration.ShowLoginGreeting,
            ShowLeisureDock = configuration.ShowLeisureDock,
            HaloFollowsPlayer = configuration.HaloFollowsPlayer,
            FrameNativeHoldouts = configuration.FrameNativeHoldouts,
            FollowJobColors = configuration.FollowJobColors,
            SelectedTheme = configuration.SelectedTheme,
            InterfaceScale = configuration.InterfaceScale,
            HudOpacity = configuration.HudOpacity,
            HaloRadius = configuration.HaloRadius,
            HaloThickness = configuration.HaloThickness,
            HaloVerticalOffset = configuration.HaloVerticalOffset,
            NativeStatusEffectsPlacement = ClonePlacement(configuration.NativeStatusEffectsPlacement),
            NativeScenarioGuidePlacement = CloneQuestPlacement(configuration, HudElementIds.NativeScenarioGuide),
            NativeQuestListPlacement = CloneQuestPlacement(configuration, HudElementIds.NativeQuestList),
            NativeDutyInfoPlacement = CloneQuestPlacement(configuration, HudElementIds.NativeDutyInfo),
        };

        foreach (var (id, layout) in configuration.HudLayouts)
            preset.HudLayouts[id] = CloneLayout(layout);
        preset.HudModeProfiles = HudModeProfileService.CloneProfiles(configuration.HudModeProfiles);

        if (configuration.NativeJobGaugePlacements.TryGetValue(preset.SourceJob, out var gauge))
            preset.JobGaugePlacement = ClonePlacement(gauge);

        return preset;
    }

    public HudPresetData Clone(string? newName = null)
    {
        var clone = new HudPresetData
        {
            Name = string.IsNullOrWhiteSpace(newName) ? Name : newName!.Trim(),
            SourceJob = SourceJob,
            CreatedUtc = DateTime.UtcNow,
            ShowLocationFrame = ShowLocationFrame,
            ShowJobRibbon = ShowJobRibbon,
            ShowPocketRibbon = ShowPocketRibbon,
            ShowMinimapFrame = ShowMinimapFrame,
            ShowChatFrame = ShowChatFrame,
            ShowPartyFrames = ShowPartyFrames,
            ShowAllianceFrames = ShowAllianceFrames,
            ShowAllianceFrameOne = ShowAllianceFrameOne,
            ShowAllianceFrameTwo = ShowAllianceFrameTwo,
            ShowPlayerFrame = ShowPlayerFrame,
            ShowTargetFrame = ShowTargetFrame,
            ShowTargetOfTargetFrame = ShowTargetOfTargetFrame,
            ShowCastBar = ShowCastBar,
            ShowPlayerCastBar = ShowPlayerCastBar,
            ShowFocusFrame = ShowFocusFrame,
            ShowEnemyList = ShowEnemyList,
            ShowActionBarFrames = ShowActionBarFrames,
            ShowActionBarOne = ShowActionBarOne,
            ShowActionBarTwo = ShowActionBarTwo,
            ShowActionBarThree = ShowActionBarThree,
            ActionBarOneColumns = HotbarGridLayouts.NormalizeColumns(ActionBarOneColumns),
            ActionBarTwoColumns = HotbarGridLayouts.NormalizeColumns(ActionBarTwoColumns),
            ActionBarThreeColumns = HotbarGridLayouts.NormalizeColumns(ActionBarThreeColumns),
            PetBarColumns = HotbarGridLayouts.NormalizeColumns(PetBarColumns),
            ShowCrossHotbar = ShowCrossHotbar,
            ShowPetBar = ShowPetBar,
            ShowUtilityBarFrames = ShowUtilityBarFrames,
            ShowRaidTools = ShowRaidTools,
            ShowRaidBuffs = ShowRaidBuffs,
            ShowRaidDebuffs = ShowRaidDebuffs,
            ShowRaidersKit = ShowRaidersKit,
            ShowCombatHalo = ShowCombatHalo,
            ShowCombatHaloInRaidReady = ShowCombatHaloInRaidReady,
            ShowGreeting = ShowGreeting,
            ShowLeisureDock = ShowLeisureDock,
            HaloFollowsPlayer = HaloFollowsPlayer,
            FrameNativeHoldouts = FrameNativeHoldouts,
            FollowJobColors = FollowJobColors,
            SelectedTheme = SelectedTheme,
            InterfaceScale = InterfaceScale,
            HudOpacity = HudOpacity,
            HaloRadius = HaloRadius,
            HaloThickness = HaloThickness,
            HaloVerticalOffset = HaloVerticalOffset,
            JobGaugePlacement = ClonePlacement(JobGaugePlacement),
            NativeStatusEffectsPlacement = ClonePlacement(NativeStatusEffectsPlacement),
            NativeScenarioGuidePlacement = ClonePlacement(NativeScenarioGuidePlacement),
            NativeQuestListPlacement = ClonePlacement(NativeQuestListPlacement),
            NativeDutyInfoPlacement = ClonePlacement(NativeDutyInfoPlacement),
        };
        foreach (var (id, layout) in HudLayouts)
            clone.HudLayouts[id] = CloneLayout(layout);
        clone.HudModeProfiles = HudModeProfileService.CloneProfiles(HudModeProfiles);
        return clone;
    }

    public void ApplyTo(Configuration configuration, string targetJob)
    {
        configuration.HudLayouts = new Dictionary<string, HudElementLayout>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, layout) in HudLayouts)
            configuration.HudLayouts[id] = CloneLayout(layout);
        UpgradeLegacyLayouts(configuration.HudLayouts);
        configuration.HudModeProfiles = HudModeProfileService.CloneProfiles(HudModeProfiles);
        HudModeProfileService.EnsureCollections(configuration);

        configuration.ShowLocationFrame = ShowLocationFrame;
        configuration.ShowJobRibbon = ShowJobRibbon;
        configuration.ShowPocketRibbon = ShowPocketRibbon;
        configuration.ShowMinimapFrame = ShowMinimapFrame;
        configuration.ShowChatFrame = ShowChatFrame;
        configuration.ShowPartyFrames = ShowPartyFrames;
        configuration.ShowAllianceFrames = ShowAllianceFrames;
        configuration.ShowAllianceFrameOne = ShowAllianceFrameOne;
        configuration.ShowAllianceFrameTwo = ShowAllianceFrameTwo;
        configuration.ShowPlayerFrame = ShowPlayerFrame;
        configuration.ShowTargetFrame = ShowTargetFrame;
        configuration.ShowTargetOfTargetFrame = ShowTargetOfTargetFrame;
        configuration.ShowCastBar = ShowCastBar;
        configuration.ShowPlayerCastBar = ShowPlayerCastBar;
        configuration.ShowFocusFrame = ShowFocusFrame;
        configuration.ShowEnemyList = ShowEnemyList;
        configuration.ShowActionBarFrames = ShowActionBarFrames;
        configuration.ShowActionBarOne = ShowActionBarOne;
        configuration.ShowActionBarTwo = ShowActionBarTwo;
        configuration.ShowActionBarThree = ShowActionBarThree;
        configuration.ActionBarOneColumns = HotbarGridLayouts.NormalizeColumns(ActionBarOneColumns);
        configuration.ActionBarTwoColumns = HotbarGridLayouts.NormalizeColumns(ActionBarTwoColumns);
        configuration.ActionBarThreeColumns = HotbarGridLayouts.NormalizeColumns(ActionBarThreeColumns);
        configuration.PetBarColumns = HotbarGridLayouts.NormalizeColumns(PetBarColumns);
        configuration.ShowCrossHotbar = ShowCrossHotbar;
        configuration.ShowPetBar = ShowPetBar;
        configuration.ShowUtilityBarFrames = ShowUtilityBarFrames;
        configuration.ShowRaidTools = ShowRaidTools;
        configuration.ShowRaidBuffs = ShowRaidBuffs;
        configuration.ShowRaidDebuffs = ShowRaidDebuffs;
        configuration.ShowRaidersKit = ShowRaidersKit;
        configuration.ShowCombatHalo = ShowCombatHalo;
        configuration.ShowCombatHaloInRaidReady = ShowCombatHaloInRaidReady;
        configuration.ShowLoginGreeting = ShowGreeting;
        configuration.ShowLeisureDock = ShowLeisureDock;
        configuration.HaloFollowsPlayer = HaloFollowsPlayer;
        configuration.FrameNativeHoldouts = FrameNativeHoldouts;
        configuration.FollowJobColors = FollowJobColors;
        configuration.SelectedTheme = SelectedTheme;
        configuration.InterfaceScale = Math.Clamp(InterfaceScale, 0.60f, 2.50f);
        configuration.HudOpacity = Math.Clamp(HudOpacity, 0.35f, 1f);
        configuration.HaloRadius = Math.Clamp(HaloRadius, 75f, 190f);
        configuration.HaloThickness = Math.Clamp(HaloThickness, 2f, 12f);
        configuration.HaloVerticalOffset = Math.Clamp(HaloVerticalOffset, -140f, 100f);

        if (!string.IsNullOrWhiteSpace(targetJob) && JobGaugePlacement is not null)
        {
            var placement = ClonePlacement(JobGaugePlacement)!;

            ClearOriginalAnchors(placement);
            configuration.NativeJobGaugePlacements[targetJob.ToUpperInvariant()] = placement;
        }

        if (NativeStatusEffectsPlacement is not null)
            configuration.NativeStatusEffectsPlacement = ClonePlacement(NativeStatusEffectsPlacement)!;

        configuration.NativeQuestElementPlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        ApplyQuestPlacement(configuration, HudElementIds.NativeScenarioGuide, NativeScenarioGuidePlacement);
        ApplyQuestPlacement(configuration, HudElementIds.NativeQuestList, NativeQuestListPlacement);
        ApplyQuestPlacement(configuration, HudElementIds.NativeDutyInfo, NativeDutyInfoPlacement);
    }

    private static NativeJobGaugePlacement? CloneQuestPlacement(Configuration configuration, string elementId)
    {
        configuration.NativeQuestElementPlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        return configuration.NativeQuestElementPlacements.TryGetValue(elementId, out var placement)
            ? ClonePlacement(placement)
            : null;
    }

    private static void ApplyQuestPlacement(Configuration configuration, string elementId, NativeJobGaugePlacement? source)
    {
        if (source is null)
        {
            configuration.NativeQuestElementPlacements.Remove(elementId);
            return;
        }

        var placement = ClonePlacement(source)!;


        placement.HasOriginal = false;
        configuration.NativeQuestElementPlacements[elementId] = placement;
    }

    private static void UpgradeLegacyLayouts(Dictionary<string, HudElementLayout> layouts)
    {
        if (layouts.TryGetValue(HudElementIds.Alliance, out var alliance) &&
            (!layouts.ContainsKey(HudElementIds.AllianceOne) || !layouts.ContainsKey(HudElementIds.AllianceTwo)))
        {
            var gap = 8f / 1920f;
            var width = MathF.Max(180f / 1920f, (alliance.Width - gap) * 0.5f);
            layouts.TryAdd(HudElementIds.AllianceOne, new HudElementLayout { X = alliance.X, Y = alliance.Y, Width = width, Height = alliance.Height });
            layouts.TryAdd(HudElementIds.AllianceTwo, new HudElementLayout { X = alliance.X + width + gap, Y = alliance.Y, Width = width, Height = alliance.Height });
        }

        if (layouts.TryGetValue(HudElementIds.ActionBars, out var actionBars) &&
            (!layouts.ContainsKey(HudElementIds.ActionBarOne) || !layouts.ContainsKey(HudElementIds.ActionBarTwo) || !layouts.ContainsKey(HudElementIds.ActionBarThree)))
        {
            var gap = 3f / 1080f;
            var height = MathF.Max(32f / 1080f, (actionBars.Height - gap * 2f) / 3f);
            layouts.TryAdd(HudElementIds.ActionBarThree, new HudElementLayout { X = actionBars.X, Y = actionBars.Y, Width = actionBars.Width, Height = height });
            layouts.TryAdd(HudElementIds.ActionBarTwo, new HudElementLayout { X = actionBars.X, Y = actionBars.Y + height + gap, Width = actionBars.Width, Height = height });
            layouts.TryAdd(HudElementIds.ActionBarOne, new HudElementLayout { X = actionBars.X, Y = actionBars.Y + (height + gap) * 2f, Width = actionBars.Width, Height = height });
        }

        if (layouts.TryGetValue(HudElementIds.Target, out var target) &&
            (!layouts.ContainsKey(HudElementIds.CastBar) || !layouts.ContainsKey(HudElementIds.TargetOfTarget)))
        {
            var gapX = 8f / 1920f;
            var gapY = 6f / 1080f;
            var targetHeight = MathF.Max(58f / 1080f, target.Height * 0.68f);
            target.Height = targetHeight;
            var castWidth = MathF.Max(220f / 1920f, target.Width * 0.60f - gapX * 0.5f);
            var targetOfTargetWidth = MathF.Max(170f / 1920f, target.Width - castWidth - gapX);
            var lowerY = target.Y + targetHeight + gapY;
            layouts.TryAdd(HudElementIds.CastBar, new HudElementLayout { X = target.X, Y = lowerY, Width = castWidth, Height = 42f / 1080f });
            layouts.TryAdd(HudElementIds.TargetOfTarget, new HudElementLayout { X = target.X + castWidth + gapX, Y = lowerY, Width = targetOfTargetWidth, Height = 42f / 1080f });
        }
    }

    private static HudElementLayout CloneLayout(HudElementLayout layout) => new()
    {
        X = layout.X,
        Y = layout.Y,
        Width = layout.Width,
        Height = layout.Height,
    };

    private static NativeJobGaugePlacement? ClonePlacement(NativeJobGaugePlacement? placement)
        => placement is null ? null : new NativeJobGaugePlacement
        {
            X = placement.X,
            Y = placement.Y,
            OriginalX = placement.OriginalX,
            OriginalY = placement.OriginalY,
            Scale = placement.Scale,
            OriginalScale = placement.OriginalScale,
            HasOriginal = placement.HasOriginal,
            Components = CloneComponents(placement.Components),
        };

    private static void ClearOriginalAnchors(NativeJobGaugePlacement placement)
    {
        placement.HasOriginal = false;
        placement.Components ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in placement.Components.Values)
        {
            if (component is not null)
                ClearOriginalAnchors(component);
        }
    }

    private static Dictionary<string, NativeJobGaugePlacement> CloneComponents(Dictionary<string, NativeJobGaugePlacement>? source)
    {
        var clone = new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return clone;

        foreach (var (key, placement) in source)
        {
            if (placement is not null)
                clone[key] = ClonePlacement(placement)!;
        }

        return clone;
    }

}
