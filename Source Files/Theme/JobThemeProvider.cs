using System;
using System.Collections.Generic;
using System.Numerics;

namespace REFrameXIV.Theme;

public static class JobThemeProvider
{
    private static readonly Vector4 Panel = new(0.035f, 0.040f, 0.050f, 0.96f);
    private static readonly Vector4 PanelAlt = new(0.070f, 0.076f, 0.090f, 0.96f);
    private static readonly Vector4 Text = new(0.94f, 0.93f, 0.90f, 1.00f);
    private static readonly Vector4 Muted = new(0.57f, 0.59f, 0.64f, 1.00f);
    private static readonly Vector4 Success = new(0.35f, 0.78f, 0.58f, 1.00f);
    private static readonly Vector4 Warning = new(0.91f, 0.70f, 0.30f, 1.00f);
    private static readonly Vector4 Danger = new(0.87f, 0.29f, 0.34f, 1.00f);

    private static readonly Dictionary<string, (Vector4 Accent, Vector4 Strong)> JobColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PLD"] = (new(0.30f, 0.58f, 0.88f, 1f), new(0.55f, 0.76f, 1.00f, 1f)),
            ["WAR"] = (new(0.69f, 0.20f, 0.20f, 1f), new(0.95f, 0.35f, 0.24f, 1f)),
            ["DRK"] = (new(0.47f, 0.18f, 0.35f, 1f), new(0.86f, 0.22f, 0.38f, 1f)),
            ["GNB"] = (new(0.42f, 0.57f, 0.67f, 1f), new(0.65f, 0.82f, 0.93f, 1f)),
            ["WHM"] = (new(0.78f, 0.83f, 0.72f, 1f), new(0.95f, 0.97f, 0.88f, 1f)),
            ["SCH"] = (new(0.48f, 0.67f, 0.38f, 1f), new(0.74f, 0.88f, 0.55f, 1f)),
            ["AST"] = (new(0.52f, 0.42f, 0.78f, 1f), new(0.88f, 0.72f, 0.98f, 1f)),
            ["SGE"] = (new(0.30f, 0.71f, 0.78f, 1f), new(0.67f, 0.94f, 0.98f, 1f)),
            ["MNK"] = (new(0.80f, 0.48f, 0.18f, 1f), new(1.00f, 0.73f, 0.28f, 1f)),
            ["DRG"] = (new(0.24f, 0.38f, 0.72f, 1f), new(0.43f, 0.65f, 1.00f, 1f)),
            ["NIN"] = (new(0.46f, 0.23f, 0.48f, 1f), new(0.82f, 0.38f, 0.67f, 1f)),
            ["SAM"] = (new(0.64f, 0.20f, 0.22f, 1f), new(0.94f, 0.38f, 0.31f, 1f)),
            ["RPR"] = (new(0.35f, 0.23f, 0.43f, 1f), new(0.66f, 0.37f, 0.73f, 1f)),
            ["VPR"] = (new(0.25f, 0.60f, 0.48f, 1f), new(0.42f, 0.88f, 0.65f, 1f)),
            ["BRD"] = (new(0.39f, 0.59f, 0.35f, 1f), new(0.67f, 0.82f, 0.46f, 1f)),
            ["MCH"] = (new(0.55f, 0.48f, 0.36f, 1f), new(0.86f, 0.70f, 0.42f, 1f)),
            ["DNC"] = (new(0.72f, 0.36f, 0.57f, 1f), new(0.98f, 0.62f, 0.80f, 1f)),
            ["BLM"] = (new(0.38f, 0.25f, 0.53f, 1f), new(0.73f, 0.43f, 0.86f, 1f)),
            ["SMN"] = (new(0.28f, 0.63f, 0.45f, 1f), new(0.47f, 0.92f, 0.66f, 1f)),
            ["RDM"] = (new(0.70f, 0.20f, 0.28f, 1f), new(1.00f, 0.42f, 0.47f, 1f)),
            ["PCT"] = (new(0.56f, 0.39f, 0.75f, 1f), new(0.92f, 0.62f, 0.96f, 1f)),
            ["BLU"] = (new(0.20f, 0.46f, 0.77f, 1f), new(0.41f, 0.74f, 1.00f, 1f)),
            ["CRP"] = (new(0.56f, 0.40f, 0.25f, 1f), new(0.82f, 0.62f, 0.37f, 1f)),
            ["BSM"] = (new(0.51f, 0.40f, 0.34f, 1f), new(0.83f, 0.60f, 0.40f, 1f)),
            ["ARM"] = (new(0.43f, 0.50f, 0.55f, 1f), new(0.72f, 0.80f, 0.84f, 1f)),
            ["GSM"] = (new(0.72f, 0.57f, 0.25f, 1f), new(1.00f, 0.83f, 0.39f, 1f)),
            ["LTW"] = (new(0.56f, 0.39f, 0.29f, 1f), new(0.84f, 0.60f, 0.42f, 1f)),
            ["WVR"] = (new(0.65f, 0.57f, 0.68f, 1f), new(0.92f, 0.82f, 0.95f, 1f)),
            ["ALC"] = (new(0.37f, 0.58f, 0.50f, 1f), new(0.60f, 0.88f, 0.72f, 1f)),
            ["CUL"] = (new(0.65f, 0.42f, 0.31f, 1f), new(0.92f, 0.65f, 0.42f, 1f)),
            ["MIN"] = (new(0.43f, 0.48f, 0.54f, 1f), new(0.69f, 0.76f, 0.82f, 1f)),
            ["BTN"] = (new(0.35f, 0.61f, 0.34f, 1f), new(0.56f, 0.85f, 0.48f, 1f)),
            ["FSH"] = (new(0.25f, 0.55f, 0.68f, 1f), new(0.49f, 0.82f, 0.91f, 1f)),
        };

    public static readonly string[] AllJobs =
    {
        "PLD", "WAR", "DRK", "GNB",
        "WHM", "SCH", "AST", "SGE",
        "MNK", "DRG", "NIN", "SAM", "RPR", "VPR",
        "BRD", "MCH", "DNC",
        "BLM", "SMN", "RDM", "PCT", "BLU",
        "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL",
        "MIN", "BTN", "FSH",
    };

    private static readonly Dictionary<string, string> JobNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PLD"] = "Paladin", ["WAR"] = "Warrior", ["DRK"] = "Dark Knight", ["GNB"] = "Gunbreaker",
        ["WHM"] = "White Mage", ["SCH"] = "Scholar", ["AST"] = "Astrologian", ["SGE"] = "Sage",
        ["MNK"] = "Monk", ["DRG"] = "Dragoon", ["NIN"] = "Ninja", ["SAM"] = "Samurai", ["RPR"] = "Reaper", ["VPR"] = "Viper",
        ["BRD"] = "Bard", ["MCH"] = "Machinist", ["DNC"] = "Dancer",
        ["BLM"] = "Black Mage", ["SMN"] = "Summoner", ["RDM"] = "Red Mage", ["PCT"] = "Pictomancer", ["BLU"] = "Blue Mage",
        ["CRP"] = "Carpenter", ["BSM"] = "Blacksmith", ["ARM"] = "Armorer", ["GSM"] = "Goldsmith",
        ["LTW"] = "Leatherworker", ["WVR"] = "Weaver", ["ALC"] = "Alchemist", ["CUL"] = "Culinarian",
        ["MIN"] = "Miner", ["BTN"] = "Botanist", ["FSH"] = "Fisher",
    };

    public static ThemePalette Get(
        string jobAbbreviation,
        bool followJobColors,
        ThemePreset selectedTheme,
        IReadOnlyDictionary<string, JobThemeColorOverride>? overrides = null)
    {
        var jobKey = NormalizeJob(jobAbbreviation);
        if (followJobColors && JobColors.TryGetValue(jobKey, out var job))
        {
            var accent = job.Accent;
            var strong = job.Strong;
            if (overrides is not null && overrides.TryGetValue(jobKey, out var custom) && custom is not null)
            {
                accent = custom.Accent;
                strong = custom.Highlight;
            }

            return BuildJobPalette(accent, strong);
        }

        return GetPreset(selectedTheme);
    }

    public static ThemePalette ApplyJobColors(
        ThemePalette basePalette,
        string jobAbbreviation,
        IReadOnlyDictionary<string, JobThemeColorOverride>? overrides = null)
    {
        var jobKey = NormalizeJob(jobAbbreviation);
        if (!JobColors.TryGetValue(jobKey, out var job))
            return basePalette;

        var accent = job.Accent;
        var strong = job.Strong;
        if (overrides is not null && overrides.TryGetValue(jobKey, out var custom) && custom is not null)
        {
            accent = custom.Accent;
            strong = custom.Highlight;
        }

        var middle = Blend(accent, strong, 0.50f);
        return basePalette with
        {
            Accent = accent,
            AccentMid = middle,
            AccentStrong = strong,
            GradientStart = accent,
            GradientMid = middle,
            GradientEnd = strong,
        };
    }

    public static ThemePalette GetPreset(ThemePreset preset) => preset switch
    {
        ThemePreset.DeepCrimsonBlushRose => DeepCrimsonBlushRose(),
        ThemePreset.ObsidianSlate => ObsidianSlate(),
        ThemePreset.RainbowDash => RainbowDash(),
        ThemePreset.RoyalPurpleDeepGold => RoyalPurpleDeepGold(),
        ThemePreset.RoyalBlueCrystalWhite => RoyalBlueCrystalWhite(),
        ThemePreset.ObsidianCrimsonDarkGrey => ObsidianCrimsonDarkGrey(),
        ThemePreset.ObsidianDeepGoldDarkGrey => ObsidianDeepGoldDarkGrey(),
        ThemePreset.ObsidianSteelBlueDarkGrey => ObsidianSteelBlueDarkGrey(),
        ThemePreset.CottonCandy => CottonCandy(),
        ThemePreset.NeonNights => NeonNights(),
        _ => CornflowerSeafoam(),
    };

    public static bool TryGetDefaultJobColors(string jobAbbreviation, out Vector4 accent, out Vector4 highlight)
    {
        if (JobColors.TryGetValue(NormalizeJob(jobAbbreviation), out var colors))
        {
            accent = colors.Accent;
            highlight = colors.Strong;
            return true;
        }

        accent = new Vector4(0.392f, 0.584f, 0.929f, 1f);
        highlight = new Vector4(0.624f, 0.886f, 0.749f, 1f);
        return false;
    }

    public static bool IsSupportedJob(string jobAbbreviation) => JobColors.ContainsKey(NormalizeJob(jobAbbreviation));

    public static string JobLabel(string jobAbbreviation)
    {
        var key = NormalizeJob(jobAbbreviation);
        return JobNames.TryGetValue(key, out var name) ? $"{key} — {name}" : key;
    }

    private static ThemePalette BuildJobPalette(Vector4 accent, Vector4 strong)
    {
        var middle = Blend(accent, strong, 0.50f);
        return new ThemePalette(
            accent, middle, strong, Panel, PanelAlt, Text, Muted, Success, Warning, Danger,
            accent, middle, strong);
    }

    private static ThemePalette CornflowerSeafoam()
    {
        var cornflower = Hex(0x6495ED);
        var cornflowerLight = Hex(0x86A9F2);
        var seafoam = Hex(0x9FE3BF);
        return new ThemePalette(
            cornflower, cornflowerLight, seafoam,
            new Vector4(0.025f, 0.050f, 0.075f, 0.82f),
            new Vector4(0.055f, 0.105f, 0.120f, 0.78f),
            new Vector4(0.94f, 0.98f, 0.98f, 1f),
            new Vector4(0.66f, 0.76f, 0.79f, 1f),
            Success, Warning, Danger,
            cornflower, cornflowerLight, seafoam);
    }

    private static ThemePalette DeepCrimsonBlushRose()
    {
        var crimson = Hex(0x6D1028);
        var rose = Hex(0xE89AAF);
        var titleRose = Hex(0xF2B3C2);
        var obsidian = Hex(0x090A0E);
        return new ThemePalette(
            crimson, rose, titleRose,
            new Vector4(obsidian.X, obsidian.Y, obsidian.Z, 0.94f),
            new Vector4(0.105f, 0.035f, 0.055f, 0.88f),
            new Vector4(1.00f, 0.94f, 0.95f, 1f),
            new Vector4(0.78f, 0.63f, 0.67f, 1f),
            Success, Warning, Danger,
            crimson, rose, obsidian);
    }

    private static ThemePalette ObsidianSlate()
    {
        var obsidian = Hex(0x090A0E);
        var slate = Hex(0x66707C);
        var white = Hex(0xFFFFFF);
        return new ThemePalette(
            slate, Hex(0xAAB1BA), white,
            new Vector4(obsidian.X, obsidian.Y, obsidian.Z, 0.95f),
            new Vector4(0.075f, 0.082f, 0.095f, 0.89f),
            new Vector4(0.96f, 0.97f, 0.99f, 1f),
            new Vector4(0.63f, 0.67f, 0.72f, 1f),
            Success, Warning, Danger,
            obsidian, slate, white);
    }

    private static ThemePalette RainbowDash()
    {
        var start = new Vector4(0.20f, 0.78f, 0.98f, 1f);
        var middle = new Vector4(1.00f, 0.80f, 0.22f, 1f);
        var end = new Vector4(0.96f, 0.30f, 0.68f, 1f);
        return new ThemePalette(
            start, middle, end,
            new Vector4(0.035f, 0.040f, 0.090f, 0.90f),
            new Vector4(0.075f, 0.070f, 0.145f, 0.84f),
            new Vector4(0.97f, 0.98f, 1.00f, 1f),
            new Vector4(0.69f, 0.72f, 0.82f, 1f),
            Success, Warning, Danger,
            start, middle, end);
    }

    private static ThemePalette RoyalPurpleDeepGold()
    {
        var purple = Hex(0x6A3D9A);
        var gold = Hex(0xC79A35);
        var goldHighlight = Hex(0xE5C15D);
        var obsidian = Hex(0x090A0E);
        return new ThemePalette(
            purple, gold, goldHighlight,
            new Vector4(obsidian.X, obsidian.Y, obsidian.Z, 0.94f),
            new Vector4(0.105f, 0.060f, 0.135f, 0.88f),
            new Vector4(0.98f, 0.94f, 0.84f, 1f),
            new Vector4(0.72f, 0.65f, 0.71f, 1f),
            Success, Warning, Danger,
            purple, gold, obsidian);
    }

    private static ThemePalette RoyalBlueCrystalWhite()
    {
        var royalBlue = Hex(0x2457AE);
        var white = Hex(0xFFFFFF);
        var pearl = Hex(0xF3EFE5);
        return new ThemePalette(
            royalBlue, Hex(0x8EAFE1), pearl,
            new Vector4(0.020f, 0.045f, 0.095f, 0.92f),
            new Vector4(0.055f, 0.105f, 0.175f, 0.86f),
            new Vector4(0.97f, 0.99f, 1.00f, 1f),
            new Vector4(0.68f, 0.76f, 0.86f, 1f),
            Success, Warning, Danger,
            royalBlue, white, pearl);
    }

    private static ThemePalette ObsidianCrimsonDarkGrey()
    {
        var start = new Vector4(0.44f, 0.025f, 0.055f, 1f);
        var middle = new Vector4(0.72f, 0.055f, 0.095f, 1f);
        var end = new Vector4(0.96f, 0.16f, 0.20f, 1f);
        return new ThemePalette(
            start, middle, end,
            new Vector4(0.012f, 0.014f, 0.018f, 0.95f),
            new Vector4(0.090f, 0.095f, 0.110f, 0.89f),
            new Vector4(0.98f, 0.96f, 0.97f, 1f),
            new Vector4(0.67f, 0.62f, 0.64f, 1f),
            Success, Warning, Danger,
            start, middle, end);
    }

    private static ThemePalette ObsidianDeepGoldDarkGrey()
    {
        var start = new Vector4(0.48f, 0.30f, 0.025f, 1f);
        var middle = new Vector4(0.78f, 0.52f, 0.065f, 1f);
        var end = new Vector4(1.00f, 0.79f, 0.18f, 1f);
        return new ThemePalette(
            start, middle, end,
            new Vector4(0.012f, 0.014f, 0.018f, 0.95f),
            new Vector4(0.090f, 0.095f, 0.110f, 0.89f),
            new Vector4(1.00f, 0.98f, 0.91f, 1f),
            new Vector4(0.70f, 0.66f, 0.56f, 1f),
            Success, Warning, Danger,
            start, middle, end);
    }

    private static ThemePalette ObsidianSteelBlueDarkGrey()
    {
        var start = new Vector4(0.18f, 0.34f, 0.46f, 1f);
        var middle = new Vector4(0.32f, 0.52f, 0.66f, 1f);
        var end = new Vector4(0.56f, 0.76f, 0.88f, 1f);
        return new ThemePalette(
            start, middle, end,
            new Vector4(0.012f, 0.014f, 0.018f, 0.95f),
            new Vector4(0.090f, 0.095f, 0.110f, 0.89f),
            new Vector4(0.95f, 0.98f, 1.00f, 1f),
            new Vector4(0.60f, 0.68f, 0.74f, 1f),
            Success, Warning, Danger,
            start, middle, end);
    }

    private static ThemePalette CottonCandy()
    {
        var selection = Hex(0xFA9FC5);
        var accentSoft = Hex(0xFFDEEC);
        var white = Hex(0xFFFFFF);
        var title = Hex(0xF86CAB);
        var window = Hex(0xFDBDD7);
        return new ThemePalette(
            selection, accentSoft, title,
            new Vector4(window.X, window.Y, window.Z, 0.94f),
            new Vector4(accentSoft.X, accentSoft.Y, accentSoft.Z, 0.88f),
            Hex(0x4B1831),
            Hex(0x7B4A61),
            Success, Warning, Danger,
            selection, accentSoft, white);
    }

    private static ThemePalette NeonNights()
    {
        var start = new Vector4(0.98f, 0.035f, 0.68f, 1f);
        var middle = new Vector4(0.56f, 0.10f, 1.00f, 1f);
        var end = new Vector4(1.00f, 0.94f, 0.10f, 1f);
        return new ThemePalette(
            start, middle, end,
            new Vector4(0.022f, 0.006f, 0.050f, 0.95f),
            new Vector4(0.085f, 0.020f, 0.135f, 0.90f),
            new Vector4(0.99f, 0.97f, 1.00f, 1f),
            new Vector4(0.72f, 0.60f, 0.82f, 1f),
            Success, Warning, Danger,
            start, middle, end);
    }

    private static Vector4 Hex(uint rgb)
        => new(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);

    private static string NormalizeJob(string value) => string.IsNullOrWhiteSpace(value)
        ? "XIV"
        : value.Trim().ToUpperInvariant();

    private static Vector4 Blend(Vector4 a, Vector4 b, float amount)
        => Vector4.Lerp(a, b, Math.Clamp(amount, 0f, 1f));
}
