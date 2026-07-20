using System;

namespace REFrameXIV.Models;

[Serializable]
public sealed class CustomIntegration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        Name ??= string.Empty;
        Command ??= string.Empty;
    }
}
