using System.Numerics;

namespace REFrameXIV.Theme;


public readonly record struct ThemePalette(
    Vector4 Accent,
    Vector4 AccentMid,
    Vector4 AccentStrong,
    Vector4 Panel,
    Vector4 PanelAlt,
    Vector4 Text,
    Vector4 Muted,
    Vector4 Success,
    Vector4 Warning,
    Vector4 Danger,
    Vector4 GradientStart,
    Vector4 GradientMid,
    Vector4 GradientEnd,
    Vector4 Input = default,
    Vector4 InputHovered = default,
    Vector4 InputActive = default,
    Vector4 Button = default,
    Vector4 ButtonHovered = default,
    Vector4 ButtonActive = default,
    Vector4 Navigation = default,
    Vector4 NavigationHovered = default,
    Vector4 NavigationActive = default,
    Vector4 NavigationSelected = default,
    Vector4 Dock = default,
    Vector4 DockBorder = default,
    Vector4 DockButton = default,
    Vector4 DockButtonHovered = default,
    Vector4 DockButtonActive = default,
    Vector4 DockDivider = default,
    Vector4 DockText = default,
    bool HasExtendedColors = false)
{
    public Vector4 ResolvedInput => HasExtendedColors
        ? Input
        : new Vector4(0.94f, 0.96f, 1.00f, 1.00f);

    public Vector4 ResolvedInputHovered => HasExtendedColors
        ? InputHovered
        : new Vector4(0.96f, 0.98f, 1.00f, 1.00f);

    public Vector4 ResolvedInputActive => HasExtendedColors
        ? InputActive
        : new Vector4(0.98f, 0.99f, 1.00f, 1.00f);

    public Vector4 ResolvedButton => HasExtendedColors ? Button : Accent;
    public Vector4 ResolvedButtonHovered => HasExtendedColors ? ButtonHovered : AccentStrong;
    public Vector4 ResolvedButtonActive => HasExtendedColors ? ButtonActive : AccentStrong;

    public Vector4 ResolvedNavigation => HasExtendedColors
        ? Navigation
        : new Vector4(0f, 0f, 0f, 0f);
    public Vector4 ResolvedNavigationHovered => HasExtendedColors
        ? NavigationHovered
        : new Vector4(Accent.X, Accent.Y, Accent.Z, 0.26f);
    public Vector4 ResolvedNavigationActive => HasExtendedColors
        ? NavigationActive
        : new Vector4(AccentStrong.X, AccentStrong.Y, AccentStrong.Z, 0.42f);
    public Vector4 ResolvedNavigationSelected => HasExtendedColors
        ? NavigationSelected
        : new Vector4(Accent.X, Accent.Y, Accent.Z, 0.42f);

    public Vector4 ResolvedDock => HasExtendedColors
        ? Dock
        : new Vector4(Panel.X, Panel.Y, Panel.Z, 0.94f);
    public Vector4 ResolvedDockBorder => HasExtendedColors
        ? DockBorder
        : new Vector4(AccentStrong.X, AccentStrong.Y, AccentStrong.Z, 0.58f);
    public Vector4 ResolvedDockButton => HasExtendedColors
        ? DockButton
        : new Vector4(Accent.X, Accent.Y, Accent.Z, 0.10f);
    public Vector4 ResolvedDockButtonHovered => HasExtendedColors
        ? DockButtonHovered
        : new Vector4(AccentStrong.X, AccentStrong.Y, AccentStrong.Z, 0.30f);
    public Vector4 ResolvedDockButtonActive => HasExtendedColors
        ? DockButtonActive
        : new Vector4(AccentStrong.X, AccentStrong.Y, AccentStrong.Z, 0.52f);
    public Vector4 ResolvedDockDivider => HasExtendedColors
        ? DockDivider
        : new Vector4(AccentStrong.X, AccentStrong.Y, AccentStrong.Z, 0.55f);
    public Vector4 ResolvedDockText => HasExtendedColors ? DockText : Text;
}
