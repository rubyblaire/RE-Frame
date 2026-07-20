using System;
using System.Numerics;
using System.Linq;

namespace REFrameXIV.Models;


public readonly record struct HotbarGridShape(int Columns, int Rows)
{
    public string Label => $"{Columns}×{Rows}";
}

public static class HotbarGridLayouts
{
    public static readonly HotbarGridShape[] Supported =
    {
        new(12, 1),
        new(6, 2),
        new(4, 3),
        new(3, 4),
    };

    public static bool IsCombatHotbar(string elementId)
        => elementId is HudElementIds.ActionBarOne or HudElementIds.ActionBarTwo or HudElementIds.ActionBarThree ||
           HudElementIds.IsAdditionalCombatHotbar(elementId);

    public static bool IsConfigurableHotbar(string elementId)
        => IsCombatHotbar(elementId) || elementId == HudElementIds.PetBar;

    public static int NormalizeColumns(int columns) => columns switch
    {
        12 or 6 or 4 or 3 => columns,
        _ => 12,
    };

    public static HotbarGridShape FromColumns(int columns)
    {
        columns = NormalizeColumns(columns);
        return new HotbarGridShape(columns, 12 / columns);
    }

    public static HotbarGridShape Resolve(Configuration configuration, uint hotbarId)
    {
        var additional = configuration.AdditionalCombatHotbars?.FirstOrDefault(bar => bar.RuntimeHotbarId == hotbarId);
        if (additional is not null)
            return FromColumns(additional.Columns);

        return FromColumns(hotbarId switch
        {
            0u => configuration.ActionBarOneColumns,
            1u => configuration.ActionBarTwoColumns,
            2u => configuration.ActionBarThreeColumns,
            ReframeHotbarIds.PetBar => configuration.PetBarColumns,
            _ => 12,
        });
    }

    public static HotbarGridShape Resolve(Configuration configuration, string elementId)
        => FromColumns(GetColumns(configuration, elementId));

    public static int GetColumns(Configuration configuration, string elementId)
    {
        if (HudElementIds.TryParseAdditionalCombatHotbar(elementId, out var id))
        {
            var additional = configuration.AdditionalCombatHotbars?.FirstOrDefault(bar => bar.Id == id);
            return additional is null ? 12 : NormalizeColumns(additional.Columns);
        }

        return elementId switch
        {
            HudElementIds.ActionBarOne => NormalizeColumns(configuration.ActionBarOneColumns),
            HudElementIds.ActionBarTwo => NormalizeColumns(configuration.ActionBarTwoColumns),
            HudElementIds.ActionBarThree => NormalizeColumns(configuration.ActionBarThreeColumns),
            HudElementIds.PetBar => NormalizeColumns(configuration.PetBarColumns),
            _ => 12,
        };
    }

    public static void SetColumns(Configuration configuration, string elementId, int columns)
    {
        columns = NormalizeColumns(columns);
        if (HudElementIds.TryParseAdditionalCombatHotbar(elementId, out var id))
        {
            var additional = configuration.AdditionalCombatHotbars?.FirstOrDefault(bar => bar.Id == id);
            if (additional is not null)
                additional.Columns = columns;
            return;
        }

        switch (elementId)
        {
            case HudElementIds.ActionBarOne:
                configuration.ActionBarOneColumns = columns;
                break;
            case HudElementIds.ActionBarTwo:
                configuration.ActionBarTwoColumns = columns;
                break;
            case HudElementIds.ActionBarThree:
                configuration.ActionBarThreeColumns = columns;
                break;
            case HudElementIds.PetBar:
                configuration.PetBarColumns = columns;
                break;
        }
    }

    public static Vector2 MinimumSize(Configuration configuration, string elementId)
        => MinimumSize(Resolve(configuration, elementId));

    public static Vector2 MinimumSize(HotbarGridShape shape)
    {
        const float minimumSlot = 21.75f;
        const float gap = 3f;
        const float padding = 6f;
        return new Vector2(
            MathF.Max(32f, shape.Columns * minimumSlot + MathF.Max(0, shape.Columns - 1) * gap + padding),
            MathF.Max(32f, shape.Rows * minimumSlot + MathF.Max(0, shape.Rows - 1) * gap + padding));
    }


    public static void ChangeShape(
        Configuration configuration,
        string elementId,
        int columns,
        Vector2 viewportSize)
    {
        if (!IsConfigurableHotbar(elementId))
            return;

        columns = NormalizeColumns(columns);
        var previousColumns = GetColumns(configuration, elementId);
        if (previousColumns == columns)
            return;

        configuration.HudLayouts ??= new();
        viewportSize = Vector2.Max(viewportSize, Vector2.One);
        var previousShape = FromColumns(previousColumns);
        var nextShape = FromColumns(columns);
        var gap = MathF.Max(1f, 3f * Math.Clamp(configuration.InterfaceScale, 0.60f, 2.50f));

        if (configuration.HudLayouts.TryGetValue(elementId, out var shared) && shared is not null)
            RefitLayout(shared, previousShape, nextShape, viewportSize, gap);

        if (configuration.HudModeProfiles is not null)
        {
            foreach (var profile in configuration.HudModeProfiles.Values)
            {
                if (profile is null)
                    continue;
                profile.EnsureValid();
                if (profile.HudLayouts.TryGetValue(elementId, out var layout) && layout is not null)
                    RefitLayout(layout, previousShape, nextShape, viewportSize, gap);
            }
        }

        SetColumns(configuration, elementId, columns);
    }

    private static void RefitLayout(
        HudElementLayout layout,
        HotbarGridShape previousShape,
        HotbarGridShape nextShape,
        Vector2 viewportSize,
        float gap)
    {
        var previousSize = new Vector2(layout.Width * viewportSize.X, layout.Height * viewportSize.Y);
        previousSize = Vector2.Max(previousSize, MinimumSize(previousShape));
        var previousPosition = new Vector2(layout.X * viewportSize.X, layout.Y * viewportSize.Y);

        var availableWidth = (previousSize.X - gap * MathF.Max(0, previousShape.Columns - 1)) / previousShape.Columns;
        var availableHeight = (previousSize.Y - gap * MathF.Max(0, previousShape.Rows - 1)) / previousShape.Rows;
        var slotSize = MathF.Max(18f, MathF.Min(availableWidth, availableHeight));

        var nextSize = new Vector2(
            nextShape.Columns * slotSize + MathF.Max(0, nextShape.Columns - 1) * gap,
            nextShape.Rows * slotSize + MathF.Max(0, nextShape.Rows - 1) * gap);
        nextSize = Vector2.Max(nextSize, MinimumSize(nextShape));

        var center = previousPosition + previousSize * 0.5f;
        var nextPosition = center - nextSize * 0.5f;
        layout.X = Math.Clamp(nextPosition.X / viewportSize.X, -0.5f, 1.5f);
        layout.Y = Math.Clamp(nextPosition.Y / viewportSize.Y, -0.5f, 1.5f);
        layout.Width = Math.Clamp(nextSize.X / viewportSize.X, 1f / viewportSize.X, 2f);
        layout.Height = Math.Clamp(nextSize.Y / viewportSize.Y, 1f / viewportSize.Y, 2f);
    }
}
