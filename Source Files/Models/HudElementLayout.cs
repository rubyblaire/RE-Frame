using System;
using REFrameXIV.Localization;

namespace REFrameXIV.Models;

[Serializable]
public sealed class HudElementLayout
{

    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}

public static class HudElementIds
{
    public const string Location = "location";
    public const string JobRibbon = "job-ribbon";
    public const string PocketRibbon = "pocket-ribbon";
    public const string Minimap = "minimap";
    public const string ForgeCoordinates = "forge-coordinates";
    public const string Chat = "chat";
    public const string Party = "party";


    public const string Alliance = "alliance";
    public const string ActionBars = "action-bars";

    public const string AllianceOne = "alliance-one";
    public const string AllianceTwo = "alliance-two";
    public const string Player = "player";
    public const string Target = "target";
    public const string TargetOfTarget = "target-of-target";
    public const string CastBar = "cast-bar";
    public const string PlayerCastBar = "player-cast-bar";
    public const string Focus = "focus";
    public const string EnemyList = "enemy-list";
    public const string ActionBarOne = "action-bar-one";
    public const string ActionBarTwo = "action-bar-two";
    public const string ActionBarThree = "action-bar-three";
    public const string CrossHotbar = "cross-hotbar";
    public const string PetBar = "pet-bar";
    public const string UtilityBars = "utility-bars";
    public const string UtilityBarsTwo = "utility-bars-two";
    private const string AdditionalCombatHotbarPrefix = "action-bar-extra-";
    public const string RaidTools = "raid-tools";
    public const string RaidBuffs = "raid-buffs";
    public const string RaidDebuffs = "raid-debuffs";
    public const string RaidersKit = "raiders-kit";
    public const string LimitBreak = "limit-break";
    public const string CombatHalo = "combat-halo";
    public const string Greeting = "greeting";
    public const string LeisureDock = "leisure-dock";
    public const string NativeJobGauge = "native-job-gauge";
    public const string NativeStatusEffects = "native-status-effects";
    public const string NativeScenarioGuide = "native-scenario-guide";
    public const string NativeQuestList = "native-quest-list";
    public const string NativeDutyInfo = "native-duty-info";


    public static readonly string[] ShareCodeRf3V6 =
    {
        Location, JobRibbon, PocketRibbon, Minimap, Chat, Party, AllianceOne, AllianceTwo, Player, Target,
        TargetOfTarget, CastBar, PlayerCastBar, Focus, EnemyList, ActionBarOne, ActionBarTwo, ActionBarThree,
        CrossHotbar, PetBar, UtilityBars, RaidTools, RaidBuffs, RaidDebuffs, RaidersKit, LimitBreak, CombatHalo, Greeting, LeisureDock,
    };

    public static readonly string[] All =
    {
        Location, JobRibbon, PocketRibbon, Minimap, ForgeCoordinates, Chat, Party, AllianceOne, AllianceTwo, Player, Target,
        TargetOfTarget, CastBar, PlayerCastBar, Focus, EnemyList, ActionBarOne, ActionBarTwo, ActionBarThree,
        CrossHotbar, PetBar, UtilityBars, RaidTools, RaidBuffs, RaidDebuffs, RaidersKit, LimitBreak, CombatHalo, Greeting, LeisureDock,
    };


    public static readonly string[] ShareCodeV11 =
    {
        Location, JobRibbon, PocketRibbon, Minimap, Chat, Party, AllianceOne, AllianceTwo, Player, Target,
        TargetOfTarget, CastBar, PlayerCastBar, Focus, EnemyList, ActionBarOne, ActionBarTwo, ActionBarThree,
        CrossHotbar, PetBar, UtilityBars, RaidTools, LimitBreak, CombatHalo, Greeting, LeisureDock,
    };


    public static readonly string[] ShareCodeRf3V1 =
    {
        Location, JobRibbon, PocketRibbon, Minimap, Chat, Party, AllianceOne, AllianceTwo, Player, Target,
        TargetOfTarget, CastBar, PlayerCastBar, Focus, EnemyList, ActionBarOne, ActionBarTwo, ActionBarThree,
        CrossHotbar, PetBar, UtilityBars, RaidTools, LimitBreak, CombatHalo, Greeting, LeisureDock,
    };


    public static readonly string[] ShareCodeV7 =
    {
        Location, JobRibbon, Minimap, Chat, Party, AllianceOne, AllianceTwo, Player, Target,
        TargetOfTarget, CastBar, Focus, EnemyList, ActionBarOne, ActionBarTwo, ActionBarThree,
        PetBar, UtilityBars, RaidTools, LimitBreak, CombatHalo, Greeting, LeisureDock,
    };


    public static readonly string[] ShareCodeV10 =
    {
        Location, JobRibbon, Minimap, Chat, Party, AllianceOne, AllianceTwo, Player, Target,
        TargetOfTarget, CastBar, Focus, EnemyList, ActionBarOne, ActionBarTwo, ActionBarThree,
        CrossHotbar, PetBar, UtilityBars, RaidTools, LimitBreak, CombatHalo, Greeting, LeisureDock,
    };


    public static readonly string[] ShareCodeV5 =
    {
        Location, JobRibbon, Minimap, Chat, Party, AllianceOne, AllianceTwo, Player, Target,
        TargetOfTarget, CastBar, Focus, EnemyList, ActionBarOne, ActionBarTwo, ActionBarThree,
        PetBar, UtilityBars, RaidTools, CombatHalo, LeisureDock,
    };


    public static readonly string[] ShareCodeV4 =
    {
        Location, JobRibbon, Minimap, Chat, Party, Alliance, Player, Target, Focus, EnemyList,
        ActionBars, PetBar, UtilityBars, RaidTools, CombatHalo, LeisureDock,
    };


    public static readonly string[] ShareCodeV3 =
    {
        Location, JobRibbon, Minimap, Chat, Party, Player, Target, Focus, EnemyList,
        ActionBars, PetBar, UtilityBars, RaidTools, CombatHalo, LeisureDock,
    };

    public static string AdditionalCombatHotbar(uint id) => $"{AdditionalCombatHotbarPrefix}{id}";

    public static bool IsAdditionalCombatHotbar(string id)
        => TryParseAdditionalCombatHotbar(id, out _);

    public static bool TryParseAdditionalCombatHotbar(string id, out uint hotbarId)
    {
        hotbarId = 0;
        if (string.IsNullOrWhiteSpace(id) ||
            !id.StartsWith(AdditionalCombatHotbarPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return uint.TryParse(id[AdditionalCombatHotbarPrefix.Length..], out hotbarId) && hotbarId > 0;
    }

    public static string Label(string id) => id switch
    {
        Location => Localizer.Text("hud.location", "Location / Navigation"),
        JobRibbon => Localizer.Text("hud.job", "Job / Level Ribbon"),
        PocketRibbon => Localizer.Text("hud.pocket", "Pocket Ribbon"),
        Minimap => Localizer.Text("hud.minimap", "Minimap Frame"),
        ForgeCoordinates => Localizer.Text("hud.forge.coordinates", "RE:Forge Map Coordinates"),
        Chat => Localizer.Text("hud.chat", "Chat / Comms Frame"),
        Party => Localizer.Text("hud.party", "Party Frames"),
        Alliance => Localizer.Text("hud.alliance.legacy", "Alliance Frames (Legacy)"),
        AllianceOne => Localizer.Text("hud.alliance.one", "Alliance Group Frame 1"),
        AllianceTwo => Localizer.Text("hud.alliance.two", "Alliance Group Frame 2"),
        Player => Localizer.Text("hud.player", "Player Frame"),
        Target => Localizer.Text("hud.target", "Target Frame"),
        TargetOfTarget => Localizer.Text("hud.targettarget", "Target of Target Frame"),
        CastBar => Localizer.Text("hud.cast.target", "Target Cast Bar"),
        PlayerCastBar => Localizer.Text("hud.cast.player", "Player Cast Bar"),
        Focus => Localizer.Text("hud.focus", "Focus Target Frame"),
        EnemyList => Localizer.Text("hud.enemy", "Enemy List"),
        ActionBars => Localizer.Text("hud.actions.legacy", "Combat Action Bars 1–3 (Legacy)"),
        ActionBarOne => Localizer.Text("hud.action.one", "Combat Hotbar 1"),
        ActionBarTwo => Localizer.Text("hud.action.two", "Combat Hotbar 2"),
        ActionBarThree => Localizer.Text("hud.action.three", "Combat Hotbar 3"),
        CrossHotbar => Localizer.Text("hud.cross", "Controller Cross Hotbar"),
        PetBar => Localizer.Text("hud.pet", "Pet Bar"),
        UtilityBars => Localizer.Text("hud.utility", "Utility Hotbar 6 (Compact)"),
        UtilityBarsTwo => Localizer.Text("hud.utility.two", "Utility Hotbar 2 (RE:Frame)"),
        RaidTools => Localizer.Text("hud.raid.tools", "Raid Tools"),
        RaidBuffs => Localizer.Text("hud.raid.buffs", "Raid Buffs"),
        RaidDebuffs => Localizer.Text("hud.raid.debuffs", "Raid Debuffs"),
        RaidersKit => Localizer.Text("hud.raid.consumables", "Raid Consumables"),
        LimitBreak => Localizer.Text("hud.limit", "Limit Break Gauge"),
        CombatHalo => Localizer.Text("hud.halo", "Twin Arc Halo"),
        Greeting => Localizer.Text("hud.greeting", "Greeting"),
        LeisureDock => Localizer.Text("hud.dock", "Adaptive Dock"),
        NativeJobGauge => Localizer.Text("hud.native.job", "Native Job Gauge"),
        NativeStatusEffects => Localizer.Text("hud.native.status", "Native Status Effects"),
        NativeScenarioGuide => Localizer.Text("hud.native.scenario", "Main Scenario Guide"),
        NativeQuestList => Localizer.Text("hud.native.quest", "Quest / FATE List"),
        NativeDutyInfo => Localizer.Text("hud.native.duty", "Duty Information"),
        _ when TryParseAdditionalCombatHotbar(id, out var additionalId) => $"Additional Combat Hotbar {additionalId}",
        _ => id,
    };
}
