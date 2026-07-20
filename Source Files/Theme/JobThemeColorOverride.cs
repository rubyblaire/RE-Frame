using System;
using System.Numerics;

namespace REFrameXIV.Theme;

[Serializable]
public sealed class JobThemeColorOverride
{
    public float AccentR { get; set; }
    public float AccentG { get; set; }
    public float AccentB { get; set; }
    public float HighlightR { get; set; }
    public float HighlightG { get; set; }
    public float HighlightB { get; set; }

    public Vector4 Accent => new(Clamp(AccentR), Clamp(AccentG), Clamp(AccentB), 1f);
    public Vector4 Highlight => new(Clamp(HighlightR), Clamp(HighlightG), Clamp(HighlightB), 1f);

    public static JobThemeColorOverride From(Vector4 accent, Vector4 highlight) => new()
    {
        AccentR = Clamp(accent.X),
        AccentG = Clamp(accent.Y),
        AccentB = Clamp(accent.Z),
        HighlightR = Clamp(highlight.X),
        HighlightG = Clamp(highlight.Y),
        HighlightB = Clamp(highlight.Z),
    };

    public void SetAccent(Vector3 color)
    {
        AccentR = Clamp(color.X);
        AccentG = Clamp(color.Y);
        AccentB = Clamp(color.Z);
    }

    public void SetHighlight(Vector3 color)
    {
        HighlightR = Clamp(color.X);
        HighlightG = Clamp(color.Y);
        HighlightB = Clamp(color.Z);
    }

    private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);
}
