using System;
using System.Collections.Generic;

namespace REFrameXIV.Models;


[Serializable]
public sealed class HudModeProfile
{
    public Dictionary<string, HudElementLayout> HudLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ElementVisibility { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ElementLocks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void EnsureValid()
    {
        if (HudLayouts is null)
            HudLayouts = new Dictionary<string, HudElementLayout>(StringComparer.OrdinalIgnoreCase);
        else if (!HudLayouts.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
            HudLayouts = new Dictionary<string, HudElementLayout>(HudLayouts, StringComparer.OrdinalIgnoreCase);

        if (ElementVisibility is null)
            ElementVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        else if (!ElementVisibility.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
            ElementVisibility = new Dictionary<string, bool>(ElementVisibility, StringComparer.OrdinalIgnoreCase);

        if (ElementLocks is null)
            ElementLocks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        else if (!ElementLocks.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
            ElementLocks = new Dictionary<string, bool>(ElementLocks, StringComparer.OrdinalIgnoreCase);
    }

    public HudModeProfile Clone()
    {
        EnsureValid();
        var clone = new HudModeProfile();
        foreach (var (id, layout) in HudLayouts)
        {
            if (layout is null)
                continue;

            clone.HudLayouts[id] = new HudElementLayout
            {
                X = layout.X,
                Y = layout.Y,
                Width = layout.Width,
                Height = layout.Height,
            };
        }

        foreach (var (id, visible) in ElementVisibility)
            clone.ElementVisibility[id] = visible;
        foreach (var (id, locked) in ElementLocks)
            clone.ElementLocks[id] = locked;

        return clone;
    }
}
