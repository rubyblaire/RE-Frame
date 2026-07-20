using System;
using System.Collections.Generic;

namespace REFrameXIV.Models;


[Serializable]
public sealed class HudLayoutState
{
    public HudPresetData Preset { get; set; } = new();
    public Dictionary<string, NativeJobGaugePlacement> NativeJobGaugePlacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public NativeJobGaugePlacement NativeStatusEffectsPlacement { get; set; } = new();
    public Dictionary<string, NativeJobGaugePlacement> NativeQuestElementPlacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int HudLayoutReferenceWidth { get; set; } = 1920;
    public int HudLayoutReferenceHeight { get; set; } = 1080;

    public static HudLayoutState Capture(Configuration configuration, string sourceJob)
    {
        var preset = HudPresetData.Capture(configuration, "Layout State", sourceJob);
        preset.CreatedUtc = DateTime.UnixEpoch;
        var state = new HudLayoutState
        {
            Preset = preset,
            HudLayoutReferenceWidth = Math.Max(1, configuration.HudLayoutReferenceWidth),
            HudLayoutReferenceHeight = Math.Max(1, configuration.HudLayoutReferenceHeight),
            NativeStatusEffectsPlacement = ClonePlacement(configuration.NativeStatusEffectsPlacement ?? new NativeJobGaugePlacement()),
        };

        if (configuration.NativeJobGaugePlacements is not null)
        {
            foreach (var (job, placement) in configuration.NativeJobGaugePlacements)
            {
                if (placement is not null)
                    state.NativeJobGaugePlacements[job] = ClonePlacement(placement);
            }
        }

        if (configuration.NativeQuestElementPlacements is not null)
        {
            foreach (var (id, placement) in configuration.NativeQuestElementPlacements)
            {
                if (placement is not null)
                    state.NativeQuestElementPlacements[id] = ClonePlacement(placement);
            }
        }

        return state;
    }

    public void ApplyTo(Configuration configuration, string targetJob)
    {
        Preset ??= new HudPresetData();
        Preset.HudLayouts ??= new Dictionary<string, HudElementLayout>(StringComparer.OrdinalIgnoreCase);
        Preset.HudModeProfiles ??= new Dictionary<string, HudModeProfile>(StringComparer.OrdinalIgnoreCase);
        Preset.ApplyTo(configuration, targetJob);

        configuration.NativeJobGaugePlacements = new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (NativeJobGaugePlacements is not null)
        {
            foreach (var (job, placement) in NativeJobGaugePlacements)
            {
                if (placement is not null)
                    configuration.NativeJobGaugePlacements[job] = ClonePlacement(placement);
            }
        }

        configuration.NativeStatusEffectsPlacement = ClonePlacement(NativeStatusEffectsPlacement ?? new NativeJobGaugePlacement());
        configuration.NativeQuestElementPlacements = new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (NativeQuestElementPlacements is not null)
        {
            foreach (var (id, placement) in NativeQuestElementPlacements)
            {
                if (placement is not null)
                    configuration.NativeQuestElementPlacements[id] = ClonePlacement(placement);
            }
        }

        configuration.HudLayoutReferenceWidth = Math.Max(1, HudLayoutReferenceWidth);
        configuration.HudLayoutReferenceHeight = Math.Max(1, HudLayoutReferenceHeight);
    }

    public HudLayoutState Clone()
    {
        var sourcePreset = Preset ?? new HudPresetData();
        sourcePreset.HudLayouts ??= new Dictionary<string, HudElementLayout>(StringComparer.OrdinalIgnoreCase);
        sourcePreset.HudModeProfiles ??= new Dictionary<string, HudModeProfile>(StringComparer.OrdinalIgnoreCase);
        var clonedPreset = sourcePreset.Clone("Layout State");
        clonedPreset.CreatedUtc = DateTime.UnixEpoch;
        var clone = new HudLayoutState
        {
            Preset = clonedPreset,
            HudLayoutReferenceWidth = HudLayoutReferenceWidth,
            HudLayoutReferenceHeight = HudLayoutReferenceHeight,
            NativeStatusEffectsPlacement = ClonePlacement(NativeStatusEffectsPlacement ?? new NativeJobGaugePlacement()),
        };

        if (NativeJobGaugePlacements is not null)
        {
            foreach (var (job, placement) in NativeJobGaugePlacements)
            {
                if (placement is not null)
                    clone.NativeJobGaugePlacements[job] = ClonePlacement(placement);
            }
        }

        if (NativeQuestElementPlacements is not null)
        {
            foreach (var (id, placement) in NativeQuestElementPlacements)
            {
                if (placement is not null)
                    clone.NativeQuestElementPlacements[id] = ClonePlacement(placement);
            }
        }

        return clone;
    }

    private static NativeJobGaugePlacement ClonePlacement(NativeJobGaugePlacement source)
        => new()
        {
            X = source.X,
            Y = source.Y,
            OriginalX = source.OriginalX,
            OriginalY = source.OriginalY,
            Scale = source.Scale,
            OriginalScale = source.OriginalScale,
            HasOriginal = source.HasOriginal,
            Components = CloneComponents(source.Components),
        };

    private static Dictionary<string, NativeJobGaugePlacement> CloneComponents(Dictionary<string, NativeJobGaugePlacement>? source)
    {
        var clone = new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return clone;

        foreach (var (key, placement) in source)
        {
            if (placement is not null)
                clone[key] = ClonePlacement(placement);
        }

        return clone;
    }
}
