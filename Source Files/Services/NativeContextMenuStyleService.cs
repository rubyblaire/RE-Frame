using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using NumericsVector2 = System.Numerics.Vector2;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace REFrameXIV.Services;


public sealed unsafe class NativeContextMenuStyleService : IDisposable
{
    private static readonly string[] MenuAddonNames =
    {
        "ContextMenu",
        "AddonContextSub",
        "AddonContextMenuTitle",
    };

    private static readonly ByteColor Obsidian = new() { R = 18, G = 20, B = 27, A = 255 };
    private static readonly ByteColor ObsidianDeep = new() { R = 10, G = 12, B = 17, A = 255 };
    private static readonly ByteColor AncientIvory = new() { R = 239, G = 228, B = 196, A = 255 };
    private static readonly ByteColor MutedIvory = new() { R = 133, G = 129, B = 116, A = 255 };
    private static readonly ByteColor TextEdge = new() { R = 5, G = 6, B = 9, A = 220 };
    private static readonly ByteColor HoverGrey = new() { R = 108, G = 112, B = 120, A = 184 };

    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly List<NativeMenuBounds> visibleMenuBounds = new(3);
    private readonly Dictionary<nint, NativeNodeSnapshot> nodeSnapshots = new();
    private readonly HashSet<nint> visitedManagers = new();

    private long openingGraceUntilMs;
    private bool hadVisibleMenu;
    private bool disposed;

    public NativeContextMenuStyleService(
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
        addonLifecycle.RegisterListener(AddonEvent.PreDraw, OnMenuPreDraw);
    }

    public bool IsAnyMenuOpen { get; private set; }


    public IReadOnlyList<NativeMenuBounds> VisibleMenuBounds => visibleMenuBounds;

    public void MarkOpening(ulong actorId)
    {
        _ = actorId;
        openingGraceUntilMs = Environment.TickCount64 + 500;
        IsAnyMenuOpen = true;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed)
            return;

        visibleMenuBounds.Clear();
        var visibleAddons = new List<nint>(3);

        foreach (var addonName in MenuAddonNames)
        {
            var addon = TryGetVisibleAddon(addonName);
            if (addon == null)
                continue;

            visibleAddons.Add((nint)addon);
            TryAddVisibleBounds(addonName, addon);
        }

        var menuVisible = visibleAddons.Count > 0;

        if ((!configuration.SkinNativeContextMenus || !menuVisible) &&
            nodeSnapshots.Count > 0 &&
            (hadVisibleMenu || !configuration.SkinNativeContextMenus))
        {
            RestoreCurrentMenuNodes();
        }

        hadVisibleMenu = menuVisible;
        IsAnyMenuOpen = menuVisible || Environment.TickCount64 < openingGraceUntilMs;
    }

    private void OnMenuPreDraw(AddonEvent _, AddonArgs args)
    {
        if (disposed || !configuration.SkinNativeContextMenus ||
            !MenuAddonNames.Contains(args.AddonName, StringComparer.Ordinal))
            return;

        var addon = TryGetVisibleAddon(args.AddonName);
        if (addon != null)
            TryStyleAddon(addon);
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

    private void TryAddVisibleBounds(string addonName, AtkUnitBase* addon)
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

            var kind = addonName switch
            {
                "ContextMenu" => NativeMenuKind.ContextMenu,
                "AddonContextSub" => NativeMenuKind.Submenu,
                _ => NativeMenuKind.Title,
            };


            visibleMenuBounds.Add(new NativeMenuBounds(min, max, kind));
        }
        catch
        {

        }
    }

    private void TryStyleAddon(AtkUnitBase* addon)
    {
        try
        {
            visitedManagers.Clear();
            var manager = &addon->UldManager;
            var rootWidth = manager->RootNodeWidth > 0 ? manager->RootNodeWidth : addon->RootNode->Width;
            var rootHeight = manager->RootNodeHeight > 0 ? manager->RootNodeHeight : addon->RootNode->Height;
            StyleManager(manager, rootWidth, rootHeight, 0);
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame skipped one native context-menu style update.");
        }
    }

    private void StyleManager(AtkUldManager* manager, float rootWidth, float rootHeight, int depth)
    {
        if (manager == null || manager->NodeList == null || manager->NodeListCount == 0 || depth > 6)
            return;

        if (!visitedManagers.Add((nint)manager))
            return;

        var safeRootWidth = MathF.Max(1f, rootWidth);
        var safeRootHeight = MathF.Max(1f, rootHeight);
        var rootArea = safeRootWidth * safeRootHeight;

        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            var rawType = (ushort)node->Type;
            if (rawType == (ushort)NodeType.Text)
            {
                StyleTextNode((AtkTextNode*)node, null);
                continue;
            }

            if (rawType == (ushort)NodeType.Image || rawType == (ushort)NodeType.NineGrid)
            {
                var nodeArea = (float)node->Width * node->Height;
                var widthRatio = node->Width / safeRootWidth;
                var heightRatio = node->Height / safeRootHeight;


                if (nodeArea >= rootArea * 0.38f && widthRatio >= 0.62f && heightRatio >= 0.52f)
                    StyleBackgroundNode(node, heightRatio > 0.85f ? ObsidianDeep : Obsidian);

                continue;
            }

            if (rawType < 1000)
                continue;

            var componentNode = (AtkComponentNode*)node;
            var component = componentNode->Component;
            if (component == null)
                continue;

            var componentType = component->GetComponentType();
            if (componentType == ComponentType.List)
                StyleList((AtkComponentList*)component, depth + 1);

            var childManager = &component->UldManager;
            var childWidth = childManager->RootNodeWidth > 0
                ? childManager->RootNodeWidth
                : component->OwnerNode != null ? component->OwnerNode->AtkResNode.Width : safeRootWidth;
            var childHeight = childManager->RootNodeHeight > 0
                ? childManager->RootNodeHeight
                : component->OwnerNode != null ? component->OwnerNode->AtkResNode.Height : safeRootHeight;
            StyleManager(childManager, childWidth, childHeight, depth + 1);
        }
    }

    private void StyleList(AtkComponentList* list, int depth)
    {
        if (list == null || list->ListLength <= 0 ||
            list->ItemRendererList == null || list->AllocatedItemRendererListLength <= 0)
            return;

        var listLength = Math.Clamp(list->ListLength, 0, 256);
        var hoveredIndex = ResolveHoveredIndex(list);
        var selectedIndex = list->SelectedItemIndex;
        var mousePosition = ImGui.GetIO().MousePos;
        var allocated = Math.Clamp(list->AllocatedItemRendererListLength, 0, 96);

        for (var slot = 0; slot < allocated; slot++)
        {
            var listItem = &list->ItemRendererList[slot];
            var renderer = listItem->AtkComponentListItemRenderer;
            if (renderer == null)
                continue;

            var renderedIndex = renderer->ListItemIndex;
            if (renderedIndex < 0 || renderedIndex >= listLength)
                continue;

            var baseComponent = (AtkComponentBase*)renderer;
            var rendererManager = &baseComponent->UldManager;
            var rendererWidth = rendererManager->RootNodeWidth > 0
                ? (float)rendererManager->RootNodeWidth
                : baseComponent->OwnerNode != null
                    ? (float)baseComponent->OwnerNode->AtkResNode.Width
                    : (float)list->ItemWidth;
            var rendererHeight = rendererManager->RootNodeHeight > 0
                ? (float)rendererManager->RootNodeHeight
                : baseComponent->OwnerNode != null
                    ? (float)baseComponent->OwnerNode->AtkResNode.Height
                    : (float)list->ItemHeight;
            StyleManager(rendererManager, rendererWidth, rendererHeight, depth + 1);

            var button = (AtkComponentButton*)renderer;
            var disabled = listItem->IsDisabled;
            if (button->ButtonTextNode != null)
                StyleTextNode(button->ButtonTextNode, disabled);

            var isHovered = renderedIndex == hoveredIndex || IsRendererUnderMouse(renderer, button, mousePosition);
            var isSelected = renderedIndex == selectedIndex;
            if (button->ButtonBGNode != null)
                StyleRowBackground(button->ButtonBGNode, isHovered, isSelected);

            var rowTemplateCount = renderer->RowTemplateNodeCount & 0xFFFF;
            if (rowTemplateCount == 1 && renderer->RowTemplateNode != null)
                StyleRowBackground(renderer->RowTemplateNode, isHovered, isSelected);

            StyleRendererRowNodes(rendererManager, rendererWidth, rendererHeight, button->ButtonBGNode, isHovered, isSelected);
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

    private void StyleRendererRowNodes(
        AtkUldManager* manager,
        float rendererWidth,
        float rendererHeight,
        AtkResNode* primaryBackground,
        bool hovered,
        bool selected)
    {
        if (manager == null || manager->NodeList == null || manager->NodeListCount == 0)
            return;

        var safeWidth = MathF.Max(1f, rendererWidth);
        var safeHeight = MathF.Max(1f, rendererHeight);
        var safeArea = safeWidth * safeHeight;
        var count = Math.Min((int)manager->NodeListCount, 96);
        for (var i = 0; i < count; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node == primaryBackground)
                continue;

            var rawType = (ushort)node->Type;
            if (rawType != (ushort)NodeType.Image && rawType != (ushort)NodeType.NineGrid)
                continue;

            var width = (float)node->Width;
            var height = (float)node->Height;
            var rowSized = width >= safeWidth * 0.55f &&
                           height >= safeHeight * 0.45f &&
                           width * height >= safeArea * 0.28f;
            if (rowSized)
                StyleRowBackground(node, hovered, selected);
        }
    }

    private void StyleBackgroundNode(AtkResNode* node, ByteColor tint)
    {
        SnapshotNode(node, null);
        var original = nodeSnapshots[(nint)node];
        node->Color = new ByteColor { R = tint.R, G = tint.G, B = tint.B, A = original.Color.A };
        node->IsDirty = true;
    }

    private void StyleRowBackground(AtkResNode* node, bool hovered, bool selected)
    {
        SnapshotNode(node, null);
        var original = nodeSnapshots[(nint)node];
        var baseAlpha = original.Color.A;

        if (hovered)
        {
            node->NodeFlags |= NodeFlags.Visible;
            node->Color = new ByteColor
            {
                R = HoverGrey.R,
                G = HoverGrey.G,
                B = HoverGrey.B,
                A = (byte)Math.Max(baseAlpha, HoverGrey.A),
            };
        }
        else if (selected)
        {
            node->NodeFlags |= NodeFlags.Visible;
            node->Color = new ByteColor { R = 70, G = 73, B = 80, A = (byte)Math.Max(baseAlpha, (byte)105) };
        }
        else
        {
            node->NodeFlags = original.NodeFlags;
            node->Color = new ByteColor { R = 24, G = 26, B = 32, A = baseAlpha };
        }

        node->IsDirty = true;
    }

    private void StyleTextNode(AtkTextNode* textNode, bool? disabledOverride)
    {
        if (textNode == null)
            return;

        var node = (AtkResNode*)textNode;
        SnapshotNode(node, textNode);

        var original = nodeSnapshots[(nint)node];
        var disabled = disabledOverride ?? IsProbablyDisabled(original.TextColor);
        var desired = disabled ? MutedIvory : AncientIvory;
        textNode->TextColor = new ByteColor { R = desired.R, G = desired.G, B = desired.B, A = original.TextColor.A };
        textNode->EdgeColor = new ByteColor { R = TextEdge.R, G = TextEdge.G, B = TextEdge.B, A = (byte)Math.Max(original.EdgeColor.A, TextEdge.A) };
        node->IsDirty = true;
    }

    private static bool IsProbablyDisabled(ByteColor color)
    {
        var luminance = (color.R * 3 + color.G * 6 + color.B) / 10;
        return color.A < 150 || luminance < 115;
    }

    private void SnapshotNode(AtkResNode* node, AtkTextNode* textNode)
    {
        var key = (nint)node;
        if (nodeSnapshots.ContainsKey(key))
            return;

        nodeSnapshots[key] = new NativeNodeSnapshot(
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

    private void RestoreCurrentMenuNodes()
    {
        try
        {
            visitedManagers.Clear();
            foreach (var addonName in MenuAddonNames)
            {
                var addon = gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
                if (addon == null || !addon->IsReady || addon->RootNode == null)
                    continue;

                RestoreManager(&addon->UldManager, 0);
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not restore one stale context-menu node; the addon will rebuild it normally.");
        }
        finally
        {
            nodeSnapshots.Clear();
            visitedManagers.Clear();
        }
    }

    private void RestoreManager(AtkUldManager* manager, int depth)
    {
        if (manager == null || manager->NodeList == null || depth > 6)
            return;

        if (!visitedManagers.Add((nint)manager))
            return;

        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            RestoreNode(node);

            var rawType = (ushort)node->Type;
            if (rawType < 1000)
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component == null)
                continue;

            if (component->GetComponentType() == ComponentType.List)
                RestoreList((AtkComponentList*)component, depth + 1);

            RestoreManager(&component->UldManager, depth + 1);
        }
    }

    private void RestoreList(AtkComponentList* list, int depth)
    {
        var count = Math.Clamp(list->ListLength, 0, 64);
        for (var i = 0; i < count; i++)
        {
            AtkComponentListItemRenderer* renderer;
            try
            {
                renderer = list->GetItemRenderer(i);
            }
            catch
            {
                continue;
            }

            if (renderer == null)
                continue;

            var button = (AtkComponentButton*)renderer;
            if (button->ButtonTextNode != null)
                RestoreNode((AtkResNode*)button->ButtonTextNode);
            if (button->ButtonBGNode != null)
                RestoreNode(button->ButtonBGNode);

            RestoreManager(&((AtkComponentBase*)renderer)->UldManager, depth + 1);
        }
    }

    private void RestoreNode(AtkResNode* node)
    {
        if (!nodeSnapshots.TryGetValue((nint)node, out var snapshot))
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
        addonLifecycle.UnregisterListener(OnMenuPreDraw);
        if (nodeSnapshots.Count > 0)
            RestoreCurrentMenuNodes();
        visibleMenuBounds.Clear();
        IsAnyMenuOpen = false;
    }

    private readonly record struct NativeNodeSnapshot(
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

public enum NativeMenuKind
{
    ContextMenu,
    Submenu,
    Title,
}

public readonly record struct NativeMenuBounds(
    NumericsVector2 Min,
    NumericsVector2 Max,
    NativeMenuKind Kind)
{
    public bool IsMenuPanel => Kind is NativeMenuKind.ContextMenu or NativeMenuKind.Submenu;
    public bool IsSubmenu => Kind == NativeMenuKind.Submenu;
}
