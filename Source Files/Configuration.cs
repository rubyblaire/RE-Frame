using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using REFrameXIV.Models;
using REFrameXIV.Theme;

namespace REFrameXIV;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 121;


    public bool SetupWizardCompleted { get; set; }


    public bool TourGuideSeen { get; set; }


    public PluginLanguage LanguagePreference { get; set; } = PluginLanguage.Automatic;


    public UiMode ModeOverride { get; set; } = UiMode.Auto;
    public bool ShowHudOverlay { get; set; } = true;
    public bool ReplaceNativeHud { get; set; } = true;


    public bool OverrideNativeNameplates { get; set; } = false;
    public bool OverrideNativeLocationAndParameter { get; set; } = true;
    public bool OverrideNativeCurrencyAndInventory { get; set; } = true;
    public bool OverrideNativePlayerCastBar { get; set; } = true;
    public bool OverrideNativePartyList { get; set; } = true;
    public bool OverrideNativeAllianceLists { get; set; } = true;
    public bool OverrideNativeTargetInfo { get; set; } = true;
    public bool OverrideNativeFocusTarget { get; set; } = true;
    public bool OverrideNativeEnemyList { get; set; } = true;
    public bool OverrideNativeStatusEffects { get; set; } = true;
    public bool OverrideNativeJobGauges { get; set; } = true;
    public bool OverrideNativeQuestElements { get; set; } = true;
    public bool HideNativeActionBars { get; set; } = true;
    public bool ReplaceNativeCrossHotbar { get; set; } = true;
    public bool EnableHudMouseInteraction { get; set; } = true;
    public bool EnableMouseoverTargeting { get; set; } = true;
    public bool StickyTargeting { get; set; } = false;
    public bool RightClickOpensNativeContextMenu { get; set; } = true;
    public bool SkinNativeContextMenus { get; set; } = false;
    public bool SkinNativeWindows { get; set; } = false;
    public bool NativeWindowGlassEffect { get; set; } = false;
    public float NativeWindowGlassOpacity { get; set; } = 1.0f;
    public bool FrameNativeHoldouts { get; set; } = true;


    public bool FollowJobColors { get; set; } = true;
    public ThemePreset SelectedTheme { get; set; } = ThemePreset.CornflowerSeafoam;
    public Dictionary<string, JobThemeColorOverride> JobThemeColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);


    public bool UseForgeTheme { get; set; }
    public string ActiveForgeThemeId { get; set; } = string.Empty;
    public List<ForgeThemeDefinition> ForgeThemes { get; set; } = new();


    public bool ForgeSquareMinimapEnabled { get; set; }
    public bool ForgeSquareMinimapFollowPlayer { get; set; } = true;
    public float ForgeSquareMinimapOverscan { get; set; } = 1.00f;
    public float ForgeSquareMinimapZoom { get; set; } = 1.0f;


    public string ForgeMembershipToken { get; set; } = string.Empty;


    public string ForgePendingConnectUrl { get; set; } = string.Empty;
    public string ForgeDiscordUserId { get; set; } = string.Empty;
    public string ForgeDiscordDisplayName { get; set; } = string.Empty;
    public bool ForgeMembershipActive { get; set; }
    public ForgeEntitlementTier ForgeMembershipTier { get; set; } = ForgeEntitlementTier.None;
    public DateTime ForgeMembershipLastCheckedUtc { get; set; } = DateTime.MinValue;


    public ForgePremiumSettings ForgePremium { get; set; } = new();
    public bool ReducedMotion { get; set; }
    public bool EnhancedActionHighlights { get; set; } = true;
    public bool EnableGlitchSplashScreen { get; set; } = true;
    public bool EnableAfkScreen { get; set; } = true;
    public int AfkTimeoutMinutes { get; set; } = 5;
    public bool AfkScreenAudioEnabled { get; set; } = true;
    public float AfkScreenVolume { get; set; } = 1.0f;
    public float InterfaceScale { get; set; } = 1.0f;
    public float TextScale { get; set; } = 1.0f;


    public int HudLayoutReferenceWidth { get; set; } = 1920;
    public int HudLayoutReferenceHeight { get; set; } = 1080;


    public bool StickyCombatMode { get; set; }
    public bool StickyQuestMode { get; set; }
    public CooldownDisplayStyle CooldownStyle { get; set; } = CooldownDisplayStyle.FfxivClock;
    public Vector4 CooldownTextColor { get; set; } = new(1f, 1f, 1f, 1f);
    public bool ShowLimitBreakGauge { get; set; } = true;
    public LimitBreakLayout LimitBreakLayout { get; set; } = LimitBreakLayout.Horizontal;
    public TimeDisplayMode ClockMode { get; set; } = TimeDisplayMode.Local;
    public bool ShowLoginGreeting { get; set; } = true;
    public float GreetingVoiceVolume { get; set; } = 0.41f;
    public bool ShowArdynChantButton { get; set; }
    public float ArdynChantVolume { get; set; } = 1.0f;
    public string LastGreetingPeriodKey { get; set; } = string.Empty;
    public int NextMorningGreetingVoiceIndex { get; set; }
    public int NextAfternoonGreetingVoiceIndex { get; set; }
    public int NextEveningGreetingVoiceIndex { get; set; }

    public string ActiveGreetingVoicePackId { get; set; } = "jarvin";
    public List<GreetingVoicePack> CustomGreetingVoicePacks { get; set; } = new();
    public Dictionary<string, GreetingVoiceRotationState> GreetingVoiceRotationStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, HudElementLayout> HudLayouts { get; set; } = new();
    public Dictionary<string, HudModeProfile> HudModeProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, NativeJobGaugePlacement> NativeJobGaugePlacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public NativeJobGaugePlacement NativeStatusEffectsPlacement { get; set; } = new();
    public Dictionary<string, NativeJobGaugePlacement> NativeQuestElementPlacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowHudEditorGrid { get; set; } = true;
    public float HudEditorGridSize { get; set; } = 8f;
    public bool HudEditorSnappingEnabled { get; set; } = false;
    public float HudEditorSnapTolerance { get; set; } = 8f;
    public bool ShowHudEditorScreenBounds { get; set; }
    public bool ShowHudEditorGeneralSafeArea { get; set; }
    public bool ShowHudEditorStreamSafeArea { get; set; }
    public bool ShowHudEditorUltrawideSafeArea { get; set; }
    public bool ShowHudEditorCenterGuides { get; set; } = true;
    public List<LayoutRecoverySnapshot> RecentLayoutHistory { get; set; } = new();


    public float HudEditorToolbarX { get; set; } = 14f / 1920f;
    public float HudEditorToolbarY { get; set; } = 14f / 1080f;
    public float HudEditorPanelX { get; set; } = 1596f / 1920f;
    public float HudEditorPanelY { get; set; } = 86f / 1080f;


    public float BarEditPaletteX { get; set; } = -1f;
    public float BarEditPaletteY { get; set; } = -1f;


    public List<ReframeHotbarKeybind> ReframeHotbarKeybinds { get; set; } = new();

    public bool ShowLocationFrame { get; set; } = true;
    public bool ShowJobRibbon { get; set; } = true;
    public bool ShowPocketRibbon { get; set; } = true;
    public bool ShowMinimapFrame { get; set; } = true;
    public bool ShowChatFrame { get; set; } = true;
    public bool ShowPartyFrames { get; set; } = true;
    public bool ShowAllianceFrames { get; set; } = true;
    public bool ShowAllianceFrameOne { get; set; } = true;
    public bool ShowAllianceFrameTwo { get; set; } = true;
    public bool ShowPlayerFrame { get; set; } = false;
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


    public List<ReframeAdditionalHotbar> AdditionalCombatHotbars { get; set; } = new();
    public uint NextAdditionalCombatHotbarId { get; set; } = 1;

    public bool ShowCrossHotbar { get; set; } = true;
    public bool ShowPetBar { get; set; } = true;
    public bool ShowUtilityBarFrames { get; set; } = true;
    public bool ShowSecondUtilityBarFrames { get; set; }
    public List<ReframeVirtualHotbarSlot> SecondUtilityBarSlots { get; set; } = new();
    public bool ShowRaidTools { get; set; } = true;
    public bool ShowRaidBuffs { get; set; } = true;
    public bool ShowRaidDebuffs { get; set; } = true;
    public bool TransparentRaidBuffBackground { get; set; }
    public bool TransparentRaidDebuffBackground { get; set; }
    public bool ShowRaidersKit { get; set; } = true;
    public string RaidersKitFoodOverride { get; set; } = string.Empty;
    public string RaidersKitPotionOverride { get; set; } = string.Empty;
    public bool ShowStatusEffectsInRaidAndCombat { get; set; } = true;
    public bool ShowCombatHalo { get; set; } = true;
    public bool ShowCombatHaloInRaidReady { get; set; } = true;
    public bool ShowLeisureDock { get; set; } = true;

    public Dictionary<string, List<DockButtonConfig>> DockButtonLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowQuestNativeElements { get; set; } = true;
    public bool HaloFollowsPlayer { get; set; } = false;

    public float HaloRadius { get; set; } = 118f;
    public float HaloThickness { get; set; } = 6f;
    public float HaloVerticalOffset { get; set; } = -20f;
    public float HudOpacity { get; set; } = 1.0f;


    public Dictionary<string, HudPresetData> GeneralHudPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, HudPresetData>> JobHudPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ActiveJobHudPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActiveGeneralHudPreset { get; set; } = string.Empty;
    public bool AutoApplyHudPresets { get; set; } = true;


    public string PenumbraCommand { get; set; } = "/penumbra";
    public string GlamourerCommand { get; set; } = "/glamourer";
    public string LifestreamCommand { get; set; } = "/lifestream";
    public string BoneSmithCommand { get; set; } = "/bonesmith";
    public string ScenekeeperCommand { get; set; } = "/scenekeeper";
    public string CharacterSelectCommand { get; set; } = string.Empty;
    public List<CustomIntegration> CustomIntegrations { get; set; } = new();
    public string RaidWaymarkCommand { get; set; } = "/waymark";
    public string RaidCountdownCommand { get; set; } = "/countdown";
    public string RaidStrategyCommand { get; set; } = "/strategyboard";
}
