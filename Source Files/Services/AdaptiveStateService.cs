using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using REFrameXIV.Models;

namespace REFrameXIV.Services;


public sealed class AdaptiveStateService
{
    private static readonly TimeSpan CombatReleaseDelay = TimeSpan.FromSeconds(3.25);

    private readonly Configuration configuration;
    private readonly ICondition condition;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private DateTime lastCombatObservedUtc;

    public AdaptiveStateService(
        Configuration configuration,
        ICondition condition,
        IClientState clientState,
        IPlayerState playerState)
    {
        this.configuration = configuration;
        this.condition = condition;
        this.clientState = clientState;
        this.playerState = playerState;
    }

    public UiMode EffectiveMode
    {
        get
        {
            if (configuration.ModeOverride != UiMode.Auto)
                return HudModeProfileService.Normalize(configuration.ModeOverride);

            if (!clientState.IsLoggedIn)
                return UiMode.Leisure;

            var now = DateTime.UtcNow;
            var inCombat = condition[ConditionFlag.InCombat];
            if (inCombat)
                lastCombatObservedUtc = now;


            if (WorkJobActive || WorkActivityActive)
                return UiMode.Work;


            if (configuration.StickyCombatMode)
                return UiMode.RaidReady;

            if (inCombat)
            {
                return UiMode.RaidReady;
            }

            if (lastCombatObservedUtc != default && now - lastCombatObservedUtc < CombatReleaseDelay)
                return UiMode.RaidReady;

            if (condition.Any(
                    ConditionFlag.BoundByDuty,
                    ConditionFlag.BoundByDuty56,
                    ConditionFlag.BoundByDuty95,
                    ConditionFlag.InDeepDungeon,
                    ConditionFlag.PvPDisplayActive))
            {
                return UiMode.RaidReady;
            }

            if (configuration.StickyQuestMode)
                return UiMode.Quest;

            return UiMode.Leisure;
        }
    }


    public bool WorkJobActive
    {
        get
        {
            if (!playerState.IsLoaded || !playerState.ClassJob.IsValid)
                return false;

            var classJobId = playerState.ClassJob.RowId;
            return classJobId is >= 8 and <= 18;
        }
    }

    public bool CraftingActivityActive =>
        condition[ConditionFlag.Crafting] ||
        condition[ConditionFlag.PreparingToCraft];

    public bool GatheringActivityActive =>
        condition[ConditionFlag.Gathering] ||
        condition[ConditionFlag.ExecutingGatheringAction];

    public bool WorkActivityActive => CraftingActivityActive || GatheringActivityActive;

    public bool CombatPresentationActive =>
        condition[ConditionFlag.InCombat] ||
        (lastCombatObservedUtc != default && DateTime.UtcNow - lastCombatObservedUtc < CombatReleaseDelay);

    public string ActivityLabel
    {
        get
        {
            if (clientState.IsGPosing)
                return "GPOSE";
            if (CraftingActivityActive)
                return "CRAFTING";
            if (GatheringActivityActive)
                return "GATHERING";

            return EffectiveMode switch
            {
                UiMode.RaidReady => "RAID",
                UiMode.Quest => "QUEST",
                UiMode.Work => "WORK",
                UiMode.Roleplay => "ROLEPLAY",
                _ => "LEISURE",
            };
        }
    }
}
