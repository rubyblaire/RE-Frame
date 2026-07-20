using System;

namespace REFrameXIV.Models;

[Serializable]
public sealed class GreetingVoicePack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom Voice Pack";
    public int GreetingSetCount { get; set; } = 1;

    public void EnsureValid()
    {
        if (!Guid.TryParseExact(Id, "N", out _))
            Id = Guid.NewGuid().ToString("N");

        Name = string.IsNullOrWhiteSpace(Name) ? "Custom Voice Pack" : Name.Trim();
        GreetingSetCount = Math.Clamp(GreetingSetCount, 1, 3);
    }
}
