using System;

namespace REFrameXIV.Theme;

[Serializable]
public sealed class ForgeStyleSettings
{
    public float WindowRounding { get; set; } = 14f;
    public float ChildRounding { get; set; } = 11f;
    public float FrameRounding { get; set; } = 8f;
    public float BorderOpacity { get; set; } = 0.46f;
    public float ButtonOpacity { get; set; } = 0.28f;
    public float ButtonHoverOpacity { get; set; } = 0.42f;
    public float ButtonActiveOpacity { get; set; } = 0.52f;
    public float InputOpacity { get; set; } = 0.22f;
    public float InputHoverOpacity { get; set; } = 0.29f;
    public float InputActiveOpacity { get; set; } = 0.36f;

    public static ForgeStyleSettings Default => new();

    public ForgeStyleSettings Clone() => new()
    {
        WindowRounding = WindowRounding,
        ChildRounding = ChildRounding,
        FrameRounding = FrameRounding,
        BorderOpacity = BorderOpacity,
        ButtonOpacity = ButtonOpacity,
        ButtonHoverOpacity = ButtonHoverOpacity,
        ButtonActiveOpacity = ButtonActiveOpacity,
        InputOpacity = InputOpacity,
        InputHoverOpacity = InputHoverOpacity,
        InputActiveOpacity = InputActiveOpacity,
    };

    public void Normalize()
    {
        WindowRounding = Math.Clamp(WindowRounding, 0f, 28f);
        ChildRounding = Math.Clamp(ChildRounding, 0f, 24f);
        FrameRounding = Math.Clamp(FrameRounding, 0f, 20f);
        BorderOpacity = Math.Clamp(BorderOpacity, 0f, 1f);
        ButtonOpacity = Math.Clamp(ButtonOpacity, 0f, 1f);
        ButtonHoverOpacity = Math.Clamp(ButtonHoverOpacity, 0f, 1f);
        ButtonActiveOpacity = Math.Clamp(ButtonActiveOpacity, 0f, 1f);
        InputOpacity = Math.Clamp(InputOpacity, 0f, 1f);
        InputHoverOpacity = Math.Clamp(InputHoverOpacity, 0f, 1f);
        InputActiveOpacity = Math.Clamp(InputActiveOpacity, 0f, 1f);
    }
}
