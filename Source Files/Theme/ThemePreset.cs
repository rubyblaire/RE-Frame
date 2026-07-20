using REFrameXIV.Localization;

namespace REFrameXIV.Theme;

public enum ThemePreset
{
    CornflowerSeafoam = 0,
    DeepCrimsonBlushRose = 1,
    ObsidianSlate = 2,
    RainbowDash = 3,
    RoyalPurpleDeepGold = 4,
    RoyalBlueCrystalWhite = 5,
    ObsidianCrimsonDarkGrey = 6,
    ObsidianDeepGoldDarkGrey = 7,
    ObsidianSteelBlueDarkGrey = 8,
    CottonCandy = 9,
    NeonNights = 10,
}

public static class ThemePresetInfo
{
    public static readonly ThemePreset[] All =
    {
        ThemePreset.CornflowerSeafoam,
        ThemePreset.DeepCrimsonBlushRose,
        ThemePreset.ObsidianSlate,
        ThemePreset.RainbowDash,
        ThemePreset.RoyalPurpleDeepGold,
        ThemePreset.RoyalBlueCrystalWhite,
        ThemePreset.ObsidianCrimsonDarkGrey,
        ThemePreset.ObsidianDeepGoldDarkGrey,
        ThemePreset.ObsidianSteelBlueDarkGrey,
        ThemePreset.CottonCandy,
        ThemePreset.NeonNights,
    };

    public static string Label(ThemePreset preset) => preset switch
    {
        ThemePreset.CornflowerSeafoam => Localizer.Text("theme.cornflowerSeafoam", "Little Mermaid"),
        ThemePreset.DeepCrimsonBlushRose => Localizer.Text("theme.deepCrimsonBlushRose", "Dracula"),
        ThemePreset.ObsidianSlate => Localizer.Text("theme.obsidianSlate", "Monochrome"),
        ThemePreset.RainbowDash => Localizer.Text("theme.rainbowDash", "Rainbow Dash"),
        ThemePreset.RoyalPurpleDeepGold => Localizer.Text("theme.royalPurpleDeepGold", "Royalty"),
        ThemePreset.RoyalBlueCrystalWhite => Localizer.Text("theme.royalBlueCrystalWhite", "Crystal Waters"),
        ThemePreset.ObsidianCrimsonDarkGrey => Localizer.Text("theme.obsidianCrimsonDarkGrey", "Gothic Heart"),
        ThemePreset.ObsidianDeepGoldDarkGrey => Localizer.Text("theme.obsidianDeepGoldDarkGrey", "Hidden King"),
        ThemePreset.ObsidianSteelBlueDarkGrey => Localizer.Text("theme.obsidianSteelBlueDarkGrey", "Night Sky"),
        ThemePreset.CottonCandy => Localizer.Text("theme.cottonCandy", "Cotton Candy"),
        ThemePreset.NeonNights => Localizer.Text("theme.neonNights", "Neon Nights"),
        _ => preset.ToString(),
    };
}
