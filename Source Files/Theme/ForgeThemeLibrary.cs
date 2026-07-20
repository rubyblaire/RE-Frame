using System;
using System.Collections.Generic;
using System.Linq;

namespace REFrameXIV.Theme;

public static class ForgeThemeLibrary
{
    public static void Ensure(Configuration configuration)
    {
        configuration.ForgeThemes ??= new List<ForgeThemeDefinition>();
        configuration.ForgeThemes.RemoveAll(theme => theme is null);

        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in configuration.ForgeThemes)
        {
            theme.Normalize();
            while (!knownIds.Add(theme.Id))
                theme.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(configuration.ActiveForgeThemeId) ||
            configuration.ForgeThemes.All(theme => !string.Equals(theme.Id, configuration.ActiveForgeThemeId, StringComparison.OrdinalIgnoreCase)))
        {
            configuration.ActiveForgeThemeId = string.Empty;
            configuration.UseForgeTheme = false;
        }
    }

    public static ForgeThemeDefinition? GetActive(Configuration configuration)
    {
        if (!configuration.UseForgeTheme || string.IsNullOrWhiteSpace(configuration.ActiveForgeThemeId))
            return null;

        return configuration.ForgeThemes?.FirstOrDefault(theme =>
            theme is not null &&
            string.Equals(theme.Id, configuration.ActiveForgeThemeId, StringComparison.OrdinalIgnoreCase));
    }

    public static ForgeThemeDefinition? Find(Configuration configuration, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return configuration.ForgeThemes?.FirstOrDefault(theme =>
            theme is not null &&
            string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static void Activate(Configuration configuration, ForgeThemeDefinition theme)
    {
        theme.Normalize();
        configuration.ActiveForgeThemeId = theme.Id;
        configuration.UseForgeTheme = true;
    }
}
