using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Player;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LuminaMainCommand = Lumina.Excel.Sheets.MainCommand;
using REFrameXIV.Models;
using REFrameXIV.Localization;
using REFrameXIV.Services;
using REFrameXIV.Theme;
using REFrameXIV.UI;
using REFrameXIV.Windows;

namespace REFrameXIV;

public sealed class Plugin : IDalamudPlugin
{
    internal static Plugin Instance { get; private set; } = null!;
    private const string MainCommand = "/reframe";
    private const string ShortCommand = "/rf";
    private const string RefCommand = "/ref";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;

    public Configuration Configuration { get; }
    public AdaptiveStateService AdaptiveState { get; }
    public CombatTelemetryService CombatTelemetry { get; }
    public NativeHudVisibilityService NativeHudVisibility { get; }
    public ForgeAccessService ForgeAccess { get; }
    public ForgeVaultService ForgeVault { get; }
    public ForgeSceneAutomationService ForgeAutomation { get; }
    public ForgePlusService ForgePlus { get; }
    public ForgeSquareMapService ForgeSquareMap { get; }
    public CrossHotbarStateService CrossHotbarState { get; }
    public AdditionalHotbarService AdditionalHotbars { get; }
    public HotbarInputService HotbarInput { get; }
    public ReframeHotbarKeybindRuntimeService KeybindRuntime { get; }
    public HotbarEditingService HotbarEditing { get; }
    public BarInputDiagnostics BarInputDiagnostics { get; }
    public GearsetService Gearsets { get; }
    public HudTargetingService HudTargeting { get; }
    public StickyTargetingService StickyTargeting { get; }
    public NativeContextMenuStyleService NativeContextMenus { get; }
    public NativeWindowSkinService NativeWindows { get; }
    public PartyConnectionService PartyConnections { get; }
    public AllianceFrameService AllianceFrames { get; }
    public EnemyListService EnemyList { get; }
    public PetBarStateService PetBarState { get; }
    public HudPresetService HudPresets { get; }
    public HudEditHistoryService LayoutHistory { get; }
    public LayoutRecoveryService LayoutRecovery { get; }
    public AfkScreenService AfkScreen { get; }
    public ArdynChantService ArdynChant { get; }
    public WindowSystem WindowSystem { get; } = new("REFrameXIV");

    private readonly MainWindow mainWindow;
    private readonly MainWindow forgeWindow;
    private readonly ConfigWindow configWindow;
    private readonly TourGuideWindow tourGuideWindow;
    private readonly HudOverlayWindow hudOverlayWindow;
    private readonly HotbarInteractionWindow actionBarOneInteractionWindow;
    private readonly HotbarInteractionWindow actionBarTwoInteractionWindow;
    private readonly HotbarInteractionWindow actionBarThreeInteractionWindow;
    private readonly CrossHotbarInteractionWindow crossHotbarInteractionWindow;
    private readonly PetBarInteractionWindow petBarInteractionWindow;
    private readonly HotbarInteractionWindow utilityBarInteractionWindow;
    private readonly HotbarInteractionWindow secondUtilityBarInteractionWindow;
    private readonly List<HotbarInteractionWindow> additionalCombatBarInteractionWindows = new();
    private readonly LeisureDockInteractionWindow leisureDockInteractionWindow;
    private readonly RaidToolsInteractionWindow raidToolsInteractionWindow;
    private readonly ActorInteractionWindow partyInteractionWindow;
    private readonly ActorInteractionWindow enemyListInteractionWindow;
    private readonly ActorInteractionWindow targetInteractionWindow;
    private readonly ActorInteractionWindow focusInteractionWindow;
    private readonly CommandPaletteWindow commandPaletteWindow;
    private readonly SplashWindow splashWindow;
    private readonly LoginGreetingWindow loginGreetingWindow;
    private readonly HudEditorWindow hudEditorWindow;
    private readonly HotbarSlotEditorWindow hotbarSlotEditorWindow;
    private readonly ActionPaletteWindow actionPaletteWindow;
    private readonly HotbarInputLayer hotbarInputLayer;
    private readonly JobSwitcherWindow jobSwitcherWindow;
    private readonly JobRibbonInteractionWindow jobRibbonInteractionWindow;
    private readonly PocketDeckWindow pocketDeckWindow;
    private readonly PocketRibbonInteractionWindow pocketRibbonInteractionWindow;
    private readonly AfkBackdropWindow afkBackdropWindow;
    private readonly ForgeAnimationWheelWindow forgeAnimationWheelWindow;

    private bool wasLoggedIn;
    private bool loginGreetingPending;
    private bool effectiveModeInitialized;
    private bool ardynChantPlayRequested;
    private bool ardynChantStopRequested;
    private UiMode lastEffectiveMode = UiMode.Leisure;
    private bool? barEditPreviousHudOverlayVisibility;
    private bool resumeTourAfterEditorWorkspace;
    private bool additionalHotbarWindowSyncRequested;

    public bool IsHudEditMode { get; private set; }
    public UiMode HudEditPreviewMode { get; private set; } = UiMode.Leisure;
    public UiMode CurrentHudMode => IsHudEditMode
        ? HudModeProfileService.Normalize(HudEditPreviewMode)
        : HudModeProfileService.Normalize(AdaptiveState.EffectiveMode);

    public bool ShouldUseForgeSquareMinimap =>
        ForgeAccess.HasAccess && Configuration.ForgeSquareMinimapEnabled;


    public bool ShouldUseWorkstationDock(UiMode mode)
    {
        mode = HudModeProfileService.Normalize(mode);
        return mode == UiMode.Work ||
               (mode != UiMode.Quest && !IsHudEditMode && AdaptiveState.WorkJobActive);
    }
    public LeisureDockPopup ActiveLeisureDockPopup { get; private set; } = LeisureDockPopup.None;
    public WorkstationDockPopup ActiveWorkstationDockPopup { get; private set; } = WorkstationDockPopup.None;
    public RoleplayDockPopup ActiveRoleplayDockPopup { get; private set; } = RoleplayDockPopup.None;

    private long leisureDockPopupOpenedAtMs;
    private long workstationDockPopupOpenedAtMs;
    private long roleplayDockPopupOpenedAtMs;
    private string cachedWorldName = "Eorzea";
    private DateTime nextWorldNameRefreshUtc = DateTime.MinValue;
    private DateTime nextForgePremiumPollUtc = DateTime.MinValue;
    private Task<ForgeVaultResult>? automaticVaultSnapshotTask;

    public Plugin()
    {
        Instance = this;
        var storedConfiguration = PluginInterface.GetPluginConfig() as Configuration;
        Configuration = storedConfiguration ?? new Configuration();
        Localizer.Initialize(() => Configuration.LanguagePreference);
        GreetingVoicePackService.NormalizeConfiguration(Configuration);
        if (Configuration.Version < 4)
        {
            Configuration.EnableHudMouseInteraction = true;
            Configuration.FrameNativeHoldouts = true;
        }
        HudLayout.EnsureDefaults(Configuration);
        if (Configuration.Version < 49)
        {


            Configuration.NativeStatusEffectsPlacement = new NativeJobGaugePlacement();
            Configuration.Version = 49;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 50)
        {


            var viewportReference = new System.Numerics.Vector2(1920f, 1080f);
            var originReference = System.Numerics.Vector2.Zero;
            var utility = HudLayout.Resolve(Configuration, HudElementIds.UtilityBars, originReference, viewportReference);
            HudLayout.Store(
                Configuration,
                HudElementIds.UtilityBars,
                new HudBounds(utility.Position, new System.Numerics.Vector2(150f, 116f)),
                originReference,
                viewportReference);
            Configuration.Version = 50;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 51)
        {


            Configuration.RaidWaymarkCommand = "/maincommand \"Waymarks\"";
            Configuration.RaidCountdownCommand = "/maincommand \"Countdown\"";
            Configuration.RaidStrategyCommand = "/maincommand \"Strategy Board\"";
            Configuration.NativeStatusEffectsPlacement = new NativeJobGaugePlacement();
            Configuration.Version = 51;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 52)
        {


            Configuration.RaidWaymarkCommand = "/waymark";
            Configuration.RaidCountdownCommand = "/countdown";
            Configuration.RaidStrategyCommand = "/strategyboard";
            Configuration.Version = 52;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 53)
        {


            Configuration.RaidWaymarkCommand = "/waymark";
            Configuration.RaidCountdownCommand = "/countdown";
            Configuration.RaidStrategyCommand = "/strategyboard";
            Configuration.Version = 53;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 54)
        {


            Configuration.Version = 54;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 55)
        {


            Configuration.NativeStatusEffectsPlacement = new NativeJobGaugePlacement();
            Configuration.Version = 55;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 56)
        {


            Configuration.ShowEnemyList = false;
            var viewportReference = new System.Numerics.Vector2(1920f, 1080f);
            var originReference = System.Numerics.Vector2.Zero;
            var targetBounds = HudLayout.Resolve(Configuration, HudElementIds.Target, originReference, viewportReference);
            if (targetBounds.Size.Y < 98f)
            {
                var addedHeight = 98f - targetBounds.Size.Y;
                HudLayout.Store(
                    Configuration,
                    HudElementIds.Target,
                    new HudBounds(
                        targetBounds.Position - new System.Numerics.Vector2(0f, addedHeight),
                        new System.Numerics.Vector2(targetBounds.Size.X, 98f)),
                    originReference,
                    viewportReference);
            }
            Configuration.Version = 56;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 57)
        {


            Configuration.ShowEnemyList = true;
            Configuration.Version = 57;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 58)
        {


            Configuration.EnableMouseoverTargeting = true;
            Configuration.Version = 58;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 59)
        {


            Configuration.EnableHudMouseInteraction = true;
            Configuration.EnableMouseoverTargeting = true;
            Configuration.Version = 59;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 60)
        {


            Configuration.EnableHudMouseInteraction = true;
            Configuration.EnableMouseoverTargeting = true;
            Configuration.Version = 60;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 61)
        {


            Configuration.EnableHudMouseInteraction = true;
            Configuration.EnableMouseoverTargeting = true;
            Configuration.Version = 61;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 62)
        {


            Configuration.EnableHudMouseInteraction = true;
            Configuration.EnableMouseoverTargeting = true;
            Configuration.Version = 62;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 63)
        {


            Configuration.EnableHudMouseInteraction = true;
            Configuration.Version = 63;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 64)
        {


            Configuration.ShowQuestNativeElements = true;
            Configuration.AutoApplyHudPresets = true;
            Configuration.GeneralHudPresets ??= new System.Collections.Generic.Dictionary<string, HudPresetData>(StringComparer.OrdinalIgnoreCase);
            Configuration.JobHudPresets ??= new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, HudPresetData>>(StringComparer.OrdinalIgnoreCase);
            Configuration.ActiveJobHudPresets ??= new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Configuration.Version = 64;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 65)
        {


            Configuration.ShowQuestNativeElements = true;
            Configuration.HudEditorToolbarX = 14f / 1920f;
            Configuration.HudEditorToolbarY = 14f / 1080f;
            Configuration.HudEditorPanelX = 1596f / 1920f;
            Configuration.HudEditorPanelY = 86f / 1080f;
            Configuration.Version = 65;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 66)
        {


            Configuration.ShowQuestNativeElements = true;
            Configuration.Version = 66;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 67)
        {


            Configuration.Version = 67;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 68)
        {


            Configuration.NativeQuestElementPlacements ??= new System.Collections.Generic.Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
            Configuration.Version = 68;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 69)
        {


            Configuration.Version = 69;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 70)
        {


            Configuration.Version = 70;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 71)
        {


            Configuration.Version = 71;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 72)
        {


            Configuration.CharacterSelectCommand ??= string.Empty;
            Configuration.Version = 72;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 73)
        {


            Configuration.CustomIntegrations ??= new System.Collections.Generic.List<CustomIntegration>();
            Configuration.Version = 73;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 74)
        {


            Configuration.ClockMode = TimeDisplayMode.Eorzea;
            Configuration.Version = 74;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 75)
        {


            Configuration.Version = 75;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 76)
        {


            Configuration.ShowLoginGreeting = true;
            Configuration.Version = 76;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 77)
        {


            Configuration.EnableGlitchSplashScreen = true;
            Configuration.Version = 77;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 78)
        {


            Configuration.ShowAllianceFrames = true;
            Configuration.Version = 78;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 79)
        {


            var viewportReference = new System.Numerics.Vector2(1920f, 1080f);
            var originReference = System.Numerics.Vector2.Zero;

            if (Configuration.HudLayouts.TryGetValue(HudElementIds.Alliance, out _))
            {
                var legacy = HudLayout.Resolve(Configuration, HudElementIds.Alliance, originReference, viewportReference);
                var gap = 8f;
                var width = MathF.Max(180f, (legacy.Size.X - gap) * 0.5f);
                HudLayout.Store(Configuration, HudElementIds.AllianceOne,
                    new HudBounds(legacy.Position, new System.Numerics.Vector2(width, legacy.Size.Y)), originReference, viewportReference);
                HudLayout.Store(Configuration, HudElementIds.AllianceTwo,
                    new HudBounds(legacy.Position + new System.Numerics.Vector2(width + gap, 0f), new System.Numerics.Vector2(width, legacy.Size.Y)), originReference, viewportReference);
            }

            if (Configuration.HudLayouts.TryGetValue(HudElementIds.ActionBars, out _))
            {
                var legacy = HudLayout.Resolve(Configuration, HudElementIds.ActionBars, originReference, viewportReference);
                var gap = 3f;
                var height = MathF.Max(32f, (legacy.Size.Y - gap * 2f) / 3f);
                HudLayout.Store(Configuration, HudElementIds.ActionBarThree,
                    new HudBounds(legacy.Position, new System.Numerics.Vector2(legacy.Size.X, height)), originReference, viewportReference);
                HudLayout.Store(Configuration, HudElementIds.ActionBarTwo,
                    new HudBounds(legacy.Position + new System.Numerics.Vector2(0f, height + gap), new System.Numerics.Vector2(legacy.Size.X, height)), originReference, viewportReference);
                HudLayout.Store(Configuration, HudElementIds.ActionBarOne,
                    new HudBounds(legacy.Position + new System.Numerics.Vector2(0f, (height + gap) * 2f), new System.Numerics.Vector2(legacy.Size.X, height)), originReference, viewportReference);
            }

            var target = HudLayout.Resolve(Configuration, HudElementIds.Target, originReference, viewportReference);
            var targetHeight = MathF.Max(58f, target.Size.Y * 0.68f);
            HudLayout.Store(Configuration, HudElementIds.Target,
                new HudBounds(target.Position, new System.Numerics.Vector2(target.Size.X, targetHeight)), originReference, viewportReference);
            var lowerY = target.Position.Y + targetHeight + 6f;
            var lowerGap = 8f;
            var castWidth = MathF.Max(220f, target.Size.X * 0.60f - lowerGap * 0.5f);
            var targetOfTargetWidth = MathF.Max(170f, target.Size.X - castWidth - lowerGap);
            HudLayout.Store(Configuration, HudElementIds.CastBar,
                new HudBounds(new System.Numerics.Vector2(target.Position.X, lowerY), new System.Numerics.Vector2(castWidth, 42f)), originReference, viewportReference);
            HudLayout.Store(Configuration, HudElementIds.TargetOfTarget,
                new HudBounds(new System.Numerics.Vector2(target.Position.X + castWidth + lowerGap, lowerY), new System.Numerics.Vector2(targetOfTargetWidth, 42f)), originReference, viewportReference);

            Configuration.ShowAllianceFrames = true;
            Configuration.ShowAllianceFrameOne = true;
            Configuration.ShowAllianceFrameTwo = true;
            Configuration.ShowTargetOfTargetFrame = true;
            Configuration.ShowCastBar = true;
            Configuration.ShowActionBarOne = true;
            Configuration.ShowActionBarTwo = true;
            Configuration.ShowActionBarThree = true;
            Configuration.HudLayouts.Remove(HudElementIds.Alliance);
            Configuration.HudLayouts.Remove(HudElementIds.ActionBars);
            Configuration.Version = 79;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 80)
        {


            Configuration.LastGreetingPeriodKey ??= string.Empty;
            HudLayout.EnsureDefaults(Configuration);
            Configuration.Version = 80;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 81)
        {


            HudModeProfileService.EnsureCollections(Configuration);
            if (Configuration.StickyCombatMode)
                Configuration.ModeOverride = UiMode.RaidReady;
            else if (Configuration.ModeOverride == UiMode.Combat)
                Configuration.ModeOverride = UiMode.RaidReady;
            Configuration.StickyCombatMode = false;
            Configuration.Version = 81;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 82)
        {


            HudModeProfileService.EnsureCollections(Configuration);
            Configuration.Version = 82;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 83)
        {


            Configuration.BarEditPaletteX = -1f;
            Configuration.BarEditPaletteY = -1f;
            Configuration.Version = 83;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 85)
        {


            Configuration.LastGreetingPeriodKey = string.Empty;


            Configuration.BarEditPaletteX = -1f;
            Configuration.BarEditPaletteY = -1f;
            Configuration.Version = 85;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 86)
        {


            Configuration.LastGreetingPeriodKey = string.Empty;
            Configuration.Version = 86;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 87)
        {


            Configuration.Version = 87;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 88)
        {


            HudModeProfileService.EnsureCollections(Configuration);
            HudModeProfileService.SetVisibility(Configuration, UiMode.Work, HudElementIds.LeisureDock, true);
            Configuration.Version = 88;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 89)
        {


            Configuration.Version = 89;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 90)
        {


            Configuration.ShowPocketRibbon = true;
            HudLayout.EnsureDefaults(Configuration);
            HudModeProfileService.EnsureCollections(Configuration);
            foreach (var mode in HudModeProfileService.EditableModes)
                HudModeProfileService.SetVisibility(Configuration, mode, HudElementIds.PocketRibbon, true);
            Configuration.Version = 90;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 91)
        {


            Configuration.Version = 91;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 92)
        {


            Configuration.ReframeHotbarKeybinds ??= new List<ReframeHotbarKeybind>();
            Configuration.Version = 92;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 93)
        {


            Configuration.ReframeHotbarKeybinds = new List<ReframeHotbarKeybind>();
            Configuration.Version = 93;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 94)
        {


            Configuration.ReframeHotbarKeybinds ??= new List<ReframeHotbarKeybind>();
            Configuration.Version = 94;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 96)
        {


            Configuration.ShowRaidBuffs = true;
            Configuration.ShowRaidDebuffs = true;
            Configuration.ShowRaidersKit = true;
            Configuration.RaidersKitFoodOverride ??= string.Empty;
            Configuration.RaidersKitPotionOverride ??= string.Empty;
            Configuration.Version = 96;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 97)
        {


            Configuration.StickyTargeting = true;
            Configuration.Version = 97;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 5)
        {
            Configuration.Version = 5;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 6)
        {


            var viewportReference = new System.Numerics.Vector2(1920f, 1080f);
            var originReference = System.Numerics.Vector2.Zero;
            var location = HudLayout.Resolve(Configuration, HudElementIds.Location, originReference, viewportReference);
            if (location.Size.Y < 66f)
                HudLayout.Store(Configuration, HudElementIds.Location, new HudBounds(location.Position, new System.Numerics.Vector2(MathF.Max(292f, location.Size.X), 76f)), originReference, viewportReference);
            var dock = HudLayout.Resolve(Configuration, HudElementIds.LeisureDock, originReference, viewportReference);
            if (dock.Size.Y < 42f)
                HudLayout.Store(Configuration, HudElementIds.LeisureDock, new HudBounds(dock.Position, new System.Numerics.Vector2(MathF.Max(420f, dock.Size.X), 48f)), originReference, viewportReference);
            Configuration.SelectedTheme = ThemePreset.CornflowerSeafoam;
            Configuration.ShowCombatHaloInRaidReady = true;
            Configuration.Version = 6;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 7)
        {
            Configuration.EnableMouseoverTargeting = true;
            Configuration.ShowEnemyList = true;
            Configuration.Version = 7;
            PluginInterface.SavePluginConfig(Configuration);
        }
        if (Configuration.Version < 8)
        {


            Configuration.RightClickOpensNativeContextMenu = true;
            Configuration.Version = 8;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 9)
        {
            Configuration.SkinNativeContextMenus = true;
            Configuration.Version = 9;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 10)
        {


            Configuration.SkinNativeContextMenus = false;
            Configuration.Version = 10;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 11)
        {


            Configuration.SkinNativeContextMenus = true;
            Configuration.Version = 11;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 12)
        {


            Configuration.SkinNativeContextMenus = true;
            Configuration.Version = 12;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 13)
        {


            Configuration.SkinNativeWindows = true;
            Configuration.Version = 13;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 14)
        {


            Configuration.NativeWindowGlassEffect = true;
            Configuration.NativeWindowGlassOpacity = 0.88f;
            Configuration.Version = 14;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 15)
        {


            Configuration.Version = 15;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 16)
        {
            Configuration.Version = 16;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 18)
        {
            Configuration.NativeWindowGlassEffect = false;
            Configuration.NativeWindowGlassOpacity = 1f;
            Configuration.SkinNativeWindows = true;
            Configuration.Version = 18;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 19)
        {


            Configuration.SkinNativeWindows = true;
            Configuration.Version = 19;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 20)
        {


            Configuration.NativeWindowGlassEffect = true;
            if (Configuration.NativeWindowGlassOpacity < 0.20f || Configuration.NativeWindowGlassOpacity > 1f)
                Configuration.NativeWindowGlassOpacity = 0.82f;
            Configuration.Version = 20;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 21)
        {


            Configuration.Version = 21;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 22)
        {


            Configuration.Version = 22;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 23)
        {


            Configuration.Version = 23;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 24)
        {


            Configuration.Version = 24;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 25)
        {


            Configuration.Version = 25;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 26)
        {


            Configuration.Version = 26;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 27)
        {


            Configuration.Version = 27;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 28)
        {


            Configuration.Version = 28;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 29)
        {


            Configuration.Version = 29;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 30)
        {


            Configuration.Version = 30;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 31)
        {


            Configuration.Version = 31;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 32)
        {


            Configuration.Version = 32;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 33)
        {


            Configuration.Version = 33;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 34)
        {


            Configuration.Version = 34;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 35)
        {


            if (string.IsNullOrWhiteSpace(Configuration.LifestreamCommand) ||
                string.Equals(Configuration.LifestreamCommand.Trim(), "/lifestream", StringComparison.OrdinalIgnoreCase))
            {
                Configuration.LifestreamCommand = "/li";
            }

            Configuration.Version = 35;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 36)
        {


            if (string.IsNullOrWhiteSpace(Configuration.LifestreamCommand) ||
                string.Equals(Configuration.LifestreamCommand.Trim(), "/li", StringComparison.OrdinalIgnoreCase))
            {
                Configuration.LifestreamCommand = "/lifestream";
            }

            Configuration.Version = 36;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 37)
        {


            Configuration.Version = 37;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 38)
        {


            Configuration.Version = 38;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 39)
        {


            Configuration.JobThemeColors ??= new System.Collections.Generic.Dictionary<string, JobThemeColorOverride>(StringComparer.OrdinalIgnoreCase);
            Configuration.Version = 39;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 40)
        {


            Configuration.Version = 40;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 41)
        {


            Configuration.Version = 41;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 42)
        {


            Configuration.Version = 42;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 43)
        {


            Configuration.NativeJobGaugePlacements ??= new System.Collections.Generic.Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
            Configuration.NativeStatusEffectsPlacement ??= new NativeJobGaugePlacement();
            Configuration.Version = 43;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 44)
        {


            Configuration.Version = 44;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 45)
        {


            Configuration.Version = 45;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 46)
        {


            Configuration.Version = 46;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 47)
        {


            Configuration.ShowRaidTools = true;
            Configuration.ShowStatusEffectsInRaidAndCombat = true;
            Configuration.Version = 47;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 48)
        {


            Configuration.ShowPetBar = true;
            Configuration.Version = 48;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 100)
        {


            Configuration.SetupWizardCompleted = true;
            Configuration.TourGuideSeen = false;
            if (string.IsNullOrWhiteSpace(Configuration.ScenekeeperCommand))
                Configuration.ScenekeeperCommand = "/scenekeeper";
            HudModeProfileService.EnsureCollections(Configuration);
            Configuration.Version = 100;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 101)
        {


            Configuration.NativeJobGaugePlacements ??= new System.Collections.Generic.Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
            foreach (var placement in Configuration.NativeJobGaugePlacements.Values)
            {
                if (placement is not null)
                    placement.Components ??= new System.Collections.Generic.Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
            }
            Configuration.Version = 101;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 102)
        {


            Configuration.NativeQuestElementPlacements ??= new System.Collections.Generic.Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
            Configuration.Version = 102;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 103)
        {


            Configuration.ActionBarOneColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarOneColumns);
            Configuration.ActionBarTwoColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarTwoColumns);
            Configuration.ActionBarThreeColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarThreeColumns);
            Configuration.Version = 103;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 104)
        {


            Configuration.AdditionalCombatHotbars ??= new List<ReframeAdditionalHotbar>();
            Configuration.SecondUtilityBarSlots ??= new List<ReframeVirtualHotbarSlot>();
            Configuration.NextAdditionalCombatHotbarId = Math.Max(1u, Configuration.NextAdditionalCombatHotbarId);
            Configuration.ShowSecondUtilityBarFrames = false;
            Configuration.Version = 104;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 105)
        {


            Configuration.PetBarColumns = HotbarGridLayouts.NormalizeColumns(Configuration.PetBarColumns);
            Configuration.Version = 105;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 106)
        {


            HudModeProfileService.EnsureCollections(Configuration);
            var roleplayProfile = HudModeProfileService.GetOrCreate(Configuration, UiMode.Roleplay);
            roleplayProfile.ElementVisibility[HudElementIds.UtilityBars] = true;
            roleplayProfile.ElementVisibility[HudElementIds.UtilityBarsTwo] = true;
            Configuration.Version = 106;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 107)
        {


            HudModeProfileService.EnsureCollections(Configuration);
            var roleplayProfile = HudModeProfileService.GetOrCreate(Configuration, UiMode.Roleplay);
            roleplayProfile.ElementVisibility[HudElementIds.ActionBarOne] = true;
            roleplayProfile.ElementVisibility[HudElementIds.ActionBarTwo] = true;
            roleplayProfile.ElementVisibility[HudElementIds.ActionBarThree] = true;
            foreach (var bar in Configuration.AdditionalCombatHotbars ?? new List<ReframeAdditionalHotbar>())
                roleplayProfile.ElementVisibility[bar.ElementId] = true;
            Configuration.Version = 107;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 108)
        {


            Configuration.ForgeThemes ??= new List<ForgeThemeDefinition>();
            Configuration.ActiveForgeThemeId ??= string.Empty;
            Configuration.UseForgeTheme = false;
            Configuration.Version = 108;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 109)
        {


            Configuration.ForgeSquareMinimapEnabled = false;
            Configuration.ForgeSquareMinimapFollowPlayer = true;
            Configuration.ForgeSquareMinimapOverscan = 1.02f;
            Configuration.Version = 109;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 110)
        {


            Configuration.ForgeThemes ??= new List<ForgeThemeDefinition>();
            foreach (var forgeTheme in Configuration.ForgeThemes)
                forgeTheme?.Normalize();
            Configuration.Version = 110;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 111)
        {


            Configuration.ForgeSquareMinimapZoom = 6.0f;
            HudLayout.EnsureDefaults(Configuration);
            Configuration.Version = 111;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 112)
        {


            Configuration.ForgeSquareMinimapZoom = 1.0f;
            Configuration.ForgeSquareMinimapOverscan = 1.0f;
            Configuration.ForgeMembershipToken ??= string.Empty;
            Configuration.ForgeDiscordUserId ??= string.Empty;
            Configuration.ForgeDiscordDisplayName ??= string.Empty;
            Configuration.ForgeMembershipActive = false;
            Configuration.ForgeMembershipLastCheckedUtc = DateTime.MinValue;
            Configuration.Version = 112;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 113)
        {


            Configuration.ForgePendingConnectUrl ??= string.Empty;
            if (!Configuration.ForgeMembershipActive &&
                string.IsNullOrWhiteSpace(Configuration.ForgeDiscordUserId) &&
                string.IsNullOrWhiteSpace(Configuration.ForgePendingConnectUrl))
            {
                Configuration.ForgeMembershipToken = string.Empty;
            }
            Configuration.Version = 113;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 114)
        {


            Configuration.ForgePremium ??= new ForgePremiumSettings();
            Configuration.ForgePremium.EnsureValid();
            Configuration.Version = 114;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 115)
        {


            HudModeProfileService.RemoveRetiredMapWidgets(Configuration);
            Configuration.Version = 115;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 116)
        {


            Configuration.TransparentRaidBuffBackground = false;
            Configuration.TransparentRaidDebuffBackground = false;
            Configuration.Version = 116;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 117)
        {


            Configuration.ActionBarOneColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarOneColumns);
            Configuration.ActionBarTwoColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarTwoColumns);
            Configuration.ActionBarThreeColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarThreeColumns);
            Configuration.PetBarColumns = HotbarGridLayouts.NormalizeColumns(Configuration.PetBarColumns);
            Configuration.Version = 117;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 118)
        {


            HudLayout.UpgradeStatusPanelsForDisplay(
                Configuration,
                new Vector2(
                    Math.Max(1, Configuration.HudLayoutReferenceWidth),
                    Math.Max(1, Configuration.HudLayoutReferenceHeight)));
            Configuration.Version = 118;
            PluginInterface.SavePluginConfig(Configuration);
        }

        if (Configuration.Version < 119)
        {


            Configuration.ForgePremium ??= new ForgePremiumSettings();
            Configuration.ForgePremium.AnimationLibraryEntries ??= new List<ForgeAnimationEntry>();
            Configuration.ForgePremium.AnimationLibraryRootPath ??= string.Empty;
            Configuration.ForgePremium.AnimationLibraryShowMature = false;
            Configuration.Version = 119;
            PluginInterface.SavePluginConfig(Configuration);
        }

        Configuration.JobThemeColors ??= new System.Collections.Generic.Dictionary<string, JobThemeColorOverride>(StringComparer.OrdinalIgnoreCase);
        ForgeThemeLibrary.Ensure(Configuration);
        Configuration.ForgePremium ??= new ForgePremiumSettings();
        Configuration.ForgePremium.EnsureValid();
        Configuration.HudLayoutReferenceWidth = Math.Max(1, Configuration.HudLayoutReferenceWidth);
        Configuration.HudLayoutReferenceHeight = Math.Max(1, Configuration.HudLayoutReferenceHeight);
        Configuration.ActionBarOneColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarOneColumns);
        Configuration.ActionBarTwoColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarTwoColumns);
        Configuration.ActionBarThreeColumns = HotbarGridLayouts.NormalizeColumns(Configuration.ActionBarThreeColumns);
        Configuration.PetBarColumns = HotbarGridLayouts.NormalizeColumns(Configuration.PetBarColumns);
        HudModeProfileService.EnsureCollections(Configuration);
        Configuration.NativeJobGaugePlacements ??= new System.Collections.Generic.Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in Configuration.NativeJobGaugePlacements.Values)
        {
            if (placement is not null)
                placement.Components ??= new System.Collections.Generic.Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        }
        Configuration.NativeStatusEffectsPlacement ??= new NativeJobGaugePlacement();
        Configuration.CustomIntegrations ??= new System.Collections.Generic.List<CustomIntegration>();
        for (var i = Configuration.CustomIntegrations.Count - 1; i >= 0; i--)
        {
            var integration = Configuration.CustomIntegrations[i];
            if (integration is null)
            {
                Configuration.CustomIntegrations.RemoveAt(i);
                continue;
            }

            integration.EnsureValid();
        }

        AdaptiveState = new AdaptiveStateService(Configuration, Condition, ClientState, PlayerState);
        CombatTelemetry = new CombatTelemetryService(Framework, ObjectTable, TargetManager, Condition);
        CrossHotbarState = new CrossHotbarStateService(GameGui, GameConfig);
        AdditionalHotbars = new AdditionalHotbarService(Configuration);
        BarInputDiagnostics = new BarInputDiagnostics();
        HotbarEditing = new HotbarEditingService(BarInputDiagnostics);

        if (Configuration.Version < 120)
        {
            foreach (var animation in Configuration.ForgePremium.AnimationLibraryEntries)
            {
                animation.Category = ForgeAnimationCategory.Unsorted;
                animation.CategoryAutoAssigned = false;
                animation.CategoryReason = "Category system updated; rescan or auto-categorize this mod.";
                animation.Mature = false;
            }
            Configuration.Version = 120;
            SaveConfiguration();
        }
        if (Configuration.Version < 121)
        {
            Configuration.ForgePremium ??= new ForgePremiumSettings();
            Configuration.ForgePremium.EnsureValid();
            if (Configuration.ForgeMembershipActive && Configuration.ForgeMembershipTier == ForgeEntitlementTier.None)
                Configuration.ForgeMembershipTier = ForgeEntitlementTier.Forge;
            Configuration.Version = 121;
            SaveConfiguration();
        }
        ForgeAccess = new ForgeAccessService(Configuration, SaveConfiguration, Log);
        ForgeAutomation = new ForgeSceneAutomationService(Configuration, SaveConfiguration);
        ForgePlus = new ForgePlusService(this);
        NativeHudVisibility = new NativeHudVisibilityService(
            Configuration,
            Framework,
            AddonLifecycle,
            GameGui,
            ClientState,
            PlayerState,
            AdaptiveState,
            CrossHotbarState,
            () => IsHudEditMode,
            () => HotbarEditing.IsEnabled,
            () => ShouldUseForgeSquareMinimap,
            Log);
        ForgeSquareMap = new ForgeSquareMapService(
            Configuration,
            Framework,
            GameGui,
            ClientState,
            ObjectTable,
            DataManager,
            () => ForgeAccess.HasAccess,
            () => IsHudElementVisible(HudElementIds.Minimap, CurrentHudMode),
            Log);
        Gearsets = new GearsetService(DataManager, PlayerState);
        NativeContextMenus = new NativeContextMenuStyleService(Configuration, Framework, AddonLifecycle, GameGui, Log);
        NativeWindows = new NativeWindowSkinService(Configuration, Framework, AddonLifecycle, GameGui, Log);
        PartyConnections = new PartyConnectionService(ChatGui, Framework, PartyList, Log);
        AllianceFrames = new AllianceFrameService(PartyList, ObjectTable, Log);
        HudTargeting = new HudTargetingService(Framework, TargetManager, NativeContextMenus);
        StickyTargeting = new StickyTargetingService(Configuration, TargetManager);
        HotbarInput = new HotbarInputService(HudTargeting, BarInputDiagnostics);
        KeybindRuntime = new ReframeHotbarKeybindRuntimeService(Configuration, HotbarInput, AdditionalHotbars, BarInputDiagnostics);
        EnemyList = new EnemyListService(ObjectTable, TargetManager);
        PetBarState = new PetBarStateService(GameGui, ObjectTable);
        LayoutHistory = new HudEditHistoryService(this);
        LayoutRecovery = new LayoutRecoveryService(this);
        ForgeVault = new ForgeVaultService(this);
        HudPresets = new HudPresetService(this, Framework);
        AfkScreen = new AfkScreenService(Configuration, ObjectTable, Log);
        ArdynChant = new ArdynChantService(Log);

        commandPaletteWindow = new CommandPaletteWindow(this);
        mainWindow = new MainWindow(this);
        forgeWindow = new MainWindow(this, forgeStandalone: true);
        configWindow = new ConfigWindow(this);
        tourGuideWindow = new TourGuideWindow(this);
        hudOverlayWindow = new HudOverlayWindow(this);
        actionBarOneInteractionWindow = new HotbarInteractionWindow(this, 0u);
        actionBarTwoInteractionWindow = new HotbarInteractionWindow(this, 1u);
        actionBarThreeInteractionWindow = new HotbarInteractionWindow(this, 2u);
        crossHotbarInteractionWindow = new CrossHotbarInteractionWindow(this);
        petBarInteractionWindow = new PetBarInteractionWindow(this);
        utilityBarInteractionWindow = new HotbarInteractionWindow(this);
        secondUtilityBarInteractionWindow = new HotbarInteractionWindow(this, true);
        leisureDockInteractionWindow = new LeisureDockInteractionWindow(this);
        raidToolsInteractionWindow = new RaidToolsInteractionWindow(this);
        partyInteractionWindow = new ActorInteractionWindow(this, ActorWidgetKind.Party);
        enemyListInteractionWindow = new ActorInteractionWindow(this, ActorWidgetKind.EnemyList);
        targetInteractionWindow = new ActorInteractionWindow(this, ActorWidgetKind.Target);
        focusInteractionWindow = new ActorInteractionWindow(this, ActorWidgetKind.Focus);
        splashWindow = new SplashWindow(this);
        loginGreetingWindow = new LoginGreetingWindow(this);
        hudEditorWindow = new HudEditorWindow(this);
        hotbarSlotEditorWindow = new HotbarSlotEditorWindow(this);
        actionPaletteWindow = new ActionPaletteWindow(this);
        hotbarInputLayer = new HotbarInputLayer(this);
        jobSwitcherWindow = new JobSwitcherWindow(this);
        jobRibbonInteractionWindow = new JobRibbonInteractionWindow(this);
        pocketDeckWindow = new PocketDeckWindow(this);
        pocketRibbonInteractionWindow = new PocketRibbonInteractionWindow(this);
        afkBackdropWindow = new AfkBackdropWindow(this, AfkScreen);
        forgeAnimationWheelWindow = new ForgeAnimationWheelWindow(this);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(forgeWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(tourGuideWindow);
        WindowSystem.AddWindow(hudOverlayWindow);
        WindowSystem.AddWindow(actionBarOneInteractionWindow);
        WindowSystem.AddWindow(actionBarTwoInteractionWindow);
        WindowSystem.AddWindow(actionBarThreeInteractionWindow);
        WindowSystem.AddWindow(crossHotbarInteractionWindow);
        WindowSystem.AddWindow(petBarInteractionWindow);
        WindowSystem.AddWindow(utilityBarInteractionWindow);
        WindowSystem.AddWindow(secondUtilityBarInteractionWindow);
        WindowSystem.AddWindow(leisureDockInteractionWindow);
        WindowSystem.AddWindow(raidToolsInteractionWindow);
        WindowSystem.AddWindow(partyInteractionWindow);
        WindowSystem.AddWindow(enemyListInteractionWindow);
        WindowSystem.AddWindow(targetInteractionWindow);
        WindowSystem.AddWindow(focusInteractionWindow);
        WindowSystem.AddWindow(commandPaletteWindow);
        WindowSystem.AddWindow(jobRibbonInteractionWindow);
        WindowSystem.AddWindow(jobSwitcherWindow);
        WindowSystem.AddWindow(pocketRibbonInteractionWindow);
        WindowSystem.AddWindow(pocketDeckWindow);
        WindowSystem.AddWindow(hudEditorWindow);
        WindowSystem.AddWindow(hotbarSlotEditorWindow);
        WindowSystem.AddWindow(actionPaletteWindow);
        WindowSystem.AddWindow(loginGreetingWindow);
        WindowSystem.AddWindow(forgeAnimationWheelWindow);


        WindowSystem.AddWindow(afkBackdropWindow);
        WindowSystem.AddWindow(splashWindow);
        SyncAdditionalHotbarWindows();

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            ShowInHelp = false,
        });
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand) { ShowInHelp = false });
        CommandManager.AddHandler(RefCommand, new CommandInfo(OnCommand) { ShowInHelp = false });


        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;


        PluginInterface.UiBuilder.Draw += DrawHotbarInputLayer;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;


        wasLoggedIn = ClientState.IsLoggedIn;
        effectiveModeInitialized = false;
        Framework.Update += OnFrameworkUpdate;

        hudOverlayWindow.IsOpen = Configuration.ShowHudOverlay;


        actionBarOneInteractionWindow.IsOpen = true;
        actionBarTwoInteractionWindow.IsOpen = true;
        actionBarThreeInteractionWindow.IsOpen = true;
        crossHotbarInteractionWindow.IsOpen = true;
        petBarInteractionWindow.IsOpen = true;
        utilityBarInteractionWindow.IsOpen = true;
        secondUtilityBarInteractionWindow.IsOpen = true;


        leisureDockInteractionWindow.IsOpen = false;


        raidToolsInteractionWindow.IsOpen = false;


        partyInteractionWindow.IsOpen = false;
        enemyListInteractionWindow.IsOpen = false;
        targetInteractionWindow.IsOpen = false;
        focusInteractionWindow.IsOpen = false;
        if (!Configuration.TourGuideSeen)
        {


            tourGuideWindow.OpenTour(markSeen: true);
        }


        PlayGlitchScreen();
        Log.Information("RE:Frame XIV v0.5.2.3 loaded — the complete RE:Forge+ creative suite is available, with Cloud Vault and sync preserved for base RE:Forge members.");
    }

    private void DrawHotbarInputLayer()
    {


    }

    public ThemePalette CurrentTheme
    {
        get
        {
            var forgeTheme = ForgeThemeLibrary.GetActive(Configuration);
            if (forgeTheme is null)
            {


                var builtIn = JobThemeProvider.Get(GetJobAbbreviation(), Configuration.FollowJobColors, Configuration.SelectedTheme, Configuration.JobThemeColors);
                return builtIn with
                {
                    Panel = new System.Numerics.Vector4(builtIn.Panel.X, builtIn.Panel.Y, builtIn.Panel.Z, 1f),
                    PanelAlt = new System.Numerics.Vector4(builtIn.PanelAlt.X, builtIn.PanelAlt.Y, builtIn.PanelAlt.Z, 1f),
                };
            }

            var palette = forgeTheme.ToPalette();
            return Configuration.FollowJobColors
                ? JobThemeProvider.ApplyJobColors(palette, GetJobAbbreviation(), Configuration.JobThemeColors)
                : palette;
        }
    }

    public ForgeStyleSettings CurrentThemeStyle =>
        ForgeThemeLibrary.GetActive(Configuration)?.Style ?? ForgeStyleSettings.Default;


    public HudCanvasInfo GetRenderedHudCanvas()
    {
        if (hudOverlayWindow.TryGetRenderedCanvas(out var rendered))
            return rendered;


        return HudCanvas.Current();
    }

    public string GetJobAbbreviation()
    {
        if (!PlayerState.IsLoaded || !PlayerState.ClassJob.IsValid)
            return "XIV";
        var abbreviation = PlayerState.ClassJob.Value.Abbreviation.ToString();
        return string.IsNullOrWhiteSpace(abbreviation) ? "XIV" : abbreviation;
    }

    public int GetLevel() => PlayerState.IsLoaded ? PlayerState.Level : 0;

    public string GetHudResourceLabel(UiMode mode)
    {
        if (HudModeProfileService.Normalize(mode) != UiMode.Work)
            return "MP";

        var job = GetJobAbbreviation().Trim().ToUpperInvariant();
        return job switch
        {
            "CRP" or "BSM" or "ARM" or "GSM" or "LTW" or "WVR" or "ALC" or "CUL" => "CP",
            "MIN" or "BTN" or "FSH" => "GP",
            _ when AdaptiveState.CraftingActivityActive => "CP",
            _ when AdaptiveState.GatheringActivityActive => "GP",
            _ => "MP",
        };
    }

    public (int Current, int Maximum, string Label) GetHudResourceValues(UiMode mode)
    {
        var label = GetHudResourceLabel(mode);
        var player = ObjectTable.LocalPlayer;

        if (label == "CP")
        {
            var maximum = PlayerState.IsLoaded
                ? Math.Max(0, PlayerState.GetAttribute(PlayerAttribute.CraftingPoints))
                : 0;
            var current = maximum;


            if (player is not null && maximum > 0 && player.CurrentMp > 0 && player.CurrentMp <= (uint)maximum)
                current = (int)player.CurrentMp;

            return (current, maximum, label);
        }

        if (label == "GP")
        {
            var maximum = PlayerState.IsLoaded
                ? Math.Max(0, PlayerState.GetAttribute(PlayerAttribute.GatheringPoints))
                : 0;
            var current = maximum;
            if (player is not null && maximum > 0 && player.CurrentMp > 0 && player.CurrentMp <= (uint)maximum)
                current = (int)player.CurrentMp;

            return (current, maximum, label);
        }

        return player is null
            ? (0, 0, label)
            : ((int)player.CurrentMp, (int)player.MaxMp, label);
    }

    public string GetLocationName()
    {
        if (!ClientState.IsLoggedIn)
            return "Not logged in";
        if (DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().TryGetRow(ClientState.TerritoryType, out var row))
        {
            var name = row.PlaceName.Value.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return "Eorzea";
    }

    public string GetCurrentWorldName()
    {
        if (DateTime.UtcNow < nextWorldNameRefreshUtc)
            return cachedWorldName;

        nextWorldNameRefreshUtc = DateTime.UtcNow.AddSeconds(2);
        try
        {
            var player = ObjectTable.LocalPlayer;
            if (player is null)
                return cachedWorldName;


            cachedWorldName = ReadWorldName(player, "CurrentWorld")
                ?? ReadWorldName(player, "HomeWorld")
                ?? cachedWorldName;
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "RE:Frame could not resolve the current world name.");
        }

        return cachedWorldName;
    }

    public string GetClockTimeLabel()
        => Configuration.ClockMode switch
        {
            TimeDisplayMode.Server => $"ST {DateTime.UtcNow.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
            TimeDisplayMode.Local => $"LT {DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
            _ => GetEorzeaTimeLabel(),
        };

    public void CycleClockMode()
    {
        Configuration.ClockMode = Configuration.ClockMode switch
        {
            TimeDisplayMode.Eorzea => TimeDisplayMode.Server,
            TimeDisplayMode.Server => TimeDisplayMode.Local,
            _ => TimeDisplayMode.Eorzea,
        };
        SaveConfiguration();
    }

    public void PreviewLoginGreeting()
    {


        if (AfkScreen.IsActive)
        {
            AfkScreen.Stop();
            afkBackdropWindow.IsOpen = false;
        }

        ardynChantPlayRequested = false;
        ardynChantStopRequested = false;
        ArdynChant.Stop();
        loginGreetingPending = false;
        loginGreetingWindow.Preview(DateTime.Now);
    }


    public void PlayArdynChant()
    {
        if (!Configuration.ShowArdynChantButton)
            return;

        ardynChantStopRequested = false;
        ardynChantPlayRequested = true;
    }

    public void StopArdynChant()
    {
        ardynChantPlayRequested = false;
        ardynChantStopRequested = true;
    }

    private void ProcessArdynChantRequests()
    {
        if (ardynChantStopRequested)
        {
            ardynChantStopRequested = false;
            ArdynChant.Stop();
        }

        if (!ardynChantPlayRequested)
            return;

        ardynChantPlayRequested = false;
        if (!Configuration.ShowArdynChantButton)
            return;


        if (AfkScreen.IsActive)
        {
            AfkScreen.Stop();
            afkBackdropWindow.IsOpen = false;
        }

        loginGreetingPending = false;
        if (loginGreetingWindow.IsPlaying)
            loginGreetingWindow.Cancel();
        else
            GreetingVoiceService.Stop();

        if (!ArdynChant.Play(Configuration.ArdynChantVolume))
            ChatGui.PrintError("RE:Frame could not play Ardyn's Chant. Check the plugin log for details.");
    }

    public void ToggleAfkScreenPreview()
    {
        if (AfkScreen.IsActive)
            AfkScreen.Stop();
        else
        {
            ardynChantPlayRequested = false;
            ardynChantStopRequested = false;
            ArdynChant.Stop();
            AfkScreen.Preview();
        }

        afkBackdropWindow.IsOpen = AfkScreen.IsActive;
    }

    private static bool IsInsideQuietHours(int hour, int startHour, int endHour)
    {
        hour = Math.Clamp(hour, 0, 23);
        startHour = Math.Clamp(startHour, 0, 23);
        endHour = Math.Clamp(endHour, 0, 23);
        return startHour < endHour
            ? hour >= startHour && hour < endHour
            : hour >= startHour || hour < endHour;
    }

    private bool TryPlayLeisureGreeting(DateTime localTime)
    {
        if (!IsHudElementVisible(HudElementIds.Greeting, UiMode.Leisure) || AdaptiveState.EffectiveMode != UiMode.Leisure)
            return false;

        if (ForgeAccess.HasAccess)
        {
            var director = Configuration.ForgePremium;
            director.EnsureValid();
            if (!director.VoiceDirectorEnabled)
                return false;
            if (director.VoiceQuietHoursEnabled && IsInsideQuietHours(localTime.Hour, director.VoiceQuietHoursStart, director.VoiceQuietHoursEnd))
                return false;
            if (director.VoiceCooldownMinutes > 0 &&
                DateTime.UtcNow - director.VoiceLastPlayedUtc < TimeSpan.FromMinutes(director.VoiceCooldownMinutes))
                return false;

            if (director.VoicePackByMode.TryGetValue(nameof(UiMode.Leisure), out var directedPack) &&
                !string.IsNullOrWhiteSpace(directedPack))
            {
                Configuration.ActiveGreetingVoicePackId = directedPack;
                GreetingVoicePackService.NormalizeConfiguration(Configuration);
            }
        }


        if (ardynChantPlayRequested || ArdynChant.IsPlaying)
            return false;

        if (HotbarEditing.IsEnabled)
        {
            loginGreetingPending = true;
            return false;
        }

        var periodKey = LoginGreetingWindow.ResolvePeriodKey(localTime);
        if (string.Equals(Configuration.LastGreetingPeriodKey, periodKey, StringComparison.Ordinal))
            return false;

        Configuration.LastGreetingPeriodKey = periodKey;
        if (ForgeAccess.HasAccess)
            Configuration.ForgePremium.VoiceLastPlayedUtc = DateTime.UtcNow;
        SaveConfiguration();
        loginGreetingWindow.Play(localTime);
        return true;
    }

    private void ProcessForgePremiumServices()
    {
        var now = DateTime.UtcNow;
        if (now < nextForgePremiumPollUtc)
            return;
        nextForgePremiumPollUtc = now.AddSeconds(4);

        var changed = ForgeAutomation.Update(
            ForgeAccess.HasAccess,
            GetJobAbbreviation(),
            GetLocationName(),
            Condition[ConditionFlag.InCombat],
            PartyList.Length,
            DateTime.Now.Hour);

        var detected = DisplayResolutionService.Detect(new Vector2(
            Math.Max(1, Configuration.HudLayoutReferenceWidth),
            Math.Max(1, Configuration.HudLayoutReferenceHeight)));
        changed |= ForgeAutomation.ApplyDisplayAutoSwitch(
            ForgeAccess.HasAccess,
            detected.ClientWidth,
            detected.ClientHeight);

        if (changed)
        {
            NativeHudVisibility.RefreshNow();
            ForgeSquareMap.Rebuild();
        }

        if (automaticVaultSnapshotTask is { IsCompleted: true })
        {
            try
            {
                var result = automaticVaultSnapshotTask.GetAwaiter().GetResult();
                if (!result.Success && !result.Message.Contains("not due", StringComparison.OrdinalIgnoreCase))
                    Log.Verbose("RE:Forge automatic Cloud Vault snapshot skipped: {Message}", result.Message);
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "RE:Forge automatic Cloud Vault snapshot task ended unexpectedly.");
            }
            automaticVaultSnapshotTask = null;
        }

        if (automaticVaultSnapshotTask is null &&
            ForgeAccess.HasAccess &&
            Configuration.ForgePremium.VaultAutomaticSnapshots &&
            now - Configuration.ForgePremium.VaultLastAutomaticSnapshotUtc >= TimeSpan.FromHours(20))
        {
            automaticVaultSnapshotTask = ForgeVault.RunAutomaticSnapshotAsync();
        }
    }

    public void RefreshAfkPresentationAssets()
    {
        AfkScreen.ApplyConfigurationChange();
        afkBackdropWindow.ReloadScene();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (additionalHotbarWindowSyncRequested)
        {
            additionalHotbarWindowSyncRequested = false;
            SyncAdditionalHotbarWindows();
        }

        var afkEligible = ClientState.IsLoggedIn &&
                          ObjectTable.LocalPlayer is not null &&
                          !ClientState.IsGPosing &&
                          !GameGui.GameUiHidden &&
                          !Condition[ConditionFlag.InCombat] &&
                          !Condition[ConditionFlag.WatchingCutscene] &&
                          !Condition[ConditionFlag.OccupiedInCutSceneEvent] &&
                          !NativeWindows.HasProtectedDutyWindowOpen &&
                          !splashWindow.IsPlaying &&
                          !IsHudEditMode &&
                          !HotbarEditing.IsEnabled;
        AfkScreen.Update(afkEligible);
        afkBackdropWindow.IsOpen = AfkScreen.IsActive;

        if (!ClientState.IsLoggedIn)
        {
            ardynChantPlayRequested = false;
            ardynChantStopRequested = false;
            ArdynChant.Stop();
            CloseLeisureDockPopup();
            CloseWorkstationDockPopup();
            CloseRoleplayDockPopup();
            CloseJobSwitcher();
            ClosePocketDeck();
            Gearsets.ResetSwitchGuard();
            KeybindRuntime.ResetLatch();
            StickyTargeting.ResetLatch();
            wasLoggedIn = false;
            loginGreetingPending = false;
            effectiveModeInitialized = false;
            ForgeAutomation.ResetSession();
            ForgePlus.ResetSession();
            if (loginGreetingWindow.IsPlaying)
                loginGreetingWindow.Cancel();
            return;
        }

        ProcessForgePremiumServices();
        ForgePlus.Update(
            ForgeAccess.HasPlusAccess,
            loggedIn: true,
            blocked: IsHudEditMode || HotbarEditing.IsEnabled || AfkScreen.IsActive || ClientState.IsGPosing || GameGui.GameUiHidden || tourGuideWindow.IsOpen,
            inCombat: Condition[ConditionFlag.InCombat],
            territoryName: GetLocationName());
        ProcessArdynChantRequests();

        if (!wasLoggedIn)
        {
            wasLoggedIn = true;
            loginGreetingPending = IsHudElementVisible(HudElementIds.Greeting, UiMode.Leisure);
            effectiveModeInitialized = false;
        }

        var keybindRuntimeBlocked =
            HotbarEditing.IsEnabled ||
            IsHudEditMode ||
            AfkScreen.IsActive ||
            ObjectTable.LocalPlayer is null ||
            ClientState.IsGPosing ||
            GameGui.GameUiHidden ||
            splashWindow.IsPlaying ||
            tourGuideWindow.IsOpen;


        var nativeKeybindMutationReady =
            ObjectTable.LocalPlayer is not null &&
            !ClientState.IsGPosing &&
            !GameGui.GameUiHidden &&
            !splashWindow.IsPlaying;
        if (nativeKeybindMutationReady)
            NativeHotbarKeybindService.ProcessPendingNativeMutations();


        NativeHotbarKeybindService.ProcessCaptureInput();


        KeybindRuntime.Process(keybindRuntimeBlocked);

        var stickyTargetingBlocked =
            keybindRuntimeBlocked ||
            mainWindow.IsOpen ||
            forgeWindow.IsOpen ||
            configWindow.IsOpen ||
            commandPaletteWindow.IsOpen ||
            hotbarSlotEditorWindow.IsOpen ||
            actionPaletteWindow.IsOpen;
        StickyTargeting.Process(stickyTargetingBlocked);


        if (ObjectTable.LocalPlayer is null || ClientState.IsGPosing || GameGui.GameUiHidden || splashWindow.IsPlaying)
            return;

        var currentMode = AdaptiveState.EffectiveMode;
        var returnedToLeisure = effectiveModeInitialized &&
                                lastEffectiveMode != UiMode.Leisure &&
                                currentMode == UiMode.Leisure;
        var firstReadyLeisureFrame = !effectiveModeInitialized && currentMode == UiMode.Leisure;

        var dockModeSupported = (currentMode is UiMode.Leisure or UiMode.Roleplay or UiMode.Quest or UiMode.Work) || ShouldUseWorkstationDock(currentMode);
        var dockVisible = dockModeSupported &&
                          Configuration.ShowHudOverlay &&
                          IsHudElementVisible(HudElementIds.LeisureDock, currentMode);
        var workstationDockActive = dockVisible && ShouldUseWorkstationDock(currentMode);

        if (currentMode != UiMode.Leisure)
        {
            if (loginGreetingWindow.IsPlaying && !loginGreetingWindow.IsManualPreview)
                loginGreetingWindow.Cancel();
        }

        var roleplayDockActive = dockVisible && currentMode == UiMode.Roleplay && !workstationDockActive;

        if (!dockVisible || workstationDockActive || roleplayDockActive)
            CloseLeisureDockPopup();

        if (!dockVisible || !workstationDockActive)
            CloseWorkstationDockPopup();

        if (!roleplayDockActive)
            CloseRoleplayDockPopup();

        if (loginGreetingPending)
        {
            if (currentMode == UiMode.Leisure && !HotbarEditing.IsEnabled)
            {
                loginGreetingPending = false;
                TryPlayLeisureGreeting(DateTime.Now);
            }
        }
        else if (returnedToLeisure || firstReadyLeisureFrame)
        {
            TryPlayLeisureGreeting(DateTime.Now);
        }

        lastEffectiveMode = currentMode;
        effectiveModeInitialized = true;
    }

    public static (int Hour, int Minute, float DayProgress) GetEorzeaTime()
    {
        const double eorzeaScale = 3600d / 175d;
        var eorzeaSeconds = (long)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() * eorzeaScale);
        var secondsInDay = ((eorzeaSeconds % 86400L) + 86400L) % 86400L;
        var hour = (int)(secondsInDay / 3600L);
        var minute = (int)((secondsInDay % 3600L) / 60L);
        return (hour, minute, secondsInDay / 86400f);
    }

    public static string GetEorzeaTimeLabel()
    {
        var (hour, minute, _) = GetEorzeaTime();
        var time = DateTime.Today.AddHours(hour).AddMinutes(minute);
        return $"ET {time.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }

    private static string? ReadWorldName(object player, string propertyName)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public;
        var rowReference = player.GetType().GetProperty(propertyName, flags)?.GetValue(player);
        if (rowReference is null)
            return null;

        var value = rowReference.GetType().GetProperty("Value", flags)?.GetValue(rowReference);
        if (value is null)
            return null;

        var name = value.GetType().GetProperty("Name", flags)?.GetValue(value)?.ToString();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public float GetLeisureDockPopupBlend()
    {
        if (ActiveLeisureDockPopup == LeisureDockPopup.None)
            return 0f;
        if (Configuration.ReducedMotion)
            return 1f;

        var elapsedMs = Math.Max(0L, Environment.TickCount64 - leisureDockPopupOpenedAtMs);
        var t = Math.Clamp(elapsedMs / 180f, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public void ToggleLeisureDockPopup(LeisureDockPopup popup)
    {
        if (popup == LeisureDockPopup.None || ActiveLeisureDockPopup == popup)
        {
            CloseLeisureDockPopup();
            return;
        }

        CloseWorkstationDockPopup();
        CloseRoleplayDockPopup();
        CloseJobSwitcher();
        ClosePocketDeck();
        ActiveLeisureDockPopup = popup;
        leisureDockPopupOpenedAtMs = Environment.TickCount64;
    }

    public void CloseLeisureDockPopup()
    {
        ActiveLeisureDockPopup = LeisureDockPopup.None;
        leisureDockPopupOpenedAtMs = 0L;
    }

    public float GetWorkstationDockPopupBlend()
    {
        if (ActiveWorkstationDockPopup == WorkstationDockPopup.None)
            return 0f;
        if (Configuration.ReducedMotion)
            return 1f;

        var elapsedMs = Math.Max(0L, Environment.TickCount64 - workstationDockPopupOpenedAtMs);
        var t = Math.Clamp(elapsedMs / 180f, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public void ToggleWorkstationDockPopup(WorkstationDockPopup popup)
    {
        if (popup == WorkstationDockPopup.None || ActiveWorkstationDockPopup == popup)
        {
            CloseWorkstationDockPopup();
            return;
        }

        CloseLeisureDockPopup();
        CloseRoleplayDockPopup();
        CloseJobSwitcher();
        ClosePocketDeck();
        ActiveWorkstationDockPopup = popup;
        workstationDockPopupOpenedAtMs = Environment.TickCount64;
    }

    public void CloseWorkstationDockPopup()
    {
        ActiveWorkstationDockPopup = WorkstationDockPopup.None;
        workstationDockPopupOpenedAtMs = 0L;
    }


    public float GetRoleplayDockPopupBlend()
    {
        if (ActiveRoleplayDockPopup == RoleplayDockPopup.None)
            return 0f;
        if (Configuration.ReducedMotion)
            return 1f;

        var elapsed = Math.Max(0L, Environment.TickCount64 - roleplayDockPopupOpenedAtMs);
        var t = Math.Clamp(elapsed / 150f, 0f, 1f);
        return 1f - MathF.Pow(1f - t, 3f);
    }

    public void ToggleRoleplayDockPopup(RoleplayDockPopup popup)
    {
        if (popup == RoleplayDockPopup.None || ActiveRoleplayDockPopup == popup)
        {
            CloseRoleplayDockPopup();
            return;
        }

        CloseLeisureDockPopup();
        CloseWorkstationDockPopup();
        CloseJobSwitcher();
        ClosePocketDeck();
        ActiveRoleplayDockPopup = popup;
        roleplayDockPopupOpenedAtMs = Environment.TickCount64;
    }

    public void CloseRoleplayDockPopup()
    {
        ActiveRoleplayDockPopup = RoleplayDockPopup.None;
        roleplayDockPopupOpenedAtMs = 0L;
    }

    public bool OpenExternalResource(string url, string label)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
            (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
        {
            Log.Warning("RE:Frame refused to open invalid {ResourceLabel} URL: {ResourceUrl}", label, url);
            ChatGui.PrintError($"RE:Frame could not open {label}: the link was invalid.");
            return false;
        }

        try
        {


            Dalamud.Utility.Util.OpenLink(target.AbsoluteUri);
            return true;
        }
        catch (Exception dalamudException)
        {
            try
            {

                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target.AbsoluteUri,
                    UseShellExecute = true,
                });
                if (process is not null)
                    return true;
            }
            catch (Exception shellException)
            {
                Log.Warning(shellException, "RE:Frame Windows-shell fallback could not open {ResourceLabel}: {ResourceUrl}", label, url);
            }

            Log.Warning(dalamudException, "RE:Frame could not open {ResourceLabel}: {ResourceUrl}", label, url);
            ChatGui.PrintError($"RE:Frame could not open {label}. Copy the link from The Forge and open it manually.");
            return false;
        }
    }

    public void OpenTeleportWindow()
        => OpenNativeMainCommandWindow(
            "Teleport",
            "/teleport",
            "Teleport",
            "Teleportation",
            "Téléportation",
            "テレポ");

    public void OpenQuestLogWindow()
        => OpenNativeMainCommandWindow(
            "Quest Log",
            "/journal",
            "Journal",
            "Quest Log",
            "Auftragsbuch",
            "Journal des quêtes",
            "ジャーナル");

    public void OpenDutyFinderWindow()
        => OpenNativeMainCommandWindow(
            "Duty Finder",
            "/dutyfinder",
            "Duty Finder",
            "Inhaltssuche",
            "Outil de mission",
            "コンテンツファインダー");

    private void OpenNativeMainCommandWindow(string displayName, string fallbackCommand, params string[] liveNames)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<LuminaMainCommand>();
            foreach (var row in sheet)
            {
                var name = row.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name) ||
                    !liveNames.Any(candidate => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (NativeMainCommandService.TryOpen(row.RowId))
                    return;
                break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RE:Frame could not resolve FFXIV's native {WindowName} main command.", displayName);
        }


        if (NativeChatCommandService.TryExecute(fallbackCommand))
            return;

        ChatGui.PrintError($"RE:Frame could not open FFXIV's native {displayName} window.");
    }

    public void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);

    public void SetMode(UiMode mode)
    {
        CloseLeisureDockPopup();
        CloseWorkstationDockPopup();
        CloseRoleplayDockPopup();
        CloseJobSwitcher();
        ClosePocketDeck();
        var wasHidden = !Configuration.ShowHudOverlay;
        Configuration.ModeOverride = mode == UiMode.Auto
            ? UiMode.Auto
            : HudModeProfileService.Normalize(mode);
        Configuration.ShowHudOverlay = true;
        hudOverlayWindow.IsOpen = true;
        SaveConfiguration();
        if (wasHidden)
            PlayGlitchScreen();
    }

    public void SetHudVisible(bool visible)
    {
        if (!visible)
        {
            CloseLeisureDockPopup();
            CloseWorkstationDockPopup();
            CloseRoleplayDockPopup();
            CloseJobSwitcher();
            ClosePocketDeck();
        }
        Configuration.ShowHudOverlay = visible;
        hudOverlayWindow.IsOpen = visible;
        if (!visible)
            SetHudEditMode(false);
        SaveConfiguration();
        NativeHudVisibility.RefreshNow();
        if (visible)
            PlayGlitchScreen();
    }

    public void SetNativeReplacement(bool enabled)
    {
        Configuration.ReplaceNativeHud = enabled;
        if (enabled)
        {
            Configuration.ShowHudOverlay = true;
            hudOverlayWindow.IsOpen = true;
        }
        SaveConfiguration();
        NativeHudVisibility.RefreshNow();
        if (enabled)
            PlayGlitchScreen();
    }

    public void ToggleMainUi()
    {
        if (mainWindow.IsOpen)
        {
            mainWindow.IsOpen = false;
            splashWindow.Cancel();
            return;
        }

        OpenMainUi();
    }

    public void OpenMainUi()
    {
        forgeWindow.IsOpen = false;
        if (mainWindow.IsOpen)
        {
            mainWindow.BringToFront();
            return;
        }


        PlayGlitchScreen(() =>
        {
            mainWindow.IsOpen = true;
            mainWindow.BringToFront();
        });
    }

    public void OpenForgeWindow()
    {
        mainWindow.IsOpen = false;
        configWindow.IsOpen = false;
        if (forgeWindow.IsOpen)
        {
            forgeWindow.ShowForgeOverviewPage();
            return;
        }

        PlayGlitchScreen(forgeWindow.ShowForgeOverviewPage);
    }

    public void ToggleConfigUi() => configWindow.Toggle();

    public void OpenTourGuide()
    {
        resumeTourAfterEditorWorkspace = false;
        tourGuideWindow.OpenTour();
    }


    public void OpenHudEditorFromTour()
    {
        resumeTourAfterEditorWorkspace = tourGuideWindow.IsOpen;
        SetHudEditMode(true);
    }

    private void ResumeTourAfterEditorWorkspace()
    {
        if (!resumeTourAfterEditorWorkspace || IsHudEditMode || HotbarEditing.IsEnabled)
            return;

        resumeTourAfterEditorWorkspace = false;
        tourGuideWindow.ResumeTour();
    }


    public void OpenSetupWizard() => OpenTourGuide();

    public void OpenIntegrationsPage()
    {
        forgeWindow.IsOpen = false;
        if (mainWindow.IsOpen)
        {
            mainWindow.ShowIntegrationsPage();
            return;
        }

        PlayGlitchScreen(mainWindow.ShowIntegrationsPage);
    }

    public void OpenForgeImmersionPage()
    {
        mainWindow.IsOpen = false;
        configWindow.IsOpen = false;
        if (forgeWindow.IsOpen)
        {
            forgeWindow.ShowForgeImmersionPage();
            return;
        }

        PlayGlitchScreen(forgeWindow.ShowForgeImmersionPage);
    }

    public void OpenCommandPalette() => commandPaletteWindow.OpenPalette();

    public bool IsJobSwitcherOpen => jobSwitcherWindow.IsOpen;
    public bool IsPocketDeckOpen => pocketDeckWindow.IsOpen;

    public bool IsPointInsideJobSwitcher(Vector2 point)
        => jobSwitcherWindow.ContainsScreenPoint(point);

    public bool IsPointInsidePocketDeck(Vector2 point)
        => pocketDeckWindow.ContainsScreenPoint(point);

    public void ToggleJobSwitcher()
    {
        CloseLeisureDockPopup();
        CloseWorkstationDockPopup();
        CloseRoleplayDockPopup();
        ClosePocketDeck();
        jobSwitcherWindow.ToggleOpenState();
    }

    public void CloseJobSwitcher()
        => jobSwitcherWindow.Close();

    public void TogglePocketDeck()
    {
        CloseLeisureDockPopup();
        CloseWorkstationDockPopup();
        CloseRoleplayDockPopup();
        CloseJobSwitcher();
        pocketDeckWindow.ToggleOpenState();
    }

    public void ClosePocketDeck()
        => pocketDeckWindow.Close();

    public void OpenPocketMounts()
        => OpenPocketNativeMenu(
            "Mount Guide",
            new[] { "Mount Guide", "Mounts" },
            "/mountguide");

    public void OpenPocketMinions()
        => OpenPocketNativeMenu(
            "Minion Guide",
            new[] { "Minion Guide", "Minions" },
            "/minionguide");

    public void OpenPocketHuntBills()
        => OpenPocketNativeMenu(
            "Key Items",
            new[] { "Key Items", "Key Item Inventory", "Key Items Inventory" },
            "/keyitem");

    public void UsePocketDig()
    {
        if (!NativeChatCommandService.TryExecute("/ac \"Dig\""))
            ChatGui.PrintError("RE:Frame could not use FFXIV's native Dig action.");
    }

    private void OpenPocketNativeMenu(
        string label,
        IReadOnlyList<string> candidateNames,
        string fallbackCommand)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<LuminaMainCommand>();
            foreach (var row in sheet)
            {
                var name = row.Name.ToString().Trim();
                foreach (var candidate in candidateNames)
                {
                    if (!string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (NativeMainCommandService.TryOpen(row.RowId))
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RE:Frame could not resolve FFXIV's native {PocketLabel} main command.", label);
        }

        if (NativeChatCommandService.TryExecute(fallbackCommand))
            return;

        ChatGui.PrintError($"RE:Frame could not open FFXIV's native {label} window.");
    }

    public void OpenSupport()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://discord.gg/Dr836dmbqh",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RE:Frame could not open the support Discord link.");
            ChatGui.PrintError("RE:Frame could not open the support link. Discord invite: discord.gg/Dr836dmbqh");
        }
    }

    public void SetGlitchSplashEnabled(bool enabled)
    {
        Configuration.EnableGlitchSplashScreen = enabled;
        if (!enabled)
            splashWindow.CompleteImmediately();
        SaveConfiguration();
    }

    public void PlayGlitchScreen(Action? onCompleted = null)
    {
        var callback = onCompleted ?? (() => { });
        if (!Configuration.EnableGlitchSplashScreen)
        {
            splashWindow.Cancel();
            callback();
            return;
        }

        splashWindow.Play(callback);
    }

    public void OpenMainUiImmediate()
    {
        forgeWindow.IsOpen = false;
        PlayGlitchScreen(() =>
        {
            mainWindow.IsOpen = true;
            mainWindow.BringToFront();
        });
    }

    public void ActivateReFrameUi()
    {
        Configuration.ShowHudOverlay = true;
        Configuration.ReplaceNativeHud = true;
        hudOverlayWindow.IsOpen = true;
        SaveConfiguration();
        NativeHudVisibility.RefreshNow();
        PlayGlitchScreen();
        ChatGui.Print("RE:Frame UI activated.");
    }

    public void RefreshReFrame()
    {
        Configuration.ShowHudOverlay = true;
        hudOverlayWindow.IsOpen = true;
        HudLayout.EnsureDefaults(Configuration);
        if (Configuration.Version < 49)
        {


            Configuration.NativeStatusEffectsPlacement = new NativeJobGaugePlacement();
            Configuration.Version = 49;
            PluginInterface.SavePluginConfig(Configuration);
        }
        SaveConfiguration();
        NativeHudVisibility.RefreshNow();
        PlayGlitchScreen(() => TryPlayLeisureGreeting(DateTime.Now));
        ChatGui.Print("RE:Frame refreshed.");
    }

    public void FitHudToViewport(Vector2 viewportSize)
    {
        LayoutRecovery.Create("Before Resolution Fit");
        LayoutHistory.Clear();
        viewportSize = Vector2.Max(viewportSize, Vector2.One);
        var previousViewport = new Vector2(
            Math.Max(1, Configuration.HudLayoutReferenceWidth),
            Math.Max(1, Configuration.HudLayoutReferenceHeight));

        HudLayout.RefitAllLayouts(Configuration, previousViewport, viewportSize);

        Configuration.HudLayoutReferenceWidth = Math.Max(1, (int)MathF.Round(viewportSize.X));
        Configuration.HudLayoutReferenceHeight = Math.Max(1, (int)MathF.Round(viewportSize.Y));


        Configuration.InterfaceScale = 1f;
        SaveConfiguration();
        NativeHudVisibility.RefreshNow();
        ChatGui.Print($"RE:Frame fitted the HUD to {Configuration.HudLayoutReferenceWidth} × {Configuration.HudLayoutReferenceHeight}; Buffs and Debuffs were scaled for the display while other elements preserved their practical pixel sizes.");
    }

    public void SetHudEditMode(bool enabled, bool resumeTourWhenClosed = true)
    {


        if (enabled && HotbarEditing.IsEnabled)
            SetBarEditMode(false, false, false);

        if (enabled)
        {
            tourGuideWindow.IsOpen = false;
            CloseLeisureDockPopup();
            CloseWorkstationDockPopup();
            CloseRoleplayDockPopup();
            CloseJobSwitcher();
            ClosePocketDeck();
            HudEditPreviewMode = HudModeProfileService.Normalize(AdaptiveState.EffectiveMode);
        }
        else
        {
            LayoutHistory.Clear();
        }
        var wasHidden = !Configuration.ShowHudOverlay;
        IsHudEditMode = enabled;
        hudEditorWindow.IsOpen = enabled;
        if (enabled)
        {
            Configuration.ShowHudOverlay = true;
            hudOverlayWindow.IsOpen = true;
            hudEditorWindow.BringToFront();
        }
        SaveConfiguration();
        if (enabled && wasHidden)
            PlayGlitchScreen(() => hudEditorWindow.BringToFront());
        if (!enabled && resumeTourWhenClosed)
            ResumeTourAfterEditorWorkspace();
    }


    public void OpenSkillEdit() => SetBarEditMode(true);

    public void SetBarEditMode(bool enabled, bool announce = true, bool resumeTourWhenClosed = true)
    {
        if (enabled == HotbarEditing.IsEnabled)
            return;

        if (enabled)
        {
            tourGuideWindow.IsOpen = false;


            if (IsHudEditMode)
                SetHudEditMode(false, false);

            CloseLeisureDockPopup();
            CloseWorkstationDockPopup();
            CloseRoleplayDockPopup();
            CloseJobSwitcher();
            ClosePocketDeck();
            AfkScreen.Stop();
            afkBackdropWindow.IsOpen = false;
            commandPaletteWindow.IsOpen = false;
            mainWindow.IsOpen = false;
            forgeWindow.IsOpen = false;
            configWindow.IsOpen = false;


            barEditPreviousHudOverlayVisibility ??= Configuration.ShowHudOverlay;
            Configuration.ShowHudOverlay = true;
            hudOverlayWindow.IsOpen = true;

            if (CrossHotbarState.TryGetState(out var liveCrossHotbar))
                HotbarEditing.SetCrossHotbarSet(liveCrossHotbar.SetNumber);
        }

        HotbarEditing.SetEnabled(enabled);
        if (enabled)
        {
            BarInputDiagnostics.ResetForEditSession();
        }
        else
        {
            hotbarSlotEditorWindow.ResetTransientState();
            if (barEditPreviousHudOverlayVisibility is { } previousHudVisibility)
            {
                Configuration.ShowHudOverlay = previousHudVisibility;
                hudOverlayWindow.IsOpen = previousHudVisibility;
                barEditPreviousHudOverlayVisibility = null;
            }
        }


        hotbarSlotEditorWindow.IsOpen = enabled;
        actionPaletteWindow.IsOpen = false;
        actionBarOneInteractionWindow.IsOpen = !enabled;
        actionBarTwoInteractionWindow.IsOpen = !enabled;
        actionBarThreeInteractionWindow.IsOpen = !enabled;
        utilityBarInteractionWindow.IsOpen = !enabled;
        secondUtilityBarInteractionWindow.IsOpen = !enabled;
        foreach (var window in additionalCombatBarInteractionWindows)
            window.IsOpen = !enabled;
        crossHotbarInteractionWindow.IsOpen = !enabled;

        NativeHudVisibility.RefreshNow();

        if (!enabled && resumeTourWhenClosed)
            ResumeTourAfterEditorWorkspace();

        if (!announce)
            return;

        ChatGui.Print(enabled
            ? CrossHotbarState.IsControllerUser
                ? "RE:Frame isolated XHB + Pet + Utility editor opened. Use the Hotbar Palette to place actions, emotes, mounts, minions, or macros on supported utility slots; use Keybinds for the Pet Bar and utility slots, then run /ref bars again to lock."
                : "RE:Frame isolated hotbar editor opened. Page through every combat bar, the Pet Bar in Keybinds, and both utility bars; actions, emotes, mounts, minions, macros, drag/drop, and keybinds work on supported native and overflow slots."
            : "RE:Frame bars locked; your normal HUD has been restored.");
    }

    public bool IsPointInsideActionPalette(Vector2 point)
        => hotbarSlotEditorWindow.IsPointInsidePalette(point);

    public void ToggleBarEditMode() => SetBarEditMode(!HotbarEditing.IsEnabled);

    public void OpenKeybindEditor()
    {
        SetBarEditMode(true, false);
        hotbarSlotEditorWindow.OpenKeybindMode();
        ChatGui.Print("RE:Frame keybind editor opened. Click any combat, Pet Bar, or utility slot and press its new key. Esc cancels; Delete clears.");
    }


    public void SetHotbarSlotEditMode(bool enabled) => SetBarEditMode(enabled);

    public void SetHudEditPreviewMode(UiMode mode)
    {
        HudEditPreviewMode = HudModeProfileService.Normalize(mode);
        NativeHudVisibility.RefreshNow();
    }

    public void ToggleHudEditMode() => SetHudEditMode(!IsHudEditMode);

    public bool IsHudElementVisible(string id, UiMode? mode = null)
    {
        var shared = GetSharedHudElementVisibility(id);
        var resolvedMode = mode ?? CurrentHudMode;
        return HudModeProfileService.ResolveVisibility(Configuration, resolvedMode, id, shared);
    }

    private bool GetSharedHudElementVisibility(string id) => id switch
    {
        HudElementIds.Location => Configuration.ShowLocationFrame,
        HudElementIds.JobRibbon => Configuration.ShowJobRibbon,
        HudElementIds.PocketRibbon => Configuration.ShowPocketRibbon,
        HudElementIds.Minimap => Configuration.ShowMinimapFrame,
        HudElementIds.ForgeCoordinates
            => ShouldUseForgeSquareMinimap && Configuration.ShowMinimapFrame,
        HudElementIds.Chat => Configuration.ShowChatFrame,
        HudElementIds.Party => Configuration.ShowPartyFrames,
        HudElementIds.AllianceOne => Configuration.ShowAllianceFrames && Configuration.ShowAllianceFrameOne,
        HudElementIds.AllianceTwo => Configuration.ShowAllianceFrames && Configuration.ShowAllianceFrameTwo,
        HudElementIds.Player => Configuration.ShowPlayerFrame,
        HudElementIds.Target => Configuration.ShowTargetFrame,
        HudElementIds.TargetOfTarget => Configuration.ShowTargetOfTargetFrame,
        HudElementIds.CastBar => Configuration.ShowCastBar,
        HudElementIds.PlayerCastBar => Configuration.ShowPlayerCastBar,
        HudElementIds.Focus => Configuration.ShowFocusFrame,
        HudElementIds.EnemyList => Configuration.ShowEnemyList,
        HudElementIds.ActionBarOne => Configuration.ShowActionBarFrames && Configuration.ShowActionBarOne,
        HudElementIds.ActionBarTwo => Configuration.ShowActionBarFrames && Configuration.ShowActionBarTwo,
        HudElementIds.ActionBarThree => Configuration.ShowActionBarFrames && Configuration.ShowActionBarThree,
        HudElementIds.CrossHotbar => Configuration.ShowCrossHotbar,
        HudElementIds.PetBar => Configuration.ShowPetBar,
        HudElementIds.UtilityBars => Configuration.ShowUtilityBarFrames,
        HudElementIds.UtilityBarsTwo => Configuration.ShowSecondUtilityBarFrames,
        HudElementIds.RaidTools => Configuration.ShowRaidTools,
        HudElementIds.RaidBuffs => Configuration.ShowRaidBuffs,
        HudElementIds.RaidDebuffs => Configuration.ShowRaidDebuffs,
        HudElementIds.RaidersKit => Configuration.ShowRaidersKit,
        HudElementIds.LimitBreak => Configuration.ShowLimitBreakGauge,
        HudElementIds.CombatHalo => Configuration.ShowCombatHalo,
        HudElementIds.Greeting => Configuration.ShowLoginGreeting,
        HudElementIds.LeisureDock => Configuration.ShowLeisureDock,
        _ => AdditionalHotbars.GetSharedVisibility(id, true),
    };

    public IEnumerable<string> GetEditableHudElementIds()
    {
        foreach (var id in HudElementIds.All)
            yield return id;
        yield return HudElementIds.UtilityBarsTwo;
        foreach (var id in AdditionalHotbars.ElementIds)
            yield return id;
    }

    public string GetHudElementLabel(string id)
        => AdditionalHotbars.GetElementLabel(id);

    public bool ShouldExposeHudElement(string id) => id switch
    {
        HudElementIds.CrossHotbar => Configuration.ReplaceNativeCrossHotbar,
        HudElementIds.ForgeCoordinates
            => ShouldUseForgeSquareMinimap,
        _ => true,
    };

    public void SetHudElementVisible(string id, bool visible, UiMode? mode = null)
    {
        var resolvedMode = mode ?? CurrentHudMode;
        HudModeProfileService.SetVisibility(Configuration, resolvedMode, id, visible);
        if (id == HudElementIds.Greeting && !visible && resolvedMode == UiMode.Leisure)
            loginGreetingWindow.Cancel();
        if (id == HudElementIds.LeisureDock && !visible && resolvedMode == UiMode.Leisure)
            CloseLeisureDockPopup();
        if (id == HudElementIds.LeisureDock && !visible && resolvedMode == UiMode.Work)
            CloseWorkstationDockPopup();
        if (id == HudElementIds.LeisureDock && !visible && resolvedMode == UiMode.Roleplay)
            CloseRoleplayDockPopup();
        if (id == HudElementIds.JobRibbon && !visible)
            CloseJobSwitcher();
        if (id == HudElementIds.PocketRibbon && !visible)
            ClosePocketDeck();
        SaveConfiguration();
        NativeHudVisibility.RefreshNow();
    }

    public void RequestAdditionalHotbarWindowSync()
        => additionalHotbarWindowSyncRequested = true;

    public void SyncAdditionalHotbarWindows()
    {
        foreach (var window in additionalCombatBarInteractionWindows)
        {
            window.IsOpen = false;
            WindowSystem.RemoveWindow(window);
            window.Dispose();
        }
        additionalCombatBarInteractionWindows.Clear();

        foreach (var bar in AdditionalHotbars.CombatBars)
        {
            var window = new HotbarInteractionWindow(this, bar)
            {
                IsOpen = !HotbarEditing.IsEnabled,
            };
            additionalCombatBarInteractionWindows.Add(window);
            WindowSystem.AddWindow(window);
        }
    }

    public void RestoreFinalFantasyUi()
    {
        CloseLeisureDockPopup();
        CloseWorkstationDockPopup();
        CloseRoleplayDockPopup();
        CloseJobSwitcher();
        ClosePocketDeck();
        SetHudEditMode(false);
        Configuration.ReplaceNativeHud = false;
        Configuration.ShowHudOverlay = false;
        hudOverlayWindow.IsOpen = false;
        SaveConfiguration();
        NativeHudVisibility.RestoreAll();
        ChatGui.Print("Final Fantasy XIV UI restored. Use /reframe command to reopen Command Center.");
    }

    public void EquipSavedJob(SavedJobGearset gearset)
    {
        if (Gearsets.TryEquip(gearset, out var message))
            ChatGui.Print(message);
        else
            ChatGui.PrintError(message);
    }

    public bool EquipJobGearset(JobGearsetOverview gearset)
    {
        if (Gearsets.TryEquip(gearset, out var message))
        {
            ChatGui.Print(message);
            return true;
        }

        ChatGui.PrintError(message);
        return false;
    }

    public void ReportIntegratedNativeUi()
    {
        var visible = NativeHudVisibility.GetVisibleIntegratedHoldouts();
        if (visible.Count == 0)
        {
            ChatGui.Print("RE:Frame did not detect any known integrated native holdouts currently visible.");
            return;
        }

        ChatGui.Print($"RE:Frame is currently integrating these native systems: {string.Join(", ", visible)}. They remain native until their full replacements are functional.");
    }

    public void ToggleForgeAnimationWheel()
    {
        if (!ForgeAccess.HasPlusAccess)
        {
            ChatGui.Print("RE:Forge+ is required for the Animation Wheel.");
            OpenForgeWindow();
            return;
        }
        forgeAnimationWheelWindow.ToggleOpenState();
    }

    public bool TryExecuteForgeCommand(string? command)
    {
        var normalized = command?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        var commandName = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (CommandManager.Commands.ContainsKey(commandName))
        {
            CommandManager.ProcessCommand(normalized);
            return true;
        }

        return NativeChatCommandService.TryExecute(normalized);
    }

    public void RunIntegrationCommand(string command, string displayName)
    {
        if (TryRunIntegrationCommand(command))
            return;

        ChatGui.Print($"RE:Frame could not find the {displayName} command. You can edit it on the Integrations page.");
        OpenIntegrationsPage();
    }

    public void OpenScenekeeper()
        => RunIntegrationCommand(Configuration.ScenekeeperCommand, "Scenekeeper");

    public void ExecuteDockCommand(string command, string label)
    {
        var normalized = (command ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "/")
        {
            ChatGui.PrintError($"RE:Frame dock button '{label}' does not have a command configured.");
            return;
        }
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        if (!TryRunIntegrationCommand(normalized))
            ChatGui.PrintError($"RE:Frame could not find the command for '{label}': {normalized}");
    }

    public void SetRoleplayChatMode(string channelCommand, string channelLabel)
    {
        if (NativeChatCommandService.TryExecute(channelCommand))
            return;

        ChatGui.PrintError($"RE:Frame could not switch chat to {channelLabel}.");
    }

    public void OpenAppearanceWorkspace()
    {
        var penumbraOpened = TryRunIntegrationCommand(Configuration.PenumbraCommand);
        var glamourerOpened = TryRunIntegrationCommand(Configuration.GlamourerCommand);

        if (penumbraOpened && glamourerOpened)
            return;

        var missing = !penumbraOpened && !glamourerOpened
            ? "Penumbra and Glamourer"
            : !penumbraOpened ? "Penumbra" : "Glamourer";

        ChatGui.Print($"RE:Frame opened the available Appearance tools, but could not find {missing}. Check the slash commands on the RE:Frame Integrations page.");

        if (!penumbraOpened && !glamourerOpened)
            OpenIntegrationsPage();
    }

    public string ResolveCharacterSelectCommand()
    {
        var configured = Configuration.CharacterSelectCommand?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredRoot = configured.Split(' ')[0];
            if (CommandManager.Commands.ContainsKey(configuredRoot))
                return configured;
        }

        string bestMatch = string.Empty;
        var bestScore = 0;
        foreach (var commandName in CommandManager.Commands.Keys)
        {
            var score = ScoreCharacterSelectCommand(commandName);
            if (score <= bestScore)
                continue;

            bestMatch = commandName;
            bestScore = score;
        }

        return bestMatch;
    }

    public void OpenCharacterSelect()
    {
        var command = ResolveCharacterSelectCommand();
        if (TryRunIntegrationCommand(command))
            return;

        ChatGui.Print("RE:Frame could not detect Character Select's registered command. Enter its slash command on the RE:Frame Integrations page.");
        OpenIntegrationsPage();
    }

    private static int ScoreCharacterSelectCommand(string commandName)
    {
        Span<char> buffer = stackalloc char[commandName.Length];
        var length = 0;
        foreach (var character in commandName)
        {
            if (char.IsLetterOrDigit(character))
                buffer[length++] = char.ToLowerInvariant(character);
        }

        var normalized = new string(buffer[..length]);
        return normalized switch
        {
            "characterselect" => 100,
            "selectcharacter" => 95,
            "characterselector" => 90,
            "charselect" => 85,
            "charsel" => 80,
            "cselect" => 75,
            _ when normalized.Contains("characterselect", StringComparison.Ordinal) => 70,
            _ when normalized.Contains("selectcharacter", StringComparison.Ordinal) => 65,
            _ => 0,
        };
    }

    private static bool TryRunIntegrationCommand(string command)
    {
        var normalized = command.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var commandName = normalized.Split(' ')[0];
        if (!CommandManager.Commands.ContainsKey(commandName))
            return false;

        CommandManager.ProcessCommand(normalized);
        return true;
    }

    public void Dispose()
    {
        if (HotbarEditing.IsEnabled)
            SetBarEditMode(false, false, false);

        KeybindRuntime.Dispose();
        NativeHotbarKeybindService.Shutdown();

        PluginInterface.UiBuilder.Draw -= DrawHotbarInputLayer;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(ShortCommand);
        CommandManager.RemoveHandler(RefCommand);
        mainWindow.Dispose();
        forgeWindow.Dispose();
        ForgeSquareMap.Dispose();
        ForgeVault.Dispose();
        ForgeAccess.Dispose();
        NativeHudVisibility.Dispose();
        CombatTelemetry.Dispose();
        HudTargeting.Dispose();
        PartyConnections.Dispose();
        HudPresets.Dispose();
        ardynChantPlayRequested = false;
        ardynChantStopRequested = false;
        ArdynChant.Dispose();
        AfkScreen.Dispose();
        NativeWindows.Dispose();
        NativeContextMenus.Dispose();
        WindowSystem.RemoveAllWindows();
        configWindow.Dispose();
        tourGuideWindow.Dispose();
        hudOverlayWindow.Dispose();
        actionBarOneInteractionWindow.Dispose();
        actionBarTwoInteractionWindow.Dispose();
        actionBarThreeInteractionWindow.Dispose();
        crossHotbarInteractionWindow.Dispose();
        petBarInteractionWindow.Dispose();
        utilityBarInteractionWindow.Dispose();
        secondUtilityBarInteractionWindow.Dispose();
        foreach (var window in additionalCombatBarInteractionWindows)
            window.Dispose();
        additionalCombatBarInteractionWindows.Clear();
        raidToolsInteractionWindow.Dispose();
        leisureDockInteractionWindow.Dispose();
        partyInteractionWindow.Dispose();
        enemyListInteractionWindow.Dispose();
        targetInteractionWindow.Dispose();
        focusInteractionWindow.Dispose();
        commandPaletteWindow.Dispose();
        splashWindow.Dispose();
        loginGreetingWindow.Dispose();
        hudEditorWindow.Dispose();
        hotbarSlotEditorWindow.Dispose();
        actionPaletteWindow.Dispose();
        jobSwitcherWindow.Dispose();
        jobRibbonInteractionWindow.Dispose();
        pocketDeckWindow.Dispose();
        pocketRibbonInteractionWindow.Dispose();
        afkBackdropWindow.Dispose();
        forgeAnimationWheelWindow.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "command": case "center": case "search": case "aether": OpenCommandPalette(); break;
            case "edit": case "edithud": case "layout": ToggleHudEditMode(); break;
            case "bars": case "hotbars": case "skills": case "skilledit": case "actions": ToggleBarEditMode(); break;
            case "keybinds": case "binds": case "keys": OpenKeybindEditor(); break;
            case "refresh": case "reload": RefreshReFrame(); break;
            case "config": case "settings": ToggleConfigUi(); break;
            case "forge": case "reforgeforge": OpenForgeWindow(); break;
            case "wheel": case "animationwheel": ToggleForgeAnimationWheel(); break;
            case "dancer":
                if (!ForgeAccess.HasPlusAccess)
                {
                    ChatGui.Print("RE:Forge+ is required for Dancer.");
                    OpenForgeWindow();
                    break;
                }
                Configuration.ForgePremium.Dancer.Enabled = !Configuration.ForgePremium.Dancer.Enabled;
                SaveConfiguration();
                ChatGui.Print($"RE:Forge+ Dancer {(Configuration.ForgePremium.Dancer.Enabled ? "enabled" : "disabled")}.");
                break;
            case "roulette": case "animationroulette":
                if (!ForgeAccess.HasPlusAccess)
                {
                    ChatGui.Print("RE:Forge+ is required for Animation Roulette.");
                    OpenForgeWindow();
                    break;
                }
                Configuration.ForgePremium.AnimationRoulette.Enabled = !Configuration.ForgePremium.AnimationRoulette.Enabled;
                SaveConfiguration();
                ChatGui.Print($"RE:Forge+ Animation Roulette {(Configuration.ForgePremium.AnimationRoulette.Enabled ? "enabled" : "disabled")}.");
                break;
            case "tour": case "guide": case "setup": case "wizard": OpenTourGuide(); break;
            case "greetingpreview": PreviewLoginGreeting(); break;
            case "auto": SetMode(UiMode.Auto); break;
            case "leisure": case "casual": SetMode(UiMode.Leisure); break;
            case "roleplay": case "rp": case "social": SetMode(UiMode.Roleplay); break;
            case "quest": SetMode(UiMode.Quest); break;
            case "raid": case "ready": SetMode(UiMode.RaidReady); break;
            case "work": case "craft": case "gather": SetMode(UiMode.Work); break;
            case "combat": SetMode(UiMode.RaidReady); break;
            case "hud": SetHudVisible(!Configuration.ShowHudOverlay); break;
            case "replace": case "on": case "reframe":
                ActivateReFrameUi();
                break;
            case "restore": case "ffui": case "native": case "off":
                RestoreFinalFantasyUi();
                break;
            case "audit": ReportIntegratedNativeUi(); break;
            case "lbdiag": case "limitbreakdiag":
                var diagnostic = HudRenderer.BuildLimitBreakDiagnostic();
                Log.Information("{Diagnostic}", diagnostic);
                try
                {
                    var diagnosticPath = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "limit-break-diagnostic.txt");
                    File.WriteAllText(diagnosticPath, diagnostic + Environment.NewLine);
                    ChatGui.Print($"RE:Frame Limit Break diagnostic saved: {diagnosticPath}");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to save Limit Break diagnostic file.");
                    ChatGui.Print("RE:Frame Limit Break diagnostic was written to the Dalamud log.");
                }
                ChatGui.Print(diagnostic);
                break;
            case "version": ChatGui.Print("RE:Frame XIV v0.5.2.3 is currently loaded — the complete RE:Forge+ creative suite is available."); break;
            case "reset": CombatTelemetry.Reset(); break;
            case "help": ChatGui.Print("RE:Frame: /reframe, /ref, /rf, forge, wheel, dancer, roulette, command, edit, bars, keybinds, refresh, config, tour, auto, leisure, roleplay, quest, raid, work, hud, on, ffui, restore, audit, lbdiag, reset, version"); break;
            default: ToggleMainUi(); break;
        }
    }
}
