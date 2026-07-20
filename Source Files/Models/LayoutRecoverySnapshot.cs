using System;

namespace REFrameXIV.Models;

[Serializable]
public sealed class LayoutRecoverySnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "Layout restore point";
    public HudLayoutState State { get; set; } = new();
}
