using System;

namespace REFrameXIV.Models;

[Serializable]
public sealed class DockButtonConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;

    public string Action { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public DockButtonConfig Clone() => new()
    {
        Id = Id, Label = Label, Visible = Visible, Action = Action, Command = Command,
    };
}
