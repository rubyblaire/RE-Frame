namespace REFrameXIV.Models;


public sealed class ReframeHotbarKeybind
{
    public uint HotbarId { get; set; }
    public uint SlotId { get; set; }
    public int BindingIndex { get; set; }
    public byte Key { get; set; }
    public byte Modifiers { get; set; }
}
