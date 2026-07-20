using System;
using System.Collections.Generic;
using System.Numerics;
using REFrameXIV.Models;
using REFrameXIV.Services;

namespace REFrameXIV.UI;

public readonly record struct HudBounds(Vector2 Position, Vector2 Size);


public static class HudLayout
{
    private static readonly Dictionary<string, HudBounds> ReferenceDefaults = new()
    {
        [HudElementIds.Location] = new(new Vector2(28f, 22f), new Vector2(292f, 76f)),
        [HudElementIds.JobRibbon] = new(new Vector2(835f, 14f), new Vector2(250f, 35f)),
        [HudElementIds.PocketRibbon] = new(new Vector2(1095f, 14f), new Vector2(180f, 35f)),
        [HudElementIds.Minimap] = new(new Vector2(1688f, 18f), new Vector2(210f, 210f)),
        [HudElementIds.ForgeCoordinates] = new(new Vector2(1710f, 232f), new Vector2(166f, 32f)),
        [HudElementIds.Chat] = new(new Vector2(18f, 720f), new Vector2(520f, 320f)),
        [HudElementIds.Party] = new(new Vector2(28f, 410f), new Vector2(255f, 306f)),

        [HudElementIds.Alliance] = new(new Vector2(1378f, 350f), new Vector2(500f, 250f)),
        [HudElementIds.ActionBars] = new(new Vector2(704f, 922f), new Vector2(513f, 126f)),
        [HudElementIds.AllianceOne] = new(new Vector2(1378f, 350f), new Vector2(245f, 250f)),
        [HudElementIds.AllianceTwo] = new(new Vector2(1633f, 350f), new Vector2(245f, 250f)),
        [HudElementIds.Player] = new(new Vector2(1228f, 594f), new Vector2(300f, 58f)),
        [HudElementIds.Target] = new(new Vector2(650f, 716f), new Vector2(620f, 68f)),
        [HudElementIds.TargetOfTarget] = new(new Vector2(1030f, 790f), new Vector2(240f, 42f)),
        [HudElementIds.CastBar] = new(new Vector2(650f, 790f), new Vector2(370f, 42f)),
        [HudElementIds.PlayerCastBar] = new(new Vector2(775f, 844f), new Vector2(370f, 42f)),
        [HudElementIds.Focus] = new(new Vector2(1295f, 702f), new Vector2(250f, 34f)),
        [HudElementIds.EnemyList] = new(new Vector2(1570f, 330f), new Vector2(300f, 290f)),
        [HudElementIds.ActionBarThree] = new(new Vector2(704f, 922f), new Vector2(513f, 40f)),
        [HudElementIds.ActionBarTwo] = new(new Vector2(704f, 965f), new Vector2(513f, 40f)),
        [HudElementIds.ActionBarOne] = new(new Vector2(704f, 1008f), new Vector2(513f, 40f)),
        [HudElementIds.CrossHotbar] = new(new Vector2(590f, 862f), new Vector2(740f, 138f)),
        [HudElementIds.PetBar] = new(new Vector2(704f, 870f), new Vector2(513f, 42f)),
        [HudElementIds.UtilityBars] = new(new Vector2(470f, 870f), new Vector2(150f, 116f)),
        [HudElementIds.UtilityBarsTwo] = new(new Vector2(628f, 870f), new Vector2(150f, 116f)),
        [HudElementIds.RaidTools] = new(new Vector2(1180f, 930f), new Vector2(500f, 52f)),
        [HudElementIds.RaidBuffs] = new(new Vector2(360f, 610f), new Vector2(330f, 88f)),
        [HudElementIds.RaidDebuffs] = new(new Vector2(1230f, 610f), new Vector2(330f, 88f)),
        [HudElementIds.RaidersKit] = new(new Vector2(820f, 884f), new Vector2(280f, 70f)),
        [HudElementIds.LimitBreak] = new(new Vector2(760f, 846f), new Vector2(400f, 28f)),
        [HudElementIds.CombatHalo] = new(new Vector2(760f, 330f), new Vector2(400f, 400f)),


        [HudElementIds.Greeting] = new(new Vector2(735f, 952f), new Vector2(450f, 54f)),
        [HudElementIds.LeisureDock] = new(new Vector2(640f, 1016f), new Vector2(640f, 48f)),
    };

    public static void EnsureDefaults(Configuration configuration)
    {
        configuration.HudLayouts ??= new Dictionary<string, HudElementLayout>();
        foreach (var id in HudElementIds.All)
            EnsureDefault(configuration, id);
        EnsureDefault(configuration, HudElementIds.UtilityBarsTwo);

        AdditionalHotbarService.EnsureLayoutDefaults(configuration);
    }


    private static void EnsureDefault(Configuration configuration, string id)
    {
        if (configuration.HudLayouts.ContainsKey(id))
            return;

        var reference = ReferenceDefaults[id];
        configuration.HudLayouts[id] = new HudElementLayout
        {
            X = reference.Position.X / 1920f,
            Y = reference.Position.Y / 1080f,
            Width = reference.Size.X / 1920f,
            Height = reference.Size.Y / 1080f,
        };
    }

    public static HudBounds Resolve(
        Configuration configuration,
        string id,
        Vector2 origin,
        Vector2 viewportSize,
        UiMode mode = UiMode.Auto)
    {
        EnsureDefaults(configuration);
        var hasModeLayout = HudModeProfileService.TryGetLayout(configuration, mode, id, out var modeLayout);
        var layout = hasModeLayout
            ? modeLayout
            : configuration.HudLayouts.TryGetValue(id, out var sharedLayout)
                ? sharedLayout
                : null;
        if (layout is null)
            return Default(id, origin, viewportSize);

        var position = origin + new Vector2(layout.X * viewportSize.X, layout.Y * viewportSize.Y);
        var size = new Vector2(layout.Width * viewportSize.X, layout.Height * viewportSize.Y);
        var minimum = MinimumSize(configuration, id);
        size = Vector2.Max(size, minimum);
        return new HudBounds(position, size);
    }

    public static HudBounds Default(string id, Vector2 origin, Vector2 viewportSize)
    {
        var reference = ReferenceDefaults.TryGetValue(id, out var value)
            ? value
            : new HudBounds(Vector2.Zero, new Vector2(200f, 80f));
        return new HudBounds(
            origin + new Vector2(reference.Position.X / 1920f * viewportSize.X, reference.Position.Y / 1080f * viewportSize.Y),
            new Vector2(reference.Size.X / 1920f * viewportSize.X, reference.Size.Y / 1080f * viewportSize.Y));
    }

    public static void Store(
        Configuration configuration,
        string id,
        HudBounds bounds,
        Vector2 origin,
        Vector2 viewportSize,
        UiMode mode = UiMode.Auto)
    {
        EnsureDefaults(configuration);
        var local = bounds.Position - origin;
        var minimum = MinimumSize(configuration, id);
        var clampedSize = Vector2.Max(bounds.Size, minimum);
        var layout = new HudElementLayout
        {
            X = Math.Clamp(local.X / Math.Max(1f, viewportSize.X), -0.5f, 1.5f),
            Y = Math.Clamp(local.Y / Math.Max(1f, viewportSize.Y), -0.5f, 1.5f),
            Width = Math.Clamp(clampedSize.X / Math.Max(1f, viewportSize.X), minimum.X / Math.Max(1f, viewportSize.X), 2f),
            Height = Math.Clamp(clampedSize.Y / Math.Max(1f, viewportSize.Y), minimum.Y / Math.Max(1f, viewportSize.Y), 2f),
        };

        if (mode == UiMode.Auto)
            configuration.HudLayouts[id] = layout;
        else
            HudModeProfileService.SetLayout(configuration, mode, id, layout);
    }

    public static void Reset(Configuration configuration, string id, UiMode mode = UiMode.Auto)
    {
        if (mode == UiMode.Auto)
        {
            configuration.HudLayouts.Remove(id);
            EnsureDefaults(configuration);
        }
        else
        {
            HudModeProfileService.ResetLayout(configuration, mode, id);
        }
    }

    public static void ResetAll(Configuration configuration, UiMode mode = UiMode.Auto)
    {
        if (mode == UiMode.Auto)
        {
            configuration.HudLayouts.Clear();
            HudModeProfileService.ClearAllModeLayouts(configuration);
            EnsureDefaults(configuration);
        }
        else
        {
            if (HudModeProfileService.TryGetProfile(configuration, mode, out var profile))
                profile.HudLayouts.Clear();
        }
    }


    public static void UpgradeStatusPanelsForDisplay(Configuration configuration, Vector2 viewportSize)
    {
        EnsureDefaults(configuration);
        HudModeProfileService.EnsureCollections(configuration);

        viewportSize = Vector2.Max(viewportSize, Vector2.One);
        var displayScale = HudCanvas.ReferenceDisplayScale(viewportSize);
        UpgradeStatusPanelLayout(configuration.HudLayouts, HudElementIds.RaidBuffs, viewportSize, displayScale);
        UpgradeStatusPanelLayout(configuration.HudLayouts, HudElementIds.RaidDebuffs, viewportSize, displayScale);

        foreach (var profile in configuration.HudModeProfiles.Values)
        {
            if (profile is null)
                continue;

            profile.EnsureValid();
            UpgradeStatusPanelLayout(profile.HudLayouts, HudElementIds.RaidBuffs, viewportSize, displayScale);
            UpgradeStatusPanelLayout(profile.HudLayouts, HudElementIds.RaidDebuffs, viewportSize, displayScale);
        }
    }

    private static void UpgradeStatusPanelLayout(
        IDictionary<string, HudElementLayout> layouts,
        string id,
        Vector2 viewportSize,
        float displayScale)
    {
        if (!layouts.TryGetValue(id, out var layout) || layout is null ||
            !ReferenceDefaults.TryGetValue(id, out var reference))
            return;

        var currentPosition = new Vector2(layout.X * viewportSize.X, layout.Y * viewportSize.Y);
        var currentSize = new Vector2(layout.Width * viewportSize.X, layout.Height * viewportSize.Y);
        var targetSize = reference.Size * displayScale;
        var nextSize = Vector2.Max(currentSize, targetSize);
        if (Vector2.DistanceSquared(currentSize, nextSize) < 1f)
            return;

        var normalizedCenterX = (currentPosition.X + currentSize.X * 0.5f) / viewportSize.X;
        if (normalizedCenterX >= 0.5f)
            currentPosition.X -= nextSize.X - currentSize.X;

        layout.X = Math.Clamp(currentPosition.X / viewportSize.X, -0.5f, 1.5f);
        layout.Y = Math.Clamp(currentPosition.Y / viewportSize.Y, -0.5f, 1.5f);
        layout.Width = Math.Clamp(nextSize.X / viewportSize.X, 1f / viewportSize.X, 2f);
        layout.Height = Math.Clamp(nextSize.Y / viewportSize.Y, 1f / viewportSize.Y, 2f);
    }


    public static void RefitAllLayouts(
        Configuration configuration,
        Vector2 previousViewport,
        Vector2 nextViewport)
    {
        EnsureDefaults(configuration);
        HudModeProfileService.EnsureCollections(configuration);

        previousViewport = Vector2.Max(previousViewport, Vector2.One);
        nextViewport = Vector2.Max(nextViewport, Vector2.One);
        if (Vector2.DistanceSquared(previousViewport, nextViewport) < 1f)
            return;

        foreach (var id in new List<string>(configuration.HudLayouts.Keys))
        {
            if (configuration.HudLayouts.TryGetValue(id, out var layout) && layout is not null)
                configuration.HudLayouts[id] = RefitLayout(
                    layout,
                    previousViewport,
                    nextViewport,
                    ShouldScaleWithDisplay(id));
        }

        foreach (var profile in configuration.HudModeProfiles.Values)
        {
            if (profile is null)
                continue;

            profile.EnsureValid();
            foreach (var id in new List<string>(profile.HudLayouts.Keys))
            {
                var layout = profile.HudLayouts[id];
                if (layout is not null)
                    profile.HudLayouts[id] = RefitLayout(
                        layout,
                        previousViewport,
                        nextViewport,
                        ShouldScaleWithDisplay(id));
            }
        }
    }

    private static bool ShouldScaleWithDisplay(string id)
        => id is HudElementIds.RaidBuffs or HudElementIds.RaidDebuffs;

    private static HudElementLayout RefitLayout(
        HudElementLayout source,
        Vector2 previousViewport,
        Vector2 nextViewport,
        bool scaleWithDisplay)
    {
        var previousPosition = new Vector2(
            source.X * previousViewport.X,
            source.Y * previousViewport.Y);
        var previousSize = new Vector2(
            source.Width * previousViewport.X,
            source.Height * previousViewport.Y);

        var displayRatio = MathF.Min(
            nextViewport.X / previousViewport.X,
            nextViewport.Y / previousViewport.Y);
        var downscale = MathF.Min(1f, displayRatio);
        var appliedScale = scaleWithDisplay
            ? Math.Clamp(displayRatio, 0.50f, 2.50f)
            : downscale;
        var nextSize = Vector2.Max(previousSize * appliedScale, Vector2.One);
        var nextPosition = new Vector2(
            ReanchorAxis(previousPosition.X, previousSize.X, previousViewport.X, nextViewport.X, nextSize.X, appliedScale),
            ReanchorAxis(previousPosition.Y, previousSize.Y, previousViewport.Y, nextViewport.Y, nextSize.Y, appliedScale));

        return new HudElementLayout
        {
            X = Math.Clamp(nextPosition.X / nextViewport.X, -0.5f, 1.5f),
            Y = Math.Clamp(nextPosition.Y / nextViewport.Y, -0.5f, 1.5f),
            Width = Math.Clamp(nextSize.X / nextViewport.X, 1f / nextViewport.X, 2f),
            Height = Math.Clamp(nextSize.Y / nextViewport.Y, 1f / nextViewport.Y, 2f),
        };
    }

    private static float ReanchorAxis(
        float previousPosition,
        float previousSize,
        float previousExtent,
        float nextExtent,
        float nextSize,
        float downscale)
    {
        var previousCenter = previousPosition + previousSize * 0.5f;
        var normalizedCenter = previousCenter / MathF.Max(1f, previousExtent);

        if (normalizedCenter <= 0.36f)
            return previousPosition * downscale;

        if (normalizedCenter >= 0.64f)
        {
            var previousRightMargin = previousExtent - (previousPosition + previousSize);
            return nextExtent - previousRightMargin * downscale - nextSize;
        }

        var centerOffset = previousCenter - previousExtent * 0.5f;
        return nextExtent * 0.5f + centerOffset * downscale - nextSize * 0.5f;
    }


    public static float MinimapOuterRadius(HudBounds bounds, float interfaceScale)
        => MathF.Max(30f, MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.5f - 5f * interfaceScale);


    public static float MinimapApertureRadius(HudBounds bounds, float interfaceScale)
    {
        var outer = MinimapOuterRadius(bounds, interfaceScale);
        return MathF.Max(24f, outer * 0.86f);
    }


    public static float MinimapContentOverscan(float interfaceScale)
        => Math.Clamp(1.20f + (interfaceScale - 1f) * 0.02f, 1.18f, 1.24f);


    public static HudBounds ForgeSquareMinimapOuterBounds(HudBounds bounds, float interfaceScale)
    {
        var side = MathF.Max(72f, MathF.Min(bounds.Size.X, bounds.Size.Y) - 6f * interfaceScale);
        var center = bounds.Position + bounds.Size * 0.5f;
        return new HudBounds(center - new Vector2(side * 0.5f), new Vector2(side));
    }


    public static HudBounds ForgeSquareMinimapAperture(HudBounds bounds, float interfaceScale)
    {
        var outer = ForgeSquareMinimapOuterBounds(bounds, interfaceScale);
        var inset = Math.Clamp(MathF.Max(6f * interfaceScale, outer.Size.X * 0.045f), 6f, outer.Size.X * 0.14f);
        var size = Vector2.Max(new Vector2(48f), outer.Size - new Vector2(inset * 2f));
        return new HudBounds(outer.Position + new Vector2(inset), size);
    }


    public static HudBounds LocationChantButton(HudBounds locationBounds, float interfaceScale)
    {
        var inset = Math.Clamp(9f * interfaceScale, 7f, 14f);
        var maximum = MathF.Max(18f, locationBounds.Size.Y - inset * 2f);
        var size = Math.Clamp(27f * interfaceScale, 22f, MathF.Min(34f, maximum));
        return new HudBounds(
            new Vector2(
                locationBounds.Position.X + locationBounds.Size.X - inset - size,
                locationBounds.Position.Y + (locationBounds.Size.Y - size) * 0.5f),
            new Vector2(size));
    }


    public static float EnemyListRowHeight(float interfaceScale)
        => Math.Clamp(33f * interfaceScale, 28f, 50f);

    public static Vector2 MinimumSize(Configuration configuration, string id)
        => HotbarGridLayouts.IsConfigurableHotbar(id)
            ? HotbarGridLayouts.MinimumSize(configuration, id)
            : MinimumSize(id);

    public static Vector2 MinimumSize(string id) => id switch
    {
        HudElementIds.Location => new Vector2(210f, 66f),
        HudElementIds.JobRibbon => new Vector2(140f, 28f),
        HudElementIds.PocketRibbon => new Vector2(130f, 28f),
        HudElementIds.Minimap => new Vector2(100f, 100f),
        HudElementIds.ForgeCoordinates => new Vector2(112f, 26f),
        HudElementIds.Chat => new Vector2(240f, 140f),
        HudElementIds.Party => new Vector2(150f, 90f),
        HudElementIds.Alliance => new Vector2(360f, 190f),
        HudElementIds.AllianceOne or HudElementIds.AllianceTwo => new Vector2(180f, 190f),
        HudElementIds.Player => new Vector2(170f, 44f),
        HudElementIds.Target => new Vector2(240f, 58f),
        HudElementIds.TargetOfTarget => new Vector2(170f, 34f),
        HudElementIds.CastBar => new Vector2(220f, 34f),
        HudElementIds.PlayerCastBar => new Vector2(220f, 34f),
        HudElementIds.Focus => new Vector2(140f, 28f),
        HudElementIds.EnemyList => new Vector2(180f, 90f),
        HudElementIds.ActionBars => new Vector2(300f, 78f),
        HudElementIds.ActionBarOne or HudElementIds.ActionBarTwo or HudElementIds.ActionBarThree => new Vector2(300f, 32f),
        HudElementIds.CrossHotbar => new Vector2(420f, 104f),
        HudElementIds.PetBar => new Vector2(260f, 36f),
        HudElementIds.UtilityBars or HudElementIds.UtilityBarsTwo => new Vector2(108f, 84f),
        HudElementIds.RaidTools => new Vector2(270f, 42f),
        HudElementIds.LimitBreak => new Vector2(180f, 24f),
        HudElementIds.CombatHalo => new Vector2(190f, 190f),
        HudElementIds.Greeting => new Vector2(220f, 42f),
        HudElementIds.LeisureDock => new Vector2(520f, 42f),
        _ => new Vector2(100f, 50f),
    };


    public static HudBounds ActionBars(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.ActionBars, origin, viewportSize, mode);

    public static HudBounds ActionBarOne(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.ActionBarOne, origin, viewportSize, mode);

    public static HudBounds ActionBarTwo(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.ActionBarTwo, origin, viewportSize, mode);

    public static HudBounds ActionBarThree(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.ActionBarThree, origin, viewportSize, mode);

    public static HudBounds PetBar(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.PetBar, origin, viewportSize, mode);

    public static HudBounds UtilityBars(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.UtilityBars, origin, viewportSize, mode);

    public static HudBounds UtilityBarsTwo(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.UtilityBarsTwo, origin, viewportSize, mode);

    public static HudBounds RaidTools(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.RaidTools, origin, viewportSize, mode);

    public static HudBounds LeisureDock(Configuration configuration, Vector2 origin, Vector2 viewportSize, UiMode mode = UiMode.Auto)
        => Resolve(configuration, HudElementIds.LeisureDock, origin, viewportSize, mode);

    public static HudBounds ResolveLeisureDockPopup(
        HudBounds dockBounds,
        LeisureDockPopup popup,
        Vector2 viewportOrigin,
        Vector2 viewportSize,
        float scale,
        float animationProgress)
    {
        const int dockSegmentCount = 4;
        var segmentWidth = dockBounds.Size.X / dockSegmentCount;
        var segmentIndex = popup switch
        {
            LeisureDockPopup.Appearance => 2f,
            LeisureDockPopup.Docks => 3f,
            _ => 1f,
        };
        var desiredWidth = popup switch
        {
            LeisureDockPopup.Appearance => 330f * scale,
            LeisureDockPopup.Docks => 590f * scale,
            _ => 250f * scale,
        };
        var maximumWidth = MathF.Max(1f, viewportSize.X - 16f * scale);
        var popupWidth = MathF.Min(MathF.Max(segmentWidth, desiredWidth), maximumWidth);
        var popupHeight = Math.Clamp(dockBounds.Size.Y * 0.82f, 32f * scale, 42f * scale);
        var segmentCenterX = dockBounds.Position.X + segmentWidth * (segmentIndex + 0.5f);
        var x = segmentCenterX - popupWidth * 0.5f;
        var minX = viewportOrigin.X + 8f * scale;
        var maxX = viewportOrigin.X + viewportSize.X - popupWidth - 8f * scale;
        x = Math.Clamp(x, minX, MathF.Max(minX, maxX));

        var gap = 7f * scale;
        var finalY = dockBounds.Position.Y - popupHeight - gap;
        var opensDownward = finalY < viewportOrigin.Y + 8f * scale;
        if (opensDownward)
            finalY = dockBounds.Position.Y + dockBounds.Size.Y + gap;

        var slide = (1f - Math.Clamp(animationProgress, 0f, 1f)) * 8f * scale;
        var y = opensDownward ? finalY - slide : finalY + slide;
        return new HudBounds(new Vector2(x, y), new Vector2(popupWidth, popupHeight));
    }

    public static HudBounds ResolveRoleplayDockPopup(
        HudBounds dockBounds,
        RoleplayDockPopup popup,
        Vector2 viewportOrigin,
        Vector2 viewportSize,
        float scale,
        float animationProgress)
    {
        const int dockSegmentCount = 4;
        var segmentWidth = dockBounds.Size.X / dockSegmentCount;
        var anchorIndex = popup == RoleplayDockPopup.ChatChannels ? 0f : 3f;
        var desiredWidth = (popup == RoleplayDockPopup.ChatChannels ? 520f : 590f) * scale;
        var maximumWidth = MathF.Max(1f, viewportSize.X - 16f * scale);
        var popupWidth = MathF.Min(MathF.Max(segmentWidth, desiredWidth), maximumWidth);
        var popupHeight = Math.Clamp(dockBounds.Size.Y * 0.84f, 34f * scale, 44f * scale);
        var segmentCenterX = dockBounds.Position.X + segmentWidth * (anchorIndex + 0.5f);
        var x = segmentCenterX - popupWidth * 0.5f;
        var minX = viewportOrigin.X + 8f * scale;
        var maxX = viewportOrigin.X + viewportSize.X - popupWidth - 8f * scale;
        x = Math.Clamp(x, minX, MathF.Max(minX, maxX));

        var gap = 7f * scale;
        var finalY = dockBounds.Position.Y - popupHeight - gap;
        var opensDownward = finalY < viewportOrigin.Y + 8f * scale;
        if (opensDownward)
            finalY = dockBounds.Position.Y + dockBounds.Size.Y + gap;

        var slide = (1f - Math.Clamp(animationProgress, 0f, 1f)) * 8f * scale;
        var y = opensDownward ? finalY - slide : finalY + slide;
        return new HudBounds(new Vector2(x, y), new Vector2(popupWidth, popupHeight));
    }

    public static HudBounds ResolveWorkstationDockPopup(
        HudBounds dockBounds,
        WorkstationDockPopup popup,
        Vector2 viewportOrigin,
        Vector2 viewportSize,
        float scale,
        float animationProgress)
    {
        const int dockSegmentCount = 3;
        var segmentWidth = dockBounds.Size.X / dockSegmentCount;
        var popupSegmentCount = popup == WorkstationDockPopup.Resources ? 3 : 5;
        var anchorIndex = popup == WorkstationDockPopup.Resources ? 1 : 2;
        var desiredWidth = (popup == WorkstationDockPopup.Resources ? 540f : 590f) * scale;
        var maximumWidth = MathF.Max(1f, viewportSize.X - 16f * scale);
        var popupWidth = MathF.Min(MathF.Max(segmentWidth * MathF.Min(3f, popupSegmentCount), desiredWidth), maximumWidth);
        var popupHeight = Math.Clamp(dockBounds.Size.Y * 0.84f, 34f * scale, 44f * scale);
        var dockSegmentCenterX = dockBounds.Position.X + segmentWidth * (anchorIndex + 0.5f);
        var x = dockSegmentCenterX - popupWidth * 0.5f;
        var minX = viewportOrigin.X + 8f * scale;
        var maxX = viewportOrigin.X + viewportSize.X - popupWidth - 8f * scale;
        x = Math.Clamp(x, minX, MathF.Max(minX, maxX));

        var gap = 7f * scale;
        var finalY = dockBounds.Position.Y - popupHeight - gap;
        var opensDownward = finalY < viewportOrigin.Y + 8f * scale;
        if (opensDownward)
            finalY = dockBounds.Position.Y + dockBounds.Size.Y + gap;

        var slide = (1f - Math.Clamp(animationProgress, 0f, 1f)) * 8f * scale;
        var y = opensDownward ? finalY - slide : finalY + slide;
        return new HudBounds(new Vector2(x, y), new Vector2(popupWidth, popupHeight));
    }
}
