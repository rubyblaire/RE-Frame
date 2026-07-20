using System;

namespace REFrameXIV.Models;

[Serializable]
public sealed class GreetingVoiceRotationState
{
    public int NextMorningIndex { get; set; }
    public int NextAfternoonIndex { get; set; }
    public int NextEveningIndex { get; set; }
}
