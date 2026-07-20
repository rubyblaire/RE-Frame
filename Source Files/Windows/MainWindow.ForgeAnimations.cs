using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed partial class MainWindow
{
    private string forgeAnimationSearch = string.Empty;
    private string forgeAnimationSelectedId = string.Empty;
    private string forgeAnimationStatus = string.Empty;
    private ForgeAnimationCategory? forgeAnimationCategory;
    private bool forgeAnimationFavoritesOnly;
    private bool forgeAnimationRecentOnly;
    private bool forgeAnimationShowAdvanced;

    private void DrawForgeAnimationLibrary(Configuration configuration)
    {
        var settings = configuration.ForgePremium;
        settings.EnsureValid();
        var theme = plugin.CurrentTheme;

        DrawAnimationLibraryHeader(settings);
        ImGui.Spacing();

        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var navWidth = available > 900f ? 190f : 165f;
        var inspectorWidth = available > 1050f ? 300f : 260f;

        DrawAnimationNavigation(settings, navWidth);
        ImGui.SameLine();
        DrawAnimationResults(settings, MathF.Max(280f, available - navWidth - inspectorWidth - 16f));
        ImGui.SameLine();
        DrawAnimationInspectorPanel(settings);
    }

    private void DrawAnimationLibraryHeader(ForgePremiumSettings settings)
    {
        var theme = plugin.CurrentTheme;
        if (ImGui.BeginChild("##forge-animation-header", new Vector2(0f, 150f), true))
        {
            UiStyles.SectionLabel("Penumbra library", theme);
            ImGui.TextDisabled(settings.AnimationLibraryEntries.Count == 0
                ? "Choose the folder where Penumbra stores your installed mods."
                : $"Connected through Penumbra · {settings.AnimationLibraryEntries.Count:N0} animation mods");

            var path = settings.AnimationLibraryRootPath;
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            const float browseWidth = 92f;
            const float scanWidth = 110f;
            ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - browseWidth - scanWidth - (spacing * 2f)));
            ImGui.InputTextWithHint("##forge-animation-root", "No Penumbra folder selected", ref path, 1024, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("BROWSE", new Vector2(browseWidth, 0f)))
            {
                var startPath = Directory.Exists(settings.AnimationLibraryRootPath) ? settings.AnimationLibraryRootPath : null;
                voicePackFileDialog.OpenFolderDialog(
                    "Select your Penumbra mods folder",
                    (success, selectedPath) =>
                    {
                        if (!success || string.IsNullOrWhiteSpace(selectedPath))
                            return;
                        settings.AnimationLibraryRootPath = selectedPath;
                        plugin.SaveConfiguration();
                        RefreshAnimationLibrary(settings);
                    },
                    startPath);
            }

            ImGui.SameLine();
            var canScan = Directory.Exists(settings.AnimationLibraryRootPath);
            if (!canScan) ImGui.BeginDisabled();
            if (ImGui.Button(settings.AnimationLibraryEntries.Count == 0 ? "SCAN" : "RESCAN", new Vector2(scanWidth, 0f)))
                RefreshAnimationLibrary(settings);
            if (!canScan) ImGui.EndDisabled();

            if (!string.IsNullOrWhiteSpace(forgeAnimationStatus))
                ImGui.TextDisabled(forgeAnimationStatus);
            else
                ImGui.TextDisabled("RE:Forge asks Penumbra for its registered mod list. Animation files never become library entries.");

            var showMature = settings.AnimationLibraryShowMature;
            if (ImGui.Checkbox("Show mature animations##forge-animation-mature-opt-in", ref showMature))
            {
                settings.AnimationLibraryShowMature = showMature;
                plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
    }

    private void RefreshAnimationLibrary(ForgePremiumSettings settings)
    {
        var result = ForgeAnimationLibraryService.Scan(settings.AnimationLibraryRootPath, settings.AnimationLibraryEntries);
        forgeAnimationStatus = result.Message;
        if (!result.Succeeded)
            return;

        settings.AnimationLibraryEntries = result.Entries.ToList();
        settings.EnsureValid();
        if (settings.AnimationLibraryEntries.All(item => !string.Equals(item.Id, forgeAnimationSelectedId, StringComparison.OrdinalIgnoreCase)))
            forgeAnimationSelectedId = string.Empty;
        plugin.SaveConfiguration();
    }

    private void DrawAnimationNavigation(ForgePremiumSettings settings, float width)
    {
        if (ImGui.BeginChild("##forge-animation-nav", new Vector2(width, 0f), true))
        {
            UiStyles.SectionLabel("Library", plugin.CurrentTheme);
            DrawAnimationFilterButton("All", null);
            DrawAnimationFilterButton("Favorites", null, favorites: true);
            DrawAnimationFilterButton("Recent", null, recent: true);

            ImGui.Spacing();
            UiStyles.Divider(plugin.CurrentTheme);
            ImGui.Spacing();
            UiStyles.SectionLabel("Categories", plugin.CurrentTheme);
            foreach (var category in Enum.GetValues<ForgeAnimationCategory>())
                DrawAnimationFilterButton(AnimationCategoryLabel(category), category);

            ImGui.Spacing();
            UiStyles.Divider(plugin.CurrentTheme);
            ImGui.Spacing();
            ImGui.TextWrapped("One installed Penumbra mod appears once in this list.");
            ImGui.Spacing();
            ImGui.TextDisabled("Only the mod name is shown. Individual animation files and option folders stay hidden.");
        }
        ImGui.EndChild();
    }

    private void DrawAnimationResults(ForgePremiumSettings settings, float width)
    {
        if (ImGui.BeginChild("##forge-animation-results", new Vector2(width, 0f), true))
        {
            var search = forgeAnimationSearch;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##forge-animation-search", "Search your animation library...", ref search, 160))
                forgeAnimationSearch = search;
            ImGui.Spacing();

            var query = settings.AnimationLibraryEntries
                .Where(item => !item.Hidden)
                .Where(item => settings.AnimationLibraryShowMature || !item.Mature)
                .Where(item => !forgeAnimationFavoritesOnly || item.Favorite)
                .Where(item => !forgeAnimationRecentOnly || item.TimesUsed > 0)
                .Where(item => forgeAnimationCategory is null || item.Category == forgeAnimationCategory)
                .Where(AnimationMatchesSearch)
                .OrderByDescending(item => item.Favorite)
                .ThenByDescending(item => forgeAnimationRecentOnly ? item.LastUsedUtc : DateTime.MinValue)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var context = forgeAnimationFavoritesOnly ? "Favorites" : forgeAnimationRecentOnly ? "Recently played" : "Animation mods";
            ImGui.TextUnformatted(context);
            ImGui.SameLine();
            ImGui.TextDisabled($"{query.Count:N0}");
            UiStyles.Divider(plugin.CurrentTheme);
            ImGui.Spacing();

            if (query.Count == 0)
            {
                ImGui.TextWrapped(settings.AnimationLibraryEntries.Count == 0
                    ? "Your library is empty. Make sure Penumbra is enabled, select its mod root, and scan again."
                    : "Nothing matches this search or filter.");
            }
            else
            {
                foreach (var item in query)
                    DrawAnimationLibraryRow(item);
            }
        }
        ImGui.EndChild();
    }

    private void DrawAnimationLibraryRow(ForgeAnimationEntry item)
    {
        var selected = string.Equals(forgeAnimationSelectedId, item.Id, StringComparison.OrdinalIgnoreCase);
        ImGui.PushID(item.Id);

        var start = ImGui.GetCursorScreenPos();
        if (ImGui.Selectable("##animation-card", selected, ImGuiSelectableFlags.None, new Vector2(0f, 42f)))
            forgeAnimationSelectedId = item.Id;

        var textStart = start + new Vector2(12f, 12f);
        var creatorSuffix = string.IsNullOrWhiteSpace(item.Creator) ? string.Empty : $" - {item.Creator}";
        var displayName = $"{(item.Favorite ? "★  " : string.Empty)}{item.Name}{creatorSuffix}";
        ImGui.GetWindowDrawList().AddText(textStart, ImGui.GetColorU32(ImGuiCol.Text), displayName);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(displayName);
        ImGui.PopID();
    }

    private void DrawAnimationInspectorPanel(ForgePremiumSettings settings)
    {
        if (ImGui.BeginChild("##forge-animation-inspector", Vector2.Zero, true))
        {
            var selected = settings.AnimationLibraryEntries.FirstOrDefault(item => string.Equals(item.Id, forgeAnimationSelectedId, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                UiStyles.SectionLabel("Now selected", plugin.CurrentTheme);
                ImGui.TextWrapped("Choose an animation to view it, favorite it, rename it, or assign a launch command.");
            }
            else
            {
                DrawForgeAnimationInspector(settings, selected);
            }
        }
        ImGui.EndChild();
    }

    private void DrawAnimationFilterButton(string label, ForgeAnimationCategory? category, bool favorites = false, bool recent = false)
    {
        var selected = favorites ? forgeAnimationFavoritesOnly : recent ? forgeAnimationRecentOnly : !forgeAnimationFavoritesOnly && !forgeAnimationRecentOnly && forgeAnimationCategory == category;
        if (UiStyles.NavButton($"{label}##animation-filter-{label}", selected, plugin.CurrentTheme, -1f))
        {
            forgeAnimationFavoritesOnly = favorites;
            forgeAnimationRecentOnly = recent;
            forgeAnimationCategory = favorites || recent ? null : category;
        }
    }

    private bool AnimationMatchesSearch(ForgeAnimationEntry item)
    {
        if (string.IsNullOrWhiteSpace(forgeAnimationSearch))
            return true;
        var search = forgeAnimationSearch.Trim();
        return item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               item.Creator.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               item.Pack.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawForgeAnimationInspector(ForgePremiumSettings settings, ForgeAnimationEntry item)
    {
        var theme = plugin.CurrentTheme;
        var changed = false;

        UiStyles.SectionLabel("Penumbra mod", theme);
        var name = item.Name;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##animation-name", ref name, 120)) { item.Name = name; changed = true; }
        if (!string.IsNullOrWhiteSpace(item.Creator))
        {
            ImGui.TextDisabled($"by {item.Creator}");
            ImGui.Spacing();
        }

        var favoriteLabel = item.Favorite ? "★ FAVORITED" : "☆ ADD TO FAVORITES";
        if (ImGui.Button(favoriteLabel, new Vector2(-1f, 34f)))
        {
            item.Favorite = !item.Favorite;
            changed = true;
        }

        var actionWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        if (ImGui.Button("ENABLE MOD", new Vector2(actionWidth, 34f)))
            ForgeAnimationLibraryService.TryEnableForCurrentCharacter(item, out forgeAnimationStatus);
        ImGui.SameLine();
        if (ImGui.Button("OPEN IN PENUMBRA", new Vector2(actionWidth, 34f)))
            ForgeAnimationLibraryService.TryOpenInPenumbra(item, out forgeAnimationStatus);

        ImGui.Spacing();
        var canLaunch = !string.IsNullOrWhiteSpace(item.TriggerCommand);
        if (!canLaunch) ImGui.BeginDisabled();
        if (ImGui.Button("PLAY", new Vector2(-1f, 40f)))
        {
            plugin.RunIntegrationCommand(item.TriggerCommand, item.Name);
            item.TimesUsed++;
            item.LastUsedUtc = DateTime.UtcNow;
            changed = true;
        }
        if (!canLaunch) ImGui.EndDisabled();
        ImGui.TextDisabled(canLaunch ? (item.TimesUsed == 0 ? $"Ready: {item.TriggerCommand}" : $"Played {item.TimesUsed:N0} time{(item.TimesUsed == 1 ? string.Empty : "s")}") : "Command was not detected; add it under More");

        ImGui.Spacing();
        if (ImGui.Button(forgeAnimationShowAdvanced ? "LESS" : "MORE", new Vector2(-1f, 30f)))
            forgeAnimationShowAdvanced = !forgeAnimationShowAdvanced;

        if (forgeAnimationShowAdvanced)
        {
            ImGui.Spacing();
            UiStyles.Divider(theme);
            ImGui.Spacing();

            UiStyles.SectionLabel("Launch command", theme);
            ImGui.TextWrapped("The command RE:Forge runs when you press Play.");
            var command = item.TriggerCommand;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##animation-command", "/emote or companion-plugin command", ref command, 200))
            {
                item.TriggerCommand = command;
                changed = true;
            }

            ImGui.Spacing();
            UiStyles.SectionLabel("Organization", theme);

            ImGui.TextUnformatted("Category");
            var category = item.Category;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("##animation-category", AnimationCategoryLabel(category)))
            {
                foreach (var option in Enum.GetValues<ForgeAnimationCategory>())
                {
                    if (!ImGui.Selectable(AnimationCategoryLabel(option), option == category))
                        continue;
                    item.Category = option;
                    item.CategoryAutoAssigned = false;
                    item.CategoryReason = "Assigned by user";
                    changed = true;
                }
                ImGui.EndCombo();
            }

            ImGui.TextDisabled(item.CategoryAutoAssigned
                ? item.CategoryReason
                : "Manual category override");
            if (ImGui.Button("AUTO-CATEGORIZE", new Vector2(-1f, 28f)))
            {
                ForgeAnimationLibraryService.AutoCategorize(item);
                changed = true;
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Tags");
            ImGui.TextWrapped("Separate custom tags with commas.");
            var tags = string.Join(", ", item.Tags);
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##animation-tags", "dance, looping, couple", ref tags, 300))
            {
                item.Tags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                changed = true;
            }

            ImGui.Spacing();
            var mature = item.Mature;
            if (ImGui.Checkbox("Contains mature content##animation-mature", ref mature))
            {
                item.Mature = mature;
                if (mature) item.Category = ForgeAnimationCategory.Mature;
                item.CategoryAutoAssigned = false;
                item.CategoryReason = "Assigned by user";
                changed = true;
            }

            ImGui.Spacing();
            UiStyles.SectionLabel("Source", theme);
            ImGui.TextWrapped(item.SourcePath);
            if (!Directory.Exists(item.SourcePath) && !File.Exists(item.SourcePath))
                ImGui.TextDisabled("The indexed mod folder is currently missing.");

            ImGui.Spacing();
            if (ImGui.Button("REMOVE FROM LIBRARY", new Vector2(-1f, 30f)))
            {
                settings.AnimationLibraryEntries.Remove(item);
                forgeAnimationSelectedId = string.Empty;
                plugin.SaveConfiguration();
                return;
            }
        }

        if (changed)
        {
            item.EnsureValid();
            plugin.SaveConfiguration();
        }
    }

    private static string AnimationCategoryLabel(ForgeAnimationCategory category) => category switch
    {
        ForgeAnimationCategory.Dote => "Dote",
        ForgeAnimationCategory.Idle => "Idle",
        ForgeAnimationCategory.Job => "Job",
        ForgeAnimationCategory.Mature => "Mature",
        ForgeAnimationCategory.Music => "Music",
        ForgeAnimationCategory.Teleport => "Teleport",
        ForgeAnimationCategory.Dance => "Dance",
        _ => "Unsorted",
    };
}
