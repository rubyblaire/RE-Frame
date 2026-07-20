using System;
using System.Collections.Generic;
using System.Numerics;

namespace REFrameXIV.Theme;


public sealed record ForgeWorkshopThemeDefinition(
    string Name,
    string Description,
    string VoteDescription,
    ThemePreset SourcePreset,
    ThemePalette Palette);

public static class ForgeWorkshopThemeCatalog
{
    public static readonly ForgeWorkshopThemeDefinition IshgardianGothic = new(
        "Ishgardian Gothic",
        "Obsidian cathedral panels, layered black and iron-grey surfaces, and decisive crimson ornament.",
        "Obsidian cathedral black, iron grey, and crimson ornament.",
        ThemePreset.ObsidianCrimsonDarkGrey,
        BuildIshgardianGothic());

    public static readonly ForgeWorkshopThemeDefinition AllaganTerminal = new(
        "Allagan Terminal",
        "Obsidian magitek glass driven by neon teal systems, bright red alerts, and clinical white data.",
        "Obsidian magitek glass, neon teal systems, bright red alerts, and white data.",
        ThemePreset.NeonNights,
        BuildAllaganTerminal());

    public static readonly ForgeWorkshopThemeDefinition CrystallineDream = new(
        "Crystalline Dream",
        "Crystal Tower blues, polished silver, radiant white highlights, and an obsidian architectural base.",
        "Crystal Tower blues, polished silver, radiant white, and obsidian structure.",
        ThemePreset.RoyalBlueCrystalWhite,
        BuildCrystallineDream());

    public static readonly ForgeWorkshopThemeDefinition EorzeanNoir = new(
        "Eorzean Noir",
        "Obsidian cinema-black panels, muted green details, bright orange focus accents, and crisp white type.",
        "Obsidian noir, muted green details, bright orange focus, and white type.",
        ThemePreset.ObsidianSlate,
        BuildEorzeanNoir());

    public static IReadOnlyList<ForgeWorkshopThemeDefinition> All { get; } = new[]
    {
        IshgardianGothic,
        AllaganTerminal,
        CrystallineDream,
        EorzeanNoir,
    };

    public static bool TryGet(string name, out ForgeWorkshopThemeDefinition definition)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                definition = candidate;
                return true;
            }
        }

        definition = IshgardianGothic;
        return false;
    }

    private static ThemePalette BuildAllaganTerminal()
    {
        var obsidian = Hex(0x06080B);
        var panelAlt = Hex(0x0B1719);
        var neonTeal = Hex(0x00E6D2);
        var tealLight = Hex(0x70FFF1);
        var alertRed = Hex(0xFF263B);
        var alertRedSoft = Hex(0xFF6675);
        var white = Hex(0xF7FFFF);
        var muted = Hex(0x89A6A5);

        return new ThemePalette(
            Accent: neonTeal,
            AccentMid: tealLight,
            AccentStrong: alertRed,
            Panel: WithAlpha(obsidian, 0.97f),
            PanelAlt: WithAlpha(panelAlt, 0.94f),
            Text: white,
            Muted: muted,
            Success: neonTeal,
            Warning: alertRedSoft,
            Danger: alertRed,
            GradientStart: obsidian,
            GradientMid: neonTeal,
            GradientEnd: alertRed,
            Input: WithAlpha(Hex(0x0A2021), 0.96f),
            InputHovered: WithAlpha(Hex(0x103638), 0.98f),
            InputActive: WithAlpha(Hex(0x153F42), 1.00f),
            Button: WithAlpha(Hex(0x0D292B), 0.98f),
            ButtonHovered: WithAlpha(neonTeal, 0.48f),
            ButtonActive: WithAlpha(alertRed, 0.72f),
            Navigation: new Vector4(0f, 0f, 0f, 0f),
            NavigationHovered: WithAlpha(neonTeal, 0.22f),
            NavigationActive: WithAlpha(alertRed, 0.38f),
            NavigationSelected: WithAlpha(neonTeal, 0.40f),
            Dock: WithAlpha(obsidian, 0.95f),
            DockBorder: WithAlpha(neonTeal, 0.72f),
            DockButton: WithAlpha(neonTeal, 0.10f),
            DockButtonHovered: WithAlpha(alertRed, 0.34f),
            DockButtonActive: WithAlpha(alertRed, 0.60f),
            DockDivider: WithAlpha(tealLight, 0.58f),
            DockText: white,
            HasExtendedColors: true);
    }

    private static ThemePalette BuildIshgardianGothic()
    {
        var black = Hex(0x020304);
        var obsidian = Hex(0x08090C);
        var charcoal = Hex(0x15171B);
        var ironGrey = Hex(0x666B73);
        var silverGrey = Hex(0xBFC3CA);
        var crimson = Hex(0x9C1535);
        var crimsonBright = Hex(0xD3264D);
        var white = Hex(0xF3F4F6);
        var muted = Hex(0x858991);

        return new ThemePalette(
            Accent: ironGrey,
            AccentMid: crimson,
            AccentStrong: crimsonBright,
            Panel: WithAlpha(black, 0.98f),
            PanelAlt: WithAlpha(obsidian, 0.96f),
            Text: white,
            Muted: muted,
            Success: silverGrey,
            Warning: crimson,
            Danger: crimsonBright,
            GradientStart: black,
            GradientMid: ironGrey,
            GradientEnd: crimsonBright,
            Input: WithAlpha(charcoal, 0.98f),
            InputHovered: WithAlpha(Hex(0x24262C), 1.00f),
            InputActive: WithAlpha(Hex(0x302229), 1.00f),
            Button: WithAlpha(charcoal, 0.98f),
            ButtonHovered: WithAlpha(crimson, 0.46f),
            ButtonActive: WithAlpha(crimsonBright, 0.66f),
            Navigation: new Vector4(0f, 0f, 0f, 0f),
            NavigationHovered: WithAlpha(ironGrey, 0.22f),
            NavigationActive: WithAlpha(crimsonBright, 0.36f),
            NavigationSelected: WithAlpha(crimson, 0.42f),
            Dock: WithAlpha(black, 0.96f),
            DockBorder: WithAlpha(crimson, 0.70f),
            DockButton: WithAlpha(ironGrey, 0.12f),
            DockButtonHovered: WithAlpha(crimson, 0.34f),
            DockButtonActive: WithAlpha(crimsonBright, 0.58f),
            DockDivider: WithAlpha(silverGrey, 0.42f),
            DockText: white,
            HasExtendedColors: true);
    }

    private static ThemePalette BuildEorzeanNoir()
    {
        var obsidian = Hex(0x070907);
        var panelAlt = Hex(0x101510);
        var mutedGreen = Hex(0x74866A);
        var greenLight = Hex(0xA2B397);
        var brightOrange = Hex(0xFF861A);
        var orangeLight = Hex(0xFFB14A);
        var white = Hex(0xF8F5ED);
        var muted = Hex(0x9B9B92);

        return new ThemePalette(
            Accent: mutedGreen,
            AccentMid: brightOrange,
            AccentStrong: orangeLight,
            Panel: WithAlpha(obsidian, 0.98f),
            PanelAlt: WithAlpha(panelAlt, 0.95f),
            Text: white,
            Muted: muted,
            Success: greenLight,
            Warning: brightOrange,
            Danger: Hex(0xE64B35),
            GradientStart: obsidian,
            GradientMid: mutedGreen,
            GradientEnd: brightOrange,
            Input: WithAlpha(Hex(0x151B15), 0.98f),
            InputHovered: WithAlpha(Hex(0x242E22), 1.00f),
            InputActive: WithAlpha(Hex(0x352A1B), 1.00f),
            Button: WithAlpha(Hex(0x172017), 0.98f),
            ButtonHovered: WithAlpha(mutedGreen, 0.48f),
            ButtonActive: WithAlpha(brightOrange, 0.70f),
            Navigation: new Vector4(0f, 0f, 0f, 0f),
            NavigationHovered: WithAlpha(mutedGreen, 0.24f),
            NavigationActive: WithAlpha(brightOrange, 0.38f),
            NavigationSelected: WithAlpha(brightOrange, 0.42f),
            Dock: WithAlpha(obsidian, 0.96f),
            DockBorder: WithAlpha(brightOrange, 0.70f),
            DockButton: WithAlpha(mutedGreen, 0.12f),
            DockButtonHovered: WithAlpha(brightOrange, 0.34f),
            DockButtonActive: WithAlpha(brightOrange, 0.62f),
            DockDivider: WithAlpha(greenLight, 0.48f),
            DockText: white,
            HasExtendedColors: true);
    }

    private static ThemePalette BuildCrystallineDream()
    {
        var obsidian = Hex(0x050811);
        var towerBlue = Hex(0x235FC2);
        var crystalBlue = Hex(0x58B8FF);
        var deepBlue = Hex(0x0B1B36);
        var silver = Hex(0xB9C8DA);
        var silverLight = Hex(0xDCE7F2);
        var white = Hex(0xF7FCFF);
        var muted = Hex(0x8FA2B8);

        return new ThemePalette(
            Accent: towerBlue,
            AccentMid: crystalBlue,
            AccentStrong: silverLight,
            Panel: WithAlpha(obsidian, 0.98f),
            PanelAlt: WithAlpha(deepBlue, 0.95f),
            Text: white,
            Muted: muted,
            Success: crystalBlue,
            Warning: silver,
            Danger: Hex(0xD64D68),
            GradientStart: obsidian,
            GradientMid: towerBlue,
            GradientEnd: white,
            Input: WithAlpha(Hex(0x0B203E), 0.98f),
            InputHovered: WithAlpha(Hex(0x12325C), 1.00f),
            InputActive: WithAlpha(Hex(0x1D4778), 1.00f),
            Button: WithAlpha(Hex(0x102B50), 0.98f),
            ButtonHovered: WithAlpha(crystalBlue, 0.44f),
            ButtonActive: WithAlpha(silver, 0.60f),
            Navigation: new Vector4(0f, 0f, 0f, 0f),
            NavigationHovered: WithAlpha(crystalBlue, 0.22f),
            NavigationActive: WithAlpha(silver, 0.34f),
            NavigationSelected: WithAlpha(towerBlue, 0.46f),
            Dock: WithAlpha(obsidian, 0.96f),
            DockBorder: WithAlpha(crystalBlue, 0.72f),
            DockButton: WithAlpha(towerBlue, 0.14f),
            DockButtonHovered: WithAlpha(crystalBlue, 0.36f),
            DockButtonActive: WithAlpha(silver, 0.58f),
            DockDivider: WithAlpha(silverLight, 0.52f),
            DockText: white,
            HasExtendedColors: true);
    }

    private static Vector4 Hex(uint rgb)
        => new(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);

    private static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, alpha);
}
