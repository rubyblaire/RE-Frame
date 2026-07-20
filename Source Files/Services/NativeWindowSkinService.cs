using System;
using System.Collections.Generic;
using NumericsVector2 = System.Numerics.Vector2;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace REFrameXIV.Services;


public sealed unsafe class NativeWindowSkinService : IDisposable
{
    private const int MaxNestedDepth = 4;
    private const int MaxSimpleDepth = 1;
    private const int MaxManagersPerPass = 160;
    private const int MaxNodesPerPass = 6000;
    private const int MaxNodesPerManager = 1024;
    private const int MaxListRenderers = 128;
    private const int MaxRendererNodes = 96;


    private static readonly HashSet<string> HudOcclusionWindowNames = new(StringComparer.Ordinal)
    {
        "ContentsFinder",
        "ContentsFinderSetting",
        "ContentsFinderConfirm",
    };

    private static readonly HashSet<string> SkinWindowNames = new(StringComparer.Ordinal)
    {
        "SelectString",
        "SelectIconString",
        "SelectYesno",
        "SelectOk",
        "InputString",
        "InputNumeric",
        "InputSearch",
        "Notification",
        "ReadyCheck",
        "VoteKick",
        "VoteMvp",
        "VoteTreasure",
        "VoteTreasureAnswer",
        "RetainerTaskAsk",
        "RetainerSellConfirm",
        "MateriaAttachDialog",
        "SalvageDialog",
        "RepairRequest",
        "ShopCardDialog",
        "SystemMenu",
        "ConfigSystem",
        "ConfigCharacter",
        "Social",
        "Inventory",
        "InventoryLarge",
        "InventoryExpansion",
        "InventoryBuddy",
        "InventoryBuddy2",
        "InventoryBuddy3",
        "InventoryCrystal",
        "InventoryEventItem",
        "InventoryKeyItem",
        "ArmouryBoard",
        "ArmouryChest",
        "Character",
        "CharacterStatus",
        "CharacterClass",
        "CharacterRepute",
        "CharacterProfile",
        "ActionMenu",
        "ActionMenuReplace",
        "Journal",
        "RecipeNote",
        "GatheringNote",
        "Teleport",
        "Map",
        "Currency",
        "MountNoteBook",
        "MinionNoteBook",
        "Achievement",
        "RetainerList",
        "RetainerSellList",
        "Shop",
        "ShopExchangeCurrency",
        "ItemSearch",
        "ItemSearchResult",
        "FreeCompany",
        "FriendList",
        "SocialList",
        "PartyMemberList",
        "LinkShell",
        "CrossWorldLinkshell",
        "BlackList",
        "MuteList",
        "ContactList",
        "HousingMenu",
        "HousingGoods",
        "_ChatLog",
        "ChatLog",
        "_ChatLogPanel_0",
        "ChatLogPanel_0",
        "_ChatLogPanel_1",
        "ChatLogPanel_1",
        "_ChatLogPanel_2",
        "ChatLogPanel_2",
        "_ChatLogPanel_3",
        "ChatLogPanel_3",
    };


    private static readonly HashSet<string> ComplexWindowNames = new(StringComparer.Ordinal)
    {
        "SystemMenu",
        "ConfigSystem",
        "ConfigCharacter",
        "Social",
        "Inventory",
        "InventoryLarge",
        "InventoryExpansion",
        "InventoryBuddy",
        "InventoryBuddy2",
        "InventoryBuddy3",
        "InventoryCrystal",
        "InventoryEventItem",
        "InventoryKeyItem",
        "ArmouryBoard",
        "ArmouryChest",
        "Character",
        "CharacterStatus",
        "CharacterClass",
        "CharacterRepute",
        "CharacterProfile",
        "Journal",
        "RecipeNote",
        "GatheringNote",
        "Teleport",
        "Currency",
        "MountNoteBook",
        "MinionNoteBook",
        "Achievement",
        "RetainerList",
        "RetainerSellList",
        "Shop",
        "ShopExchangeCurrency",
        "ItemSearch",
        "ItemSearchResult",
        "FreeCompany",
        "FriendList",
        "SocialList",
        "PartyMemberList",
        "LinkShell",
        "CrossWorldLinkshell",
        "BlackList",
        "MuteList",
        "ContactList",
        "HousingMenu",
        "HousingGoods",
    };

    private static readonly ByteColor ObsidianDeep = new() { R = 7, G = 9, B = 13, A = 255 };
    private static readonly ByteColor ObsidianMain = new() { R = 11, G = 13, B = 18, A = 255 };
    private static readonly ByteColor ObsidianPanel = new() { R = 16, G = 18, B = 24, A = 255 };
    private static readonly ByteColor ObsidianChrome = new() { R = 23, G = 26, B = 33, A = 255 };
    private static readonly ByteColor AncientIvory = new() { R = 239, G = 228, B = 196, A = 255 };
    private static readonly ByteColor MutedIvory = new() { R = 143, G = 137, B = 121, A = 255 };
    private static readonly ByteColor TextEdge = new() { R = 4, G = 5, B = 8, A = 225 };
    private static readonly ByteColor HoverGrey = new() { R = 104, G = 108, B = 116, A = 178 };
    private static readonly ByteColor SelectedGrey = new() { R = 72, G = 76, B = 84, A = 142 };
    private static readonly ByteColor GlossButton = new() { R = 28, G = 31, B = 38, A = 218 };
    private static readonly ByteColor ButtonIvory = new() { R = 239, G = 228, B = 196, A = 244 };
    private static readonly ByteColor ButtonIvoryHover = new() { R = 255, G = 248, B = 224, A = 255 };
    private static readonly ByteColor ButtonIvoryPressed = new() { R = 205, G = 193, B = 160, A = 250 };
    private static readonly ByteColor ButtonIvoryDisabled = new() { R = 132, G = 127, B = 112, A = 190 };
    private static readonly ByteColor ButtonTextBlack = new() { R = 8, G = 9, B = 11, A = 255 };
    private static readonly ByteColor ButtonTextDisabled = new() { R = 38, G = 37, B = 34, A = 220 };
    private static readonly ByteColor ButtonIconBlack = new() { R = 8, G = 9, B = 11, A = 255 };

    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Dictionary<nint, WindowNodeSnapshot> snapshots = new();
    private readonly List<NativeWindowBounds> visibleWindowBounds = new(48);
    private readonly List<NativeWindowBounds> hudOcclusionWindowBounds = new(8);
    private readonly HashSet<string> styledAddonNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> blockedAddonNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> interactionBlockedAddonNames = new(StringComparer.Ordinal);
    private readonly HashSet<nint> visitedManagers = new();

    private int managersVisitedThisPass;
    private int nodesVisitedThisPass;
    private bool disposed;

    public NativeWindowSkinService(
        Configuration configuration,
        IFramework framework,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.framework = framework;
        this.addonLifecycle = addonLifecycle;
        this.gameGui = gameGui;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
        addonLifecycle.RegisterListener(AddonEvent.PreDraw, OnPreDraw);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, OnPreFinalize);
    }

    public int ActiveWindowCount => styledAddonNames.Count;
    public int BlockedWindowCount => blockedAddonNames.Count;
    public int HoverBlockedWindowCount => interactionBlockedAddonNames.Count;
    public bool HasProtectedDutyWindowOpen { get; private set; }
    public IReadOnlyList<NativeWindowBounds> VisibleWindowBounds => visibleWindowBounds;
    public IReadOnlyList<NativeWindowBounds> HudOcclusionWindowBounds => hudOcclusionWindowBounds;

    public bool IsPointInsideHudOcclusion(NumericsVector2 point)
    {
        foreach (var bounds in hudOcclusionWindowBounds)
        {
            if (point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
                point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y)
                return true;
        }

        return false;
    }

    public void ApplyConfigurationChange()
    {
        if (!configuration.SkinNativeWindows)
            RestoreVisibleNodesSafely();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed)
            return;

        visibleWindowBounds.Clear();
        hudOcclusionWindowBounds.Clear();
        HasProtectedDutyWindowOpen = false;


        foreach (var addonName in HudOcclusionWindowNames)
        {
            var addon = TryGetVisibleAddon(addonName);
            if (addon != null)
            {
                HasProtectedDutyWindowOpen = true;
                TryAddVisibleBounds(addon, hudOcclusionWindowBounds);
            }
        }


        foreach (var addonName in SkinWindowNames)
        {


            if (IsChatLogFamily(addonName))
                continue;

            var addon = TryGetVisibleAddon(addonName);
            if (addon == null)
                continue;

            TryAddVisibleBounds(addon, hudOcclusionWindowBounds);

            if (configuration.SkinNativeWindows || configuration.NativeWindowGlassEffect)
                TryAddVisibleBounds(addon, visibleWindowBounds);
        }
    }

    private AtkUnitBase* TryGetVisibleAddon(string addonName)
    {
        try
        {
            var addon = gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
            if (addon == null ||
                !addon->IsReady ||
                !addon->IsVisible ||
                addon->Alpha == 0 ||
                addon->RootNode == null ||
                !addon->RootNode->IsVisible())
                return null;

            return addon;
        }
        catch
        {
            return null;
        }
    }

    private void TryAddVisibleBounds(AtkUnitBase* addon, List<NativeWindowBounds> destination)
    {
        try
        {
            Bounds nativeBounds;
            addon->GetRootBounds(&nativeBounds);

            var min = new NumericsVector2(nativeBounds.Pos1.X, nativeBounds.Pos1.Y);
            var max = new NumericsVector2(nativeBounds.Pos2.X, nativeBounds.Pos2.Y);

            if (max.X <= min.X || max.Y <= min.Y)
            {
                var width = addon->GetScaledWidth(true);
                var height = addon->GetScaledHeight(true);
                if (width <= 0f || height <= 0f)
                    return;

                min = new NumericsVector2(addon->X, addon->Y);
                max = min + new NumericsVector2(width, height);
            }


            if (max.X - min.X >= 24f && max.Y - min.Y >= 24f)
                destination.Add(new NativeWindowBounds(min, max));
        }
        catch
        {

        }
    }

    private void OnPreDraw(AddonEvent _, AddonArgs args)
    {
        var addonName = args.AddonName;
        if (disposed ||
            !configuration.SkinNativeWindows ||
            !SkinWindowNames.Contains(addonName) ||
            blockedAddonNames.Contains(addonName))
            return;

        try
        {
            var addon = gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
            if (addon == null ||
                !addon->IsReady ||
                !addon->IsVisible ||
                addon->Alpha == 0 ||
                addon->RootNode == null ||
                !addon->RootNode->IsVisible())
                return;

            BeginTraversalPass();

            var manager = &addon->UldManager;
            var rootWidth = manager->RootNodeWidth > 0
                ? (float)manager->RootNodeWidth
                : (float)addon->RootNode->Width;
            var rootHeight = manager->RootNodeHeight > 0
                ? (float)manager->RootNodeHeight
                : (float)addon->RootNode->Height;
            if (IsChatLogFamily(addonName))
            {


                StyleChatShellOnly(addonName, addon, manager, rootWidth, rootHeight);
            }
            else if (IsActionMenuFamily(addonName))
            {


                StyleActionMenuShellOnly(addonName, addon, manager, rootWidth, rootHeight);
            }
            else
            {
                var maxDepth = ComplexWindowNames.Contains(addonName) ? MaxNestedDepth : MaxSimpleDepth;
                StyleManager(addonName, addon, manager, rootWidth, rootHeight, 0, maxDepth);
            }

            styledAddonNames.Add(addonName);
        }
        catch (InteractionStylingException interactionEx)
        {


            interactionBlockedAddonNames.Add(addonName);
            styledAddonNames.Add(addonName);
            log.Warning(interactionEx.InnerException ?? interactionEx,
                $"RE:Frame disabled custom native hover/tab feedback for {addonName} for this session; its obsidian window skin remains active.");
        }
        catch (Exception ex)
        {


            blockedAddonNames.Add(addonName);
            styledAddonNames.Remove(addonName);
            RemoveSnapshotsForName(addonName);
            log.Warning(ex, $"RE:Frame disabled native-window styling for {addonName} for this session.");
        }
    }

    private void OnPreFinalize(AddonEvent _, AddonArgs args)
    {
        if (disposed)
            return;


        RemoveSnapshotsForName(args.AddonName);
        styledAddonNames.Remove(args.AddonName);
    }

    private void BeginTraversalPass()
    {
        visitedManagers.Clear();
        managersVisitedThisPass = 0;
        nodesVisitedThisPass = 0;
    }

    private void StyleChatShellOnly(
        string addonName,
        AtkUnitBase* owner,
        AtkUldManager* manager,
        float rootWidth,
        float rootHeight)
    {


        StyleChatSurfaceManager(addonName, owner, manager, rootWidth, rootHeight, 0);
    }

    private void StyleChatSurfaceManager(
        string addonName,
        AtkUnitBase* owner,
        AtkUldManager* manager,
        float rootWidth,
        float rootHeight,
        int depth)
    {
        if (manager == null ||
            manager->NodeList == null ||
            manager->NodeListCount == 0 ||
            depth > 3 ||
            !visitedManagers.Add((nint)manager))
            return;

        if (++managersVisitedThisPass > 40)
            return;

        var count = Math.Min((int)manager->NodeListCount, 512);
        if (count <= 0 || nodesVisitedThisPass + count > 2600)
            return;

        nodesVisitedThisPass += count;
        var safeRootWidth = MathF.Max(1f, rootWidth);
        var safeRootHeight = MathF.Max(1f, rootHeight);
        var rootArea = safeRootWidth * safeRootHeight;

        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !node->IsVisible())
                continue;

            var rawType = (ushort)node->Type;
            if (rawType == (ushort)NodeType.Image || rawType == (ushort)NodeType.NineGrid)
            {
                var width = (float)node->Width;
                var height = (float)node->Height;
                if (width <= 0f || height <= 0f)
                    continue;

                var widthRatio = width / safeRootWidth;
                var heightRatio = height / safeRootHeight;
                var areaRatio = width * height / rootArea;

                var mainSurface = areaRatio >= 0.18f &&
                                  widthRatio >= 0.52f &&
                                  heightRatio >= 0.22f;
                var broadSurface = areaRatio >= 0.08f &&
                                   widthRatio >= 0.62f &&
                                   heightRatio >= 0.08f;
                var panelSurface = width >= 96f &&
                                   height >= 48f &&
                                   areaRatio >= 0.06f;


                var shallowChrome = width >= 24f &&
                                    height >= 10f &&
                                    height <= 48f &&
                                    widthRatio >= 0.72f &&
                                    heightRatio >= 0.52f &&
                                    areaRatio >= 0.34f;
                var compactControlFace = safeRootWidth <= 72f &&
                                         safeRootHeight <= 72f &&
                                         widthRatio >= 0.78f &&
                                         heightRatio >= 0.72f &&
                                         areaRatio >= 0.58f;

                if (mainSurface || broadSurface || panelSurface || shallowChrome || compactControlFace)
                {
                    var tint = mainSurface && heightRatio >= 0.62f
                        ? ObsidianDeep
                        : shallowChrome || compactControlFace || broadSurface
                            ? ObsidianChrome
                            : ObsidianMain;
                    StyleChatBackgroundNode(
                        addonName,
                        owner,
                        node,
                        tint,
                        mainSurface ? (byte)252 : (byte)248);
                }

                continue;
            }

            if (rawType < 1000 || depth >= 3)
                continue;

            var componentNode = (AtkComponentNode*)node;
            var component = componentNode->Component;
            if (component == null ||
                (component->OwnerNode != null && component->OwnerNode != componentNode))
                continue;

            ComponentType componentType;
            try
            {
                componentType = component->GetComponentType();
            }
            catch
            {
                continue;
            }

            if (!ShouldTraverseChatComponent(componentType))
                continue;

            var childManager = &component->UldManager;
            var childWidth = ResolveManagerWidth(component, childManager, safeRootWidth);
            var childHeight = ResolveManagerHeight(component, childManager, safeRootHeight);
            StyleChatSurfaceManager(
                addonName,
                owner,
                childManager,
                childWidth,
                childHeight,
                depth + 1);


            if (IsButtonLike(componentType))
                StyleChatCompactControlIcons(addonName, owner, component);
        }
    }

    private void StyleChatCompactControlIcons(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentBase* component)
    {
        if (component == null || component->OwnerNode == null)
            return;

        var ownerNode = (AtkResNode*)component->OwnerNode;
        var ownerWidth = (float)ownerNode->Width;
        var ownerHeight = (float)ownerNode->Height;


        if (ownerWidth < 8f || ownerHeight < 8f || ownerWidth > 180f || ownerHeight > 56f)
            return;

        var button = (AtkComponentButton*)component;
        var manager = &component->UldManager;
        if (manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var background = (AtkResNode*)button->ButtonBGNode;
        var ownerArea = MathF.Max(1f, ownerWidth * ownerHeight);
        var count = Math.Min((int)manager->NodeListCount, 48);


        if (background == null)
        {
            var largestArea = 0f;
            for (var i = 0; i < count; i++)
            {
                var candidate = manager->NodeList[i];
                if (candidate == null || !candidate->IsVisible())
                    continue;

                var type = (ushort)candidate->Type;
                if (type != (ushort)NodeType.Image && type != (ushort)NodeType.NineGrid)
                    continue;

                var area = (float)candidate->Width * candidate->Height;
                if (area < ownerArea * 0.50f || area > ownerArea * 1.08f || area <= largestArea)
                    continue;

                background = candidate;
                largestArea = area;
            }
        }

        var hovered = button->IsEnabled &&
                      ((background != null && IsPointInsideNode(background, ImGui.GetIO().MousePos)) ||
                       IsPointInsideNode(ownerNode, ImGui.GetIO().MousePos));


        if (background != null)
            StyleChatControlBackground(addonName, owner, background, hovered, button->IsEnabled);

        if (button->ButtonTextNode != null)
            StyleStandardButtonText(addonName, owner, button->ButtonTextNode, button->IsEnabled, useDarkText: false);

        var styledGlyph = false;
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node == background || !node->IsVisible())
                continue;

            var type = (ushort)node->Type;
            if (type == (ushort)NodeType.Text)
            {
                StyleTextNode(addonName, owner, (AtkTextNode*)node, !button->IsEnabled);
                continue;
            }

            if (type != (ushort)NodeType.Image && type != (ushort)NodeType.NineGrid)
                continue;

            var width = (float)node->Width;
            var height = (float)node->Height;
            var area = width * height;
            if (width < 3f || height < 3f || width > ownerWidth * 1.05f || height > ownerHeight * 1.05f ||
                area > ownerArea * 1.02f)
                continue;


            var fullSizeTabLayer = ownerWidth > 48f &&
                                   width >= ownerWidth * 0.72f &&
                                   height >= ownerHeight * 0.62f &&
                                   area >= ownerArea * 0.52f;
            if (fullSizeTabLayer)
            {
                StyleChatControlBackground(addonName, owner, node, hovered, button->IsEnabled);
                continue;
            }

            SnapshotNode(addonName, owner, node, null);
            if (!snapshots.TryGetValue((nint)node, out var original))
                continue;

            var tint = button->IsEnabled ? AncientIvory : MutedIvory;
            ApplyVisualTint(
                node,
                original,
                tint,
                (byte)Math.Max((int)original.Color.A, button->IsEnabled ? 224 : 160),
                hovered ? 16 : 7,
                hovered ? 16 : 7,
                hovered ? 18 : 9);
            styledGlyph = true;
        }


        if (!styledGlyph && background != null)
            StyleChatControlBackground(addonName, owner, background, hovered, button->IsEnabled);
    }

    private void StyleChatControlBackground(
        string addonName,
        AtkUnitBase* owner,
        AtkResNode* node,
        bool hovered,
        bool enabled)
    {
        if (node == null)
            return;

        SnapshotNode(addonName, owner, node, null);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;

        var tint = !enabled
            ? ObsidianMain
            : hovered
                ? ObsidianPanel
                : ObsidianChrome;
        var minimumAlpha = enabled ? (byte)248 : (byte)218;
        ApplyVisualTint(
            node,
            original,
            tint,
            (byte)Math.Max((int)original.Color.A, minimumAlpha),
            hovered ? 9 : 0,
            hovered ? 9 : 0,
            hovered ? 12 : 0);
        node->NodeFlags = original.NodeFlags | NodeFlags.Visible;
        node->MultiplyRed = 100;
        node->MultiplyGreen = 100;
        node->MultiplyBlue = 100;
        node->IsDirty = true;
    }

    private void StyleChatBackgroundNode(
        string addonName,
        AtkUnitBase* owner,
        AtkResNode* node,
        ByteColor tint,
        byte minimumAlpha)
    {
        SnapshotNode(addonName, owner, node, null);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;


        var alpha = (byte)Math.Max((int)original.Color.A, minimumAlpha);
        ApplyVisualTint(node, original, tint, alpha, 0, 0, 0);
        node->MultiplyRed = 100;
        node->MultiplyGreen = 100;
        node->MultiplyBlue = 100;
        node->IsDirty = true;
    }

    private void StyleActionMenuShellOnly(
        string addonName,
        AtkUnitBase* owner,
        AtkUldManager* manager,
        float rootWidth,
        float rootHeight)
    {
        if (manager == null || manager->NodeList == null || manager->NodeListCount == 0)
            return;

        if (!visitedManagers.Add((nint)manager))
            return;

        var count = Math.Min((int)manager->NodeListCount, MaxNodesPerManager);
        if (count <= 0 || nodesVisitedThisPass + count > MaxNodesPerPass)
            return;

        nodesVisitedThisPass += count;
        var safeRootWidth = MathF.Max(1f, rootWidth);
        var safeRootHeight = MathF.Max(1f, rootHeight);
        var rootArea = safeRootWidth * safeRootHeight;

        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            var rawType = (ushort)node->Type;
            if (rawType == (ushort)NodeType.Text)
            {
                StyleTextNode(addonName, owner, (AtkTextNode*)node);
                continue;
            }

            if (rawType == (ushort)NodeType.Image || rawType == (ushort)NodeType.NineGrid)
                TryStyleSurfaceNode(addonName, owner, node, safeRootWidth, safeRootHeight, rootArea, 0);


        }
    }

    private void StyleManager(
        string addonName,
        AtkUnitBase* owner,
        AtkUldManager* manager,
        float rootWidth,
        float rootHeight,
        int depth,
        int maxDepth)
    {
        if (manager == null ||
            manager->NodeList == null ||
            manager->NodeListCount == 0 ||
            depth > maxDepth)
            return;

        if (!visitedManagers.Add((nint)manager))
            return;

        if (++managersVisitedThisPass > MaxManagersPerPass)
            return;

        var count = Math.Min((int)manager->NodeListCount, MaxNodesPerManager);
        if (count <= 0 || nodesVisitedThisPass + count > MaxNodesPerPass)
            return;

        nodesVisitedThisPass += count;

        var safeRootWidth = MathF.Max(1f, rootWidth);
        var safeRootHeight = MathF.Max(1f, rootHeight);
        var rootArea = safeRootWidth * safeRootHeight;

        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            var rawType = (ushort)node->Type;
            if (rawType == (ushort)NodeType.Text)
            {
                StyleTextNode(addonName, owner, (AtkTextNode*)node);
                continue;
            }

            if (rawType == (ushort)NodeType.Image || rawType == (ushort)NodeType.NineGrid)
            {
                TryStyleSurfaceNode(addonName, owner, node, safeRootWidth, safeRootHeight, rootArea, depth);
                continue;
            }

            if (rawType < 1000)
                continue;

            var componentNode = (AtkComponentNode*)node;
            var component = componentNode->Component;
            if (component == null)
                continue;


            if (component->OwnerNode != null && component->OwnerNode != componentNode)
                continue;

            ComponentType componentType;
            try
            {
                componentType = component->GetComponentType();
            }
            catch
            {
                continue;
            }

            if (depth < maxDepth && ShouldTraverseComponent(componentType))
            {
                var childManager = &component->UldManager;
                var childWidth = ResolveManagerWidth(component, childManager, safeRootWidth);
                var childHeight = ResolveManagerHeight(component, childManager, safeRootHeight);
                StyleManager(addonName, owner, childManager, childWidth, childHeight, depth + 1, maxDepth);
            }

            if (componentType == ComponentType.Window)
            {
                try
                {
                    StyleWindowTitleIcons(addonName, owner, component);
                }
                catch
                {


                }
            }

            if (interactionBlockedAddonNames.Contains(addonName))
                continue;

            try
            {
                if (componentType == ComponentType.List)
                    StyleVisibleListRows(addonName, owner, (AtkComponentList*)component);
                else if (IsButtonLike(componentType))
                    StyleButtonComponent(addonName, owner, component, componentType);
            }
            catch (Exception ex)
            {
                throw new InteractionStylingException(ex);
            }
        }
    }

    private void TryStyleSurfaceNode(
        string addonName,
        AtkUnitBase* owner,
        AtkResNode* node,
        float safeRootWidth,
        float safeRootHeight,
        float rootArea,
        int depth)
    {
        var nodeWidth = (float)node->Width;
        var nodeHeight = (float)node->Height;
        if (nodeWidth <= 0f || nodeHeight <= 0f)
            return;

        var nodeArea = nodeWidth * nodeHeight;
        var widthRatio = nodeWidth / safeRootWidth;
        var heightRatio = nodeHeight / safeRootHeight;


        if (addonName == "Teleport" &&
            nodeWidth <= 96f &&
            nodeHeight <= 72f)
            return;


        var areaThreshold = depth == 0 ? 0.30f : 0.22f;
        var largeSurface = nodeArea >= rootArea * areaThreshold &&
                           widthRatio >= (depth == 0 ? 0.46f : 0.36f) &&
                           heightRatio >= (depth == 0 ? 0.28f : 0.20f);
        var horizontalChrome = nodeWidth >= 72f &&
                               nodeHeight >= 16f &&
                               widthRatio >= 0.50f &&
                               heightRatio >= 0.06f &&
                               heightRatio < 0.36f;
        var verticalPanel = nodeWidth >= 64f &&
                            nodeHeight >= 72f &&
                            widthRatio >= 0.20f &&
                            heightRatio >= 0.42f;

        if (!largeSurface && !horizontalChrome && !verticalPanel)
            return;

        var surfaceKind = horizontalChrome
            ? SurfaceKind.Chrome
            : verticalPanel && !largeSurface
                ? SurfaceKind.Panel
                : SurfaceKind.Main;

        var tint = surfaceKind switch
        {
            SurfaceKind.Chrome => ObsidianChrome,
            SurfaceKind.Panel => ObsidianPanel,
            _ when heightRatio > 0.84f => ObsidianDeep,
            _ => ObsidianMain,
        };

        StyleBackgroundNode(addonName, owner, node, tint);
    }

    private static float ResolveManagerWidth(AtkComponentBase* component, AtkUldManager* manager, float fallback)
    {
        if (manager->RootNodeWidth > 0)
            return manager->RootNodeWidth;
        if (component->OwnerNode != null && component->OwnerNode->AtkResNode.Width > 0)
            return component->OwnerNode->AtkResNode.Width;
        return fallback;
    }

    private static float ResolveManagerHeight(AtkComponentBase* component, AtkUldManager* manager, float fallback)
    {
        if (manager->RootNodeHeight > 0)
            return manager->RootNodeHeight;
        if (component->OwnerNode != null && component->OwnerNode->AtkResNode.Height > 0)
            return component->OwnerNode->AtkResNode.Height;
        return fallback;
    }

    private static bool ShouldTraverseChatComponent(ComponentType componentType)
        => componentType is
            ComponentType.Base or
            ComponentType.Window or
            ComponentType.Button or
            ComponentType.CheckBox or
            ComponentType.RadioButton or
            ComponentType.Tab or
            ComponentType.TextInput or
            ComponentType.TextNineGrid or
            ComponentType.Multipurpose;

    private static bool ShouldTraverseComponent(ComponentType componentType)
        => componentType is
            ComponentType.Base or
            ComponentType.Window or
            ComponentType.Button or
            ComponentType.CheckBox or
            ComponentType.DropDownList or
            ComponentType.NumericInput or
            ComponentType.TextInput or
            ComponentType.List or
            ComponentType.RadioButton or
            ComponentType.Tab or
            ComponentType.TreeList or
            ComponentType.ScrollBar or
            ComponentType.Slider or
            ComponentType.TextNineGrid or
            ComponentType.Multipurpose;

    private static bool IsButtonLike(ComponentType componentType)
        => componentType is
            ComponentType.Button or
            ComponentType.CheckBox or
            ComponentType.RadioButton or
            ComponentType.Tab;

    private void StyleButtonComponent(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentBase* component,
        ComponentType componentType)
    {
        var button = (AtkComponentButton*)component;
        var enabled = button->IsEnabled;
        var ownerNode = component->OwnerNode != null
            ? (AtkResNode*)component->OwnerNode
            : null;
        var background = button->ButtonBGNode;


        if (addonName == "Teleport" && IsCompactTeleportControl(ownerNode, background))
        {
            if (button->ButtonTextNode != null)
                StyleTextNode(addonName, owner, button->ButtonTextNode, !enabled);
            return;
        }

        var mousePosition = ImGui.GetIO().MousePos;
        var hovered = enabled &&
                      ((background != null && IsPointInsideNode(background, mousePosition)) ||
                       IsPointInsideNode(ownerNode, mousePosition));

        var titleBarButton = componentType == ComponentType.Button &&
                             IsTitleBarButton(owner, ownerNode);
        var characterCompactControl = IsCharacterFamily(addonName) &&
                                      (componentType is ComponentType.Button or ComponentType.CheckBox or ComponentType.RadioButton) &&
                                      IsCompactCharacterControlShape(ownerNode, background);
        var actionButton = !titleBarButton &&
                           ((componentType == ComponentType.Button && IsActionButtonShape(ownerNode, background)) ||
                            characterCompactControl);

        var selected = componentType switch
        {
            ComponentType.CheckBox => button->IsChecked,
            ComponentType.RadioButton or ComponentType.Tab =>
                button->IsChecked || ((AtkComponentRadioButton*)component)->IsSelected,
            _ => false,
        };

        if (actionButton)
        {


            var hasIvoryFace = StyleActionButtonChrome(
                addonName,
                owner,
                component,
                background,
                hovered,
                selected,
                enabled,
                characterCompactControl);
            StyleActionButtonTextNodes(
                addonName,
                owner,
                component,
                button->ButtonTextNode,
                enabled,
                hasIvoryFace && !IsCharacterFamily(addonName));

            if (button->ButtonTextNode == null && hasIvoryFace)
                StyleActionButtonIcons(addonName, owner, component, background, enabled);

            return;
        }

        if (button->ButtonTextNode != null)
            StyleTextNode(addonName, owner, button->ButtonTextNode, !enabled);

        if (background != null)
            StyleInteractiveBackground(addonName, owner, background, hovered, selected);


        if (titleBarButton && button->ButtonTextNode == null)
            StyleCompactButtonIcons(addonName, owner, component, background, hovered, !enabled);
    }

    private static bool IsCompactTeleportControl(AtkResNode* ownerNode, AtkResNode* background)
    {
        var width = ownerNode != null && ownerNode->Width > 0
            ? (float)ownerNode->Width
            : background != null ? (float)background->Width : 0f;
        var height = ownerNode != null && ownerNode->Height > 0
            ? (float)ownerNode->Height
            : background != null ? (float)background->Height : 0f;

        return width >= 8f && width <= 96f &&
               height >= 8f && height <= 72f;
    }

    private static bool IsActionButtonShape(AtkResNode* ownerNode, AtkResNode* background)
    {
        var width = ownerNode != null && ownerNode->Width > 0
            ? (float)ownerNode->Width
            : background != null ? (float)background->Width : 0f;
        var height = ownerNode != null && ownerNode->Height > 0
            ? (float)ownerNode->Height
            : background != null ? (float)background->Height : 0f;

        return width >= 18f && width <= 480f &&
               height >= 14f && height <= 96f;
    }

    private static bool IsCompactCharacterControlShape(AtkResNode* ownerNode, AtkResNode* background)
    {
        var width = ownerNode != null && ownerNode->Width > 0
            ? (float)ownerNode->Width
            : background != null ? (float)background->Width : 0f;
        var height = ownerNode != null && ownerNode->Height > 0
            ? (float)ownerNode->Height
            : background != null ? (float)background->Height : 0f;


        return width >= 10f && width <= 72f &&
               height >= 10f && height <= 72f;
    }

    private static bool IsCharacterFamily(string addonName)
        => addonName.StartsWith("Character", StringComparison.Ordinal);

    private static bool IsActionMenuFamily(string addonName)
        => addonName is "ActionMenu" or "ActionMenuReplace";

    private static bool IsChatLogFamily(string addonName)
        => addonName is
            "_ChatLog" or "ChatLog" or
            "_ChatLogPanel_0" or "ChatLogPanel_0" or
            "_ChatLogPanel_1" or "ChatLogPanel_1" or
            "_ChatLogPanel_2" or "ChatLogPanel_2" or
            "_ChatLogPanel_3" or "ChatLogPanel_3";

    private static bool IsTitleBarButton(AtkUnitBase* owner, AtkResNode* ownerNode)
    {
        if (owner == null || ownerNode == null ||
            ownerNode->Width == 0 || ownerNode->Height == 0 ||
            ownerNode->Width > 72 || ownerNode->Height > 72)
            return false;

        try
        {
            Bounds ownerBounds;
            owner->GetRootBounds(&ownerBounds);
            Bounds buttonBounds;
            ownerNode->GetBounds(&buttonBounds);

            var rootMaxX = MathF.Max(ownerBounds.Pos1.X, ownerBounds.Pos2.X);
            var rootMinY = MathF.Min(ownerBounds.Pos1.Y, ownerBounds.Pos2.Y);
            var buttonMinX = MathF.Min(buttonBounds.Pos1.X, buttonBounds.Pos2.X);
            var buttonMaxX = MathF.Max(buttonBounds.Pos1.X, buttonBounds.Pos2.X);
            var buttonMinY = MathF.Min(buttonBounds.Pos1.Y, buttonBounds.Pos2.Y);
            var buttonMaxY = MathF.Max(buttonBounds.Pos1.Y, buttonBounds.Pos2.Y);
            var centerX = (buttonMinX + buttonMaxX) * 0.5f;
            var centerY = (buttonMinY + buttonMaxY) * 0.5f;

            return centerX >= rootMaxX - 96f &&
                   centerY <= rootMinY + 58f;
        }
        catch
        {
            return false;
        }
    }

    private bool StyleActionButtonChrome(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentBase* component,
        AtkResNode* primaryBackground,
        bool hovered,
        bool selected,
        bool enabled,
        bool forceCompactSurfaces)
    {
        var styledFace = false;
        if (primaryBackground != null)
        {
            StyleStandardButtonBackground(addonName, owner, primaryBackground, hovered, selected, enabled);
            styledFace = true;
        }

        if (component == null || component->OwnerNode == null)
            return styledFace;

        var ownerNode = (AtkResNode*)component->OwnerNode;
        var ownerWidth = MathF.Max(1f, ownerNode->Width);
        var ownerHeight = MathF.Max(1f, ownerNode->Height);
        var ownerArea = ownerWidth * ownerHeight;
        var manager = &component->UldManager;
        if (manager->NodeList == null || manager->NodeListCount == 0)
            return styledFace;


        var count = Math.Min((int)manager->NodeListCount, 64);
        var styledSurfaceCount = 0;
        for (var i = 0; i < count && styledSurfaceCount < 6; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node == primaryBackground)
                continue;

            var rawType = (ushort)node->Type;
            if (rawType != (ushort)NodeType.Image && rawType != (ushort)NodeType.NineGrid)
                continue;

            if (!IsActionButtonSurfaceCandidate(node, ownerWidth, ownerHeight, ownerArea, forceCompactSurfaces))
                continue;

            StyleStandardButtonBackground(addonName, owner, node, hovered, selected, enabled);
            styledFace = true;
            styledSurfaceCount++;
        }

        return styledFace;
    }

    private static bool IsActionButtonSurfaceCandidate(
        AtkResNode* node,
        float ownerWidth,
        float ownerHeight,
        float ownerArea,
        bool allowHiddenCompactLayer)
    {
        if (node == null || node->Width == 0 || node->Height == 0)
            return false;

        var rawType = (ushort)node->Type;
        if (rawType != (ushort)NodeType.Image && rawType != (ushort)NodeType.NineGrid)
            return false;


        if (!node->IsVisible() && (!allowHiddenCompactLayer || node->Color.A == 0))
            return false;

        var width = (float)node->Width;
        var height = (float)node->Height;
        var area = width * height;
        var widthRatio = width / MathF.Max(1f, ownerWidth);
        var heightRatio = height / MathF.Max(1f, ownerHeight);
        var areaRatio = area / MathF.Max(1f, ownerArea);

        if (allowHiddenCompactLayer)
            return widthRatio >= 0.72f && heightRatio >= 0.72f && areaRatio >= 0.52f;

        return rawType == (ushort)NodeType.NineGrid
            ? widthRatio >= 0.52f && heightRatio >= 0.42f && areaRatio >= 0.24f
            : widthRatio >= 0.70f && heightRatio >= 0.70f && areaRatio >= 0.52f;
    }

    private void StyleActionButtonTextNodes(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentBase* component,
        AtkTextNode* primaryText,
        bool enabled,
        bool useDarkText)
    {
        if (primaryText != null)
            StyleStandardButtonText(addonName, owner, primaryText, enabled, useDarkText);

        var manager = &component->UldManager;
        if (manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var count = Math.Min((int)manager->NodeListCount, 64);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node == (AtkResNode*)primaryText ||
                (ushort)node->Type != (ushort)NodeType.Text)
                continue;

            StyleStandardButtonText(addonName, owner, (AtkTextNode*)node, enabled, useDarkText);
        }
    }

    private void StyleActionButtonIcons(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentBase* component,
        AtkResNode* background,
        bool enabled)
    {
        if (component == null || component->OwnerNode == null)
            return;

        var ownerNode = (AtkResNode*)component->OwnerNode;
        var ownerWidth = MathF.Max(1f, ownerNode->Width);
        var ownerHeight = MathF.Max(1f, ownerNode->Height);
        var ownerArea = ownerWidth * ownerHeight;
        var manager = &component->UldManager;
        if (manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var count = Math.Min((int)manager->NodeListCount, 64);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node == background || !node->IsVisible() ||
                (ushort)node->Type != (ushort)NodeType.Image ||
                IsActionButtonSurfaceCandidate(node, ownerWidth, ownerHeight, ownerArea, false))
                continue;

            var width = (float)node->Width;
            var height = (float)node->Height;
            if (width < 3f || height < 3f ||
                width > ownerWidth || height > ownerHeight ||
                width * height > ownerArea * 0.48f)
                continue;

            SnapshotNode(addonName, owner, node, null);
            if (!snapshots.TryGetValue((nint)node, out var original))
                continue;

            var tint = enabled ? ButtonIconBlack : ButtonTextDisabled;
            ApplyButtonMaterial(node, original, tint, enabled ? 0 : 8, (byte)(enabled ? 100 : 88));
        }
    }

    private void StyleCompactButtonIcons(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentBase* component,
        AtkResNode* background,
        bool hovered,
        bool disabled)
    {
        if (component == null || component->OwnerNode == null)
            return;

        var ownerNode = (AtkResNode*)component->OwnerNode;
        var ownerWidth = (float)ownerNode->Width;
        var ownerHeight = (float)ownerNode->Height;
        if (ownerWidth < 8f || ownerHeight < 8f || ownerWidth > 72f || ownerHeight > 72f)
            return;

        var manager = &component->UldManager;
        if (manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var ownerArea = MathF.Max(1f, ownerWidth * ownerHeight);
        var count = Math.Min((int)manager->NodeListCount, 48);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node == background || !node->IsVisible())
                continue;

            var rawType = (ushort)node->Type;
            if (rawType != (ushort)NodeType.Image && rawType != (ushort)NodeType.NineGrid)
                continue;

            var width = (float)node->Width;
            var height = (float)node->Height;
            if (width < 3f || height < 3f ||
                width > ownerWidth || height > ownerHeight ||
                width * height > ownerArea * 0.78f)
                continue;

            SnapshotNode(addonName, owner, node, null);
            if (!snapshots.TryGetValue((nint)node, out var original))
                continue;

            var tint = disabled ? MutedIvory : AncientIvory;
            ApplyVisualTint(
                node,
                original,
                tint,
                (byte)Math.Max((int)original.Color.A, disabled ? 150 : 218),
                hovered ? 14 : 5,
                hovered ? 14 : 5,
                hovered ? 16 : 7);
        }
    }

    private void StyleWindowTitleIcons(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentBase* windowComponent)
    {
        if (windowComponent == null || windowComponent->OwnerNode == null)
            return;

        var ownerNode = (AtkResNode*)windowComponent->OwnerNode;
        if (!ownerNode->IsVisible() || ownerNode->Width < 80 || ownerNode->Height < 40)
            return;

        Bounds ownerBounds;
        ownerNode->GetBounds(&ownerBounds);
        var ownerMinX = MathF.Min(ownerBounds.Pos1.X, ownerBounds.Pos2.X);
        var ownerMaxX = MathF.Max(ownerBounds.Pos1.X, ownerBounds.Pos2.X);
        var ownerMinY = MathF.Min(ownerBounds.Pos1.Y, ownerBounds.Pos2.Y);

        var manager = &windowComponent->UldManager;
        if (manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var count = Math.Min((int)manager->NodeListCount, 128);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !node->IsVisible())
                continue;

            var rawType = (ushort)node->Type;
            if (rawType != (ushort)NodeType.Image && rawType != (ushort)NodeType.NineGrid)
                continue;

            var width = (float)node->Width;
            var height = (float)node->Height;
            if (width < 4f || height < 4f || width > 36f || height > 36f)
                continue;

            Bounds bounds;
            node->GetBounds(&bounds);
            var minX = MathF.Min(bounds.Pos1.X, bounds.Pos2.X);
            var maxX = MathF.Max(bounds.Pos1.X, bounds.Pos2.X);
            var minY = MathF.Min(bounds.Pos1.Y, bounds.Pos2.Y);
            var maxY = MathF.Max(bounds.Pos1.Y, bounds.Pos2.Y);
            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;


            if (centerX < ownerMaxX - 72f || centerX > ownerMaxX + 4f ||
                centerY < ownerMinY - 4f || centerY > ownerMinY + 54f ||
                centerX < ownerMinX)
                continue;

            SnapshotNode(addonName, owner, node, null);
            if (!snapshots.TryGetValue((nint)node, out var original))
                continue;

            ApplyVisualTint(
                node,
                original,
                AncientIvory,
                (byte)Math.Max((int)original.Color.A, 218),
                6,
                6,
                8);
        }
    }

    private void StyleVisibleListRows(string addonName, AtkUnitBase* owner, AtkComponentList* list)
    {
        if (list == null || list->ListLength <= 0 || !list->IsItemInteractionEnabled)
            return;

        if (list->ItemRendererList == null || list->AllocatedItemRendererListLength <= 0)
            return;

        var listLength = Math.Clamp(list->ListLength, 0, 4096);
        var hoveredIndex = ResolveHoveredIndex(list);
        var selectedIndex = list->SelectedItemIndex;
        var mousePosition = ImGui.GetIO().MousePos;
        var allocated = Math.Clamp(list->AllocatedItemRendererListLength, 0, MaxListRenderers);

        for (var slot = 0; slot < allocated; slot++)
        {
            var listItem = &list->ItemRendererList[slot];
            var renderer = listItem->AtkComponentListItemRenderer;
            if (renderer == null)
                continue;

            var renderedIndex = renderer->ListItemIndex;
            if (renderedIndex < 0 || renderedIndex >= listLength)
                continue;

            var button = (AtkComponentButton*)renderer;
            var isHovered = renderedIndex == hoveredIndex || IsRendererUnderMouse(renderer, button, mousePosition);
            var isSelected = renderedIndex == selectedIndex;
            var disabled = listItem->IsDisabled;

            StyleLiveRenderer(addonName, owner, renderer, list, isHovered, isSelected, disabled);
        }
    }

    private static int ResolveHoveredIndex(AtkComponentList* list)
    {
        if (list->HoveredItemIndex >= 0)
            return list->HoveredItemIndex;
        if (list->HoveredItemIndex2 >= 0)
            return list->HoveredItemIndex2;
        return list->HoveredItemIndex3;
    }

    private static bool IsRendererUnderMouse(
        AtkComponentListItemRenderer* renderer,
        AtkComponentButton* button,
        NumericsVector2 mousePosition)
    {
        if (button != null && IsPointInsideNode(button->ButtonBGNode, mousePosition))
            return true;

        var baseComponent = (AtkComponentBase*)renderer;
        if (baseComponent->OwnerNode != null &&
            IsPointInsideNode((AtkResNode*)baseComponent->OwnerNode, mousePosition))
            return true;

        var templateCount = renderer->RowTemplateNodeCount & 0xFFFF;
        return templateCount == 1 && IsPointInsideNode(renderer->RowTemplateNode, mousePosition);
    }

    private static bool IsPointInsideNode(AtkResNode* node, NumericsVector2 point)
    {
        if (node == null || !node->IsVisible() || node->Width == 0 || node->Height == 0)
            return false;

        Bounds bounds;
        node->GetBounds(&bounds);
        var minX = MathF.Min(bounds.Pos1.X, bounds.Pos2.X);
        var maxX = MathF.Max(bounds.Pos1.X, bounds.Pos2.X);
        var minY = MathF.Min(bounds.Pos1.Y, bounds.Pos2.Y);
        var maxY = MathF.Max(bounds.Pos1.Y, bounds.Pos2.Y);
        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
    }

    private void StyleLiveRenderer(
        string addonName,
        AtkUnitBase* owner,
        AtkComponentListItemRenderer* renderer,
        AtkComponentList* list,
        bool hovered,
        bool selected,
        bool disabled)
    {
        var button = (AtkComponentButton*)renderer;
        if (button->ButtonTextNode != null)
            StyleTextNode(addonName, owner, button->ButtonTextNode, disabled);

        if (button->ButtonBGNode != null)
            StyleRowBackground(addonName, owner, button->ButtonBGNode, hovered, selected);

        var rowTemplateCount = renderer->RowTemplateNodeCount & 0xFFFF;
        if (rowTemplateCount == 1 && renderer->RowTemplateNode != null)
            StyleRowBackground(addonName, owner, renderer->RowTemplateNode, hovered, selected);

        var baseComponent = (AtkComponentBase*)renderer;
        var rendererManager = &baseComponent->UldManager;
        if (rendererManager->NodeList == null || rendererManager->NodeListCount == 0)
            return;

        var rowWidth = MathF.Max(1f,
            list->ItemWidth > 0 ? (float)list->ItemWidth : (float)rendererManager->RootNodeWidth);
        var rowHeight = MathF.Max(1f,
            list->ItemHeight > 0 ? (float)list->ItemHeight : (float)rendererManager->RootNodeHeight);
        var rowArea = rowWidth * rowHeight;
        var nodeCount = Math.Min((int)rendererManager->NodeListCount, MaxRendererNodes);

        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            var rowNode = rendererManager->NodeList[nodeIndex];
            if (rowNode == null || rowNode == button->ButtonBGNode)
                continue;

            var rawType = (ushort)rowNode->Type;
            if (rawType == (ushort)NodeType.Text)
            {
                StyleTextNode(addonName, owner, (AtkTextNode*)rowNode, disabled);
                continue;
            }

            if (rawType != (ushort)NodeType.Image && rawType != (ushort)NodeType.NineGrid)
                continue;

            var width = (float)rowNode->Width;
            var height = (float)rowNode->Height;
            if (width <= 0f || height <= 0f)
                continue;

            var rowSized = width >= rowWidth * 0.55f &&
                           height >= rowHeight * 0.45f &&
                           width * height >= rowArea * 0.28f;
            if (rowSized)
                StyleRowBackground(addonName, owner, rowNode, hovered, selected);
        }
    }

    private void StyleStandardButtonText(
        string addonName,
        AtkUnitBase* owner,
        AtkTextNode* textNode,
        bool enabled,
        bool useDarkText)
    {
        if (textNode == null)
            return;

        var node = (AtkResNode*)textNode;
        SnapshotNode(addonName, owner, node, textNode);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;


        var forceReadableCharacterText = IsCharacterFamily(addonName) && !useDarkText;
        var desired = useDarkText
            ? enabled ? ButtonTextBlack : ButtonTextDisabled
            : forceReadableCharacterText
                ? AncientIvory
                : enabled ? AncientIvory : MutedIvory;
        var minimumAlpha = useDarkText
            ? desired.A
            : forceReadableCharacterText
                ? (byte)238
                : (byte)(enabled ? 225 : 190);
        textNode->TextColor = new ByteColor
        {
            R = desired.R,
            G = desired.G,
            B = desired.B,
            A = original.TextColor.A == 0
                ? (byte)0
                : (byte)Math.Max((int)original.TextColor.A, minimumAlpha),
        };
        textNode->EdgeColor = useDarkText
            ? new ByteColor
            {
                R = 0,
                G = 0,
                B = 0,
                A = enabled ? (byte)42 : (byte)28,
            }
            : new ByteColor
            {
                R = TextEdge.R,
                G = TextEdge.G,
                B = TextEdge.B,
                A = (byte)Math.Max((int)original.EdgeColor.A, TextEdge.A),
            };
        node->IsDirty = true;
    }

    private void StyleStandardButtonBackground(
        string addonName,
        AtkUnitBase* owner,
        AtkResNode* node,
        bool hovered,
        bool selected,
        bool enabled)
    {
        if (node == null)
            return;

        SnapshotNode(addonName, owner, node, null);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;

        node->NodeFlags = original.NodeFlags | NodeFlags.Visible;
        var desired = !enabled
            ? ButtonIvoryDisabled
            : hovered
                ? ButtonIvoryHover
                : selected
                    ? ButtonIvoryPressed
                    : ButtonIvory;

        var additive = !enabled ? 28 : hovered ? 112 : selected ? 72 : 88;
        var multiply = (byte)(!enabled ? 88 : 100);
        ApplyButtonMaterial(node, original, desired, additive, multiply);
    }

    private static void ApplyButtonMaterial(
        AtkResNode* node,
        WindowNodeSnapshot original,
        ByteColor tint,
        int additive,
        byte multiply)
    {
        node->Color = new ByteColor
        {
            R = tint.R,
            G = tint.G,
            B = tint.B,
            A = (byte)Math.Max((int)original.Color.A, tint.A),
        };
        node->AddRed = AddClamped(original.AddRed, additive);
        node->AddGreen = AddClamped(original.AddGreen, additive);
        node->AddBlue = AddClamped(original.AddBlue, additive);
        node->MultiplyRed = multiply;
        node->MultiplyGreen = multiply;
        node->MultiplyBlue = multiply;
        node->NodeFlags = original.NodeFlags | NodeFlags.Visible;
        node->IsDirty = true;
    }

    private void StyleInteractiveBackground(
        string addonName,
        AtkUnitBase* owner,
        AtkResNode* node,
        bool hovered,
        bool selected,
        bool forceVisibleChrome = false)
    {
        if (node == null)
            return;

        SnapshotNode(addonName, owner, node, null);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;

        if (!hovered && !selected)
        {
            node->NodeFlags = forceVisibleChrome
                ? original.NodeFlags | NodeFlags.Visible
                : original.NodeFlags;
            if ((original.NodeFlags & NodeFlags.Visible) == 0 && !forceVisibleChrome)
            {
                RestoreVisualNode(node, original);
                return;
            }

            var normalTint = forceVisibleChrome ? GlossButton : ObsidianChrome;
            var normalAlpha = forceVisibleChrome
                ? (byte)Math.Max((int)original.Color.A, GlossButton.A)
                : original.Color.A;
            ApplyVisualTint(
                node,
                original,
                normalTint,
                normalAlpha,
                forceVisibleChrome ? 7 : 0,
                forceVisibleChrome ? 7 : 0,
                forceVisibleChrome ? 10 : 0);
            return;
        }

        node->NodeFlags |= NodeFlags.Visible;
        var desired = hovered ? HoverGrey : SelectedGrey;
        ApplyVisualTint(
            node,
            original,
            desired,
            (byte)Math.Max((int)original.Color.A, desired.A),
            hovered ? 10 : 5,
            hovered ? 10 : 5,
            hovered ? 12 : 7);
    }

    private void StyleRowBackground(
        string addonName,
        AtkUnitBase* owner,
        AtkResNode* node,
        bool hovered,
        bool selected)
    {
        if (node == null)
            return;

        SnapshotNode(addonName, owner, node, null);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;

        if (!hovered && !selected)
        {
            RestoreVisualNode(node, original);
            return;
        }

        node->NodeFlags |= NodeFlags.Visible;
        var desired = hovered ? HoverGrey : SelectedGrey;
        ApplyVisualTint(
            node,
            original,
            desired,
            (byte)Math.Max((int)original.Color.A, desired.A),
            hovered ? 10 : 5,
            hovered ? 10 : 5,
            hovered ? 12 : 7);
    }

    private static void ApplyVisualTint(
        AtkResNode* node,
        WindowNodeSnapshot original,
        ByteColor tint,
        byte alpha,
        int addRed,
        int addGreen,
        int addBlue)
    {
        node->Color = new ByteColor
        {
            R = tint.R,
            G = tint.G,
            B = tint.B,
            A = alpha,
        };
        node->AddRed = AddClamped(original.AddRed, addRed);
        node->AddGreen = AddClamped(original.AddGreen, addGreen);
        node->AddBlue = AddClamped(original.AddBlue, addBlue);
        node->MultiplyRed = original.MultiplyRed;
        node->MultiplyGreen = original.MultiplyGreen;
        node->MultiplyBlue = original.MultiplyBlue;
        node->IsDirty = true;
    }

    private static void RestoreVisualNode(AtkResNode* node, WindowNodeSnapshot original)
    {
        node->Color = original.Color;
        node->AddRed = original.AddRed;
        node->AddGreen = original.AddGreen;
        node->AddBlue = original.AddBlue;
        node->MultiplyRed = original.MultiplyRed;
        node->MultiplyGreen = original.MultiplyGreen;
        node->MultiplyBlue = original.MultiplyBlue;
        node->NodeFlags = original.NodeFlags;
        node->IsDirty = true;
    }

    private void StyleBackgroundNode(
        string addonName,
        AtkUnitBase* owner,
        AtkResNode* node,
        ByteColor tint)
    {
        SnapshotNode(addonName, owner, node, null);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;


        ApplyVisualTint(node, original, tint, original.Color.A, 0, 0, 0);
    }

    private void StyleTextNode(string addonName, AtkUnitBase* owner, AtkTextNode* textNode)
        => StyleTextNode(addonName, owner, textNode, null);

    private void StyleTextNode(string addonName, AtkUnitBase* owner, AtkTextNode* textNode, bool? disabledOverride)
    {
        if (textNode == null)
            return;

        var node = (AtkResNode*)textNode;
        SnapshotNode(addonName, owner, node, textNode);
        if (!snapshots.TryGetValue((nint)node, out var original))
            return;

        var forceReadableCharacterText = IsCharacterFamily(addonName) && original.TextColor.A > 0;
        var forceReadableActionMenuText = IsActionMenuFamily(addonName) && original.TextColor.A > 0;
        var disabled = forceReadableCharacterText || forceReadableActionMenuText
            ? false
            : disabledOverride ?? IsProbablyDisabled(original.TextColor);
        var desired = disabled ? MutedIvory : AncientIvory;
        var minimumAlpha = forceReadableCharacterText || forceReadableActionMenuText
            ? 238
            : 0;
        textNode->TextColor = new ByteColor
        {
            R = desired.R,
            G = desired.G,
            B = desired.B,
            A = original.TextColor.A == 0
                ? (byte)0
                : (byte)Math.Max((int)original.TextColor.A, minimumAlpha),
        };
        textNode->EdgeColor = new ByteColor
        {
            R = TextEdge.R,
            G = TextEdge.G,
            B = TextEdge.B,
            A = (byte)Math.Max(original.EdgeColor.A, TextEdge.A),
        };
        node->IsDirty = true;
    }

    private static short AddClamped(short original, int amount)
        => (short)Math.Clamp((int)original + amount, short.MinValue, short.MaxValue);

    private static bool IsProbablyDisabled(ByteColor color)
    {
        var luminance = (color.R * 3 + color.G * 6 + color.B) / 10;
        return color.A < 150 || luminance < 108;
    }

    private void SnapshotNode(string addonName, AtkUnitBase* owner, AtkResNode* node, AtkTextNode* textNode)
    {
        var key = (nint)node;
        if (snapshots.ContainsKey(key))
            return;

        snapshots[key] = new WindowNodeSnapshot(
            addonName,
            (nint)owner,
            node->Color,
            node->AddRed,
            node->AddGreen,
            node->AddBlue,
            node->MultiplyRed,
            node->MultiplyGreen,
            node->MultiplyBlue,
            node->NodeFlags,
            textNode != null,
            textNode != null ? textNode->TextColor : default,
            textNode != null ? textNode->EdgeColor : default,
            textNode != null ? textNode->BackgroundColor : default,
            textNode != null ? textNode->TextFlags : TextFlags.None);
    }

    private void RestoreVisibleNodesSafely()
    {
        foreach (var addonName in new List<string>(styledAddonNames))
        {
            try
            {
                var addon = gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
                if (addon == null || !addon->IsReady || addon->RootNode == null)
                    continue;

                BeginTraversalPass();
                if (IsChatLogFamily(addonName))
                    RestoreChatShellOnly(addonName, &addon->UldManager, 0);
                else if (IsActionMenuFamily(addonName))
                    RestoreActionMenuShellOnly(addonName, &addon->UldManager);
                else
                {
                    var maxDepth = ComplexWindowNames.Contains(addonName) ? MaxNestedDepth : MaxSimpleDepth;
                    RestoreManager(addonName, &addon->UldManager, 0, maxDepth);
                }
            }
            catch (Exception ex)
            {
                log.Verbose(ex,
                    $"RE:Frame skipped restoring part of {addonName}; reopening the window will rebuild its native material.");
            }
            finally
            {
                RemoveSnapshotsForName(addonName);
            }
        }

        snapshots.Clear();
        styledAddonNames.Clear();
        visitedManagers.Clear();
        visibleWindowBounds.Clear();
    }

    private void RestoreChatShellOnly(string addonName, AtkUldManager* manager, int depth)
    {
        if (manager == null ||
            manager->NodeList == null ||
            manager->NodeListCount == 0 ||
            depth > 2 ||
            !visitedManagers.Add((nint)manager))
            return;

        var count = Math.Min((int)manager->NodeListCount, 512);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            RestoreNodeIfOwned(addonName, node);

            var rawType = (ushort)node->Type;
            if (rawType < 1000 || depth >= 2)
                continue;

            var componentNode = (AtkComponentNode*)node;
            var component = componentNode->Component;
            if (component == null ||
                (component->OwnerNode != null && component->OwnerNode != componentNode))
                continue;

            ComponentType componentType;
            try
            {
                componentType = component->GetComponentType();
            }
            catch
            {
                continue;
            }

            if (ShouldTraverseChatComponent(componentType))
                RestoreChatShellOnly(addonName, &component->UldManager, depth + 1);
        }
    }

    private void RestoreActionMenuShellOnly(string addonName, AtkUldManager* manager)
    {
        if (manager == null || manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var count = Math.Min((int)manager->NodeListCount, MaxNodesPerManager);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node != null)
                RestoreNodeIfOwned(addonName, node);
        }
    }

    private void RestoreManager(string addonName, AtkUldManager* manager, int depth, int maxDepth)
    {
        if (manager == null ||
            manager->NodeList == null ||
            manager->NodeListCount == 0 ||
            depth > maxDepth ||
            !visitedManagers.Add((nint)manager))
            return;

        if (++managersVisitedThisPass > MaxManagersPerPass)
            return;

        var count = Math.Min((int)manager->NodeListCount, MaxNodesPerManager);
        if (count <= 0 || nodesVisitedThisPass + count > MaxNodesPerPass)
            return;

        nodesVisitedThisPass += count;

        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            RestoreNodeIfOwned(addonName, node);

            var rawType = (ushort)node->Type;
            if (rawType < 1000)
                continue;

            var componentNode = (AtkComponentNode*)node;
            var component = componentNode->Component;
            if (component == null ||
                (component->OwnerNode != null && component->OwnerNode != componentNode))
                continue;

            ComponentType componentType;
            try
            {
                componentType = component->GetComponentType();
            }
            catch
            {
                continue;
            }

            if (IsButtonLike(componentType))
                RestoreButtonNodes(addonName, (AtkComponentButton*)component);
            if (componentType == ComponentType.List)
                RestoreVisibleListRows(addonName, (AtkComponentList*)component, depth + 1, maxDepth);

            if (depth < maxDepth && ShouldTraverseComponent(componentType))
                RestoreManager(addonName, &component->UldManager, depth + 1, maxDepth);
        }
    }

    private void RestoreButtonNodes(string addonName, AtkComponentButton* button)
    {
        if (button == null)
            return;

        if (button->ButtonTextNode != null)
            RestoreNodeIfOwned(addonName, (AtkResNode*)button->ButtonTextNode);
        if (button->ButtonBGNode != null)
            RestoreNodeIfOwned(addonName, button->ButtonBGNode);

        var manager = &((AtkComponentBase*)button)->UldManager;
        if (manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var count = Math.Min((int)manager->NodeListCount, 64);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node != null)
                RestoreNodeIfOwned(addonName, node);
        }
    }

    private void RestoreVisibleListRows(
        string addonName,
        AtkComponentList* list,
        int depth,
        int maxDepth)
    {
        if (list == null || list->ItemRendererList == null || list->AllocatedItemRendererListLength <= 0)
            return;

        var allocated = Math.Clamp(list->AllocatedItemRendererListLength, 0, MaxListRenderers);
        for (var slot = 0; slot < allocated; slot++)
        {
            var renderer = list->ItemRendererList[slot].AtkComponentListItemRenderer;
            if (renderer == null)
                continue;

            var button = (AtkComponentButton*)renderer;
            RestoreButtonNodes(addonName, button);

            var templateCount = renderer->RowTemplateNodeCount & 0xFFFF;
            if (templateCount == 1 && renderer->RowTemplateNode != null)
                RestoreNodeIfOwned(addonName, renderer->RowTemplateNode);

            RestoreManager(addonName, &((AtkComponentBase*)renderer)->UldManager, depth, maxDepth);
        }
    }

    private void RestoreNodeIfOwned(string addonName, AtkResNode* node)
    {
        if (node == null ||
            !snapshots.TryGetValue((nint)node, out var snapshot) ||
            !string.Equals(snapshot.AddonName, addonName, StringComparison.Ordinal))
            return;

        RestoreNode(node, snapshot);
        snapshots.Remove((nint)node);
    }

    private void RemoveSnapshotsForName(string addonName)
    {
        foreach (var pair in new List<KeyValuePair<nint, WindowNodeSnapshot>>(snapshots))
        {
            if (string.Equals(pair.Value.AddonName, addonName, StringComparison.Ordinal))
                snapshots.Remove(pair.Key);
        }
    }

    private static void RestoreNode(AtkResNode* node, WindowNodeSnapshot snapshot)
    {
        if (node == null)
            return;

        node->Color = snapshot.Color;
        node->AddRed = snapshot.AddRed;
        node->AddGreen = snapshot.AddGreen;
        node->AddBlue = snapshot.AddBlue;
        node->MultiplyRed = snapshot.MultiplyRed;
        node->MultiplyGreen = snapshot.MultiplyGreen;
        node->MultiplyBlue = snapshot.MultiplyBlue;
        node->NodeFlags = snapshot.NodeFlags;

        if (snapshot.IsText)
        {
            var textNode = (AtkTextNode*)node;
            textNode->TextColor = snapshot.TextColor;
            textNode->EdgeColor = snapshot.EdgeColor;
            textNode->BackgroundColor = snapshot.BackgroundColor;
            textNode->TextFlags = snapshot.TextFlags;
        }

        node->IsDirty = true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        framework.Update -= OnFrameworkUpdate;
        addonLifecycle.UnregisterListener(OnPreDraw, OnPreFinalize);


        RestoreVisibleNodesSafely();
        snapshots.Clear();
        styledAddonNames.Clear();
        blockedAddonNames.Clear();
        interactionBlockedAddonNames.Clear();
        visibleWindowBounds.Clear();
        hudOcclusionWindowBounds.Clear();
        HasProtectedDutyWindowOpen = false;
        visitedManagers.Clear();
    }

    private enum SurfaceKind
    {
        Main,
        Panel,
        Chrome,
    }

    private sealed class InteractionStylingException : Exception
    {
        public InteractionStylingException(Exception innerException)
            : base("Native interaction styling failed.", innerException)
        {
        }
    }

    private readonly record struct WindowNodeSnapshot(
        string AddonName,
        nint OwnerAddon,
        ByteColor Color,
        short AddRed,
        short AddGreen,
        short AddBlue,
        byte MultiplyRed,
        byte MultiplyGreen,
        byte MultiplyBlue,
        NodeFlags NodeFlags,
        bool IsText,
        ByteColor TextColor,
        ByteColor EdgeColor,
        ByteColor BackgroundColor,
        TextFlags TextFlags);
}

public readonly record struct NativeWindowBounds(NumericsVector2 Min, NumericsVector2 Max);
