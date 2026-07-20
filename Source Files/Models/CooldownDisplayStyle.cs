namespace REFrameXIV.Models;

public enum CooldownDisplayStyle
{
    ReframeFill = 0,
    FfxivClock = 1,
}

public static class CooldownDisplayStyleInfo
{
    public static string Label(CooldownDisplayStyle style) => style switch
    {
        CooldownDisplayStyle.FfxivClock => "FFXIV clock",
        _ => "RE:Frame fill",
    };
}
