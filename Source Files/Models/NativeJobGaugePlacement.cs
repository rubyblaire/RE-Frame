using System;
using System.Collections.Generic;

namespace REFrameXIV.Models;


[Serializable]
public sealed class NativeJobGaugePlacement
{
    public float X { get; set; }
    public float Y { get; set; }
    public float OriginalX { get; set; }
    public float OriginalY { get; set; }
    public float Scale { get; set; } = 1f;
    public float OriginalScale { get; set; } = 1f;
    public bool HasOriginal { get; set; }


    public Dictionary<string, NativeJobGaugePlacement> Components { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
