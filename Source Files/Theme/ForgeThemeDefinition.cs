using System;
using System.Numerics;

namespace REFrameXIV.Theme;

[Serializable]
public sealed class ForgeThemeDefinition
{
    public const int CurrentVisualSchema = 2;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My Forge";
    public ThemePreset SourcePreset { get; set; } = ThemePreset.CornflowerSeafoam;
    public int VisualSchemaVersion { get; set; }


    public Vector4 Accent { get; set; }
    public Vector4 AccentMid { get; set; }
    public Vector4 AccentStrong { get; set; }
    public Vector4 Panel { get; set; }
    public Vector4 PanelAlt { get; set; }
    public Vector4 Text { get; set; }
    public Vector4 Muted { get; set; }
    public Vector4 Success { get; set; }
    public Vector4 Warning { get; set; }
    public Vector4 Danger { get; set; }
    public Vector4 GradientStart { get; set; }
    public Vector4 GradientMid { get; set; }
    public Vector4 GradientEnd { get; set; }


    public Vector4 Input { get; set; }
    public Vector4 InputHovered { get; set; }
    public Vector4 InputActive { get; set; }
    public Vector4 Button { get; set; }
    public Vector4 ButtonHovered { get; set; }
    public Vector4 ButtonActive { get; set; }
    public Vector4 Navigation { get; set; }
    public Vector4 NavigationHovered { get; set; }
    public Vector4 NavigationActive { get; set; }
    public Vector4 NavigationSelected { get; set; }


    public Vector4 Dock { get; set; }
    public Vector4 DockBorder { get; set; }
    public Vector4 DockButton { get; set; }
    public Vector4 DockButtonHovered { get; set; }
    public Vector4 DockButtonActive { get; set; }
    public Vector4 DockDivider { get; set; }
    public Vector4 DockText { get; set; }

    public ForgeStyleSettings Style { get; set; } = ForgeStyleSettings.Default;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public ForgeThemeDefinition()
    {


        ApplyPaletteCore(JobThemeProvider.GetPreset(SourcePreset));
        VisualSchemaVersion = 0;
    }

    public static ForgeThemeDefinition FromPalette(string name, ThemePalette palette, ThemePreset sourcePreset)
    {
        var theme = new ForgeThemeDefinition
        {
            Name = string.IsNullOrWhiteSpace(name) ? "My Forge" : name.Trim(),
            SourcePreset = sourcePreset,
        };
        theme.ApplyPalette(palette);
        theme.Normalize();
        return theme;
    }

    public ThemePalette ToPalette() => new(
        Accent,
        AccentMid,
        AccentStrong,
        Panel,
        PanelAlt,
        Text,
        Muted,
        Success,
        Warning,
        Danger,
        GradientStart,
        GradientMid,
        GradientEnd,
        Input,
        InputHovered,
        InputActive,
        Button,
        ButtonHovered,
        ButtonActive,
        Navigation,
        NavigationHovered,
        NavigationActive,
        NavigationSelected,
        Dock,
        DockBorder,
        DockButton,
        DockButtonHovered,
        DockButtonActive,
        DockDivider,
        DockText,
        true);

    public void ApplyPalette(ThemePalette palette)
    {
        ApplyPaletteCore(palette);
        VisualSchemaVersion = CurrentVisualSchema;
        ModifiedUtc = DateTime.UtcNow;
    }

    private void ApplyPaletteCore(ThemePalette palette)
    {
        Accent = palette.Accent;
        AccentMid = palette.AccentMid;
        AccentStrong = palette.AccentStrong;
        Panel = palette.Panel;
        PanelAlt = palette.PanelAlt;
        Text = palette.Text;
        Muted = palette.Muted;
        Success = palette.Success;
        Warning = palette.Warning;
        Danger = palette.Danger;
        GradientStart = palette.GradientStart;
        GradientMid = palette.GradientMid;
        GradientEnd = palette.GradientEnd;

        Input = palette.ResolvedInput;
        InputHovered = palette.ResolvedInputHovered;
        InputActive = palette.ResolvedInputActive;
        Button = palette.ResolvedButton;
        ButtonHovered = palette.ResolvedButtonHovered;
        ButtonActive = palette.ResolvedButtonActive;
        Navigation = palette.ResolvedNavigation;
        NavigationHovered = palette.ResolvedNavigationHovered;
        NavigationActive = palette.ResolvedNavigationActive;
        NavigationSelected = palette.ResolvedNavigationSelected;
        Dock = palette.ResolvedDock;
        DockBorder = palette.ResolvedDockBorder;
        DockButton = palette.ResolvedDockButton;
        DockButtonHovered = palette.ResolvedDockButtonHovered;
        DockButtonActive = palette.ResolvedDockButtonActive;
        DockDivider = palette.ResolvedDockDivider;
        DockText = palette.ResolvedDockText;
    }

    public void ResetFromPreset(ThemePreset preset)
    {
        SourcePreset = preset;
        ApplyPalette(JobThemeProvider.GetPreset(preset));
    }

    public ForgeThemeDefinition Clone(string? name = null)
    {
        var clone = FromPalette(
            string.IsNullOrWhiteSpace(name) ? $"{Name} Copy" : name!,
            ToPalette(),
            SourcePreset);
        clone.Style = (Style ?? ForgeStyleSettings.Default).Clone();
        return clone;
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        Name = string.IsNullOrWhiteSpace(Name) ? "My Forge" : Name.Trim();
        if (Name.Length > 48)
            Name = Name[..48];

        Accent = ClampColor(Accent);
        AccentMid = ClampColor(AccentMid);
        AccentStrong = ClampColor(AccentStrong);
        Panel = ClampColor(Panel);
        PanelAlt = ClampColor(PanelAlt);
        Text = ClampColor(Text);
        Muted = ClampColor(Muted);
        Success = ClampColor(Success);
        Warning = ClampColor(Warning);
        Danger = ClampColor(Danger);
        GradientStart = ClampColor(GradientStart);
        GradientMid = ClampColor(GradientMid);
        GradientEnd = ClampColor(GradientEnd);

        if (VisualSchemaVersion < CurrentVisualSchema)
        {


            var legacyPalette = new ThemePalette(
                Accent, AccentMid, AccentStrong, Panel, PanelAlt, Text, Muted,
                Success, Warning, Danger, GradientStart, GradientMid, GradientEnd);
            Input = legacyPalette.ResolvedInput;
            InputHovered = legacyPalette.ResolvedInputHovered;
            InputActive = legacyPalette.ResolvedInputActive;
            Button = legacyPalette.ResolvedButton;
            ButtonHovered = legacyPalette.ResolvedButtonHovered;
            ButtonActive = legacyPalette.ResolvedButtonActive;
            Navigation = legacyPalette.ResolvedNavigation;
            NavigationHovered = legacyPalette.ResolvedNavigationHovered;
            NavigationActive = legacyPalette.ResolvedNavigationActive;
            NavigationSelected = legacyPalette.ResolvedNavigationSelected;
            Dock = legacyPalette.ResolvedDock;
            DockBorder = legacyPalette.ResolvedDockBorder;
            DockButton = legacyPalette.ResolvedDockButton;
            DockButtonHovered = legacyPalette.ResolvedDockButtonHovered;
            DockButtonActive = legacyPalette.ResolvedDockButtonActive;
            DockDivider = legacyPalette.ResolvedDockDivider;
            DockText = legacyPalette.ResolvedDockText;
            VisualSchemaVersion = CurrentVisualSchema;
        }

        Input = ClampColor(Input);
        InputHovered = ClampColor(InputHovered);
        InputActive = ClampColor(InputActive);
        Button = ClampColor(Button);
        ButtonHovered = ClampColor(ButtonHovered);
        ButtonActive = ClampColor(ButtonActive);
        Navigation = ClampColor(Navigation);
        NavigationHovered = ClampColor(NavigationHovered);
        NavigationActive = ClampColor(NavigationActive);
        NavigationSelected = ClampColor(NavigationSelected);
        Dock = ClampColor(Dock);
        DockBorder = ClampColor(DockBorder);
        DockButton = ClampColor(DockButton);
        DockButtonHovered = ClampColor(DockButtonHovered);
        DockButtonActive = ClampColor(DockButtonActive);
        DockDivider = ClampColor(DockDivider);
        DockText = ClampColor(DockText);

        Style ??= ForgeStyleSettings.Default;
        Style.Normalize();
        if (CreatedUtc == default) CreatedUtc = DateTime.UtcNow;
        if (ModifiedUtc == default) ModifiedUtc = CreatedUtc;
    }

    private static Vector4 ClampColor(Vector4 color) => new(
        Math.Clamp(color.X, 0f, 1f),
        Math.Clamp(color.Y, 0f, 1f),
        Math.Clamp(color.Z, 0f, 1f),
        Math.Clamp(color.W, 0f, 1f));
}
