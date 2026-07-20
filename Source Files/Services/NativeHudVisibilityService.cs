using System;
using System.Collections.Generic;
using Vector2 = System.Numerics.Vector2;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;
using REFrameXIV.Localization;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Services;


public readonly record struct NativeJobGaugeComponentInfo(
    string Key,
    string Label,
    Vector2 Position,
    Vector2 Size);


public sealed unsafe class NativeHudVisibilityService : IDisposable
{
    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly AdaptiveStateService adaptiveState;
    private readonly CrossHotbarStateService crossHotbarState;
    private readonly Func<bool> isHudEditMode;
    private readonly Func<bool> isBarEditMode;
    private readonly Func<bool> useForgeSquareMinimap;
    private readonly IPluginLog log;
    private readonly HashSet<string> hiddenByUs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> observedJobGaugeAddons = new(StringComparer.OrdinalIgnoreCase);

    private DateTime nextRefreshUtc;
    private bool disposed;
    private string jobGaugeOverrideJob = string.Empty;
    private bool statusEffectsOverrideActive;
    private bool questDockWasActive;
    private bool questHudCommandsIssued;
    private bool nativeBarEditSurfaceReady;
    private DateTime nextNativeHotbarEnableUtc = DateTime.MinValue;
    private DateTime minimapNativeEditorSettleUntilUtc = DateTime.MinValue;
    private readonly HashSet<string> questElementOverridesActive = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] PlayerStatusAddons = { "_Status", "Status", "_StatusCustom0", "StatusCustom0", "_StatusCustom1", "StatusCustom1", "_StatusCustom2", "StatusCustom2", "_StatusCustom3", "StatusCustom3" };
    private static readonly string[] TargetStatusAddons = { "_TargetInfoBuffDebuff", "TargetInfoBuffDebuff", "_TargetInfoBuffDebuff2", "TargetInfoBuffDebuff2" };
    private static readonly string[] QuestScenarioAddons =
    {
        "_ScenarioTree", "ScenarioTree", "_ScenarioTree2", "ScenarioTree2",
        "_ScenarioGuide", "ScenarioGuide", "_MainScenarioGuide", "MainScenarioGuide",
    };
    private static readonly string[] QuestTrackerAddons =
    {
        "_ToDoList", "ToDoList", "_ToDoList2", "ToDoList2",
        "_FateList", "FateList", "_QuestList", "QuestList",
    };
    private static readonly string[] QuestDutyAddons =
    {
        "_ContentsInfo", "ContentsInfo", "_ContentsInfo2", "ContentsInfo2",
        "_ContentsInfoDetail", "ContentsInfoDetail", "_DutyList", "DutyList",
    };
    private static readonly string[] QuestCurrencyAddons =
    {
        "_Money", "Money", "_Money2", "Money2",
    };


    private static readonly string[] PlayerCastBarAddons =
    {


        "_ProgressBar", "ProgressBar", "_ProgressBar2", "ProgressBar2",
        "_CastBar", "CastBar", "_CastBar2", "CastBar2",
        "_CastingBar", "CastingBar", "_PlayerCastBar", "PlayerCastBar",
    };
    private static readonly string[] LimitBreakAddons =
    {
        "_LimitBreak", "LimitBreak", "_LimitBreak2", "LimitBreak2",
        "_LimitBreakGauge", "LimitBreakGauge", "_LimitBreakBar", "LimitBreakBar",
        "_LimitGauge", "LimitGauge",
    };
    private static readonly string[] MinimapAddons =
    {
        "_NaviMap", "NaviMap", "_MiniMap", "MiniMap",
    };
    private static readonly string[] NameplateAddons =
    {
        "_NamePlate", "NamePlate", "_NamePlate2", "NamePlate2",
    };

    private readonly record struct NativeAddonSnapshot(string Name, short X, short Y, ushort Width, ushort Height, float Scale);

    private enum MinimapNodeRole
    {
        Map,
        Coordinates,
        Celestial,
        ZoomIn,
        ZoomOut,
    }

    private enum MinimapNodeTree
    {
        Addon,
        MapComponent,
    }

    private readonly record struct NativeNodeRect(Vector2 Min, Vector2 Max)
    {
        public Vector2 Size => Max - Min;
        public Vector2 Center => (Min + Max) * 0.5f;

        public NativeNodeRect Expand(float amount)
            => new(Min - new Vector2(amount), Max + new Vector2(amount));

        public NativeNodeRect Inset(float amount)
        {
            var safe = MathF.Max(0f, amount);
            var nextMin = Min + new Vector2(safe);
            var nextMax = Max - new Vector2(safe);
            return nextMax.X > nextMin.X + 1f && nextMax.Y > nextMin.Y + 1f
                ? new NativeNodeRect(nextMin, nextMax)
                : this;
        }

        public float IntersectionArea(NativeNodeRect other)
        {
            var width = MathF.Max(0f, MathF.Min(Max.X, other.Max.X) - MathF.Max(Min.X, other.Min.X));
            var height = MathF.Max(0f, MathF.Min(Max.Y, other.Max.Y) - MathF.Max(Min.Y, other.Min.Y));
            return width * height;
        }
    }

    private readonly record struct MinimapNodeTransform(
        MinimapNodeTree Tree,
        nint Address,
        uint NodeId,
        NodeType Type,
        float X,
        float Y,
        float ScaleX,
        float ScaleY,
        float OriginX,
        float OriginY,
        NativeNodeRect ScreenRect,
        MinimapNodeRole Role);

    private readonly record struct MinimapChromeNodeSnapshot(
        MinimapNodeTree Tree,
        nint Address,
        uint NodeId,
        NodeType Type,
        byte Alpha,
        bool WasVisible);

    private readonly record struct MinimapControlCandidate(
        MinimapNodeTree Tree,
        nint Address,
        NativeNodeRect ScreenRect,
        bool IsCollision);

    private sealed class MinimapCompositionSnapshot
    {
        public MinimapCompositionSnapshot(
            NativeAddonSnapshot addon,
            nint addonAddress,
            nint rootAddress,
            nint nodeListAddress,
            ushort nodeListCount,
            nint mapComponentAddress,
            nint mapComponentNodeListAddress,
            float rootScaleX,
            float rootScaleY,
            NativeNodeRect mapRect,
            Vector2 mapCenterLocal,
            List<MinimapNodeTransform> nodes,
            List<MinimapChromeNodeSnapshot> chromeNodes)
        {
            Addon = addon;
            AddonAddress = addonAddress;
            RootAddress = rootAddress;
            NodeListAddress = nodeListAddress;
            NodeListCount = nodeListCount;
            MapComponentAddress = mapComponentAddress;
            MapComponentNodeListAddress = mapComponentNodeListAddress;
            RootScaleX = rootScaleX;
            RootScaleY = rootScaleY;
            MapRect = mapRect;
            MapCenterLocal = mapCenterLocal;
            Nodes = nodes;
            ChromeNodes = chromeNodes;
        }

        public NativeAddonSnapshot Addon { get; }
        public nint AddonAddress { get; }
        public nint RootAddress { get; }
        public nint NodeListAddress { get; }
        public ushort NodeListCount { get; }
        public nint MapComponentAddress { get; }
        public nint MapComponentNodeListAddress { get; }
        public float RootScaleX { get; }
        public float RootScaleY { get; }
        public NativeNodeRect MapRect { get; }
        public Vector2 MapCenterLocal { get; }
        public List<MinimapNodeTransform> Nodes { get; }
        public List<MinimapChromeNodeSnapshot> ChromeNodes { get; }

        public bool Matches(AtkUnitBase* addon)
        {
            if (addon == null ||
                addon->RootNode == null ||
                AddonAddress != (nint)addon ||
                RootAddress != (nint)addon->RootNode ||
                NodeListAddress != (nint)addon->UldManager.NodeList ||
                addon->UldManager.NodeListCount == 0)
                return false;

            if (MapComponentAddress == 0)
                return true;

            var mapSnapshot = Nodes.FirstOrDefault(node => node.Role == MinimapNodeRole.Map);
            var mapNode = ResolveMinimapNode(&addon->UldManager, null, mapSnapshot);
            var component = GetNativeMapComponent(mapNode);
            return component != null &&
                   MapComponentAddress == (nint)component &&
                   MapComponentNodeListAddress == (nint)component->UldManager.NodeList &&
                   component->UldManager.NodeListCount > 0;
        }
    }

    private MinimapCompositionSnapshot? integratedMinimapComposition;
    private readonly List<HudBounds> minimapInteractionBounds = new(5);
    private bool minimapNeedsValidation;
    private bool minimapCenterCorrectionAttempted;
    private Vector2 minimapAnchorCorrection;
    private bool nativeHudLayoutEditorWasOpen;
    private HudBounds? minimapZoomOutVisualBounds;
    private HudBounds? minimapZoomInVisualBounds;
    private DateTime minimapRetryAfterUtc;


    private static readonly string[] StandardActionBarAddons =
    {
        "_ActionBar",
        "_ActionBar01",
        "_ActionBar02",
        "_ActionBar03",
        "_ActionBar04",
        "_ActionBar05",
        "_ActionBar06",
        "_ActionBar07",
        "_ActionBar08",
        "_ActionBar09",
    };


    private static readonly string[] ControllerCrossHotbarAddons =
    {
        "_ActionCross",
    };


    private static readonly (string Label, string[] Names)[] IntegratedHoldoutFamilies =
    {
        ("Chat", new[] { "_ChatLog", "ChatLog", "_ChatLogPanel_0", "ChatLogPanel_0" }),
        ("Minimap", new[] { "_NaviMap", "NaviMap", "_MiniMap", "MiniMap" }),
        ("Duty / Quest List", new[] { "_ToDoList", "ToDoList", "_ToDoList2", "ToDoList2" }),
        ("Target Status", new[] { "_TargetInfoBuffDebuff", "TargetInfoBuffDebuff" }),
        ("Focus Target Status", new[] { "_FocusTargetInfoBuffDebuff", "FocusTargetInfoBuffDebuff" }),
        ("Player Status", new[] { "_Status", "Status", "_StatusCustom0", "StatusCustom0", "_StatusCustom1", "StatusCustom1", "_StatusCustom2", "StatusCustom2" }),
    };

    public NativeHudVisibilityService(
        Configuration configuration,
        IFramework framework,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IClientState clientState,
        IPlayerState playerState,
        AdaptiveStateService adaptiveState,
        CrossHotbarStateService crossHotbarState,
        Func<bool> isHudEditMode,
        Func<bool> isBarEditMode,
        Func<bool> useForgeSquareMinimap,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.framework = framework;
        this.addonLifecycle = addonLifecycle;
        this.gameGui = gameGui;
        this.clientState = clientState;
        this.playerState = playerState;
        this.adaptiveState = adaptiveState;
        this.crossHotbarState = crossHotbarState;
        this.isHudEditMode = isHudEditMode;
        this.isBarEditMode = isBarEditMode;
        this.useForgeSquareMinimap = useForgeSquareMinimap;
        this.log = log;
        this.configuration.NativeJobGaugePlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        this.configuration.NativeQuestElementPlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);


        nextRefreshUtc = DateTime.UtcNow.AddMilliseconds(1500);
        minimapNativeEditorSettleUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
        framework.Update += OnFrameworkUpdate;
        addonLifecycle.RegisterListener(AddonEvent.PreDraw, OnJobGaugePreDraw);
    }

    public bool IsSuppressing => configuration.ReplaceNativeHud && configuration.ShowHudOverlay && clientState.IsLoggedIn;
    public int HiddenAddonCount => hiddenByUs.Count;
    public bool NativeBarEditSurfaceReady => nativeBarEditSurfaceReady;

    public static bool HotbarDataAvailable
    {
        get
        {
            var module = RaptureHotbarModule.Instance();
            return module != null && module->ModuleReady;
        }
    }

    public void RefreshNow()
    {
        if (disposed)
            return;


        nextRefreshUtc = DateTime.MinValue;
    }

    public void BeginNativeBarEditSession(bool controllerMode)
    {
        nativeBarEditSurfaceReady = false;
        nextNativeHotbarEnableUtc = DateTime.MinValue;
        if (!controllerMode)
            EnsureStandardHotbarHudElementsEnabled();
        RefreshNow();
    }

    public void EndNativeBarEditSession()
    {
        nativeBarEditSurfaceReady = false;
        nextNativeHotbarEnableUtc = DateTime.MinValue;
        RefreshNow();
    }


    public void PrepareForNativePlacementReplacement()
    {
        if (disposed)
            return;

        RestoreActiveJobGaugePosition();
        RestoreStatusEffectsPosition();
        RestoreQuestElementPlacements();
    }

    public void RestoreAll()
    {
        foreach (var name in hiddenByUs)
            SetVisible(name, true);

        hiddenByUs.Clear();
        RestoreActiveJobGaugePosition();
        RestoreStatusEffectsPosition();
        RestoreQuestElementPlacements();
        RestoreMinimapIntegration();
        nativeBarEditSurfaceReady = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;


        RestoreNativeHotbarEditState();
        RestoreAll();
        framework.Update -= OnFrameworkUpdate;
        addonLifecycle.UnregisterListener(OnJobGaugePreDraw);
        disposed = true;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed)
            return;

        var now = DateTime.UtcNow;
        if (now < nextRefreshUtc)
            return;

        nextRefreshUtc = now.AddMilliseconds(80);
        try
        {
            ApplyState();
        }
        catch (Exception ex)
        {


            nextRefreshUtc = now.AddSeconds(1);
            log.Warning(ex, "RE:Frame deferred native HUD visibility after a volatile addon changed.");
        }
    }

    private void ApplyState()
    {
        if (disposed)
            return;

        if (!configuration.ReplaceNativeHud || !configuration.ShowHudOverlay || !clientState.IsLoggedIn)
        {
            RestoreAll();
            return;
        }

        var mode = adaptiveState.EffectiveMode;
        var showMinimapFrame = HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.Minimap, configuration.ShowMinimapFrame);
        var squareMinimapOwnsNavigation = showMinimapFrame && useForgeSquareMinimap();
        if (!showMinimapFrame || squareMinimapOwnsNavigation)
            RestoreMinimapIntegration();


        foreach (var name in MinimapAddons)
            Apply(name, squareMinimapOwnsNavigation);


        var replaceLocationAndParameter = configuration.OverrideNativeLocationAndParameter;
        Apply("_DTR", replaceLocationAndParameter);
        Apply("DTR", replaceLocationAndParameter);
        Apply("_DTRBar", replaceLocationAndParameter);
        Apply("DTRBar", replaceLocationAndParameter);
        Apply("_ServerInfo", replaceLocationAndParameter);
        Apply("ServerInfo", replaceLocationAndParameter);

        Apply("_MainCommand", replaceLocationAndParameter);
        Apply("MainCommand", replaceLocationAndParameter);
        Apply("_MainCommand2", replaceLocationAndParameter);
        Apply("MainCommand2", replaceLocationAndParameter);
        Apply("_Exp", replaceLocationAndParameter);
        Apply("Exp", replaceLocationAndParameter);
        Apply("_Exp2", replaceLocationAndParameter);
        Apply("Exp2", replaceLocationAndParameter);
        Apply("_ExpBar", replaceLocationAndParameter);
        Apply("ExpBar", replaceLocationAndParameter);
        Apply("_ParameterWidget", replaceLocationAndParameter);
        Apply("ParameterWidget", replaceLocationAndParameter);
        Apply("_ParameterWidget2", replaceLocationAndParameter);
        Apply("ParameterWidget2", replaceLocationAndParameter);


        foreach (var name in NameplateAddons)
            Apply(name, configuration.OverrideNativeNameplates);


        foreach (var name in QuestCurrencyAddons)
            Apply(name, configuration.OverrideNativeCurrencyAndInventory);


        foreach (var name in PlayerCastBarAddons)
            Apply(name, configuration.OverrideNativePlayerCastBar);


        foreach (var name in LimitBreakAddons)
            Apply(name, configuration.ShowLimitBreakGauge);

        Apply("_PartyList", configuration.OverrideNativePartyList);
        Apply("PartyList", configuration.OverrideNativePartyList);
        Apply("_PartyList2", configuration.OverrideNativePartyList);
        Apply("PartyList2", configuration.OverrideNativePartyList);
        var replaceAllianceLists = configuration.OverrideNativeAllianceLists &&
                                   Plugin.PartyList.IsAlliance &&
                                   (HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.AllianceOne, configuration.ShowAllianceFrames && configuration.ShowAllianceFrameOne) ||
                                    HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.AllianceTwo, configuration.ShowAllianceFrames && configuration.ShowAllianceFrameTwo));
        Apply("_AllianceList", replaceAllianceLists);
        Apply("AllianceList", replaceAllianceLists);
        Apply("_AllianceList1", replaceAllianceLists);
        Apply("AllianceList1", replaceAllianceLists);
        Apply("_AllianceList2", replaceAllianceLists);
        Apply("AllianceList2", replaceAllianceLists);
        Apply("_AllianceList3", replaceAllianceLists);
        Apply("AllianceList3", replaceAllianceLists);
        Apply("_FocusTargetInfo", configuration.OverrideNativeFocusTarget);
        Apply("FocusTargetInfo", configuration.OverrideNativeFocusTarget);
        Apply("_FocusTargetInfoCastBar", configuration.OverrideNativeFocusTarget);
        Apply("FocusTargetInfoCastBar", configuration.OverrideNativeFocusTarget);
        var replaceEnemyList = HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.EnemyList, configuration.ShowEnemyList);
        var suppressEnemyList = configuration.OverrideNativeEnemyList && (replaceEnemyList || mode is UiMode.Work or UiMode.Roleplay);
        Apply("_EnemyList", suppressEnemyList);
        Apply("EnemyList", suppressEnemyList);


        var replaceTargetFamily =
            HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.Target, configuration.ShowTargetFrame) ||
            HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.TargetOfTarget, configuration.ShowTargetOfTargetFrame) ||
            HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.CastBar, configuration.ShowCastBar);
        var suppressTargetFamily = configuration.OverrideNativeTargetInfo && (replaceTargetFamily || mode is UiMode.Work or UiMode.Roleplay);
        Apply("_TargetInfo", suppressTargetFamily);
        Apply("TargetInfo", suppressTargetFamily);
        Apply("_TargetInfoMainTarget", suppressTargetFamily);
        Apply("TargetInfoMainTarget", suppressTargetFamily);
        Apply("_TargetInfoCastBar", suppressTargetFamily);
        Apply("TargetInfoCastBar", suppressTargetFamily);


        foreach (var name in TargetStatusAddons)
            Apply(name, suppressTargetFamily);


        nativeBarEditSurfaceReady = false;
        var hideStandardHotbars = configuration.HideNativeActionBars && HotbarDataAvailable;
        foreach (var name in StandardActionBarAddons)
            Apply(name, hideStandardHotbars);


        var replaceControllerCrossHotbar =
            configuration.ReplaceNativeCrossHotbar &&
            crossHotbarState.TryGetState(out var crossHotbar) &&
            !crossHotbar.PetHotbarActive;
        foreach (var name in ControllerCrossHotbarAddons)
            Apply(name, replaceControllerCrossHotbar);


        if (configuration.OverrideNativeJobGauges)
            ApplyJobGaugeModeState();
        else
            RestoreActiveJobGaugePosition();

        var partyStatusIntegrationActive =
            configuration.ShowStatusEffectsInRaidAndCombat &&
            HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.Party, configuration.ShowPartyFrames) &&
            Plugin.PartyList.Length > 1 &&
            !HudModeProfileService.IsCalmMode(mode);
        var separatedRaidStatusPanelsActive =
            mode == UiMode.RaidReady &&
            (HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.RaidBuffs, configuration.ShowRaidBuffs) ||
             HudModeProfileService.ResolveVisibility(configuration, mode, HudElementIds.RaidDebuffs, configuration.ShowRaidDebuffs));
        var showNativeStatus =
            configuration.ShowStatusEffectsInRaidAndCombat &&
            !HudModeProfileService.IsCalmMode(mode) &&
            !partyStatusIntegrationActive &&
            !separatedRaidStatusPanelsActive;

        foreach (var name in PlayerStatusAddons)
            Apply(name, configuration.OverrideNativeStatusEffects && !showNativeStatus);

        if (showNativeStatus || !configuration.OverrideNativeStatusEffects)
            ApplyConfiguredStatusEffectsPosition();

        ApplyQuestDockState();
    }


    private void ApplyQuestDockState()
    {
        if (!configuration.OverrideNativeQuestElements)
        {
            foreach (var name in QuestScenarioAddons) ForceShow(name);
            foreach (var name in QuestDutyAddons) ForceShow(name);
            foreach (var name in QuestTrackerAddons) ForceShow(name);
            RestoreQuestElementPlacements();
            questDockWasActive = false;
            questHudCommandsIssued = false;
            return;
        }

        var questDockActive = configuration.ShowQuestNativeElements &&
                              adaptiveState.EffectiveMode == UiMode.Quest;
        if (!questDockActive)
        {


            foreach (var name in QuestScenarioAddons)
                Apply(name, true);
            foreach (var name in QuestDutyAddons)
                Apply(name, true);
            foreach (var name in QuestTrackerAddons)
                Apply(name, true);
            foreach (var name in QuestCurrencyAddons)
                Apply(name, configuration.OverrideNativeCurrencyAndInventory);

            questDockWasActive = false;
            questHudCommandsIssued = false;
            RestoreQuestElementPlacements();
            return;
        }


        if (!questDockWasActive || !questHudCommandsIssued)
        {
            var scenarioEnabled = NativeChatCommandService.TryExecute("/hud \"Scenario Guide\" on");
            var dutyListEnabled = NativeChatCommandService.TryExecute("/hud \"Duty List\" on");
            questHudCommandsIssued = scenarioEnabled && dutyListEnabled;
            questDockWasActive = true;
        }


        foreach (var name in QuestScenarioAddons)
            ForceShow(name);
        foreach (var name in QuestDutyAddons)
            ForceShow(name);
        foreach (var name in QuestTrackerAddons)
            ForceShow(name);

        foreach (var name in QuestCurrencyAddons)
            Apply(name, configuration.OverrideNativeCurrencyAndInventory);

        ApplyConfiguredQuestElementPlacement(HudElementIds.NativeScenarioGuide);
        ApplyConfiguredQuestElementPlacement(HudElementIds.NativeQuestList);
        ApplyConfiguredQuestElementPlacement(HudElementIds.NativeDutyInfo);
    }


    private void RestoreNativeHotbarEditState()
    {
        foreach (var name in StandardActionBarAddons)
            SetStandardHotbarUnlocked(name, false);

        SetNativeCrossHotbarEditState(false);
        nativeBarEditSurfaceReady = false;
    }


    private void SetStandardHotbarUnlocked(string name, bool unlocked)
    {
        try
        {
            var addon = GetAddon(name);
            if (addon == null || !addon->IsReady)
                return;


            ((AddonActionBarBase*)addon)->IsLocked = !unlocked;
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not update native hotbar lock state for {AddonName}.", name);
        }
    }

    private bool SetNativeCrossHotbarEditState(bool enabled)
    {
        try
        {
            var addon = gameGui.GetAddonByName<AddonActionCross>("_ActionCross", 1);
            if (addon == null || !((AtkUnitBase*)addon)->IsReady)
                return !enabled;

            ((AddonActionBarBase*)addon)->IsLocked = !enabled;
            addon->InEditMode = enabled;
            addon->OverrideHidden = enabled;
            return !enabled || ((AtkUnitBase*)addon)->IsVisible;
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not update the native cross-hotbar edit state.");
            return false;
        }
    }


    private bool ForceShow(string name)
    {
        try
        {


            hiddenByUs.Remove(name);
            var addon = GetAddon(name);
            if (addon == null || !addon->IsReady)
                return false;

            if (!addon->IsVisible)
                addon->Show(true, 0);
            addon->IsVisible = true;
            return addon->IsVisible;
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not show native HUD addon {AddonName}.", name);
            return false;
        }
    }

    private void EnsureStandardHotbarHudElementsEnabled()
    {
        var now = DateTime.UtcNow;
        if (now < nextNativeHotbarEnableUtc)
            return;

        nextNativeHotbarEnableUtc = now.AddSeconds(1);
        for (var bar = 1; bar <= 3; bar++)
            NativeChatCommandService.TryExecute($"/hud \"Hotbar {bar}\" on");
    }


    public bool TryGetVisibleQuestElementBounds(string elementId, out Vector2 position, out Vector2 size)
        => TryGetQuestElementBounds(elementId, visibleOnly: true, out position, out size);

    public bool MoveVisibleQuestElement(string elementId, Vector2 delta)
    {
        if (delta.LengthSquared() < 0.0001f ||
            !TryGetQuestElementBounds(elementId, visibleOnly: true, out var currentPosition, out _))
            return false;

        var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly: true);
        if (snapshots.Count == 0)
            return false;

        var placement = EnsureQuestElementPlacement(elementId, currentPosition, snapshots[0].Scale);
        MoveNativeSnapshots(snapshots, delta, "Quest Dock");
        placement.X = MathF.Round(currentPosition.X + delta.X);
        placement.Y = MathF.Round(currentPosition.Y + delta.Y);
        questElementOverridesActive.Add(elementId);
        return true;
    }

    public bool ResizeVisibleQuestElement(string elementId, Vector2 delta)
    {
        if (!TryGetQuestElementBounds(elementId, visibleOnly: true, out var currentPosition, out var currentSize) ||
            currentSize.X < 1f || currentSize.Y < 1f)
            return false;

        var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly: true);
        if (snapshots.Count == 0)
            return false;

        var currentScale = Math.Clamp(snapshots[0].Scale, 0.50f, 2.00f);
        var factorX = (currentSize.X + delta.X) / currentSize.X;
        var factorY = (currentSize.Y + delta.Y) / currentSize.Y;
        var factor = MathF.Max(factorX, factorY);
        if (!float.IsFinite(factor))
            return false;

        var nextScale = Math.Clamp(currentScale * factor, 0.50f, 2.00f);
        if (MathF.Abs(nextScale - currentScale) < 0.001f)
            return false;

        var placement = EnsureQuestElementPlacement(elementId, currentPosition, currentScale);
        ScaleQuestElementAroundAnchor(elementId, snapshots, currentPosition, nextScale);
        placement.X = MathF.Round(currentPosition.X);
        placement.Y = MathF.Round(currentPosition.Y);
        placement.Scale = nextScale;
        questElementOverridesActive.Add(elementId);
        return true;
    }

    public bool HasQuestElementPlacement(string elementId)
        => configuration.NativeQuestElementPlacements?.ContainsKey(elementId) == true;

    public void ResetQuestElementPlacement(string elementId)
    {
        configuration.NativeQuestElementPlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (configuration.NativeQuestElementPlacements.TryGetValue(elementId, out var placement) && placement.HasOriginal)
        {
            var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly: false);
            if (TryGetQuestElementBounds(elementId, visibleOnly: false, out var currentPosition, out _) && snapshots.Count > 0)
            {
                ScaleQuestElementAroundAnchor(
                    elementId,
                    snapshots,
                    currentPosition,
                    placement.OriginalScale > 0f ? placement.OriginalScale : 1f);
            }

            MoveQuestElementGroupTo(elementId, new Vector2(placement.OriginalX, placement.OriginalY));
        }

        configuration.NativeQuestElementPlacements.Remove(elementId);
        questElementOverridesActive.Remove(elementId);
    }


    public bool ResetQuestElementScale(string elementId)
    {
        configuration.NativeQuestElementPlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (!configuration.NativeQuestElementPlacements.TryGetValue(elementId, out var placement) ||
            placement is null || !placement.HasOriginal ||
            !TryGetQuestElementBounds(elementId, visibleOnly: false, out var currentPosition, out _))
            return false;

        var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly: false);
        if (snapshots.Count == 0)
            return false;

        var originalScale = placement.OriginalScale > 0f ? placement.OriginalScale : 1f;
        ScaleQuestElementAroundAnchor(elementId, snapshots, currentPosition, originalScale);
        placement.X = MathF.Round(currentPosition.X);
        placement.Y = MathF.Round(currentPosition.Y);
        placement.Scale = originalScale;
        questElementOverridesActive.Add(elementId);
        return true;
    }

    public void ResetAllQuestElementPlacements()
    {
        foreach (var elementId in new[]
                 {
                     HudElementIds.NativeScenarioGuide,
                     HudElementIds.NativeQuestList,
                     HudElementIds.NativeDutyInfo,
                 })
            ResetQuestElementPlacement(elementId);

        configuration.NativeQuestElementPlacements?.Clear();
        questElementOverridesActive.Clear();
    }

    private NativeJobGaugePlacement EnsureQuestElementPlacement(string elementId, Vector2 currentPosition, float currentScale)
    {
        configuration.NativeQuestElementPlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (!configuration.NativeQuestElementPlacements.TryGetValue(elementId, out var placement))
        {
            placement = new NativeJobGaugePlacement
            {
                X = currentPosition.X,
                Y = currentPosition.Y,
                OriginalX = currentPosition.X,
                OriginalY = currentPosition.Y,
                Scale = currentScale,
                OriginalScale = currentScale,
                HasOriginal = true,
            };
            configuration.NativeQuestElementPlacements[elementId] = placement;
        }
        else if (!placement.HasOriginal)
        {
            placement.OriginalX = currentPosition.X;
            placement.OriginalY = currentPosition.Y;
            placement.OriginalScale = currentScale;
            placement.HasOriginal = true;
        }

        if (placement.Scale <= 0f)
            placement.Scale = currentScale;
        return placement;
    }

    private void ApplyConfiguredQuestElementPlacement(string elementId)
    {
        configuration.NativeQuestElementPlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (!configuration.NativeQuestElementPlacements.TryGetValue(elementId, out var placement))
            return;

        if (!TryGetQuestElementBounds(elementId, visibleOnly: false, out var currentPosition, out _))
            return;

        var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly: false);
        if (snapshots.Count == 0)
            return;

        EnsureQuestElementPlacement(elementId, currentPosition, snapshots[0].Scale);
        if (placement.Scale > 0f)
        {
            ScaleQuestElementAroundAnchor(
                elementId,
                snapshots,
                currentPosition,
                Math.Clamp(placement.Scale, 0.50f, 2.00f));
        }

        if (TryGetQuestElementBounds(elementId, visibleOnly: false, out var scaledPosition, out _))
        {
            var desired = new Vector2(placement.X, placement.Y);
            var delta = desired - scaledPosition;
            if (delta.LengthSquared() >= 0.25f)
                MoveNativeSnapshots(CollectQuestElementSnapshots(elementId, visibleOnly: false), delta, "Quest Dock");
        }

        questElementOverridesActive.Add(elementId);
    }

    private void RestoreQuestElementPlacements()
    {
        if (questElementOverridesActive.Count == 0 || configuration.NativeQuestElementPlacements is null)
            return;

        foreach (var elementId in questElementOverridesActive.ToArray())
        {
            if (!configuration.NativeQuestElementPlacements.TryGetValue(elementId, out var placement) || !placement.HasOriginal)
                continue;

            var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly: false);
            if (TryGetQuestElementBounds(elementId, visibleOnly: false, out var currentPosition, out _) && snapshots.Count > 0)
            {
                ScaleQuestElementAroundAnchor(
                    elementId,
                    snapshots,
                    currentPosition,
                    placement.OriginalScale > 0f ? placement.OriginalScale : 1f);
            }

            MoveQuestElementGroupTo(elementId, new Vector2(placement.OriginalX, placement.OriginalY));
        }

        questElementOverridesActive.Clear();
    }

    private bool MoveQuestElementGroupTo(string elementId, Vector2 desiredPosition)
    {
        if (!TryGetQuestElementBounds(elementId, visibleOnly: false, out var currentPosition, out _))
            return false;
        var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly: false);
        if (snapshots.Count == 0)
            return false;
        MoveNativeSnapshots(snapshots, desiredPosition - currentPosition, "Quest Dock");
        return true;
    }

    private bool TryGetQuestElementBounds(string elementId, bool visibleOnly, out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;
        var snapshots = CollectQuestElementSnapshots(elementId, visibleOnly);
        if (snapshots.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var foundVisualBounds = false;
        foreach (var snapshot in snapshots)
        {
            try
            {
                var addon = GetAddon(snapshot.Name);
                if (addon != null && addon->IsReady &&
                    TryGetQuestAddonVisualBounds(addon, out var visualBounds))
                {
                    min = Vector2.Min(min, visualBounds.Min);
                    max = Vector2.Max(max, visualBounds.Max);
                    foundVisualBounds = true;
                    continue;
                }
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame could not measure visible Quest Dock content for {AddonName}.", snapshot.Name);
            }


            var fallbackScale = Math.Clamp(MathF.Abs(snapshot.Scale), 0.10f, 4.00f);
            var fallbackPosition = new Vector2(snapshot.X, snapshot.Y);
            min = Vector2.Min(min, fallbackPosition);
            max = Vector2.Max(
                max,
                fallbackPosition + new Vector2(snapshot.Width, snapshot.Height) * fallbackScale);
            foundVisualBounds = true;
        }

        if (!foundVisualBounds || max.X <= min.X || max.Y <= min.Y)
            return false;
        position = min;
        size = max - min;
        return true;
    }


    private static bool TryGetQuestAddonVisualBounds(AtkUnitBase* addon, out NativeNodeRect bounds)
    {
        bounds = default;
        if (addon == null || addon->RootNode == null)
            return false;

        var manager = &addon->UldManager;
        var hasBounds = false;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        if (manager->NodeList != null && manager->NodeListCount > 0)
        {
            var count = Math.Min((int)manager->NodeListCount, 512);
            for (var index = 0; index < count; index++)
            {
                var node = manager->NodeList[index];
                if (!IsQuestVisualNodeVisible(node, addon->RootNode) ||
                    node->Type is NodeType.Collision or NodeType.ClippingMask ||
                    !TryGetNodeScreenRect(node, out var nodeBounds))
                    continue;

                var nodeSize = nodeBounds.Size;
                if (nodeSize.X > 2400f || nodeSize.Y > 1800f)
                    continue;

                min = Vector2.Min(min, nodeBounds.Min);
                max = Vector2.Max(max, nodeBounds.Max);
                hasBounds = true;
            }
        }


        if (!hasBounds && TryGetNodeScreenRect(addon->RootNode, out var rootBounds))
        {
            min = rootBounds.Min;
            max = rootBounds.Max;
            hasBounds = true;
        }

        if (!hasBounds || max.X <= min.X || max.Y <= min.Y)
            return false;

        bounds = new NativeNodeRect(min, max);
        return true;
    }

    private static bool IsQuestVisualNodeVisible(AtkResNode* node, AtkResNode* root)
    {
        if (node == null || node->Width == 0 || node->Height == 0 || node->Color.A == 0)
            return false;

        var current = node;
        for (var depth = 0; depth < 64 && current != null; depth++)
        {
            if (!current->IsVisible() || current->Color.A == 0)
                return false;
            if (current == root)
                return true;
            current = current->ParentNode;
        }


        return false;
    }

    private List<NativeAddonSnapshot> CollectQuestElementSnapshots(string elementId, bool visibleOnly)
    {


        if (!visibleOnly)
        {
            var visibleSnapshots = CollectQuestElementSnapshots(elementId, visibleOnly: true);
            if (visibleSnapshots.Count > 0)
                return visibleSnapshots;
        }

        var snapshots = new List<NativeAddonSnapshot>();
        var names = GetQuestElementAddonNames(elementId);
        var seen = new HashSet<nint>();
        foreach (var name in names)
        {
            try
            {
                var addon = GetAddon(name);
                if (addon == null || !addon->IsReady || (visibleOnly && !addon->IsVisible) || !seen.Add((nint)addon))
                    continue;

                short x = 0;
                short y = 0;
                ushort width = 0;
                ushort height = 0;
                addon->GetPosition(&x, &y);
                addon->GetSize(&width, &height, true);
                if (width == 0 || height == 0 || width > 1800 || height > 1400)
                    continue;

                snapshots.Add(new NativeAddonSnapshot(
                    name,
                    x,
                    y,
                    width,
                    height,
                    addon->RootNode != null ? addon->RootNode->ScaleX : 1f));
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame skipped volatile Quest Dock addon {AddonName}.", name);
            }
        }

        return snapshots;
    }

    private static IReadOnlyList<string> GetQuestElementAddonNames(string elementId)
        => elementId switch
        {
            HudElementIds.NativeScenarioGuide => QuestScenarioAddons,
            HudElementIds.NativeQuestList => QuestTrackerAddons,
            HudElementIds.NativeDutyInfo => QuestDutyAddons,
            _ => Array.Empty<string>(),
        };

    private void ScaleQuestElementAroundAnchor(
        string elementId,
        IReadOnlyList<NativeAddonSnapshot> snapshots,
        Vector2 anchor,
        float scale)
    {
        SetQuestElementScale(snapshots, scale);


        if (!TryGetQuestElementBounds(elementId, visibleOnly: false, out var scaledPosition, out _))
            return;

        var correction = anchor - scaledPosition;
        if (correction.LengthSquared() < 0.04f)
            return;

        MoveNativeSnapshots(
            CollectQuestElementSnapshots(elementId, visibleOnly: false),
            correction,
            "Quest Dock scale anchor");
    }

    private void SetQuestElementScale(IReadOnlyList<NativeAddonSnapshot> snapshots, float scale)
    {
        var safeScale = Math.Clamp(scale, 0.50f, 2.00f);
        foreach (var snapshot in snapshots)
        {
            try
            {
                var addon = GetAddon(snapshot.Name);
                if (addon == null || !addon->IsReady || addon->RootNode == null)
                    continue;

                addon->RootNode->SetScale(safeScale, safeScale);
                addon->RootNode->IsDirty = true;
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame could not scale Quest Dock addon {AddonName}.", snapshot.Name);
            }
        }
    }

    private void MoveNativeSnapshots(IReadOnlyList<NativeAddonSnapshot> snapshots, Vector2 delta, string familyLabel)
    {
        foreach (var snapshot in snapshots)
        {
            try
            {
                var addon = GetAddon(snapshot.Name);
                if (addon == null || !addon->IsReady)
                    continue;
                addon->SetPosition(ClampToShort(snapshot.X + delta.X), ClampToShort(snapshot.Y + delta.Y));
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame could not move {FamilyLabel} addon {AddonName}.", familyLabel, snapshot.Name);
            }
        }
    }

    private void ApplyJobGaugeModeState()
    {
        var job = GetCurrentJobAbbreviation();
        var currentNames = string.IsNullOrWhiteSpace(job)
            ? Array.Empty<string>()
            : GetJobGaugeAddonNames(job);
        var hideCurrentGauge = HudModeProfileService.IsCalmMode(adaptiveState.EffectiveMode) &&
                               !isHudEditMode();


        foreach (var hiddenName in new List<string>(hiddenByUs))
        {
            if (!IsJobGaugeAddonName(hiddenName))
                continue;

            var belongsToCurrentJob = Array.IndexOf(currentNames, hiddenName) >= 0;
            if (!hideCurrentGauge || !belongsToCurrentJob)
                Apply(hiddenName, false);
        }

        foreach (var name in currentNames)
            Apply(name, hideCurrentGauge);

        if (!hideCurrentGauge)
            ApplyConfiguredJobGaugePosition();
    }

    private static bool IsJobGaugeAddonName(string name)
        => name.StartsWith("_JobHud", StringComparison.Ordinal) ||
           name.StartsWith("JobHud", StringComparison.Ordinal);


    private void OnJobGaugePreDraw(AddonEvent _, AddonArgs args)
    {
        if (disposed || !IsJobGaugeAddonName(args.AddonName))
            return;

        var job = GetCurrentJobAbbreviation();
        if (string.IsNullOrWhiteSpace(job))
            return;

        if (!observedJobGaugeAddons.TryGetValue(job, out var names))
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            observedJobGaugeAddons[job] = names;
        }

        if (names.Add(args.AddonName))
        {
            log.Debug("RE:Frame learned native job gauge addon {AddonName} for {Job}.", args.AddonName, job);
            RefreshNow();
        }
    }


    public IReadOnlyList<NativeJobGaugeComponentInfo> GetVisibleJobGaugeComponents(string job)
    {
        var snapshots = CollectJobGaugeSnapshots(job, visibleOnly: true);
        if (snapshots.Count == 0)
            return Array.Empty<NativeJobGaugeComponentInfo>();

        return snapshots
            .OrderBy(snapshot => GetJobGaugeComponentOrder(snapshot.Name))
            .ThenBy(snapshot => NormalizeJobGaugeAddonName(snapshot.Name), StringComparer.OrdinalIgnoreCase)
            .Select(snapshot => new NativeJobGaugeComponentInfo(
                NormalizeJobGaugeAddonName(snapshot.Name),
                GetJobGaugeComponentLabel(job, snapshot.Name),
                new Vector2(snapshot.X, snapshot.Y),
                new Vector2(snapshot.Width, snapshot.Height)))
            .ToArray();
    }


    public bool TryGetVisibleJobGaugeBounds(string job, out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;
        var snapshots = CollectJobGaugeSnapshots(job, visibleOnly: true);
        if (snapshots.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var snapshot in snapshots)
        {
            var p = new Vector2(snapshot.X, snapshot.Y);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p + new Vector2(snapshot.Width, snapshot.Height));
        }

        if (max.X <= min.X || max.Y <= min.Y)
            return false;

        position = min;
        size = max - min;
        return true;
    }

    public bool TryGetVisibleJobGaugeComponentBounds(
        string job,
        string componentKey,
        out Vector2 position,
        out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;
        if (string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(componentKey))
            return false;

        var snapshot = FindJobGaugeSnapshot(job, componentKey, visibleOnly: true);
        if (snapshot is null)
            return false;

        position = new Vector2(snapshot.Value.X, snapshot.Value.Y);
        size = new Vector2(snapshot.Value.Width, snapshot.Value.Height);
        return true;
    }


    public bool MoveVisibleJobGauge(string job, Vector2 delta)
    {
        var components = GetVisibleJobGaugeComponents(job);
        return components.Count == 1 && MoveVisibleJobGaugeComponent(job, components[0].Key, delta);
    }

    public bool MoveVisibleJobGaugeComponent(string job, string componentKey, Vector2 delta)
    {
        if (string.IsNullOrWhiteSpace(job) ||
            string.IsNullOrWhiteSpace(componentKey) ||
            delta.LengthSquared() < 0.0001f)
            return false;

        var snapshot = FindJobGaugeSnapshot(job, componentKey, visibleOnly: false);
        if (snapshot is null)
            return false;

        var hadExistingFamily = configuration.NativeJobGaugePlacements?.TryGetValue(job, out var existingFamily) == true &&
                                existingFamily is not null;
        var family = GetOrCreateJobGaugeFamilyPlacement(job);
        if (hadExistingFamily)
            MigrateLegacyGroupedJobGaugePlacement(job, family, CollectJobGaugeSnapshots(job, visibleOnly: false));
        family.Components ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);

        var key = NormalizeJobGaugeAddonName(componentKey);
        if (!family.Components.TryGetValue(key, out var placement) || placement is null)
        {
            placement = new NativeJobGaugePlacement
            {
                X = snapshot.Value.X,
                Y = snapshot.Value.Y,
                OriginalX = snapshot.Value.X,
                OriginalY = snapshot.Value.Y,
                Scale = snapshot.Value.Scale,
                OriginalScale = snapshot.Value.Scale,
                HasOriginal = true,
            };
            family.Components[key] = placement;
        }
        else if (!placement.HasOriginal)
        {
            placement.OriginalX = snapshot.Value.X;
            placement.OriginalY = snapshot.Value.Y;
            placement.OriginalScale = snapshot.Value.Scale;
            placement.HasOriginal = true;
        }

        if (!MoveJobGaugeSnapshot(snapshot.Value, delta))
            return false;

        placement.X = MathF.Round(snapshot.Value.X + delta.X);
        placement.Y = MathF.Round(snapshot.Value.Y + delta.Y);
        placement.Scale = snapshot.Value.Scale;
        jobGaugeOverrideJob = job;
        return true;
    }

    public bool HasJobGaugePlacement(string job)
    {
        if (string.IsNullOrWhiteSpace(job) ||
            configuration.NativeJobGaugePlacements?.TryGetValue(job, out var family) != true ||
            family is null)
            return false;

        return family.HasOriginal || family.Components?.Count > 0;
    }

    public bool HasJobGaugeComponentPlacement(string job, string componentKey)
    {
        if (string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(componentKey) ||
            configuration.NativeJobGaugePlacements?.TryGetValue(job, out var family) != true ||
            family is null)
            return false;

        return family.Components?.TryGetValue(NormalizeJobGaugeAddonName(componentKey), out var placement) == true &&
               placement is not null;
    }

    public void ResetJobGaugeComponentPosition(string job, string componentKey)
    {
        if (string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(componentKey) ||
            configuration.NativeJobGaugePlacements?.TryGetValue(job, out var family) != true ||
            family is null)
            return;

        MigrateLegacyGroupedJobGaugePlacement(job, family, CollectJobGaugeSnapshots(job, visibleOnly: false));
        var key = NormalizeJobGaugeAddonName(componentKey);
        if (family.Components?.TryGetValue(key, out var placement) != true || placement is null)
            return;

        if (placement.HasOriginal)
            MoveJobGaugeComponentTo(job, key, new Vector2(placement.OriginalX, placement.OriginalY));

        family.Components.Remove(key);
        if (!family.HasOriginal && family.Components.Count == 0)
            configuration.NativeJobGaugePlacements.Remove(job);

        if (string.Equals(jobGaugeOverrideJob, job, StringComparison.OrdinalIgnoreCase) &&
            !HasJobGaugePlacement(job))
            jobGaugeOverrideJob = string.Empty;
    }

    public void ResetJobGaugePosition(string job)
    {
        if (string.IsNullOrWhiteSpace(job) ||
            configuration.NativeJobGaugePlacements?.TryGetValue(job, out var family) != true ||
            family is null)
            return;

        var snapshots = CollectJobGaugeSnapshots(job, visibleOnly: false);
        MigrateLegacyGroupedJobGaugePlacement(job, family, snapshots);

        if (family.Components?.Count is > 0)
        {
            foreach (var (key, placement) in family.Components.ToArray())
            {
                if (placement is not null && placement.HasOriginal)
                    MoveJobGaugeComponentTo(job, key, new Vector2(placement.OriginalX, placement.OriginalY));
            }
        }
        else if (family.HasOriginal)
        {
            MoveJobGaugeGroupTo(job, new Vector2(family.OriginalX, family.OriginalY));
        }

        configuration.NativeJobGaugePlacements.Remove(job);
        if (string.Equals(jobGaugeOverrideJob, job, StringComparison.OrdinalIgnoreCase))
            jobGaugeOverrideJob = string.Empty;
    }

    public void ResetAllJobGaugePositions()
    {
        var currentJob = GetCurrentJobAbbreviation();
        if (!string.IsNullOrWhiteSpace(currentJob))
            ResetJobGaugePosition(currentJob);

        configuration.NativeJobGaugePlacements?.Clear();
        jobGaugeOverrideJob = string.Empty;
    }

    private void ApplyConfiguredJobGaugePosition()
    {
        var job = GetCurrentJobAbbreviation();
        if (string.IsNullOrWhiteSpace(job) ||
            configuration.NativeJobGaugePlacements?.TryGetValue(job, out var family) != true ||
            family is null)
        {
            if (!string.Equals(jobGaugeOverrideJob, job, StringComparison.OrdinalIgnoreCase))
                jobGaugeOverrideJob = string.Empty;
            return;
        }

        var snapshots = CollectJobGaugeSnapshots(job, visibleOnly: false);
        if (snapshots.Count == 0)
            return;

        MigrateLegacyGroupedJobGaugePlacement(job, family, snapshots);
        if (family.Components?.Count is not > 0)
            return;

        var appliedAny = false;
        foreach (var snapshot in snapshots)
        {
            var key = NormalizeJobGaugeAddonName(snapshot.Name);
            if (!family.Components.TryGetValue(key, out var placement) || placement is null)
                continue;

            if (!placement.HasOriginal)
            {
                placement.OriginalX = snapshot.X;
                placement.OriginalY = snapshot.Y;
                placement.OriginalScale = snapshot.Scale;
                placement.HasOriginal = true;
            }

            var delta = new Vector2(placement.X - snapshot.X, placement.Y - snapshot.Y);
            if (delta.LengthSquared() >= 0.25f)
                MoveJobGaugeSnapshot(snapshot, delta);
            appliedAny = true;
        }

        if (appliedAny)
            jobGaugeOverrideJob = job;
    }

    private void RestoreActiveJobGaugePosition()
    {
        if (string.IsNullOrWhiteSpace(jobGaugeOverrideJob) ||
            configuration.NativeJobGaugePlacements?.TryGetValue(jobGaugeOverrideJob, out var family) != true ||
            family is null)
            return;

        var snapshots = CollectJobGaugeSnapshots(jobGaugeOverrideJob, visibleOnly: false);
        MigrateLegacyGroupedJobGaugePlacement(jobGaugeOverrideJob, family, snapshots);

        if (family.Components?.Count is > 0)
        {
            foreach (var (key, placement) in family.Components)
            {
                if (placement is not null && placement.HasOriginal)
                    MoveJobGaugeComponentTo(jobGaugeOverrideJob, key, new Vector2(placement.OriginalX, placement.OriginalY));
            }
        }
        else if (family.HasOriginal)
        {
            MoveJobGaugeGroupTo(jobGaugeOverrideJob, new Vector2(family.OriginalX, family.OriginalY));
        }

        jobGaugeOverrideJob = string.Empty;
    }

    private bool MoveJobGaugeGroupTo(string job, Vector2 desiredPosition)
    {
        var snapshots = CollectJobGaugeSnapshots(job, visibleOnly: false);
        if (snapshots.Count == 0)
            return false;

        var currentPosition = GetJobGaugeGroupAnchor(snapshots);
        MoveJobGaugeSnapshots(snapshots, desiredPosition - currentPosition);
        return true;
    }

    private bool MoveJobGaugeComponentTo(string job, string componentKey, Vector2 desiredPosition)
    {
        var snapshot = FindJobGaugeSnapshot(job, componentKey, visibleOnly: false);
        return snapshot is not null &&
               MoveJobGaugeSnapshot(snapshot.Value, desiredPosition - new Vector2(snapshot.Value.X, snapshot.Value.Y));
    }

    private NativeJobGaugePlacement GetOrCreateJobGaugeFamilyPlacement(string job)
    {
        configuration.NativeJobGaugePlacements ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (!configuration.NativeJobGaugePlacements.TryGetValue(job, out var family) || family is null)
        {
            family = new NativeJobGaugePlacement();
            configuration.NativeJobGaugePlacements[job] = family;
        }

        family.Components ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        return family;
    }


    private void MigrateLegacyGroupedJobGaugePlacement(
        string job,
        NativeJobGaugePlacement family,
        IReadOnlyList<NativeAddonSnapshot> snapshots)
    {
        family.Components ??= new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (family.Components.Count > 0 || snapshots.Count == 0)
            return;

        var currentGroupAnchor = GetJobGaugeGroupAnchor(snapshots);
        var desiredGroupAnchor = new Vector2(family.X, family.Y);
        var desiredDelta = desiredGroupAnchor - currentGroupAnchor;
        var legacyDelta = family.HasOriginal
            ? desiredGroupAnchor - new Vector2(family.OriginalX, family.OriginalY)
            : desiredDelta;

        foreach (var snapshot in snapshots)
        {
            var key = NormalizeJobGaugeAddonName(snapshot.Name);
            var currentPosition = new Vector2(snapshot.X, snapshot.Y);
            var desiredPosition = currentPosition + desiredDelta;
            var originalPosition = family.HasOriginal
                ? desiredPosition - legacyDelta
                : currentPosition;

            family.Components[key] = new NativeJobGaugePlacement
            {
                X = desiredPosition.X,
                Y = desiredPosition.Y,
                OriginalX = originalPosition.X,
                OriginalY = originalPosition.Y,
                Scale = snapshot.Scale,
                OriginalScale = snapshot.Scale,
                HasOriginal = true,
            };
        }

        family.HasOriginal = false;
        log.Information("RE:Frame split legacy grouped job-gauge placement for {Job} into {Count} independent components.", job, family.Components.Count);
    }

    private static Vector2 GetJobGaugeGroupAnchor(IReadOnlyList<NativeAddonSnapshot> snapshots)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        foreach (var snapshot in snapshots)
            min = Vector2.Min(min, new Vector2(snapshot.X, snapshot.Y));
        return min;
    }

    private NativeAddonSnapshot? FindJobGaugeSnapshot(string job, string componentKey, bool visibleOnly)
    {
        var normalized = NormalizeJobGaugeAddonName(componentKey);
        foreach (var snapshot in CollectJobGaugeSnapshots(job, visibleOnly))
        {
            if (string.Equals(NormalizeJobGaugeAddonName(snapshot.Name), normalized, StringComparison.OrdinalIgnoreCase))
                return snapshot;
        }

        return null;
    }

    private List<NativeAddonSnapshot> CollectJobGaugeSnapshots(string job, bool visibleOnly)
    {
        var snapshots = new List<NativeAddonSnapshot>(8);
        if (string.IsNullOrWhiteSpace(job))
            return snapshots;

        var seen = new HashSet<nint>();
        foreach (var name in GetJobGaugeAddonNames(job))
        {
            try
            {
                var addon = GetAddon(name);
                if (addon == null || !addon->IsReady || (visibleOnly && !addon->IsVisible))
                    continue;

                var address = (nint)addon;
                if (!seen.Add(address))
                    continue;

                short x = 0;
                short y = 0;
                ushort width = 0;
                ushort height = 0;
                addon->GetPosition(&x, &y);
                addon->GetSize(&width, &height, true);
                if (width == 0 || height == 0)
                    continue;

                snapshots.Add(new NativeAddonSnapshot(name, x, y, width, height, addon->RootNode != null ? addon->RootNode->ScaleX : 1f));
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame skipped volatile native job gauge addon {AddonName}.", name);
            }
        }

        return snapshots;
    }

    private void MoveJobGaugeSnapshots(IReadOnlyList<NativeAddonSnapshot> snapshots, Vector2 delta)
    {
        foreach (var snapshot in snapshots)
            MoveJobGaugeSnapshot(snapshot, delta);
    }

    private bool MoveJobGaugeSnapshot(NativeAddonSnapshot snapshot, Vector2 delta)
    {
        try
        {


            var addon = GetAddon(snapshot.Name);
            if (addon == null || !addon->IsReady)
                return false;

            addon->SetPosition(
                ClampToShort(snapshot.X + delta.X),
                ClampToShort(snapshot.Y + delta.Y));
            return true;
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not move native job gauge addon {AddonName}.", snapshot.Name);
            return false;
        }
    }

    private static string NormalizeJobGaugeAddonName(string name)
        => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim().TrimStart('_');

    private static int GetJobGaugeComponentOrder(string addonName)
    {
        var normalized = NormalizeJobGaugeAddonName(addonName);
        var index = normalized.Length - 1;
        while (index >= 0 && char.IsDigit(normalized[index]))
            index--;

        return index == normalized.Length - 1 ||
               !int.TryParse(normalized[(index + 1)..], out var order)
            ? int.MaxValue
            : order;
    }

    private static string GetJobGaugeComponentLabel(string job, string addonName)
    {
        var order = GetJobGaugeComponentOrder(addonName);
        var normalizedJob = string.IsNullOrWhiteSpace(job) ? "Job" : job.Trim().ToUpperInvariant();
        return normalizedJob switch
        {
            "RPR" when order == 0 => Localizer.Text("hud.native.job.soul", "Soul Gauge"),
            "RPR" when order == 1 => Localizer.Text("hud.native.job.death", "Death Gauge"),
            "VPR" when order == 0 => Localizer.Text("hud.native.job.vipersight", "Viper's Sight"),
            "VPR" when order == 1 => Localizer.Text("hud.native.job.serpentofferings", "Serpent Offerings Gauge"),
            _ when order != int.MaxValue => Localizer.Format("hud.native.job.component", "{0} Gauge {1}", normalizedJob, order + 1),
            _ => Localizer.Format("hud.native.job.component.single", "{0} Job Gauge", normalizedJob),
        };
    }

    public bool TryGetVisibleStatusEffectsBounds(out Vector2 position, out Vector2 size)
    {
        var snapshots = CollectStatusSnapshots(true); position = Vector2.Zero; size = Vector2.Zero; if (snapshots.Count == 0) return false;
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var snap in snapshots)
        {
            var p = new Vector2(snap.X, snap.Y);
            var scale = Math.Clamp(snap.Scale, 0.50f, 1.50f);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p + new Vector2(snap.Width, snap.Height));
        }
        if (max.X <= min.X || max.Y <= min.Y) return false; position=min; size=max-min; return true;
    }
    public bool MoveVisibleStatusEffects(Vector2 delta)
    {
        if (delta.LengthSquared() < .0001f || !TryGetVisibleStatusEffectsBounds(out var current, out _)) return false;
        var placement=configuration.NativeStatusEffectsPlacement ??= new NativeJobGaugePlacement();
        if (!placement.HasOriginal) { placement.OriginalX=current.X; placement.OriginalY=current.Y; placement.X=current.X; placement.Y=current.Y; var first=CollectStatusSnapshots(true); placement.OriginalScale=first.Count>0?first[0].Scale:1f; placement.Scale=placement.OriginalScale; placement.HasOriginal=true; }
        var snaps=CollectStatusSnapshots(true); if (snaps.Count==0) return false; MoveStatusSnapshots(snaps,delta); placement.X=MathF.Round(current.X+delta.X); placement.Y=MathF.Round(current.Y+delta.Y); statusEffectsOverrideActive=true; return true;
    }

    public bool ResizeVisibleStatusEffects(Vector2 delta)
    {
        if (!TryGetVisibleStatusEffectsBounds(out _, out var currentSize) || currentSize.X < 1f || currentSize.Y < 1f)
            return false;

        var placement = configuration.NativeStatusEffectsPlacement ??= new NativeJobGaugePlacement();
        var snapshots = CollectStatusSnapshots(true);
        if (snapshots.Count == 0)
            return false;

        var currentScale = Math.Clamp(snapshots[0].Scale, 0.50f, 1.50f);
        if (!placement.HasOriginal)
        {
            TryGetVisibleStatusEffectsBounds(out var currentPosition, out _);
            placement.OriginalX = currentPosition.X;
            placement.OriginalY = currentPosition.Y;
            placement.X = currentPosition.X;
            placement.Y = currentPosition.Y;
            placement.OriginalScale = currentScale;
            placement.Scale = currentScale;
            placement.HasOriginal = true;
        }

        var factorX = (currentSize.X + delta.X) / currentSize.X;
        var factorY = (currentSize.Y + delta.Y) / currentSize.Y;
        var factor = MathF.Max(factorX, factorY);
        if (!float.IsFinite(factor))
            return false;

        var nextScale = Math.Clamp(currentScale * factor, 0.50f, 1.50f);
        if (MathF.Abs(nextScale - currentScale) < 0.001f)
            return false;

        SetStatusEffectsScale(snapshots, nextScale);
        placement.Scale = nextScale;
        statusEffectsOverrideActive = true;
        return true;
    }

    private void ApplyConfiguredStatusEffectsScale()
    {
        var placement = configuration.NativeStatusEffectsPlacement;
        if (placement?.HasOriginal != true || placement.Scale <= 0f)
            return;
        SetStatusEffectsScale(CollectStatusSnapshots(true), Math.Clamp(placement.Scale, 0.50f, 1.50f));
    }

    private void SetStatusEffectsScale(IReadOnlyList<NativeAddonSnapshot> snapshots, float scale)
    {
        foreach (var snapshot in snapshots)
        {
            try
            {
                var addon = GetAddon(snapshot.Name);
                if (addon == null || !addon->IsReady || addon->RootNode == null)
                    continue;
                addon->RootNode->ScaleX = scale;
                addon->RootNode->ScaleY = scale;
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame could not resize status addon {AddonName}.", snapshot.Name);
            }
        }
    }

    public void ResetStatusEffectsPosition()
    {
        var p=configuration.NativeStatusEffectsPlacement; if (p?.HasOriginal==true) { MoveStatusGroupTo(new Vector2(p.OriginalX,p.OriginalY)); SetStatusEffectsScale(CollectStatusSnapshots(true), p.OriginalScale > 0f ? p.OriginalScale : 1f); } configuration.NativeStatusEffectsPlacement=new NativeJobGaugePlacement(); statusEffectsOverrideActive=false;
    }
    private void ApplyConfiguredStatusEffectsPosition()
    {
        var p=configuration.NativeStatusEffectsPlacement; if (p?.HasOriginal!=true || !TryGetVisibleStatusEffectsBounds(out var current,out _)) return; var d=new Vector2(p.X,p.Y)-current; if(d.LengthSquared()>=.25f) MoveStatusSnapshots(CollectStatusSnapshots(true),d);
        ApplyConfiguredStatusEffectsScale(); statusEffectsOverrideActive=true;
    }
    private void RestoreStatusEffectsPosition()
    { if (!statusEffectsOverrideActive) return; var p=configuration.NativeStatusEffectsPlacement; if(p?.HasOriginal==true) { MoveStatusGroupTo(new Vector2(p.OriginalX,p.OriginalY)); SetStatusEffectsScale(CollectStatusSnapshots(true), p.OriginalScale > 0f ? p.OriginalScale : 1f); } statusEffectsOverrideActive=false; }
    private bool MoveStatusGroupTo(Vector2 desired) { if(!TryGetVisibleStatusEffectsBounds(out var current,out _)) return false; var snaps=CollectStatusSnapshots(true); if(snaps.Count==0)return false; MoveStatusSnapshots(snaps,desired-current); return true; }
    private List<NativeAddonSnapshot> CollectStatusSnapshots(bool visibleOnly)
    {
        var candidates = new List<NativeAddonSnapshot>(4);
        var seen = new HashSet<nint>();
        foreach (var name in PlayerStatusAddons)
        {
            try
            {
                var addon = GetAddon(name);
                if (addon == null || !addon->IsReady || (visibleOnly && !addon->IsVisible) || !seen.Add((nint)addon))
                    continue;
                short x = 0, y = 0; ushort w = 0, h = 0;
                addon->GetPosition(&x, &y); addon->GetSize(&w, &h, true);
                if (w is > 0 and <= 1200 && h is > 0 and <= 320)
                    candidates.Add(new NativeAddonSnapshot(name, x, y, w, h, addon->RootNode != null ? addon->RootNode->ScaleX : 1f));
            }
            catch (Exception ex) { log.Verbose(ex, "RE:Frame skipped volatile status addon {AddonName}.", name); }
        }
        if (candidates.Count == 0) return candidates;


        var selected = candidates.FirstOrDefault(c =>
            string.Equals(c.Name, "_Status", StringComparison.Ordinal) ||
            string.Equals(c.Name, "Status", StringComparison.Ordinal));
        if (string.IsNullOrEmpty(selected.Name))
            selected = candidates.OrderByDescending(c => (long)c.Width * c.Height).First();
        return new List<NativeAddonSnapshot> { selected };
    }
    private void MoveStatusSnapshots(IReadOnlyList<NativeAddonSnapshot> snaps, Vector2 delta)
    { foreach(var snap in snaps) try { var addon=GetAddon(snap.Name); if(addon==null||!addon->IsReady) continue; addon->SetPosition(ClampToShort(snap.X+delta.X),ClampToShort(snap.Y+delta.Y)); } catch(Exception ex){ log.Verbose(ex,"RE:Frame could not move status addon {AddonName}.",snap.Name); } }

    private string GetCurrentJobAbbreviation()
    {
        if (!playerState.IsLoaded || !playerState.ClassJob.IsValid)
            return string.Empty;

        var abbreviation = playerState.ClassJob.Value.Abbreviation.ToString();
        return string.IsNullOrWhiteSpace(abbreviation) ? string.Empty : abbreviation;
    }

    private string[] GetJobGaugeAddonNames(string job)
    {
        if (string.IsNullOrWhiteSpace(job))
            return Array.Empty<string>();

        var names = new HashSet<string>(StringComparer.Ordinal);

        static void AddStem(HashSet<string> destination, string stem)
        {
            if (string.IsNullOrWhiteSpace(stem))
                return;

            for (var index = 0; index <= 3; index++)
            {
                destination.Add($"_JobHud{stem}{index}");
                destination.Add($"JobHud{stem}{index}");
            }
        }


        AddStem(names, job);


        if (job.Equals("RPR", StringComparison.OrdinalIgnoreCase))
            AddStem(names, "RRP");

        if (observedJobGaugeAddons.TryGetValue(job, out var observed))
        {
            foreach (var name in observed)
                names.Add(name);
        }

        return names.ToArray();
    }

    private static short ClampToShort(float value)
        => (short)Math.Clamp((int)MathF.Round(value), short.MinValue, short.MaxValue);


    public IReadOnlyList<string> GetVisibleIntegratedHoldouts()
    {
        var visible = new List<string>();
        foreach (var family in IntegratedHoldoutFamilies)
        {
            foreach (var name in family.Names)
            {
                var addon = GetAddon(name);
                if (addon == null || !addon->IsReady || !addon->IsVisible)
                    continue;

                visible.Add(family.Label);
                break;
            }
        }

        return visible;
    }


    public void SyncMinimapToFrame(HudBounds ringBounds, HudBounds viewportBounds, float interfaceScale)
    {
        if (disposed ||
            useForgeSquareMinimap() ||
            !configuration.ReplaceNativeHud ||
            !configuration.ShowHudOverlay ||
            !configuration.ShowMinimapFrame ||
            !clientState.IsLoggedIn)
        {
            RestoreMinimapIntegration();
            return;
        }


        if (IsNativeHudLayoutEditorOpen())
        {
            RestoreMinimapIntegration();
            nativeHudLayoutEditorWasOpen = true;
            SyncMinimapLayoutFromNativeEditor(ringBounds, viewportBounds);
            return;
        }

        if (nativeHudLayoutEditorWasOpen)
        {


            if (integratedMinimapComposition is not null)
            {
                RestoreMinimapIntegration();
                if (integratedMinimapComposition is not null)
                    return;
            }

            minimapInteractionBounds.Clear();
            minimapZoomOutVisualBounds = null;
            minimapZoomInVisualBounds = null;
            minimapNeedsValidation = false;
            minimapCenterCorrectionAttempted = false;
            minimapAnchorCorrection = Vector2.Zero;
            nativeHudLayoutEditorWasOpen = false;


            minimapNativeEditorSettleUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
            return;
        }

        if (DateTime.UtcNow < minimapNativeEditorSettleUntilUtc ||
            DateTime.UtcNow < minimapRetryAfterUtc)
            return;

        var live = TryGetVisibleMinimapSnapshot();
        if (live is not { } addonSnapshot)
            return;

        var addon = GetAddon(addonSnapshot.Name);
        if (addon == null || !addon->IsReady || !addon->IsVisible || addon->RootNode == null)
            return;

        var composition = integratedMinimapComposition;
        if (composition is not null && !composition.Matches(addon))
        {


            if (composition.AddonAddress == (nint)addon && composition.RootAddress == (nint)addon->RootNode)
                RestoreMinimapIntegration();
            else
            {
                integratedMinimapComposition = null;
                minimapInteractionBounds.Clear();
                minimapZoomOutVisualBounds = null;
                minimapZoomInVisualBounds = null;
                minimapNeedsValidation = false;
                minimapCenterCorrectionAttempted = false;
                minimapAnchorCorrection = Vector2.Zero;
            }

            composition = null;
        }

        if (composition is null)
        {
            composition = CaptureMinimapComposition(addon, addonSnapshot);
            if (composition is null)
                return;

            integratedMinimapComposition = composition;
            minimapNeedsValidation = true;
            minimapCenterCorrectionAttempted = false;
            minimapAnchorCorrection = Vector2.Zero;
            log.Information(
                "RE:Frame stripped and anchored {AddonName}: native map retained, {ChromeCount} chrome groups suppressed, coordinates {Coordinates}, celestial {Celestial}, zoom controls {ZoomControls}.",
                composition.Addon.Name,
                composition.ChromeNodes.Count,
                composition.Nodes.Any(node => node.Role == MinimapNodeRole.Coordinates),
                composition.Nodes.Any(node => node.Role == MinimapNodeRole.Celestial),
                composition.Nodes.Count(node => node.Role is MinimapNodeRole.ZoomIn or MinimapNodeRole.ZoomOut));
            foreach (var control in composition.Nodes.Where(node =>
                         node.Role is MinimapNodeRole.ZoomIn or MinimapNodeRole.ZoomOut))
            {
                log.Information(
                    "RE:Frame minimap {Role}: tree {Tree}, node {NodeId}, type {Type}, rect {MinX:0.0},{MinY:0.0}-{MaxX:0.0},{MaxY:0.0}.",
                    control.Role,
                    control.Tree,
                    control.NodeId,
                    control.Type,
                    control.ScreenRect.Min.X,
                    control.ScreenRect.Min.Y,
                    control.ScreenRect.Max.X,
                    control.ScreenRect.Max.Y);
            }
        }

        try
        {


            var ringCenter = ringBounds.Position + ringBounds.Size * 0.5f;
            var apertureRadius = HudLayout.MinimapApertureRadius(ringBounds, interfaceScale);
            var targetMapDiameter = MathF.Max(72f, apertureRadius * 2f);
            var sourceMapDiameter = MathF.Max(32f, MathF.Min(composition.MapRect.Size.X, composition.MapRect.Size.Y));
            var mapFit = Math.Clamp(targetMapDiameter / sourceMapDiameter, 0.40f, 3.00f);

            var nextRootScaleX = Math.Clamp(SafeScale(composition.RootScaleX) * mapFit, 0.25f, 3f);
            var nextRootScaleY = Math.Clamp(SafeScale(composition.RootScaleY) * mapFit, 0.25f, 3f);
            if (MathF.Abs(addon->RootNode->ScaleX - nextRootScaleX) >= 0.0025f ||
                MathF.Abs(addon->RootNode->ScaleY - nextRootScaleY) >= 0.0025f)
            {
                addon->RootNode->SetScale(nextRootScaleX, nextRootScaleY);
                addon->RootNode->IsDirty = true;
            }

            var rootPosition = ringCenter - new Vector2(
                composition.MapCenterLocal.X * nextRootScaleX,
                composition.MapCenterLocal.Y * nextRootScaleY);
            short currentX = 0;
            short currentY = 0;
            addon->GetPosition(&currentX, &currentY);
            var targetX = ClampToShort(rootPosition.X);
            var targetY = ClampToShort(rootPosition.Y);

            if (Math.Abs(currentX - targetX) > 1 || Math.Abs(currentY - targetY) > 1)
                addon->SetPosition(targetX, targetY);

            var manager = &addon->UldManager;
            var mapNode = composition.Nodes.FirstOrDefault(node => node.Role == MinimapNodeRole.Map);
            if (mapNode.Address == 0 || ResolveMinimapNode(manager, null, mapNode) == null)
            {
                RejectMinimapIntegration("the native map canvas was rebuilt");
                return;
            }


            minimapNeedsValidation = false;
            minimapCenterCorrectionAttempted = false;
            minimapAnchorCorrection = Vector2.Zero;
            minimapZoomInVisualBounds = null;
            minimapZoomOutVisualBounds = null;
            minimapInteractionBounds.Clear();
            minimapInteractionBounds.Add(ringBounds);
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not position the complete native minimap widget.");
            RestoreMinimapIntegration();
        }
    }

    private bool IsNativeHudLayoutEditorOpen()
    {


        string[] names = { "_HudLayout", "HudLayout", "_HudLayoutScreen", "HudLayoutScreen" };
        foreach (var name in names)
        {
            try
            {
                var addon = GetAddon(name);
                if (addon != null && addon->IsReady && addon->IsVisible)
                    return true;
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame skipped volatile HUD Layout addon {AddonName}.", name);
            }
        }

        return false;
    }

    private void SyncMinimapLayoutFromNativeEditor(HudBounds currentRingBounds, HudBounds viewportBounds)
    {
        try
        {
            var live = TryGetVisibleMinimapSnapshot();
            if (live is not { } snapshot)
                return;

            var addon = GetAddon(snapshot.Name);
            if (addon == null || !addon->IsReady || !addon->IsVisible)
                return;

            var mapNode = FindNativeMapNode(&addon->UldManager);
            if (mapNode == null || !TryGetNodeScreenRect(mapNode, out var mapRect))
                return;

            var desiredPosition = mapRect.Center - currentRingBounds.Size * 0.5f;
            if (Vector2.DistanceSquared(desiredPosition, currentRingBounds.Position) < 1f)
                return;

            HudLayout.Store(
                configuration,
                HudElementIds.Minimap,
                new HudBounds(desiredPosition, currentRingBounds.Size),
                viewportBounds.Position,
                viewportBounds.Size,
                adaptiveState.EffectiveMode);
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not adopt the vanilla HUD editor minimap position.");
        }
    }

    private void RejectMinimapIntegration(string reason)
    {
        RestoreMinimapIntegration();
        minimapNeedsValidation = false;
        minimapCenterCorrectionAttempted = false;
        minimapAnchorCorrection = Vector2.Zero;
        minimapNativeEditorSettleUntilUtc = DateTime.MinValue;
        minimapRetryAfterUtc = DateTime.UtcNow.AddSeconds(2);
        log.Warning("RE:Frame restored the native minimap because {Reason}.", reason);
    }

    private static bool TryValidateIntegratedMap(
        AtkUldManager* manager,
        MinimapNodeTransform mapSnapshot,
        HudBounds ringBounds)
    {
        var mapNode = ResolveMinimapNode(manager, null, mapSnapshot);
        if (mapNode == null || !mapNode->IsVisible() || !TryGetNodeScreenRect(mapNode, out var currentRect))
            return false;

        var ringRect = new NativeNodeRect(ringBounds.Position, ringBounds.Position + ringBounds.Size);
        var overlap = currentRect.IntersectionArea(ringRect);
        var mapArea = MathF.Max(1f, currentRect.Size.X * currentRect.Size.Y);
        var ringCenter = ringBounds.Position + ringBounds.Size * 0.5f;
        var centerDistance = Vector2.Distance(currentRect.Center, ringCenter);
        var ringRadius = MathF.Max(1f, MathF.Min(ringBounds.Size.X, ringBounds.Size.Y) * 0.5f);

        return currentRect.Size.X >= 32f &&
               currentRect.Size.Y >= 32f &&
               overlap >= mapArea * 0.20f &&
               centerDistance <= ringRadius * 0.55f;
    }

    public bool IsPointInsideIntegratedMinimap(Vector2 point)
    {
        foreach (var bounds in minimapInteractionBounds)
        {
            var max = bounds.Position + bounds.Size;
            if (point.X >= bounds.Position.X && point.X <= max.X &&
                point.Y >= bounds.Position.Y && point.Y <= max.Y)
                return true;
        }

        return false;
    }

    public bool TryGetIntegratedMinimapZoomBounds(out HudBounds zoomOut, out HudBounds zoomIn)
    {
        if (minimapZoomOutVisualBounds is { } currentZoomOut &&
            minimapZoomInVisualBounds is { } currentZoomIn)
        {
            zoomOut = currentZoomOut;
            zoomIn = currentZoomIn;
            return true;
        }

        zoomOut = default;
        zoomIn = default;
        return false;
    }

    public void RestoreMinimapIntegration()
    {
        var composition = integratedMinimapComposition;
        if (composition is null)
        {
            minimapInteractionBounds.Clear();
            minimapZoomOutVisualBounds = null;
            minimapZoomInVisualBounds = null;
            minimapNeedsValidation = false;
            minimapCenterCorrectionAttempted = false;
            minimapAnchorCorrection = Vector2.Zero;
            return;
        }

        var restored = false;
        try
        {
            var addon = GetAddon(composition.Addon.Name);
            if (addon != null && addon->IsReady && addon->RootNode != null)
            {
                addon->SetPosition(composition.Addon.X, composition.Addon.Y);
                addon->RootNode->ScaleX = SafeScale(composition.RootScaleX);
                addon->RootNode->ScaleY = SafeScale(composition.RootScaleY);
                addon->RootNode->IsDirty = true;

                var manager = &addon->UldManager;
                var mapSnapshot = composition.Nodes.FirstOrDefault(node => node.Role == MinimapNodeRole.Map);
                var mapNode = ResolveMinimapNode(manager, null, mapSnapshot);
                var mapComponentManager = GetNativeMapComponentManager(mapNode);

                foreach (var nodeSnapshot in composition.Nodes)
                {
                    var node = ResolveMinimapNode(manager, mapComponentManager, nodeSnapshot);
                    if (node == null)
                        continue;

                    node->SetPositionFloat(nodeSnapshot.X, nodeSnapshot.Y);
                    node->SetScale(nodeSnapshot.ScaleX, nodeSnapshot.ScaleY);
                    node->IsDirty = true;
                }
                foreach (var chromeSnapshot in composition.ChromeNodes)
                {
                    var ownerManager = chromeSnapshot.Tree == MinimapNodeTree.MapComponent
                        ? mapComponentManager
                        : manager;
                    var node = ResolveMinimapChromeNode(ownerManager, chromeSnapshot);
                    if (node == null)
                        continue;

                    node->SetAlpha(chromeSnapshot.Alpha);
                    node->ToggleVisibility(chromeSnapshot.WasVisible);
                    node->IsDirty = true;
                }

                restored = true;
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame deferred restoring the integrated native minimap.");
        }

        minimapInteractionBounds.Clear();
        minimapZoomOutVisualBounds = null;
        minimapZoomInVisualBounds = null;
        minimapNeedsValidation = false;
        minimapCenterCorrectionAttempted = false;
        minimapAnchorCorrection = Vector2.Zero;
        if (restored || disposed)
            integratedMinimapComposition = null;
    }

    private MinimapCompositionSnapshot? CaptureMinimapComposition(AtkUnitBase* addon, NativeAddonSnapshot addonSnapshot)
    {
        var manager = &addon->UldManager;
        var root = addon->RootNode;
        if (root == null || manager->NodeList == null || manager->NodeListCount == 0)
            return null;

        var mapNode = FindNativeMapNode(manager);
        if (mapNode == null || !TryGetNodeScreenRect(mapNode, out var outerMapRect))
            return null;

        var mapComponent = GetNativeMapComponent(mapNode);
        var mapComponentManager = mapComponent != null ? &mapComponent->UldManager : null;
        var hasExactMapContent = TryFindNativeMapContentRect(mapComponentManager, outerMapRect, out var mapRect);
        var outerDiameter = MathF.Max(1f, MathF.Min(outerMapRect.Size.X, outerMapRect.Size.Y));
        var fallbackMapRect = outerMapRect.Inset(outerDiameter * 0.105f);
        var detectedDiameter = MathF.Min(mapRect.Size.X, mapRect.Size.Y);
        var detectedOffset = Vector2.Distance(mapRect.Center, outerMapRect.Center);
        if (!hasExactMapContent ||
            detectedDiameter > outerDiameter * 0.88f ||
            detectedOffset > outerDiameter * 0.10f)
        {


            mapRect = fallbackMapRect;
            hasExactMapContent = false;
        }
        else
        {


            mapRect = mapRect.Inset(outerDiameter * 0.012f);
        }

        var coordinateTextNode = FindCoordinateTextNode(manager, mapRect);
        var celestialNode = FindCelestialNode(manager, mapNode, coordinateTextNode, mapRect);
        var coordinateRoot = FindIndependentTransformRoot(coordinateTextNode, root, mapNode, null);
        var celestialRoot = FindIndependentTransformRoot(celestialNode, root, mapNode, coordinateTextNode);
        var controlRows = FindNativeMinimapControlRows(
            addon,
            manager,
            mapComponentManager,
            mapNode,
            coordinateRoot,
            celestialRoot,
            mapRect);


        var interactiveControlRows = controlRows
            .Where(row => row.Any(candidate => candidate.IsCollision))
            .OrderBy(row => row.Average(candidate => candidate.ScreenRect.Center.Y))
            .ToList();
        var functionalControlRows = interactiveControlRows.Count >= 2
            ? interactiveControlRows
            : controlRows;
        var zoomInControls = functionalControlRows.Count > 0
            ? functionalControlRows[0]
            : new List<MinimapControlCandidate>();
        var zoomOutControls = functionalControlRows.Count > 1
            ? functionalControlRows[1]
            : new List<MinimapControlCandidate>();

        var chromeNodes = FindNativeMinimapChromeNodes(
            manager,
            root,
            mapNode,
            coordinateRoot,
            celestialRoot,
            mapRect);
        if (hasExactMapContent && mapComponentManager != null)
        {


            chromeNodes.AddRange(FindNativeMapComponentChromeNodes(
                mapComponentManager,
                mapRect));
        }


        chromeNodes.AddRange(FindCompassLetterNodes(manager, mapComponentManager, outerMapRect));
        chromeNodes.AddRange(FindPeripheralCompassGlyphNodes(
            manager,
            mapComponentManager,
            mapRect,
            coordinateRoot,
            celestialRoot,
            controlRows));


        var zoomAddresses = zoomInControls
            .Concat(zoomOutControls)
            .Select(control => (control.Tree, control.Address))
            .ToHashSet();
        foreach (var control in controlRows.SelectMany(row => row))
        {
            if (zoomAddresses.Contains((control.Tree, control.Address)))
                continue;

            var controlNode = (AtkResNode*)control.Address;
            AddChromeSnapshot(chromeNodes, control.Tree, controlNode);
        }


        var nodeSnapshots = new List<MinimapNodeTransform>(10);
        var seen = new HashSet<nint>();
        AddMinimapNodeSnapshot(nodeSnapshots, seen, mapNode, MinimapNodeRole.Map);

        if (coordinateRoot != null && !IsDescendantOrSelf(mapNode, coordinateRoot))
            AddMinimapNodeSnapshot(nodeSnapshots, seen, coordinateRoot, MinimapNodeRole.Coordinates);
        if (celestialRoot != null && !IsDescendantOrSelf(mapNode, celestialRoot))
            AddMinimapNodeSnapshot(nodeSnapshots, seen, celestialRoot, MinimapNodeRole.Celestial);

        AddMinimapControlGroupSnapshots(
            nodeSnapshots,
            seen,
            zoomInControls,
            MinimapNodeRole.ZoomIn);
        AddMinimapControlGroupSnapshots(
            nodeSnapshots,
            seen,
            zoomOutControls,
            MinimapNodeRole.ZoomOut);

        var protectedAccessories = nodeSnapshots
            .Where(node => node.Role is MinimapNodeRole.Coordinates or MinimapNodeRole.Celestial or MinimapNodeRole.ZoomIn or MinimapNodeRole.ZoomOut)
            .ToList();
        RemoveChromeRelatedToProtectedNodes(
            chromeNodes,
            manager,
            mapComponentManager,
            protectedAccessories);


        if (!nodeSnapshots.Any(node => node.Role == MinimapNodeRole.Map))
            return null;

        var rootScaleX = SafeScale(root->ScaleX);
        var rootScaleY = SafeScale(root->ScaleY);
        var addonPosition = new Vector2(addonSnapshot.X, addonSnapshot.Y);
        var mapCenterLocal = new Vector2(
            (mapRect.Center.X - addonPosition.X) / rootScaleX,
            (mapRect.Center.Y - addonPosition.Y) / rootScaleY);

        return new MinimapCompositionSnapshot(
            addonSnapshot,
            (nint)addon,
            (nint)root,
            (nint)manager->NodeList,
            manager->NodeListCount,
            (nint)mapComponent,
            mapComponentManager != null ? (nint)mapComponentManager->NodeList : 0,
            rootScaleX,
            rootScaleY,
            mapRect,
            mapCenterLocal,
            nodeSnapshots,
            chromeNodes);
    }

    private static AtkResNode* FindNativeMapNode(AtkUldManager* manager)
    {
        AtkResNode* best = null;
        var bestArea = 0f;
        var count = Math.Min((int)manager->NodeListCount, 512);
        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node == null)
                continue;

            var rawType = (ushort)node->Type;
            if (rawType >= 1000)
            {
                var componentNode = (AtkComponentNode*)node;
                if (componentNode->Component != null && componentNode->Component->GetComponentType() == ComponentType.Map &&
                    TryGetNodeScreenRect(node, out var mapRect))
                {
                    var area = mapRect.Size.X * mapRect.Size.Y;
                    if (area > bestArea)
                    {
                        best = node;
                        bestArea = area;
                    }
                }
            }
        }

        if (best != null)
            return best;


        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || node->Type == NodeType.Text || node->Type == NodeType.Collision ||
                !TryGetNodeScreenRect(node, out var rect))
                continue;

            var width = rect.Size.X;
            var height = rect.Size.Y;
            if (width < 80f || height < 80f)
                continue;

            var aspect = width / MathF.Max(1f, height);
            if (aspect is < 0.72f or > 1.38f)
                continue;

            var area = width * height;
            if (area > bestArea)
            {
                best = node;
                bestArea = area;
            }
        }

        return best;
    }

    private static AtkComponentBase* GetNativeMapComponent(AtkResNode* mapNode)
    {
        if (mapNode == null || (ushort)mapNode->Type < 1000)
            return null;

        var componentNode = (AtkComponentNode*)mapNode;
        var component = componentNode->Component;
        if (component == null || component->GetComponentType() != ComponentType.Map)
            return null;

        return component;
    }

    private static AtkUldManager* GetNativeMapComponentManager(AtkResNode* mapNode)
    {
        var component = GetNativeMapComponent(mapNode);
        if (component == null || component->UldManager.NodeList == null || component->UldManager.NodeListCount == 0)
            return null;

        return &component->UldManager;
    }

    private static bool TryFindNativeMapContentRect(
        AtkUldManager* componentManager,
        NativeNodeRect outerMapRect,
        out NativeNodeRect contentRect)
    {
        contentRect = outerMapRect;
        if (componentManager == null || componentManager->NodeList == null || componentManager->NodeListCount == 0)
            return false;

        AtkResNode* bestMask = null;
        NativeNodeRect bestRect = default;
        var bestArea = 0f;
        var outerDiameter = MathF.Max(1f, MathF.Min(outerMapRect.Size.X, outerMapRect.Size.Y));
        var count = Math.Min((int)componentManager->NodeListCount, 512);


        for (var index = 0; index < count; index++)
        {
            var node = componentManager->NodeList[index];
            if (node == null || node->Type != NodeType.ClippingMask || !TryGetNodeScreenRect(node, out var rect))
                continue;

            var width = rect.Size.X;
            var height = rect.Size.Y;
            var aspect = width / MathF.Max(1f, height);
            var centerDistance = Vector2.Distance(rect.Center, outerMapRect.Center);
            if (width < outerDiameter * 0.48f || height < outerDiameter * 0.48f ||
                width > outerDiameter * 1.08f || height > outerDiameter * 1.08f ||
                aspect is < 0.72f or > 1.38f ||
                centerDistance > outerDiameter * 0.22f)
                continue;

            var area = width * height;
            if (area <= bestArea)
                continue;

            bestMask = node;
            bestRect = rect;
            bestArea = area;
        }

        if (bestMask == null)
            return false;

        contentRect = bestRect;
        return true;
    }

    private static List<MinimapChromeNodeSnapshot> FindNativeMapComponentChromeNodes(
        AtkUldManager* componentManager,
        NativeNodeRect contentRect)
    {
        var snapshots = new List<MinimapChromeNodeSnapshot>();
        if (componentManager == null || componentManager->NodeList == null)
            return snapshots;

        var seen = new HashSet<nint>();
        var diameter = MathF.Max(1f, MathF.Min(contentRect.Size.X, contentRect.Size.Y));
        var count = Math.Min((int)componentManager->NodeListCount, 512);
        for (var index = 0; index < count; index++)
        {
            var node = componentManager->NodeList[index];
            if (node == null || node == componentManager->RootNode || node->Type == NodeType.ClippingMask ||
                node->Type == NodeType.Text || node->Type == NodeType.Collision ||
                !TryGetNodeScreenRect(node, out var rect))
                continue;

            var rawType = (ushort)node->Type;
            var isVisual = node->Type is NodeType.Image or NodeType.NineGrid or NodeType.Counter || rawType >= 1000;
            if (!isVisual)
                continue;

            var width = rect.Size.X;
            var height = rect.Size.Y;
            var centerDistance = Vector2.Distance(rect.Center, contentRect.Center);
            var extendsOutsideContent =
                rect.Min.X < contentRect.Min.X - 2f ||
                rect.Min.Y < contentRect.Min.Y - 2f ||
                rect.Max.X > contentRect.Max.X + 2f ||
                rect.Max.Y > contentRect.Max.Y + 2f;


            var isOuterShell =
                extendsOutsideContent &&
                centerDistance <= diameter * 0.20f &&
                width >= diameter * 0.90f && width <= diameter * 1.55f &&
                height >= diameter * 0.90f && height <= diameter * 1.55f;
            var isRightControl =
                rect.Center.X > contentRect.Max.X + diameter * 0.01f &&
                rect.Center.X <= contentRect.Max.X + diameter * 0.46f &&
                rect.Center.Y >= contentRect.Min.Y - diameter * 0.12f &&
                rect.Center.Y <= contentRect.Max.Y + diameter * 0.14f &&
                width >= 5f && height >= 5f &&
                width <= diameter * 0.45f && height <= diameter * 0.65f;

            if ((!isOuterShell && !isRightControl) || !seen.Add((nint)node))
                continue;

            snapshots.Add(new MinimapChromeNodeSnapshot(
                MinimapNodeTree.MapComponent,
                (nint)node,
                node->NodeId,
                node->Type,
                node->Color.A,
                node->IsVisible()));
        }

        return snapshots;
    }

    private static AtkResNode* FindCoordinateTextNode(AtkUldManager* manager, NativeNodeRect mapRect)
    {
        var count = Math.Min((int)manager->NodeListCount, 512);
        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || node->Type != NodeType.Text)
                continue;

            try
            {
                var text = ((AtkTextNode*)node)->NodeText.ToString();
                if (text.Contains("X:", StringComparison.OrdinalIgnoreCase) &&
                    text.Contains("Y:", StringComparison.OrdinalIgnoreCase))
                    return node;
            }
            catch
            {


            }
        }

        AtkResNode* best = null;
        var bestScore = float.MaxValue;
        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || node->Type != NodeType.Text || !TryGetNodeScreenRect(node, out var rect))
                continue;

            if (rect.Size.X < 40f || rect.Size.X > diameter * 1.15f ||
                rect.Size.Y < 7f || rect.Size.Y > diameter * 0.24f ||
                rect.Center.Y < mapRect.Max.Y - diameter * 0.08f ||
                rect.Center.Y > mapRect.Max.Y + diameter * 0.42f ||
                MathF.Abs(rect.Center.X - mapRect.Center.X) > diameter * 0.42f)
                continue;

            var score = MathF.Abs(rect.Center.X - mapRect.Center.X) +
                        MathF.Abs(rect.Center.Y - (mapRect.Max.Y + diameter * 0.10f));
            if (score < bestScore)
            {
                best = node;
                bestScore = score;
            }
        }

        return best;
    }

    private static AtkResNode* FindCelestialNode(
        AtkUldManager* manager,
        AtkResNode* mapNode,
        AtkResNode* coordinateNode,
        NativeNodeRect mapRect)
    {
        AtkResNode* best = null;
        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        var target = new Vector2(mapRect.Center.X, mapRect.Max.Y + diameter * 0.03f);
        var bestScore = float.MaxValue;
        var count = Math.Min((int)manager->NodeListCount, 512);

        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || node == mapNode || node == coordinateNode ||
                IsDescendantOrSelf(node, mapNode) || IsDescendantOrSelf(mapNode, node) ||
                (coordinateNode != null &&
                 (IsDescendantOrSelf(node, coordinateNode) || IsDescendantOrSelf(coordinateNode, node))) ||
                !TryGetNodeScreenRect(node, out var rect))
                continue;

            var rawType = (ushort)node->Type;
            if (rawType != (ushort)NodeType.Image && rawType != (ushort)NodeType.NineGrid && rawType < 1000)
                continue;

            var width = rect.Size.X;
            var height = rect.Size.Y;
            if (width < 5f || height < 5f || width > diameter * 0.42f || height > diameter * 0.42f)
                continue;

            var center = rect.Center;
            if (center.X < mapRect.Center.X - diameter * 0.42f ||
                center.X > mapRect.Center.X + diameter * 0.42f ||
                center.Y < mapRect.Center.Y + diameter * 0.26f ||
                center.Y > mapRect.Max.Y + diameter * 0.38f)
                continue;

            var distance = Vector2.Distance(center, target);
            var sizePenalty = MathF.Abs(width - height) * 0.35f;
            var score = distance + sizePenalty;
            if (score < bestScore)
            {
                best = node;
                bestScore = score;
            }
        }

        return best;
    }

    private static List<List<MinimapControlCandidate>> FindNativeMinimapControlRows(
        AtkUnitBase* addon,
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        AtkResNode* mapNode,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot,
        NativeNodeRect outerMapRect)
    {
        var candidates = new List<MinimapControlCandidate>();


        CollectNativeMinimapCollisionControls(
            candidates,
            addon,
            manager != null ? manager->RootNode : null,
            mapNode,
            coordinateRoot,
            celestialRoot,
            outerMapRect);
        CollectNativeMinimapControls(
            candidates,
            manager,
            manager != null ? manager->RootNode : null,
            MinimapNodeTree.Addon,
            mapNode,
            coordinateRoot,
            celestialRoot,
            outerMapRect);
        CollectNativeMinimapControls(
            candidates,
            mapComponentManager,
            mapComponentManager != null ? mapComponentManager->RootNode : null,
            MinimapNodeTree.MapComponent,
            null,
            null,
            null,
            outerMapRect);

        var ordered = candidates
            .Where(candidate => candidate.Address != 0)
            .GroupBy(candidate => (candidate.Tree, candidate.Address))
            .Select(group => group.First())
            .OrderBy(candidate => candidate.ScreenRect.Center.Y)
            .ThenBy(candidate => candidate.ScreenRect.Center.X)
            .ToList();


        var distinct = new List<MinimapControlCandidate>(ordered.Count);
        foreach (var candidate in ordered)
        {
            var duplicateIndex = distinct.FindIndex(existing =>
                existing.Tree == candidate.Tree &&
                existing.IsCollision == candidate.IsCollision &&
                (IsDescendantOrSelf((AtkResNode*)candidate.Address, (AtkResNode*)existing.Address) ||
                 IsDescendantOrSelf((AtkResNode*)existing.Address, (AtkResNode*)candidate.Address)));
            if (duplicateIndex < 0)
            {
                distinct.Add(candidate);
                continue;
            }

            var existingArea = distinct[duplicateIndex].ScreenRect.Size.X * distinct[duplicateIndex].ScreenRect.Size.Y;
            var candidateArea = candidate.ScreenRect.Size.X * candidate.ScreenRect.Size.Y;
            if (candidateArea > existingArea)
                distinct[duplicateIndex] = candidate;
        }

        var diameter = MathF.Max(1f, MathF.Min(outerMapRect.Size.X, outerMapRect.Size.Y));
        var rowTolerance = MathF.Max(7f, diameter * 0.050f);
        var rows = new List<List<MinimapControlCandidate>>(3);
        foreach (var candidate in distinct
                     .OrderBy(candidate => candidate.ScreenRect.Center.Y)
                     .ThenBy(candidate => MathF.Abs(candidate.ScreenRect.Center.X - outerMapRect.Max.X)))
        {
            List<MinimapControlCandidate>? row = null;
            foreach (var existingRow in rows)
            {
                var rowY = existingRow.Average(item => item.ScreenRect.Center.Y);
                if (MathF.Abs(rowY - candidate.ScreenRect.Center.Y) <= rowTolerance)
                {
                    row = existingRow;
                    break;
                }
            }

            if (row is null)
            {
                row = new List<MinimapControlCandidate>();
                rows.Add(row);
            }

            row.Add(candidate);
        }

        return rows
            .OrderBy(row => row.Average(item => item.ScreenRect.Center.Y))
            .Select(PruneMinimapControlRow)
            .Where(row => row.Count > 0)
            .Take(8)
            .ToList();
    }

    private static List<MinimapControlCandidate> PruneMinimapControlRow(List<MinimapControlCandidate> row)
    {
        var result = new List<MinimapControlCandidate>(row.Count);
        foreach (var treeGroup in row.GroupBy(candidate => candidate.Tree))
        {
            var byAddress = treeGroup
                .GroupBy(candidate => candidate.Address)
                .ToDictionary(group => group.Key, group => group.First());
            var addresses = PruneNestedTransformRoots(byAddress.Keys.ToList());
            foreach (var address in addresses)
                result.Add(byAddress[address]);
        }

        return result;
    }

    private static void CollectNativeMinimapControls(
        List<MinimapControlCandidate> candidates,
        AtkUldManager* ownerManager,
        AtkResNode* ownerRoot,
        MinimapNodeTree tree,
        AtkResNode* mapNode,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot,
        NativeNodeRect outerMapRect)
    {
        if (ownerManager == null || ownerManager->NodeList == null)
            return;

        var count = Math.Min((int)ownerManager->NodeListCount, 512);
        for (var index = 0; index < count; index++)
        {
            var node = ownerManager->NodeList[index];
            if (node == null || !node->IsVisible() || !TryGetNodeScreenRect(node, out var rect) ||
                !IsMinimapControlRailRect(rect, outerMapRect))
                continue;

            var rawType = (ushort)node->Type;
            var isButtonComponent = false;
            if (rawType >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                isButtonComponent = component != null && component->GetComponentType() == ComponentType.Button;
            }


            var isCollision = node->Type == NodeType.Collision;
            var isVisual = node->Type is NodeType.Image or NodeType.NineGrid or NodeType.Counter;
            if (!isButtonComponent && !isCollision && !isVisual)
                continue;

            if ((coordinateRoot != null &&
                 (IsDescendantOrSelf(node, coordinateRoot) || IsDescendantOrSelf(coordinateRoot, node))) ||
                (celestialRoot != null &&
                 (IsDescendantOrSelf(node, celestialRoot) || IsDescendantOrSelf(celestialRoot, node))))
                continue;

            var transformRoot = FindMinimapControlTransformRoot(
                node,
                ownerRoot,
                tree == MinimapNodeTree.Addon ? mapNode : null,
                outerMapRect);
            if (transformRoot == null || !TryGetNodeScreenRect(transformRoot, out var rootRect) ||
                !IsMinimapControlRailRect(rootRect, outerMapRect))
                continue;

            candidates.Add(new MinimapControlCandidate(
                tree,
                (nint)transformRoot,
                rootRect,
                isCollision));
        }
    }

    private static void CollectNativeMinimapCollisionControls(
        List<MinimapControlCandidate> candidates,
        AtkUnitBase* addon,
        AtkResNode* ownerRoot,
        AtkResNode* mapNode,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot,
        NativeNodeRect outerMapRect)
    {
        if (addon == null || addon->CollisionNodeList == null || addon->CollisionNodeListCount == 0)
            return;

        var count = Math.Min((int)addon->CollisionNodeListCount, 128);
        for (var index = 0; index < count; index++)
        {
            var collision = (AtkResNode*)addon->CollisionNodeList[index];
            if (collision == null || !collision->IsVisible() ||
                !TryGetNodeScreenRect(collision, out var rect) ||
                !IsMinimapControlRailRect(rect, outerMapRect))
                continue;

            if ((coordinateRoot != null &&
                 (IsDescendantOrSelf(collision, coordinateRoot) || IsDescendantOrSelf(coordinateRoot, collision))) ||
                (celestialRoot != null &&
                 (IsDescendantOrSelf(collision, celestialRoot) || IsDescendantOrSelf(celestialRoot, collision))))
                continue;

            var transformRoot = FindMinimapControlTransformRoot(collision, ownerRoot, mapNode, outerMapRect);
            if (transformRoot == null || !TryGetNodeScreenRect(transformRoot, out var rootRect) ||
                !IsMinimapControlRailRect(rootRect, outerMapRect))
                continue;

            candidates.Add(new MinimapControlCandidate(
                MinimapNodeTree.Addon,
                (nint)transformRoot,
                rootRect,
                true));
        }
    }

    private static bool IsMinimapControlRailRect(NativeNodeRect rect, NativeNodeRect outerMapRect)
    {
        var diameter = MathF.Max(1f, MathF.Min(outerMapRect.Size.X, outerMapRect.Size.Y));
        var width = rect.Size.X;
        var height = rect.Size.Y;
        return width >= 6f && height >= 6f &&
               width <= diameter * 0.34f && height <= diameter * 0.34f &&
               rect.Center.X >= outerMapRect.Center.X + diameter * 0.31f &&
               rect.Center.X <= outerMapRect.Max.X + diameter * 0.40f &&
               rect.Center.Y >= outerMapRect.Min.Y + diameter * 0.16f &&
               rect.Center.Y <= outerMapRect.Max.Y + diameter * 0.16f;
    }

    private static AtkResNode* FindMinimapControlTransformRoot(
        AtkResNode* node,
        AtkResNode* ownerRoot,
        AtkResNode* protectedMapNode,
        NativeNodeRect outerMapRect)
    {
        if (node == null)
            return null;

        var current = node;
        if (!TryGetNodeScreenRect(current, out var currentRect))
            return current;

        var mapArea = MathF.Max(1f, outerMapRect.Size.X * outerMapRect.Size.Y);
        for (var depth = 0; depth < 12; depth++)
        {
            var parent = current->ParentNode;
            if (parent == null || parent == ownerRoot || parent == protectedMapNode ||
                (protectedMapNode != null && IsDescendantOrSelf(protectedMapNode, parent)) ||
                !TryGetNodeScreenRect(parent, out var parentRect))
                break;

            var currentArea = MathF.Max(1f, currentRect.Size.X * currentRect.Size.Y);
            var parentArea = MathF.Max(1f, parentRect.Size.X * parentRect.Size.Y);
            var overlap = parentRect.IntersectionArea(currentRect);
            if (parentArea > mapArea * 0.16f ||
                parentArea > currentArea * 7f ||
                overlap < currentArea * 0.55f ||
                !IsMinimapControlRailRect(parentRect, outerMapRect))
                break;

            current = parent;
            currentRect = parentRect;
        }

        return current;
    }

    private static List<MinimapChromeNodeSnapshot> FindCompassLetterNodes(
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        NativeNodeRect mapRect)
    {
        var snapshots = new List<MinimapChromeNodeSnapshot>();
        CollectCompassLetterNodes(snapshots, manager, MinimapNodeTree.Addon, mapRect);
        CollectCompassLetterNodes(snapshots, mapComponentManager, MinimapNodeTree.MapComponent, mapRect);
        return snapshots;
    }

    private static void CollectCompassLetterNodes(
        List<MinimapChromeNodeSnapshot> snapshots,
        AtkUldManager* ownerManager,
        MinimapNodeTree tree,
        NativeNodeRect mapRect)
    {
        if (ownerManager == null || ownerManager->NodeList == null)
            return;

        var seen = new HashSet<nint>(snapshots.Where(snapshot => snapshot.Tree == tree).Select(snapshot => snapshot.Address));
        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        var count = Math.Min((int)ownerManager->NodeListCount, 512);
        for (var index = 0; index < count; index++)
        {
            var node = ownerManager->NodeList[index];
            if (node == null || node->Type != NodeType.Text || !TryGetNodeScreenRect(node, out var rect))
                continue;

            string text;
            try
            {
                text = ((AtkTextNode*)node)->NodeText.ToString();
            }
            catch
            {
                continue;
            }

            if (!IsCompassDirectionLabel(text))
                continue;

            var distance = Vector2.Distance(rect.Center, mapRect.Center);
            if (distance < diameter * 0.22f || distance > diameter * 0.96f ||
                rect.Size.X > diameter * 0.30f || rect.Size.Y > diameter * 0.30f ||
                !seen.Add((nint)node))
                continue;

            snapshots.Add(new MinimapChromeNodeSnapshot(
                tree,
                (nint)node,
                node->NodeId,
                node->Type,
                node->Color.A,
                node->IsVisible()));
        }
    }

    private static void AddWestCompassLeafNodes(
        List<MinimapChromeNodeSnapshot> snapshots,
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        NativeNodeRect mapRect,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot)
    {
        CollectWestCompassLeafNodes(
            snapshots,
            manager,
            MinimapNodeTree.Addon,
            mapRect,
            coordinateRoot,
            celestialRoot);
        CollectWestCompassLeafNodes(
            snapshots,
            mapComponentManager,
            MinimapNodeTree.MapComponent,
            mapRect,
            null,
            null);
    }

    private static void CollectWestCompassLeafNodes(
        List<MinimapChromeNodeSnapshot> snapshots,
        AtkUldManager* ownerManager,
        MinimapNodeTree tree,
        NativeNodeRect mapRect,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot)
    {
        if (ownerManager == null || ownerManager->NodeList == null)
            return;

        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        var nodeLimit = Math.Min((int)ownerManager->NodeListCount, 512);
        for (var index = 0; index < nodeLimit; index++)
        {
            var node = ownerManager->NodeList[index];
            if (node == null ||
                (coordinateRoot != null &&
                 (IsDescendantOrSelf(node, coordinateRoot) || IsDescendantOrSelf(coordinateRoot, node))) ||
                (celestialRoot != null &&
                 (IsDescendantOrSelf(node, celestialRoot) || IsDescendantOrSelf(celestialRoot, node))) ||
                !TryGetNodeScreenRect(node, out var rect))
                continue;

            var offset = rect.Center - mapRect.Center;
            if (offset.X < -diameter * 0.70f || offset.X > -diameter * 0.40f ||
                MathF.Abs(offset.Y) > diameter * 0.18f ||
                rect.Size.X < 2f || rect.Size.Y < 2f ||
                rect.Size.X > diameter * 0.20f || rect.Size.Y > diameter * 0.24f)
                continue;

            var isWestText = false;
            if (node->Type == NodeType.Text)
            {
                try
                {
                    isWestText = NormalizeCompassDirectionLabel(((AtkTextNode*)node)->NodeText.ToString()) == "W";
                }
                catch
                {


                }
            }

            var area = MathF.Max(1f, rect.Size.X * rect.Size.Y);
            var overlapRatio = rect.IntersectionArea(mapRect) / area;
            var isDetachedWestImage =
                (node->Type is NodeType.Image or NodeType.NineGrid or NodeType.Counter) &&
                rect.Center.X <= mapRect.Min.X + diameter * 0.015f &&
                MathF.Abs(offset.Y) <= diameter * 0.085f &&
                rect.Size.X <= diameter * 0.10f &&
                rect.Size.Y <= diameter * 0.16f &&
                rect.Size.X / MathF.Max(1f, rect.Size.Y) <= 1.35f &&
                overlapRatio <= 0.32f;

            if (!isWestText && !isDetachedWestImage)
                continue;


            AddChromeSnapshot(snapshots, tree, node);
        }
    }

    private static List<MinimapChromeNodeSnapshot> FindPeripheralCompassGlyphNodes(
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        NativeNodeRect mapRect,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot,
        IReadOnlyList<List<MinimapControlCandidate>> controlRows)
    {
        var snapshots = new List<MinimapChromeNodeSnapshot>();
        var protectedAddresses = controlRows
            .SelectMany(row => row)
            .Select(control => control.Address)
            .ToHashSet();


        CollectPeripheralCompassGlyphNodes(
            snapshots,
            mapComponentManager,
            MinimapNodeTree.MapComponent,
            mapRect,
            null,
            null,
            protectedAddresses);
        return snapshots;
    }

    private static void CollectPeripheralCompassGlyphNodes(
        List<MinimapChromeNodeSnapshot> snapshots,
        AtkUldManager* ownerManager,
        MinimapNodeTree tree,
        NativeNodeRect mapRect,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot,
        IReadOnlySet<nint> protectedAddresses)
    {
        if (ownerManager == null || ownerManager->NodeList == null)
            return;

        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        var nodeLimit = Math.Min((int)ownerManager->NodeListCount, 512);
        var seen = new HashSet<nint>(snapshots
            .Where(snapshot => snapshot.Tree == tree)
            .Select(snapshot => snapshot.Address));

        for (var index = 0; index < nodeLimit; index++)
        {
            var node = ownerManager->NodeList[index];
            if (node == null || protectedAddresses.Contains((nint)node) ||
                (coordinateRoot != null &&
                 (IsDescendantOrSelf(node, coordinateRoot) || IsDescendantOrSelf(coordinateRoot, node))) ||
                (celestialRoot != null &&
                 (IsDescendantOrSelf(node, celestialRoot) || IsDescendantOrSelf(celestialRoot, node))) ||
                !TryGetNodeScreenRect(node, out var rect))
                continue;

            var rawType = (ushort)node->Type;
            var isGlyphVisual = node->Type is NodeType.Text or NodeType.Image or NodeType.NineGrid or NodeType.Counter ||
                                rawType >= 1000;
            if (!isGlyphVisual)
                continue;

            var width = rect.Size.X;
            var height = rect.Size.Y;
            var area = MathF.Max(1f, width * height);
            if (width < 2f || height < 2f ||
                width > diameter * 0.24f || height > diameter * 0.24f)
                continue;

            var overlapRatio = rect.IntersectionArea(mapRect) / area;
            if (overlapRatio > 0.42f)
                continue;

            var offset = rect.Center - mapRect.Center;
            var horizontalCardinal =
                MathF.Abs(offset.Y) <= diameter * 0.16f &&
                MathF.Abs(offset.X) >= diameter * 0.43f &&
                MathF.Abs(offset.X) <= diameter * 0.78f;
            var verticalCardinal =
                MathF.Abs(offset.X) <= diameter * 0.16f &&
                MathF.Abs(offset.Y) >= diameter * 0.43f &&
                MathF.Abs(offset.Y) <= diameter * 0.78f;
            if (!horizontalCardinal && !verticalCardinal)
                continue;

            var candidate = rawType >= 1000
                ? FindSmallPeripheralVisualRoot(node, ownerManager->RootNode, mapRect)
                : node;
            if (candidate == null || protectedAddresses.Contains((nint)candidate) || !seen.Add((nint)candidate))
                continue;

            snapshots.Add(new MinimapChromeNodeSnapshot(
                tree,
                (nint)candidate,
                candidate->NodeId,
                candidate->Type,
                candidate->Color.A,
                candidate->IsVisible()));
        }
    }

    private static AtkResNode* FindSmallPeripheralVisualRoot(
        AtkResNode* node,
        AtkResNode* ownerRoot,
        NativeNodeRect mapRect)
    {
        if (node == null)
            return null;

        var current = node;
        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        for (var depth = 0; depth < 8; depth++)
        {
            var parent = current->ParentNode;
            if (parent == null || parent == ownerRoot || !TryGetNodeScreenRect(parent, out var rect) ||
                rect.Size.X > diameter * 0.30f || rect.Size.Y > diameter * 0.30f)
                break;
            current = parent;
        }

        return current;
    }

    private static bool IsCompassDirectionLabel(string text)
    {
        var letters = NormalizeCompassDirectionLabel(text);
        return letters is "N" or "E" or "S" or "W";
    }

    private static string NormalizeCompassDirectionLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;


        return new string(text
            .Where(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static void AddChromeSnapshot(
        List<MinimapChromeNodeSnapshot> snapshots,
        MinimapNodeTree tree,
        AtkResNode* node)
    {
        if (node == null || snapshots.Any(snapshot => snapshot.Tree == tree && snapshot.Address == (nint)node))
            return;

        snapshots.Add(new MinimapChromeNodeSnapshot(
            tree,
            (nint)node,
            node->NodeId,
            node->Type,
            node->Color.A,
            node->IsVisible()));
    }

    private static void RemoveChromeRelatedToProtectedNodes(
        List<MinimapChromeNodeSnapshot> chromeNodes,
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        IReadOnlyList<MinimapNodeTransform> protectedNodes)
    {
        for (var chromeIndex = chromeNodes.Count - 1; chromeIndex >= 0; chromeIndex--)
        {
            var chrome = chromeNodes[chromeIndex];
            var chromeManager = chrome.Tree == MinimapNodeTree.MapComponent
                ? mapComponentManager
                : manager;
            var chromeNode = ResolveMinimapChromeNode(chromeManager, chrome);
            if (chromeNode == null)
                continue;

            var remove = false;
            foreach (var moved in protectedNodes)
            {
                if (moved.Tree != chrome.Tree)
                    continue;

                var movedNode = ResolveMinimapNode(manager, mapComponentManager, moved);
                if (movedNode != null &&
                    (chromeNode == movedNode ||
                     IsDescendantOrSelf(chromeNode, movedNode) ||
                     IsDescendantOrSelf(movedNode, chromeNode)))
                {
                    remove = true;
                    break;
                }
            }

            if (remove)
                chromeNodes.RemoveAt(chromeIndex);
        }
    }

    private static List<MinimapChromeNodeSnapshot> FindNativeMinimapChromeNodes(
        AtkUldManager* manager,
        AtkResNode* root,
        AtkResNode* mapNode,
        AtkResNode* coordinateRoot,
        AtkResNode* celestialRoot,
        NativeNodeRect mapRect)
    {
        var snapshots = new List<MinimapChromeNodeSnapshot>();
        if (manager == null || manager->NodeList == null || root == null || mapNode == null)
            return snapshots;

        var candidates = new List<nint>();
        var seen = new HashSet<nint>();
        var count = Math.Min((int)manager->NodeListCount, 512);
        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        var mapArea = MathF.Max(1f, mapRect.Size.X * mapRect.Size.Y);

        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || node == root || node == mapNode ||
                node == coordinateRoot || node == celestialRoot ||
                IsDescendantOrSelf(node, mapNode) || IsDescendantOrSelf(mapNode, node) ||
                (coordinateRoot != null &&
                 (IsDescendantOrSelf(node, coordinateRoot) || IsDescendantOrSelf(coordinateRoot, node))) ||
                (celestialRoot != null &&
                 (IsDescendantOrSelf(node, celestialRoot) || IsDescendantOrSelf(celestialRoot, node))) ||
                !TryGetNodeScreenRect(node, out var rect))
                continue;

            var rawType = (ushort)node->Type;
            var isVisualNode = node->Type is NodeType.Image or NodeType.NineGrid or NodeType.Counter || rawType >= 1000;
            if (!isVisualNode)
                continue;

            var width = rect.Size.X;
            var height = rect.Size.Y;
            var area = width * height;
            var centerDistance = Vector2.Distance(rect.Center, mapRect.Center);


            var isConcentricShell =
                centerDistance <= diameter * 0.16f &&
                width >= diameter * 0.72f && width <= diameter * 1.52f &&
                height >= diameter * 0.72f && height <= diameter * 1.52f &&
                area >= mapArea * 0.48f;


            var isRightControlRail =
                rect.Center.X > mapRect.Max.X + diameter * 0.01f &&
                rect.Center.X <= mapRect.Max.X + diameter * 0.42f &&
                rect.Center.Y >= mapRect.Min.Y - diameter * 0.10f &&
                rect.Center.Y <= mapRect.Max.Y + diameter * 0.12f &&
                width >= 6f && height >= 6f &&
                width <= diameter * 0.44f && height <= diameter * 0.62f;

            if (!isConcentricShell && !isRightControlRail)
                continue;


            var candidate = rawType >= 1000
                ? FindIndependentTransformRoot(node, root, mapNode, coordinateRoot)
                : node;
            if (candidate == null || candidate == root ||
                IsDescendantOrSelf(mapNode, candidate) ||
                (coordinateRoot != null && IsDescendantOrSelf(coordinateRoot, candidate)) ||
                (celestialRoot != null && IsDescendantOrSelf(celestialRoot, candidate)) ||
                !seen.Add((nint)candidate))
                continue;

            candidates.Add((nint)candidate);
        }

        foreach (var address in PruneNestedTransformRoots(candidates))
        {
            var node = (AtkResNode*)address;
            if (node == null)
                continue;

            snapshots.Add(new MinimapChromeNodeSnapshot(
                MinimapNodeTree.Addon,
                address,
                node->NodeId,
                node->Type,
                node->Color.A,
                node->IsVisible()));
        }

        return snapshots;
    }

    private static void ApplyMinimapChromeSuppression(
        AtkUldManager* manager,
        AtkResNode* mapNode,
        IReadOnlyList<MinimapChromeNodeSnapshot> snapshots)
    {
        var mapComponentManager = GetNativeMapComponentManager(mapNode);
        foreach (var snapshot in snapshots)
        {
            var ownerManager = snapshot.Tree == MinimapNodeTree.MapComponent
                ? mapComponentManager
                : manager;
            var node = ResolveMinimapChromeNode(ownerManager, snapshot);
            if (node == null)
                continue;

            if (node->Color.A != 0)
                node->SetAlpha(0);
            if (node->IsVisible())
                node->ToggleVisibility(false);
            node->IsDirty = true;
        }
    }


    private static void SuppressResidualWestCompassGlyph(
        AtkUldManager* manager,
        AtkResNode* mapNode,
        IReadOnlyList<MinimapNodeTransform> nodes)
    {
        if (manager == null || mapNode == null || !TryGetNodeScreenRect(mapNode, out var mapRect))
            return;

        var mapComponentManager = GetNativeMapComponentManager(mapNode);
        var protectedMapComponentNodes = ResolveProtectedMinimapNodes(
            manager,
            mapComponentManager,
            nodes,
            MinimapNodeTree.MapComponent);


        SuppressResidualWestCompassGlyphInManager(
            mapComponentManager,
            mapRect,
            protectedMapComponentNodes);
    }

    private static HashSet<nint> ResolveProtectedMinimapNodes(
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        IReadOnlyList<MinimapNodeTransform> nodes,
        MinimapNodeTree tree)
    {
        var protectedNodes = new HashSet<nint>();
        foreach (var snapshot in nodes)
        {
            if (snapshot.Tree != tree ||
                snapshot.Role is not (MinimapNodeRole.Coordinates or MinimapNodeRole.Celestial or MinimapNodeRole.ZoomIn or MinimapNodeRole.ZoomOut))
                continue;

            var node = ResolveMinimapNode(manager, mapComponentManager, snapshot);
            if (node != null)
                protectedNodes.Add((nint)node);
        }

        return protectedNodes;
    }

    private static void SuppressResidualWestCompassGlyphInManager(
        AtkUldManager* ownerManager,
        NativeNodeRect mapRect,
        IReadOnlySet<nint> protectedNodes)
    {
        if (ownerManager == null || ownerManager->NodeList == null)
            return;

        var diameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        var nodeLimit = Math.Min((int)ownerManager->NodeListCount, 512);
        for (var index = 0; index < nodeLimit; index++)
        {
            var node = ownerManager->NodeList[index];
            if (node == null ||
                protectedNodes.Contains((nint)node) ||
                protectedNodes.Any(address =>
                {
                    var protectedNode = (AtkResNode*)address;
                    return protectedNode != null && (IsDescendantOrSelf(node, protectedNode) || IsDescendantOrSelf(protectedNode, node));
                }) ||
                !TryGetNodeScreenRect(node, out var rect))
                continue;

            var width = rect.Size.X;
            var height = rect.Size.Y;
            if (width < 2f || height < 2f ||
                width > diameter * 0.18f || height > diameter * 0.28f)
                continue;

            var offset = rect.Center - mapRect.Center;
            var area = MathF.Max(1f, width * height);
            var overlapRatio = rect.IntersectionArea(mapRect) / area;
            var inWestBand =
                offset.X >= -diameter * 0.66f &&
                offset.X <= -diameter * 0.43f &&
                MathF.Abs(offset.Y) <= diameter * 0.14f &&
                rect.Max.X <= mapRect.Min.X + diameter * 0.055f &&
                rect.Min.X >= mapRect.Min.X - diameter * 0.18f;
            if (!inWestBand || overlapRatio > 0.34f)
                continue;

            var isExactWestText = false;
            if (node->Type == NodeType.Text)
            {
                try
                {
                    isExactWestText = NormalizeCompassDirectionLabel(
                        ((AtkTextNode*)node)->NodeText.ToString()) == "W";
                }
                catch
                {

                }
            }

            var aspect = width / MathF.Max(1f, height);
            var isWestGlyphImage =
                node->Type is NodeType.Image or NodeType.NineGrid or NodeType.Counter &&
                aspect <= 0.90f &&
                width <= diameter * 0.085f &&
                height <= diameter * 0.18f;
            if (!isExactWestText && !isWestGlyphImage)
                continue;

            if (node->Color.A != 0)
                node->SetAlpha(0);
            if (node->IsVisible())
                node->ToggleVisibility(false);
            node->IsDirty = true;
        }
    }

    private static List<nint> FindMapTransformRoots(
        AtkUldManager* manager,
        AtkResNode* root,
        AtkResNode* mapNode,
        AtkResNode* coordinateNode,
        AtkResNode* celestialNode,
        NativeNodeRect mapRect)
    {
        var roots = new List<nint>();
        var seen = new HashSet<nint>();
        var mapDiameter = MathF.Max(1f, MathF.Min(mapRect.Size.X, mapRect.Size.Y));
        var expanded = mapRect.Expand(mapDiameter * 0.14f);
        var mapArea = MathF.Max(1f, mapRect.Size.X * mapRect.Size.Y);
        var count = Math.Min((int)manager->NodeListCount, 512);

        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || node == root)
                continue;

            var transformRoot = FindRootChild(node, root);
            if (transformRoot == null || !seen.Add((nint)transformRoot))
                continue;

            var containsMap = IsDescendantOrSelf(mapNode, transformRoot);
            if ((coordinateNode != null && IsDescendantOrSelf(coordinateNode, transformRoot)) ||
                (celestialNode != null && IsDescendantOrSelf(celestialNode, transformRoot)))
            {
                if (!containsMap)
                    continue;


                continue;
            }

            if (!TryGetNodeScreenRect(transformRoot, out var rect))
                continue;

            var area = rect.Size.X * rect.Size.Y;
            var overlap = rect.IntersectionArea(expanded);
            var centerDistance = Vector2.Distance(rect.Center, mapRect.Center);
            var likelyMapVisual = containsMap ||
                overlap >= MathF.Min(area, mapArea) * 0.18f ||
                centerDistance <= mapDiameter * 0.62f;
            var obviousRightControl = rect.Center.X > mapRect.Max.X + mapDiameter * 0.025f &&
                                      rect.Size.X < mapDiameter * 0.52f;
            var obviousBottomAccessory = rect.Center.Y > mapRect.Max.Y + mapDiameter * 0.045f;
            var oversizedContainer = area > mapArea * 3.2f && !containsMap;

            if (likelyMapVisual && !obviousRightControl && !obviousBottomAccessory && !oversizedContainer)
                roots.Add((nint)transformRoot);
        }

        var mapCovered = roots.Any(rootNode => IsDescendantOrSelf(mapNode, (AtkResNode*)rootNode));
        if (!mapCovered)
            roots.Add((nint)mapNode);

        return PruneNestedTransformRoots(roots);
    }

    private static List<nint> PruneNestedTransformRoots(List<nint> nodes)
    {
        var result = new List<nint>(nodes.Count);
        foreach (var candidate in nodes)
        {
            if (candidate == 0 || result.Any(existing => IsDescendantOrSelf((AtkResNode*)candidate, (AtkResNode*)existing)))
                continue;

            for (var index = result.Count - 1; index >= 0; index--)
            {
                if (IsDescendantOrSelf((AtkResNode*)result[index], (AtkResNode*)candidate))
                    result.RemoveAt(index);
            }

            result.Add(candidate);
        }

        return result;
    }

    private static AtkResNode* FindRootChild(AtkResNode* node, AtkResNode* root)
    {
        if (node == null || root == null)
            return null;

        var current = node;
        for (var depth = 0; depth < 64 && current->ParentNode != null; depth++)
        {
            if (current->ParentNode == root)
                return current;
            current = current->ParentNode;
        }

        return node;
    }

    private static AtkResNode* FindIndependentTransformRoot(
        AtkResNode* node,
        AtkResNode* root,
        AtkResNode* protectedNodeA,
        AtkResNode* protectedNodeB)
    {
        if (node == null || root == null)
            return null;

        var current = node;
        for (var depth = 0; depth < 64 && current->ParentNode != null && current->ParentNode != root; depth++)
        {
            var parent = current->ParentNode;
            if ((protectedNodeA != null && IsDescendantOrSelf(protectedNodeA, parent)) ||
                (protectedNodeB != null && IsDescendantOrSelf(protectedNodeB, parent)))
                break;

            current = parent;
        }

        return current;
    }

    private static bool IsDescendantOrSelf(AtkResNode* node, AtkResNode* ancestor)
    {
        if (node == null || ancestor == null)
            return false;

        var current = node;
        for (var depth = 0; depth < 96 && current != null; depth++)
        {
            if (current == ancestor)
                return true;
            current = current->ParentNode;
        }

        return false;
    }

    private static void AddMinimapNodeSnapshot(
        List<MinimapNodeTransform> snapshots,
        HashSet<nint> seen,
        AtkResNode* node,
        MinimapNodeRole role,
        MinimapNodeTree tree = MinimapNodeTree.Addon)
    {
        if (node == null || !seen.Add((nint)node) || !TryGetNodeScreenRect(node, out var screenRect))
            return;

        snapshots.Add(new MinimapNodeTransform(
            tree,
            (nint)node,
            node->NodeId,
            node->Type,
            node->X,
            node->Y,
            SafeScale(node->ScaleX),
            SafeScale(node->ScaleY),
            node->OriginX,
            node->OriginY,
            screenRect,
            role));
    }

    private static void AddMinimapControlGroupSnapshots(
        List<MinimapNodeTransform> snapshots,
        HashSet<nint> seen,
        IReadOnlyList<MinimapControlCandidate> controls,
        MinimapNodeRole role)
    {
        foreach (var control in controls)
        {
            AddMinimapNodeSnapshot(
                snapshots,
                seen,
                (AtkResNode*)control.Address,
                role,
                control.Tree);
        }
    }

    private static NativeNodeRect GetMinimapNodeGroupRect(IReadOnlyList<MinimapNodeTransform> nodes)
    {
        if (nodes.Count == 0)
            return new NativeNodeRect(Vector2.Zero, Vector2.Zero);

        var min = nodes[0].ScreenRect.Min;
        var max = nodes[0].ScreenRect.Max;
        for (var index = 1; index < nodes.Count; index++)
        {
            min = Vector2.Min(min, nodes[index].ScreenRect.Min);
            max = Vector2.Max(max, nodes[index].ScreenRect.Max);
        }

        return new NativeNodeRect(min, max);
    }

    private static void ApplyMinimapNodeGroupTransform(
        AtkUnitBase* addon,
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        IReadOnlyList<MinimapNodeTransform> nodes,
        float scaleFactor,
        float screenScaleFactor,
        Vector2 desiredGroupCenter)
    {
        if (nodes.Count == 0)
            return;

        var sourceCenter = GetMinimapNodeGroupRect(nodes).Center;
        foreach (var node in nodes)
        {
            var relativeCenter = (node.ScreenRect.Center - sourceCenter) * screenScaleFactor;
            ApplyMinimapNodeTransform(
                addon,
                manager,
                mapComponentManager,
                node,
                scaleFactor,
                desiredGroupCenter + relativeCenter);
        }
    }

    private static void ApplyMinimapNodeTransform(
        AtkUnitBase* addon,
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        MinimapNodeTransform snapshot,
        float scaleFactor,
        Vector2 desiredScreenCenter)
    {
        var node = ResolveMinimapNode(manager, mapComponentManager, snapshot);
        if (node == null)
            return;

        var desiredScaleX = Math.Clamp(snapshot.ScaleX * scaleFactor, 0.08f, 4f);
        var desiredScaleY = Math.Clamp(snapshot.ScaleY * scaleFactor, 0.08f, 4f);
        if (MathF.Abs(node->ScaleX - desiredScaleX) >= 0.0005f ||
            MathF.Abs(node->ScaleY - desiredScaleY) >= 0.0005f)
            node->SetScale(desiredScaleX, desiredScaleY);

        node->IsDirty = true;
        if (!TryGetNodeScreenRect(node, out var currentRect))
            return;

        var delta = desiredScreenCenter - currentRect.Center;
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || delta.LengthSquared() < 0.04f)
            return;

        var parentScale = GetParentScreenScale(node, addon->RootNode, addon->RootNode->ScaleX, addon->RootNode->ScaleY);
        if (snapshot.Tree == MinimapNodeTree.MapComponent)
        {


            var measuredScaleX = currentRect.Size.X / MathF.Max(0.001f, node->Width * MathF.Abs(node->ScaleX));
            var measuredScaleY = currentRect.Size.Y / MathF.Max(0.001f, node->Height * MathF.Abs(node->ScaleY));
            if (float.IsFinite(measuredScaleX) && measuredScaleX > 0.001f)
                parentScale.X = measuredScaleX;
            if (float.IsFinite(measuredScaleY) && measuredScaleY > 0.001f)
                parentScale.Y = measuredScaleY;
        }

        node->SetPositionFloat(
            node->X + delta.X / parentScale.X,
            node->Y + delta.Y / parentScale.Y);
        node->IsDirty = true;
    }

    private static Vector2 GetParentScreenScale(
        AtkResNode* node,
        AtkResNode* root,
        float rootScaleX,
        float rootScaleY)
    {
        var scale = new Vector2(SafeScale(rootScaleX), SafeScale(rootScaleY));
        var parents = new List<nint>(16);
        var current = node != null ? node->ParentNode : null;
        for (var depth = 0; depth < 64 && current != null && current != root; depth++)
        {
            parents.Add((nint)current);
            current = current->ParentNode;
        }

        for (var index = parents.Count - 1; index >= 0; index--)
        {
            var parent = (AtkResNode*)parents[index];
            scale.X *= SafeScale(parent->ScaleX);
            scale.Y *= SafeScale(parent->ScaleY);
        }

        scale.X = MathF.Max(0.001f, MathF.Abs(scale.X));
        scale.Y = MathF.Max(0.001f, MathF.Abs(scale.Y));
        return scale;
    }

    private static void EnsureMinimapNodeVisible(
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        MinimapNodeTransform snapshot)
    {
        var node = ResolveMinimapNode(manager, mapComponentManager, snapshot);
        if (node == null)
            return;

        if (node->Color.A == 0)
            node->SetAlpha(255);
        if (!node->IsVisible())
            node->ToggleVisibility(true);
        node->IsDirty = true;
    }

    private static AtkResNode* ResolveMinimapNode(
        AtkUldManager* manager,
        AtkUldManager* mapComponentManager,
        MinimapNodeTransform snapshot)
    {
        var ownerManager = snapshot.Tree == MinimapNodeTree.MapComponent
            ? mapComponentManager
            : manager;
        if (ownerManager == null || ownerManager->NodeList == null)
            return null;

        var count = Math.Min((int)ownerManager->NodeListCount, 512);
        for (var index = 0; index < count; index++)
        {
            var node = ownerManager->NodeList[index];
            if ((nint)node == snapshot.Address)
                return node;
        }

        for (var index = 0; index < count; index++)
        {
            var node = ownerManager->NodeList[index];
            if (node != null && node->NodeId == snapshot.NodeId && node->Type == snapshot.Type)
                return node;
        }

        return null;
    }

    private static AtkResNode* ResolveMinimapChromeNode(
        AtkUldManager* manager,
        MinimapChromeNodeSnapshot snapshot)
    {
        if (manager == null || manager->NodeList == null)
            return null;

        var count = Math.Min((int)manager->NodeListCount, 512);
        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if ((nint)node == snapshot.Address)
                return node;
        }

        for (var index = 0; index < count; index++)
        {
            var node = manager->NodeList[index];
            if (node != null && node->NodeId == snapshot.NodeId && node->Type == snapshot.Type)
                return node;
        }

        return null;
    }

    private static bool TryGetNodeScreenRect(AtkResNode* node, out NativeNodeRect rect)
    {
        rect = default;
        if (node == null || node->Width == 0 || node->Height == 0)
            return false;

        Bounds bounds;
        node->GetBounds(&bounds);
        var min = new Vector2(
            MathF.Min(bounds.Pos1.X, bounds.Pos2.X),
            MathF.Min(bounds.Pos1.Y, bounds.Pos2.Y));
        var max = new Vector2(
            MathF.Max(bounds.Pos1.X, bounds.Pos2.X),
            MathF.Max(bounds.Pos1.Y, bounds.Pos2.Y));
        if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) ||
            !float.IsFinite(max.X) || !float.IsFinite(max.Y) ||
            max.X - min.X < 0.5f || max.Y - min.Y < 0.5f)
            return false;

        rect = new NativeNodeRect(min, max);
        return true;
    }

    private static void ResolveAccessoryCenters(
        HudBounds ringBounds,
        HudBounds viewportBounds,
        float interfaceScale,
        Vector2 coordinateSize,
        Vector2 celestialSize,
        out Vector2 coordinateCenter,
        out Vector2 celestialCenter)
    {
        var ringCenter = ringBounds.Position + ringBounds.Size * 0.5f;
        var ringRadius = MathF.Min(ringBounds.Size.X, ringBounds.Size.Y) * 0.5f;
        var viewportMax = viewportBounds.Position + viewportBounds.Size;
        var margin = MathF.Max(5f, 7f * interfaceScale);


        var coordinateHalf = coordinateSize * 0.5f;
        coordinateCenter = new Vector2(
            ringCenter.X,
            ringBounds.Position.Y + ringBounds.Size.Y + margin + coordinateHalf.Y);


        var celestialHalf = celestialSize * 0.5f;
        var rightCenterX = ringBounds.Position.X + ringBounds.Size.X + margin + celestialHalf.X;
        var leftCenterX = ringBounds.Position.X - margin - celestialHalf.X;
        var useRight = rightCenterX + celestialHalf.X <= viewportMax.X - 2f;
        celestialCenter = new Vector2(
            useRight ? rightCenterX : leftCenterX,
            ringCenter.Y - ringRadius * 0.34f);

        coordinateCenter.X = Math.Clamp(
            coordinateCenter.X,
            viewportBounds.Position.X + coordinateHalf.X + 2f,
            viewportMax.X - coordinateHalf.X - 2f);
        coordinateCenter.Y = Math.Clamp(
            coordinateCenter.Y,
            viewportBounds.Position.Y + coordinateHalf.Y + 2f,
            viewportMax.Y - coordinateHalf.Y - 2f);
        celestialCenter.X = Math.Clamp(
            celestialCenter.X,
            viewportBounds.Position.X + celestialHalf.X + 2f,
            viewportMax.X - celestialHalf.X - 2f);
        celestialCenter.Y = Math.Clamp(
            celestialCenter.Y,
            viewportBounds.Position.Y + celestialHalf.Y + 2f,
            viewportMax.Y - celestialHalf.Y - 2f);
    }

    private static void ResolveZoomCenters(
        HudBounds ringBounds,
        HudBounds viewportBounds,
        float interfaceScale,
        Vector2 zoomInSize,
        Vector2 zoomOutSize,
        out Vector2 zoomInCenter,
        out Vector2 zoomOutCenter)
    {
        var ringCenter = ringBounds.Position + ringBounds.Size * 0.5f;
        var outerRadius = HudLayout.MinimapOuterRadius(ringBounds, interfaceScale);
        var viewportMax = viewportBounds.Position + viewportBounds.Size;
        var inHalf = zoomInSize * 0.5f;
        var outHalf = zoomOutSize * 0.5f;
        var horizontalOffset = MathF.Max(15f, 17f * interfaceScale);


        var y = ringCenter.Y - outerRadius - 1f * interfaceScale;
        zoomOutCenter = new Vector2(ringCenter.X - horizontalOffset, y);
        zoomInCenter = new Vector2(ringCenter.X + horizontalOffset, y);

        zoomInCenter.X = Math.Clamp(
            zoomInCenter.X,
            viewportBounds.Position.X + inHalf.X + 2f,
            viewportMax.X - inHalf.X - 2f);
        zoomOutCenter.X = Math.Clamp(
            zoomOutCenter.X,
            viewportBounds.Position.X + outHalf.X + 2f,
            viewportMax.X - outHalf.X - 2f);
        zoomInCenter.Y = Math.Clamp(
            zoomInCenter.Y,
            viewportBounds.Position.Y + inHalf.Y + 2f,
            viewportMax.Y - inHalf.Y - 2f);
        zoomOutCenter.Y = Math.Clamp(
            zoomOutCenter.Y,
            viewportBounds.Position.Y + outHalf.Y + 2f,
            viewportMax.Y - outHalf.Y - 2f);
    }

    private static HudBounds CenteredBounds(Vector2 center, Vector2 size, float padding)
    {
        var safeSize = Vector2.Max(size, new Vector2(12f)) + new Vector2(padding * 2f);
        return new HudBounds(center - safeSize * 0.5f, safeSize);
    }

    private static float SafeScale(float value)
        => float.IsFinite(value) && MathF.Abs(value) > 0.001f ? value : 1f;

    private NativeAddonSnapshot? TryGetVisibleMinimapSnapshot()
    {
        var seen = new HashSet<nint>();
        foreach (var name in MinimapAddons)
        {
            try
            {
                var addon = GetAddon(name);
                if (addon == null || !addon->IsReady || !addon->IsVisible || addon->RootNode == null || !seen.Add((nint)addon))
                    continue;

                short x = 0;
                short y = 0;
                ushort width = 0;
                ushort height = 0;
                addon->GetPosition(&x, &y);
                addon->GetSize(&width, &height, true);
                if (width == 0 || width > 900 || height == 0 || height > 900)
                    continue;

                var scale = addon->RootNode->ScaleX;
                if (!float.IsFinite(scale) || scale <= 0.001f)
                    scale = 1f;
                return new NativeAddonSnapshot(name, x, y, width, height, scale);
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame skipped volatile native minimap addon {AddonName}.", name);
            }
        }

        return null;
    }


    public bool TryGetVisibleAddonBounds(IReadOnlyList<string> candidateNames, out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;

        foreach (var name in candidateNames)
        {
            try
            {
                var addon = GetAddon(name);
                if (addon == null || !addon->IsReady || !addon->IsVisible)
                    continue;

                short x = 0;
                short y = 0;
                ushort width = 0;
                ushort height = 0;
                addon->GetPosition(&x, &y);
                addon->GetSize(&width, &height, true);
                if (width == 0 || height == 0)
                    continue;

                position = new Vector2(x, y);
                size = new Vector2(width, height);
                return true;
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame could not read bounds for native addon {AddonName}.", name);
            }
        }

        return false;
    }


    private void Apply(string name, bool shouldHide)
    {
        try
        {
            if (!shouldHide)
            {
                if (hiddenByUs.Remove(name))
                    SetVisible(name, true);
                return;
            }

            var addon = GetAddon(name);
            if (addon == null || !addon->IsReady)
                return;

            if (addon->IsVisible)
            {


                addon->IsVisible = false;
                hiddenByUs.Add(name);
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame skipped volatile native HUD addon {AddonName}.", name);
        }
    }

    private void SetVisible(string name, bool visible)
    {
        try
        {
            var addon = GetAddon(name);
            if (addon != null && addon->IsReady)
                addon->IsVisible = visible;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "RE:Frame could not restore native addon {AddonName}.", name);
        }
    }

    private AtkUnitBase* GetAddon(string name)
    {
        try
        {
            return gameGui.GetAddonByName<AtkUnitBase>(name, 1);
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not resolve native addon {AddonName}.", name);
            return null;
        }
    }
}
