using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.Theme;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed partial class MainWindow
{
    private ForgeCreatePage forgeCreatePage = ForgeCreatePage.ThemeStudio;
    private ForgeImmersionPage forgeImmersionPage = ForgeImmersionPage.AfkDirector;
    private ForgeAutomationPage forgeAutomationPage = ForgeAutomationPage.SceneAutomation;
    private ForgeSharePage forgeSharePage = ForgeSharePage.Workshop;
    private ForgeMembershipPage forgeMembershipPage = ForgeMembershipPage.PreviewLab;

    private string forgeSelectedAfkSceneId = string.Empty;
    private string forgeNewAfkSceneName = "My AFK Scene";
    private string forgeSelectedAutomationRuleId = string.Empty;
    private string forgeNewAutomationRuleName = "New Scene Rule";
    private string forgeNewDisplayProfileName = "Current Display";
    private string forgeFusionCode = string.Empty;
    private string forgeFusionStatus = string.Empty;
    private bool forgeFusionLayouts = true;
    private bool forgeFusionVisibility = true;
    private bool forgeFusionAppearance;
    private bool forgeFusionNativePlacements;
    private string forgeVaultSnapshotName = "My RE:Frame";
    private string forgeVaultStatus = string.Empty;
    private IReadOnlyList<ForgeVaultSnapshotInfo> forgeCloudSnapshots = Array.Empty<ForgeVaultSnapshotInfo>();
    private Task<(IReadOnlyList<ForgeVaultSnapshotInfo> Snapshots, ForgeVaultResult Result)>? forgeVaultListTask;
    private Task<ForgeVaultResult>? forgeVaultActionTask;
    private readonly CancellationTokenSource forgeVaultCancellation = new();

    private void DrawForgeOverview(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        var premium = configuration.ForgePremium;
        premium.EnsureValid();

        UiStyles.SectionLabel("Your RE:Forge suite", theme);
        ImGui.TextWrapped("Everything included with RE:Forge is organized below. Each service opens into a dedicated workspace instead of being buried inside one long settings page.");
        ImGui.Spacing();

        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var columns = width >= 900f ? 3 : width >= 620f ? 2 : 1;
        var cardWidth = MathF.Max(210f, (width - gap * (columns - 1)) / columns);
        var cards = new (string Title, string Subtitle, string Status, Vector4 Accent, Action Open)[]
        {
            ("THEME STUDIO", "Full-palette themes, materials, gradients, and live HUD previews.", $"{configuration.ForgeThemes.Count} custom theme{(configuration.ForgeThemes.Count == 1 ? string.Empty : "s")}", theme.AccentStrong, () => { forgePage = ForgePage.Create; forgeCreatePage = ForgeCreatePage.ThemeStudio; }),
            ("MAP STUDIO", "Square navigation, map framing, follow behavior, and presentation controls.", configuration.ForgeSquareMinimapEnabled ? "Square map active" : "Ready to configure", theme.Success, () => { forgePage = ForgePage.Create; forgeCreatePage = ForgeCreatePage.MapStudio; }),
            ("AFK DIRECTOR", "Build multiple still-image scenes with individual music, timing, and status cards.", $"{premium.AfkScenes.Count} scene{(premium.AfkScenes.Count == 1 ? string.Empty : "s")}", theme.AccentMid, () => { forgePage = ForgePage.Immersion; forgeImmersionPage = ForgeImmersionPage.AfkDirector; }),
            ("VOICE DIRECTOR", "Premium voice packs, quiet hours, cooldowns, and mode-directed voices.", GreetingVoicePackService.GetDisplayName(configuration, configuration.ActiveGreetingVoicePackId), theme.Warning, () => { forgePage = ForgePage.Immersion; forgeImmersionPage = ForgeImmersionPage.VoiceDirector; }),
            ("RE:FORGE+", "Motion Library, Scene Builder, Dancer, character and venue presets, scheduling, wheel, and roulette.", plugin.ForgeAccess.HasPlusAccess ? "Forge+ active" : "$5.99 creative tier", theme.AccentStrong, () => forgePage = ForgePage.ForgePlus),
            ("SCENE AUTOMATION", "Switch modes, Forge themes, and display profiles from player-state rules.", premium.SceneAutomationEnabled ? $"{premium.AutomationRules.Count(rule => rule.Enabled)} rules enabled" : "Automation paused", theme.Danger, () => { forgePage = ForgePage.Automation; forgeAutomationPage = ForgeAutomationPage.SceneAutomation; }),
            ("DISPLAY LAB", "Capture monitor environments and switch presentation scales automatically.", $"{premium.DisplayProfiles.Count} display profile{(premium.DisplayProfiles.Count == 1 ? string.Empty : "s")}", theme.AccentStrong, () => { forgePage = ForgePage.Automation; forgeAutomationPage = ForgeAutomationPage.DisplayLab; }),
            ("WORKSHOP", "Curated RE:Forge collections, private sharing, and member creations.", $"{premium.WorkshopFavorites.Count} favorite{(premium.WorkshopFavorites.Count == 1 ? string.Empty : "s")}", theme.Success, () => { forgePage = ForgePage.Share; forgeSharePage = ForgeSharePage.Workshop; }),
            ("PROFILE FUSION", "Import only the layout, visibility, appearance, or native placements you choose.", "Selective RF3 merge", theme.AccentMid, () => { forgePage = ForgePage.Share; forgeSharePage = ForgeSharePage.ProfileFusion; }),
            ("CLOUD VAULT", "Compressed account-linked snapshots, local restore points, and automatic protection.", premium.VaultAutomaticSnapshots ? "Automatic snapshots on" : "Manual snapshots", theme.Warning, () => forgePage = ForgePage.Vault),
        };

        for (var index = 0; index < cards.Length; index++)
        {
            var card = cards[index];
            DrawForgeLaunchCard(card.Title, card.Subtitle, card.Status, card.Accent, cardWidth, card.Open);
            if ((index + 1) % columns != 0 && index < cards.Length - 1)
                ImGui.SameLine();
        }

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();
        DrawForgeAtmosphere();
    }

    private void DrawForgeLaunchCard(
        string title,
        string subtitle,
        string status,
        Vector4 accent,
        float width,
        Action open)
    {
        ImGui.PushID($"forge-launch-{title}");
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.54f));
        if (ImGui.BeginChild("##launch-card", new Vector2(width, 142f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, accent);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            ImGui.TextWrapped(subtitle);
            ImGui.SetCursorPosY(MathF.Max(ImGui.GetCursorPosY(), 92f));
            ImGui.TextDisabled(status);
            ImGui.SameLine();
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - 78f));
            if (ImGui.SmallButton("OPEN"))
                open();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();
    }

    private void DrawForgeCreate(Configuration configuration)
    {
        DrawForgeServiceTabs(
            ref forgeCreatePage,
            (ForgeCreatePage.ThemeStudio, "THEME STUDIO", "Palette · materials · gradients"),
            (ForgeCreatePage.MapStudio, "MAP STUDIO", "Square map · navigation"),
            (ForgeCreatePage.DockStudio, "DOCK STUDIO", "Dock identity · presentation"));
        ImGui.Spacing();

        switch (forgeCreatePage)
        {
            case ForgeCreatePage.MapStudio:
                DrawForgeMapStudio(configuration);
                break;
            case ForgeCreatePage.DockStudio:
                DrawForgeDockStudio(configuration);
                break;
            case ForgeCreatePage.ThemeStudio:
            default:
                DrawForgePaletteWorkspace(configuration);
                break;
        }
    }

    private void DrawForgeMapStudio(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Map Studio", theme);
        ImGui.TextWrapped("Turn FFXIV's live map surface into a clean contained square navigation instrument, with coordinates positioned independently in Layout Studio.");
        ImGui.Spacing();

        if (ImGui.BeginChild("##forge-map-studio", new Vector2(0f, 390f), true))
            DrawForgeNavigation(configuration);
        ImGui.EndChild();

        ImGui.Spacing();
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var cardWidth = MathF.Max(220f, (width - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f);
        DrawForgeMiniFeature("CONTAINED", "The live AreaMap remains clipped behind the RE:Frame-owned frame.", theme.AccentStrong, cardWidth);
        ImGui.SameLine();
        DrawForgeMiniFeature("MODE AWARE", "Map visibility still follows the active HUD mode and your layout profile.", theme.Success, cardWidth);
        ImGui.SameLine();
        DrawForgeMiniFeature("NATIVE DATA", "Coordinates, map movement, and markers remain sourced from FFXIV.", theme.Warning, cardWidth);
    }

    private void DrawForgeDockStudio(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Dock Studio", theme);
        ImGui.TextWrapped("Build the buttons and ordering in the normal Dock editor, then use Theme Studio to forge the dock surface, borders, dividers, labels, hover states, and active states as one coordinated identity.");
        ImGui.Spacing();

        if (ImGui.Button("OPEN DOCK CONTENT EDITOR", new Vector2(250f, 38f)))
            page = MainPage.Docks;
        ImGui.SameLine();
        if (ImGui.Button("OPEN DOCK PALETTE", new Vector2(220f, 38f)))
        {
            forgeCreatePage = ForgeCreatePage.ThemeStudio;
            var selected = ResolveForgeSelection(configuration);
            if (selected is not null)
                forgeSelectedThemeId = selected.Id;
        }

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Quick material forge", theme);
        ImGui.TextDisabled("Apply a coordinated surface recipe to the selected Forge theme, including its dock presentation.");
        var selectedTheme = ResolveForgeSelection(configuration);
        if (selectedTheme is null)
        {
            ImGui.TextWrapped("Create or import a Forge theme in Theme Studio before applying a material recipe.");
            return;
        }

        if (ImGui.Button("GLASS", new Vector2(120f, 34f)))
        {
            ApplyForgeMaterial(selectedTheme, ForgeMaterialRecipe.Glass);
            plugin.SaveConfiguration();
        }
        ImGui.SameLine();
        if (ImGui.Button("MATTE", new Vector2(120f, 34f)))
        {
            ApplyForgeMaterial(selectedTheme, ForgeMaterialRecipe.Matte);
            plugin.SaveConfiguration();
        }
        ImGui.SameLine();
        if (ImGui.Button("LACQUER", new Vector2(120f, 34f)))
        {
            ApplyForgeMaterial(selectedTheme, ForgeMaterialRecipe.Lacquer);
            plugin.SaveConfiguration();
        }
        ImGui.SameLine();
        if (ImGui.Button("NEON", new Vector2(120f, 34f)))
        {
            ApplyForgeMaterial(selectedTheme, ForgeMaterialRecipe.Neon);
            plugin.SaveConfiguration();
        }
        ImGui.SameLine();
        if (ImGui.Button("SOFT", new Vector2(120f, 34f)))
        {
            ApplyForgeMaterial(selectedTheme, ForgeMaterialRecipe.Soft);
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        DrawForgePreview(selectedTheme.ToPalette(), selectedTheme.Style, $"{selectedTheme.Name} · DOCK IDENTITY");
    }

    private void DrawForgeImmersionWorkspace(Configuration configuration)
    {
        DrawForgeServiceTabs(
            ref forgeImmersionPage,
            (ForgeImmersionPage.AfkDirector, "AFK DIRECTOR", "Scenes · artwork · music"),
            (ForgeImmersionPage.VoiceDirector, "VOICE DIRECTOR", "Packs · quiet hours · rules"));
        ImGui.Spacing();

        if (forgeImmersionPage == ForgeImmersionPage.VoiceDirector)
            DrawForgeVoiceDirector(configuration);
        else
            DrawForgeAfkDirector(configuration);
    }

    private void DrawForgeAfkDirector(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        var settings = configuration.ForgePremium;
        settings.EnsureValid();
        if (string.IsNullOrWhiteSpace(forgeSelectedAfkSceneId))
            forgeSelectedAfkSceneId = settings.ActiveAfkSceneId;

        UiStyles.SectionLabel("AFK Director", theme);
        ImGui.TextWrapped("Create a rotating collection of full-screen AFK scenes. Each scene can use its own still artwork, PCM WAV ambience, duration, dim level, and status message.");
        ImGui.Spacing();

        var changed = false;
        var rotate = settings.RotateAfkScenes;
        if (ImGui.Checkbox("Rotate enabled AFK scenes##forge-afk-rotate", ref rotate))
        {
            settings.RotateAfkScenes = rotate;
            changed = true;
        }
        ImGui.SameLine();
        var streamSafe = settings.AfkStreamSafe;
        if (ImGui.Checkbox("Stream-safe identity##forge-afk-stream-safe", ref streamSafe))
        {
            settings.AfkStreamSafe = streamSafe;
            changed = true;
        }
        ImGui.SameLine();
        var showStatus = settings.AfkShowStatusText;
        if (ImGui.Checkbox("Show status card##forge-afk-status", ref showStatus))
        {
            settings.AfkShowStatusText = showStatus;
            changed = true;
        }
        if (!settings.AfkStreamSafe)
        {
            ImGui.SameLine();
            var showName = settings.AfkShowCharacterName;
            if (ImGui.Checkbox("Show character name##forge-afk-name", ref showName))
            {
                settings.AfkShowCharacterName = showName;
                changed = true;
            }
        }

        if (changed)
        {
            plugin.SaveConfiguration();
            plugin.RefreshAfkPresentationAssets();
        }

        ImGui.Spacing();
        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var listWidth = available >= 840f ? 270f : MathF.Min(235f, available * 0.32f);
        if (ImGui.BeginChild("##forge-afk-scenes", new Vector2(listWidth, 430f), true))
        {
            UiStyles.SectionLabel("Scene library", theme);
            foreach (var scene in settings.AfkScenes.ToArray())
            {
                var selected = string.Equals(forgeSelectedAfkSceneId, scene.Id, StringComparison.OrdinalIgnoreCase);
                var active = string.Equals(settings.ActiveAfkSceneId, scene.Id, StringComparison.OrdinalIgnoreCase);
                var label = active ? $"◆ {scene.Name}" : scene.Enabled ? scene.Name : $"○ {scene.Name}";
                if (ImGui.Selectable($"{label}##afk-scene-{scene.Id}", selected))
                    forgeSelectedAfkSceneId = scene.Id;
            }

            ImGui.Spacing();
            UiStyles.Divider(theme);
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##new-afk-scene-name", ref forgeNewAfkSceneName, 60);
            if (ImGui.Button("+ CREATE SCENE", new Vector2(-1f, 34f)))
            {
                var scene = new ForgeAfkScene
                {
                    Name = string.IsNullOrWhiteSpace(forgeNewAfkSceneName) ? "My AFK Scene" : forgeNewAfkSceneName.Trim(),
                };
                scene.EnsureValid();
                settings.AfkScenes.Add(scene);
                settings.ActiveAfkSceneId = scene.Id;
                forgeSelectedAfkSceneId = scene.Id;
                forgeNewAfkSceneName = "My AFK Scene";
                plugin.SaveConfiguration();
                plugin.RefreshAfkPresentationAssets();
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##forge-afk-scene-editor", new Vector2(0f, 430f), true))
        {
            var scene = settings.AfkScenes.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, forgeSelectedAfkSceneId, StringComparison.OrdinalIgnoreCase));
            if (scene is null)
            {
                ImGui.TextWrapped("Choose an AFK scene from the library.");
            }
            else
            {
                DrawForgeAfkSceneEditor(settings, scene);
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();
        DrawForgeAfkPersonalization(configuration);
    }

    private void DrawForgeAfkSceneEditor(ForgePremiumSettings settings, ForgeAfkScene scene)
    {
        var theme = plugin.CurrentTheme;
        var changed = false;
        UiStyles.SectionLabel("Scene workbench", theme);

        var name = scene.Name;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##afk-scene-name", ref name, 60))
        {
            scene.Name = name;
            changed = true;
        }

        var enabled = scene.Enabled;
        if (ImGui.Checkbox("Include this scene##afk-scene-enabled", ref enabled))
        {
            scene.Enabled = enabled;
            changed = true;
        }
        ImGui.SameLine();
        var active = string.Equals(settings.ActiveAfkSceneId, scene.Id, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Button(active ? "◆ ACTIVE SCENE" : "MAKE ACTIVE", new Vector2(150f, 30f)) && !active)
        {
            settings.ActiveAfkSceneId = scene.Id;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Artwork");
        var artwork = scene.ArtworkPath;
        ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - 110f));
        ImGui.InputText("##afk-artwork-path", ref artwork, 1024, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("BROWSE##afk-artwork", new Vector2(96f, 0f)))
        {
            var sceneId = scene.Id;
            voicePackFileDialog.OpenFileDialog(
                "Select AFK scene artwork",
                "Images{.png,.jpg,.jpeg}",
                (success, path) =>
                {
                    if (!success || string.IsNullOrWhiteSpace(path))
                        return;
                    var target = plugin.Configuration.ForgePremium.AfkScenes.FirstOrDefault(item => item.Id == sceneId);
                    if (target is null)
                        return;
                    target.ArtworkPath = path;
                    plugin.SaveConfiguration();
                    plugin.RefreshAfkPresentationAssets();
                });
        }
        if (!string.IsNullOrWhiteSpace(scene.ArtworkPath))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("CLEAR##afk-artwork-clear"))
            {
                scene.ArtworkPath = string.Empty;
                changed = true;
            }
        }

        ImGui.TextUnformatted("Scene audio");
        var audio = scene.AudioPath;
        ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - 110f));
        ImGui.InputText("##afk-audio-path", ref audio, 1024, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("BROWSE##afk-audio", new Vector2(96f, 0f)))
        {
            var sceneId = scene.Id;
            voicePackFileDialog.OpenFileDialog(
                "Select AFK scene audio",
                "Wave audio{.wav}",
                (success, path) =>
                {
                    if (!success || string.IsNullOrWhiteSpace(path))
                        return;
                    var target = plugin.Configuration.ForgePremium.AfkScenes.FirstOrDefault(item => item.Id == sceneId);
                    if (target is null)
                        return;
                    target.AudioPath = path;
                    plugin.SaveConfiguration();
                    plugin.RefreshAfkPresentationAssets();
                });
        }
        if (!string.IsNullOrWhiteSpace(scene.AudioPath))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("CLEAR##afk-audio-clear"))
            {
                scene.AudioPath = string.Empty;
                changed = true;
            }
        }

        ImGui.TextUnformatted("Status message");
        var status = scene.StatusText;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##afk-scene-status", ref status, 120))
        {
            scene.StatusText = status;
            changed = true;
        }

        var duration = scene.DurationSeconds;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt("Scene duration##afk-duration", ref duration, 10, 300, "%d seconds"))
        {
            scene.DurationSeconds = duration;
            changed = true;
        }
        var dim = scene.DimAmount;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("Artwork dim##afk-dim", ref dim, 0f, 0.85f, "%.2f"))
        {
            scene.DimAmount = dim;
            changed = true;
        }

        if (changed)
        {
            scene.EnsureValid();
            plugin.SaveConfiguration();
            plugin.RefreshAfkPresentationAssets();
        }

        ImGui.Spacing();
        if (ImGui.Button(plugin.AfkScreen.IsActive ? "STOP PREVIEW" : "PREVIEW THIS SCENE", new Vector2(190f, 34f)))
        {
            settings.ActiveAfkSceneId = scene.Id;
            plugin.SaveConfiguration();
            plugin.RefreshAfkPresentationAssets();
            plugin.ToggleAfkScreenPreview();
        }
        if (settings.AfkScenes.Count > 1)
        {
            ImGui.SameLine();
            if (ImGui.Button("DELETE SCENE", new Vector2(140f, 34f)))
            {
                settings.AfkScenes.Remove(scene);
                settings.EnsureValid();
                forgeSelectedAfkSceneId = settings.ActiveAfkSceneId;
                plugin.SaveConfiguration();
                plugin.RefreshAfkPresentationAssets();
            }
        }
    }

    private void DrawForgeVoiceDirector(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        var settings = configuration.ForgePremium;
        settings.EnsureValid();

        UiStyles.SectionLabel("Voice Director", theme);
        ImGui.TextWrapped("Direct how premium and custom greeting voices behave before choosing or importing packs below.");
        ImGui.Spacing();

        var changed = false;
        var enabled = settings.VoiceDirectorEnabled;
        if (ImGui.Checkbox("Enable Voice Director##voice-director-enabled", ref enabled))
        {
            settings.VoiceDirectorEnabled = enabled;
            changed = true;
        }
        var cooldown = settings.VoiceCooldownMinutes;
        ImGui.SetNextItemWidth(MathF.Min(420f, ImGui.GetContentRegionAvail().X));
        if (ImGui.SliderInt("Greeting cooldown##voice-cooldown", ref cooldown, 0, 120, "%d minutes"))
        {
            settings.VoiceCooldownMinutes = cooldown;
            changed = true;
        }

        var quiet = settings.VoiceQuietHoursEnabled;
        if (ImGui.Checkbox("Use quiet hours##voice-quiet-enabled", ref quiet))
        {
            settings.VoiceQuietHoursEnabled = quiet;
            changed = true;
        }
        if (settings.VoiceQuietHoursEnabled)
        {
            var start = settings.VoiceQuietHoursStart;
            var end = settings.VoiceQuietHoursEnd;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderInt("Quiet begins##voice-quiet-start", ref start, 0, 23, "%02d:00"))
            {
                settings.VoiceQuietHoursStart = start;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderInt("Quiet ends##voice-quiet-end", ref end, 0, 23, "%02d:00"))
            {
                settings.VoiceQuietHoursEnd = end;
                changed = true;
            }
        }

        ImGui.TextDisabled("Greeting lines rotate sequentially so a pack does not repeat the same line immediately.");

        var directedPack = settings.VoicePackByMode.TryGetValue(nameof(UiMode.Leisure), out var storedPack)
            ? storedPack
            : configuration.ActiveGreetingVoicePackId;
        var directedName = GreetingVoicePackService.GetDisplayName(configuration, directedPack);
        ImGui.TextUnformatted("Leisure greeting voice");
        ImGui.SetNextItemWidth(MathF.Min(460f, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##voice-director-leisure-pack", directedName))
        {
            foreach (var (id, name) in EnumerateVoicePacks(configuration))
            {
                var selected = string.Equals(directedPack, id, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(name, selected))
                {
                    settings.VoicePackByMode[nameof(UiMode.Leisure)] = id;
                    configuration.ActiveGreetingVoicePackId = id;
                    changed = true;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (changed)
        {
            settings.EnsureValid();
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        DrawForgeFeatureCard(
            "PREMIUM VOICE LIBRARY",
            "Member voice packs appear in this director beside imported packs. Original and licensed voices can be activated without changing the user's custom pack workflow.",
            theme.AccentStrong);
        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();
        DrawGreetingVoicePacks();
    }

    private static IEnumerable<(string Id, string Name)> EnumerateVoicePacks(Configuration configuration)
    {
        yield return (GreetingVoicePackService.JarvinPackId, GreetingVoicePackService.JarvinPackName);
        yield return (GreetingVoicePackService.RubyPackId, GreetingVoicePackService.RubyPackName);
        foreach (var pack in configuration.CustomGreetingVoicePacks.OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase))
            yield return (pack.Id, GreetingVoicePackService.GetDisplayName(configuration, pack.Id));
    }

    private void DrawForgeAutomationWorkspace(Configuration configuration)
    {
        DrawForgeServiceTabs(
            ref forgeAutomationPage,
            (ForgeAutomationPage.SceneAutomation, "SCENE AUTOMATION", "Rules · modes · themes"),
            (ForgeAutomationPage.DisplayLab, "DISPLAY LAB", "Resolution · scaling · devices"));
        ImGui.Spacing();

        if (forgeAutomationPage == ForgeAutomationPage.DisplayLab)
            DrawForgeDisplayLab(configuration);
        else
            DrawForgeSceneAutomation(configuration);
    }

    private void DrawForgeSceneAutomation(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        var settings = configuration.ForgePremium;
        settings.EnsureValid();
        var changed = false;

        UiStyles.SectionLabel("Scene Automation", theme);
        ImGui.TextWrapped("Create presentation rules that switch RE:Frame modes, Forge themes, and saved display environments when the matching player state becomes active.");
        ImGui.Spacing();

        var enabled = settings.SceneAutomationEnabled;
        if (ImGui.Checkbox("Enable Scene Automation##forge-scene-enabled", ref enabled))
        {
            settings.SceneAutomationEnabled = enabled;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(plugin.ForgeAutomation.ActiveRuleName)
            ? "No rule is active."
            : $"Active: {plugin.ForgeAutomation.ActiveRuleName}");

        ImGui.Spacing();
        ImGui.SetNextItemWidth(MathF.Max(180f, ImGui.GetContentRegionAvail().X - 150f));
        ImGui.InputText("##forge-new-rule", ref forgeNewAutomationRuleName, 60);
        ImGui.SameLine();
        if (ImGui.Button("ADD RULE", new Vector2(138f, 0f)))
        {
            var rule = new ForgeAutomationRule { Name = forgeNewAutomationRuleName };
            rule.EnsureValid();
            settings.AutomationRules.Add(rule);
            forgeSelectedAutomationRuleId = rule.Id;
            forgeNewAutomationRuleName = "New Scene Rule";
            changed = true;
        }

        if (settings.AutomationRules.Count == 0)
        {
            ImGui.Spacing();
            DrawForgeFeatureCard(
                "NO AUTOMATION RULES YET",
                "Add a rule for examples such as Savage Healer, RP Evening, Crafting Desk, or Controller Mode. Rules with the highest priority win when several conditions match.",
                theme.AccentStrong);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(forgeSelectedAutomationRuleId) ||
                settings.AutomationRules.All(rule => !string.Equals(rule.Id, forgeSelectedAutomationRuleId, StringComparison.OrdinalIgnoreCase)))
                forgeSelectedAutomationRuleId = settings.AutomationRules.OrderByDescending(rule => rule.Priority).First().Id;

            ImGui.Spacing();
            var listWidth = MathF.Min(270f, MathF.Max(210f, ImGui.GetContentRegionAvail().X * 0.30f));
            if (ImGui.BeginChild("##forge-rule-list", new Vector2(listWidth, 0f), true))
            {
                foreach (var rule in settings.AutomationRules.OrderByDescending(rule => rule.Priority).ThenBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var selected = string.Equals(rule.Id, forgeSelectedAutomationRuleId, StringComparison.OrdinalIgnoreCase);
                    var label = $"{(rule.Enabled ? "◆" : "◇")} {rule.Name}##rule-{rule.Id}";
                    if (ImGui.Selectable(label, selected))
                        forgeSelectedAutomationRuleId = rule.Id;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Priority {rule.Priority} · {DescribeAutomationRule(rule)}");
                }
            }
            ImGui.EndChild();
            ImGui.SameLine();

            var selectedRule = settings.AutomationRules.FirstOrDefault(rule =>
                string.Equals(rule.Id, forgeSelectedAutomationRuleId, StringComparison.OrdinalIgnoreCase));
            if (selectedRule is not null && ImGui.BeginChild("##forge-rule-editor", Vector2.Zero, true))
            {
                var name = selectedRule.Name;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText("##forge-rule-name", ref name, 60))
                {
                    selectedRule.Name = name;
                    changed = true;
                }

                var ruleEnabled = selectedRule.Enabled;
                if (ImGui.Checkbox("Rule enabled##forge-rule-enabled", ref ruleEnabled))
                {
                    selectedRule.Enabled = ruleEnabled;
                    changed = true;
                }
                ImGui.SameLine();
                var priority = selectedRule.Priority;
                ImGui.SetNextItemWidth(190f);
                if (ImGui.SliderInt("Priority##forge-rule-priority", ref priority, -100, 100))
                {
                    selectedRule.Priority = priority;
                    changed = true;
                }

                UiStyles.SectionLabel("Conditions", theme);
                var currentJob = string.IsNullOrWhiteSpace(selectedRule.JobAbbreviation) ? "Any job" : selectedRule.JobAbbreviation;
                ImGui.SetNextItemWidth(220f);
                if (ImGui.BeginCombo("Job##forge-rule-job", currentJob))
                {
                    if (ImGui.Selectable("Any job", string.IsNullOrWhiteSpace(selectedRule.JobAbbreviation)))
                    {
                        selectedRule.JobAbbreviation = string.Empty;
                        changed = true;
                    }
                    foreach (var job in JobThemeProvider.AllJobs)
                    {
                        if (ImGui.Selectable(job, string.Equals(selectedRule.JobAbbreviation, job, StringComparison.OrdinalIgnoreCase)))
                        {
                            selectedRule.JobAbbreviation = job;
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }

                var territory = selectedRule.TerritoryContains;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText("Territory contains##forge-rule-territory", ref territory, 80))
                {
                    selectedRule.TerritoryContains = territory;
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Optional text matched against the current territory name. Leave blank for any location.");

                var combatCondition = selectedRule.Combat;
                if (DrawRuleConditionCombo("Combat state##forge-rule-combat", ref combatCondition))
                {
                    selectedRule.Combat = combatCondition;
                    changed = true;
                }
                ImGui.SameLine();
                var partyCondition = selectedRule.Party;
                if (DrawRuleConditionCombo("Party state##forge-rule-party", ref partyCondition))
                {
                    selectedRule.Party = partyCondition;
                    changed = true;
                }

                var partySize = selectedRule.MinimumPartySize;
                ImGui.SetNextItemWidth(220f);
                if (ImGui.SliderInt("Minimum party size##forge-rule-party-size", ref partySize, 0, 24))
                {
                    selectedRule.MinimumPartySize = partySize;
                    changed = true;
                }

                var startHour = selectedRule.StartHour;
                var endHour = selectedRule.EndHour;
                ImGui.SetNextItemWidth(190f);
                if (ImGui.SliderInt("Starts##forge-rule-start", ref startHour, 0, 23, "%02d:00"))
                {
                    selectedRule.StartHour = startHour;
                    changed = true;
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(190f);
                if (ImGui.SliderInt("Ends##forge-rule-end", ref endHour, 1, 24, "%02d:00"))
                {
                    selectedRule.EndHour = endHour;
                    changed = true;
                }

                UiStyles.SectionLabel("Actions", theme);
                var targetMode = selectedRule.TargetMode;
                ImGui.SetNextItemWidth(250f);
                if (ImGui.BeginCombo("HUD mode##forge-rule-mode", targetMode.ToString()))
                {
                    foreach (var mode in Enum.GetValues<UiMode>())
                    {
                        if (ImGui.Selectable(mode.ToString(), targetMode == mode))
                        {
                            selectedRule.TargetMode = mode;
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }

                var selectedTheme = ForgeThemeLibrary.Find(configuration, selectedRule.TargetForgeThemeId);
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("Forge theme##forge-rule-theme", selectedTheme?.Name ?? "Keep current theme"))
                {
                    if (ImGui.Selectable("Keep current theme", string.IsNullOrWhiteSpace(selectedRule.TargetForgeThemeId)))
                    {
                        selectedRule.TargetForgeThemeId = string.Empty;
                        changed = true;
                    }
                    foreach (var forgeTheme in configuration.ForgeThemes.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (ImGui.Selectable(forgeTheme.Name, string.Equals(selectedRule.TargetForgeThemeId, forgeTheme.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            selectedRule.TargetForgeThemeId = forgeTheme.Id;
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }

                var displayProfile = settings.DisplayProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.Id, selectedRule.TargetDisplayProfileId, StringComparison.OrdinalIgnoreCase));
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("Display profile##forge-rule-display", displayProfile?.Name ?? "Keep current display profile"))
                {
                    if (ImGui.Selectable("Keep current display profile", string.IsNullOrWhiteSpace(selectedRule.TargetDisplayProfileId)))
                    {
                        selectedRule.TargetDisplayProfileId = string.Empty;
                        changed = true;
                    }
                    foreach (var profile in settings.DisplayProfiles.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (ImGui.Selectable(profile.Name, string.Equals(profile.Id, selectedRule.TargetDisplayProfileId, StringComparison.OrdinalIgnoreCase)))
                        {
                            selectedRule.TargetDisplayProfileId = profile.Id;
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }

                ImGui.Spacing();
                if (ImGui.Button("DELETE RULE", new Vector2(140f, 32f)))
                {
                    settings.AutomationRules.Remove(selectedRule);
                    forgeSelectedAutomationRuleId = settings.AutomationRules.FirstOrDefault()?.Id ?? string.Empty;
                    changed = true;
                }
            }
            ImGui.EndChild();
        }

        if (changed)
        {
            settings.EnsureValid();
            plugin.SaveConfiguration();
        }
    }

    private void DrawForgeDisplayLab(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        var settings = configuration.ForgePremium;
        settings.EnsureValid();
        var detected = DisplayResolutionService.Detect(HudCanvas.Current().Size);
        var changed = false;

        UiStyles.SectionLabel("Display Lab", theme);
        ImGui.TextWrapped("Capture the current game client as a reusable environment. RE:Frame can apply matching interface and text scales whenever that display returns.");
        ImGui.Spacing();

        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var cardWidth = MathF.Max(220f, (width - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f);
        DrawForgeMiniFeature("CLIENT", $"{detected.ClientWidth} × {detected.ClientHeight}", theme.AccentStrong, cardWidth);
        ImGui.SameLine();
        DrawForgeMiniFeature("MONITOR", $"{detected.MonitorWidth} × {detected.MonitorHeight}", theme.Success, cardWidth);
        ImGui.SameLine();
        DrawForgeMiniFeature("CURRENT SCALE", $"UI {configuration.InterfaceScale:0.00} · Text {configuration.TextScale:0.00}", theme.Warning, cardWidth);

        ImGui.Spacing();
        var auto = settings.AutoSwitchDisplayProfiles;
        if (ImGui.Checkbox("Automatically match saved display profiles##forge-display-auto", ref auto))
        {
            settings.AutoSwitchDisplayProfiles = auto;
            changed = true;
        }

        ImGui.SetNextItemWidth(MathF.Max(180f, ImGui.GetContentRegionAvail().X - 190f));
        ImGui.InputText("##forge-display-name", ref forgeNewDisplayProfileName, 60);
        ImGui.SameLine();
        if (ImGui.Button("CAPTURE DISPLAY", new Vector2(176f, 0f)))
        {
            var profile = new ForgeDisplayProfile
            {
                Name = forgeNewDisplayProfileName,
                ClientWidth = detected.ClientWidth,
                ClientHeight = detected.ClientHeight,
                InterfaceScale = configuration.InterfaceScale,
                TextScale = configuration.TextScale,
                AutoApply = true,
            };
            profile.EnsureValid();
            settings.DisplayProfiles.Add(profile);
            settings.ActiveDisplayProfileId = profile.Id;
            forgeNewDisplayProfileName = $"{detected.ClientWidth}×{detected.ClientHeight}";
            changed = true;
        }

        ImGui.Spacing();
        if (settings.DisplayProfiles.Count == 0)
        {
            ImGui.TextDisabled("No display profiles have been captured yet.");
        }
        else
        {
            string? deleteId = null;
            foreach (var profile in settings.DisplayProfiles.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                ImGui.PushID($"display-{profile.Id}");
                ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(theme.PanelAlt, 0.48f));
                if (ImGui.BeginChild("##display-card", new Vector2(0f, 102f), true, ImGuiWindowFlags.NoScrollbar))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, theme.AccentStrong);
                    ImGui.TextUnformatted(profile.Name);
                    ImGui.PopStyleColor();
                    ImGui.TextDisabled($"{profile.ClientWidth} × {profile.ClientHeight} · UI {profile.InterfaceScale:0.00} · Text {profile.TextScale:0.00}");

                    var profileAuto = profile.AutoApply;
                    if (ImGui.Checkbox("Auto match", ref profileAuto))
                    {
                        profile.AutoApply = profileAuto;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("APPLY", new Vector2(88f, 26f)))
                    {
                        ApplyDisplayProfile(configuration, settings, profile);
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("DELETE", new Vector2(88f, 26f)))
                        deleteId = profile.Id;
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.PopID();
            }

            if (!string.IsNullOrWhiteSpace(deleteId))
            {
                settings.DisplayProfiles.RemoveAll(profile => string.Equals(profile.Id, deleteId, StringComparison.OrdinalIgnoreCase));
                if (string.Equals(settings.ActiveDisplayProfileId, deleteId, StringComparison.OrdinalIgnoreCase))
                    settings.ActiveDisplayProfileId = string.Empty;
                changed = true;
            }
        }

        if (changed)
        {
            settings.EnsureValid();
            plugin.SaveConfiguration();
        }
    }

    private void DrawForgeShareWorkspace(Configuration configuration)
    {
        DrawForgeServiceTabs(
            ref forgeSharePage,
            (ForgeSharePage.Workshop, "WORKSHOP", "Collections · favorites"),
            (ForgeSharePage.ProfileFusion, "PROFILE FUSION", "Selective imports"),
            (ForgeSharePage.ForgeLinks, "FORGE LINKS", "Private share codes"));
        ImGui.Spacing();

        switch (forgeSharePage)
        {
            case ForgeSharePage.ProfileFusion:
                DrawForgeProfileFusion(configuration);
                break;
            case ForgeSharePage.ForgeLinks:
                DrawForgeLinks(configuration);
                break;
            case ForgeSharePage.Workshop:
            default:
                DrawForgeWorkshop(configuration);
                break;
        }
    }

    private void DrawForgeWorkshop(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        var favorites = configuration.ForgePremium.WorkshopFavorites;
        UiStyles.SectionLabel("Curated Workshop", theme);
        ImGui.TextWrapped("Install curated member theme collections directly into Theme Studio. Workshop favorites are stored with your RE:Forge configuration and protected by the Vault.");
        ImGui.Spacing();

        foreach (var collection in ForgeWorkshopThemeCatalog.All)
            DrawWorkshopCollection(configuration, collection, favorites);

        ImGui.Spacing();
        DrawForgeFeatureCard(
            "CREATOR WORKSHOP",
            "Private Forge Links and selective Profile Fusion are available now. Verified creator profiles, preview images, ratings, and moderated community publishing are represented in the suite and can be connected to the hosted Workshop backend later without changing the local format.",
            theme.AccentStrong);
    }

    private void DrawForgeProfileFusion(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Profile Fusion", theme);
        ImGui.TextWrapped("Paste an RF3, RF2, or legacy RE:Frame HUD code, inspect it, and merge only the categories you choose. A recovery snapshot is created automatically before the fusion is applied.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextMultiline("##forge-fusion-code", ref forgeFusionCode, 65536, new Vector2(-1f, 110f));

        ImGui.Checkbox("Layouts and mode profiles##fusion-layouts", ref forgeFusionLayouts);
        ImGui.SameLine();
        ImGui.Checkbox("Visibility##fusion-visibility", ref forgeFusionVisibility);
        ImGui.Checkbox("Appearance and scale##fusion-appearance", ref forgeFusionAppearance);
        ImGui.SameLine();
        ImGui.Checkbox("Native placements##fusion-native", ref forgeFusionNativePlacements);

        if (ImGui.Button("INSPECT CODE", new Vector2(160f, 34f)))
        {
            ForgeProfileFusionService.TryInspect(forgeFusionCode, out _, out forgeFusionStatus);
        }
        ImGui.SameLine();
        if (ImGui.Button("APPLY FUSION", new Vector2(160f, 34f)))
        {
            var options = new ForgeFusionOptions(
                forgeFusionLayouts,
                forgeFusionVisibility,
                forgeFusionAppearance,
                forgeFusionNativePlacements);
            ForgeProfileFusionService.TryApply(plugin, forgeFusionCode, options, out forgeFusionStatus);
        }

        if (!string.IsNullOrWhiteSpace(forgeFusionStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(forgeFusionStatus);
        }
    }

    private void DrawForgeLinks(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        CompleteShortCodeTask();
        UiStyles.SectionLabel("Private Forge Links", theme);
        ImGui.TextWrapped("Package the current HUD as a clean RF4 link code. The portable RF3 backup remains available when the online short-code service is unavailable.");
        ImGui.Spacing();

        var busy = shortCodeTask is not null;
        if (busy)
            ImGui.BeginDisabled();
        if (ImGui.Button("CREATE PRIVATE FORGE LINK", new Vector2(250f, 38f)))
        {
            if (plugin.HudPresets.TryExportCurrent("Private Forge Link", out var portableCode, out presetStatus))
                StartShortCodePublish(portableCode);
        }
        if (busy)
            ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.TextUnformatted("RF4 share code");
        ImGui.SetNextItemWidth(-1f);
        DrawBrightInputText("##forge-link-code", ref shareCode, 4096, ImGuiInputTextFlags.ReadOnly);
        if (!string.IsNullOrWhiteSpace(shareCode) && ImGui.Button("COPY RF4 LINK", new Vector2(150f, 30f)))
            ImGui.SetClipboardText(shareCode);

        if (!string.IsNullOrWhiteSpace(offlineShareCode))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Offline RF3 backup");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextMultiline("##forge-offline-code", ref offlineShareCode, 65536, new Vector2(-1f, 74f), ImGuiInputTextFlags.ReadOnly);
            if (ImGui.Button("COPY OFFLINE BACKUP", new Vector2(190f, 30f)))
                ImGui.SetClipboardText(offlineShareCode);
        }

        if (!string.IsNullOrWhiteSpace(presetStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(presetStatus);
        }
    }

    private void DrawForgeVaultWorkspace(Configuration configuration)
    {
        CompleteVaultTasks();
        var theme = plugin.CurrentTheme;
        var settings = configuration.ForgePremium;
        settings.EnsureValid();
        var changed = false;
        var busy = forgeVaultListTask is not null || forgeVaultActionTask is not null;

        UiStyles.SectionLabel("RE:Forge Cloud Vault", theme);
        ImGui.TextWrapped("Create complete compressed snapshots of RE:Frame. Local restore points work immediately; cloud snapshots use the connected Ko-fi/Discord membership identity and the protocol 2 Apps Script backend.");
        ImGui.Spacing();

        var automatic = settings.VaultAutomaticSnapshots;
        if (ImGui.Checkbox("Automatic daily cloud snapshot##forge-vault-auto", ref automatic))
        {
            settings.VaultAutomaticSnapshots = automatic;
            changed = true;
        }
        ImGui.SameLine();
        var maximum = settings.VaultMaximumSnapshots;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt("Maximum snapshots##forge-vault-max", ref maximum, 3, 20))
        {
            settings.VaultMaximumSnapshots = maximum;
            changed = true;
        }

        ImGui.SetNextItemWidth(MathF.Max(180f, ImGui.GetContentRegionAvail().X - 330f));
        ImGui.InputText("##forge-vault-name", ref forgeVaultSnapshotName, 80);
        ImGui.SameLine();
        if (ImGui.Button("LOCAL SNAPSHOT", new Vector2(150f, 0f)))
            forgeVaultStatus = plugin.ForgeVault.CreateLocalSnapshot(forgeVaultSnapshotName).Message;
        ImGui.SameLine();
        if (busy)
            ImGui.BeginDisabled();
        if (ImGui.Button("CLOUD SNAPSHOT", new Vector2(160f, 0f)))
        {
            forgeVaultStatus = "Uploading an encrypted-in-transit Cloud Vault snapshot…";
            forgeVaultActionTask = plugin.ForgeVault.SaveCloudSnapshotAsync(forgeVaultSnapshotName, forgeVaultCancellation.Token);
        }
        if (busy)
            ImGui.EndDisabled();

        if (!plugin.ForgeAccess.HasAccess)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Cloud actions require an active RE:Forge membership. Local snapshots remain available.");
        }

        ImGui.Spacing();
        UiStyles.SectionLabel("Local restore points", theme);
        var localSnapshots = plugin.ForgeVault.ListLocalSnapshots();
        if (localSnapshots.Count == 0)
            ImGui.TextDisabled("No local Vault snapshots yet.");
        foreach (var snapshot in localSnapshots)
            DrawVaultSnapshotCard(snapshot, false);

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Cloud Vault", theme);
        if (busy)
            ImGui.BeginDisabled();
        if (ImGui.Button("REFRESH CLOUD", new Vector2(160f, 30f)))
        {
            forgeVaultStatus = "Refreshing Cloud Vault…";
            forgeVaultListTask = plugin.ForgeVault.ListCloudSnapshotsAsync(forgeVaultCancellation.Token);
        }
        if (busy)
            ImGui.EndDisabled();

        if (forgeCloudSnapshots.Count == 0)
            ImGui.TextDisabled("No cloud snapshots loaded. Connect membership, then refresh the Cloud Vault.");
        foreach (var snapshot in forgeCloudSnapshots)
            DrawVaultSnapshotCard(snapshot, true);

        if (!string.IsNullOrWhiteSpace(forgeVaultStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(forgeVaultStatus);
        }

        if (changed)
        {
            settings.EnsureValid();
            plugin.SaveConfiguration();
        }
    }

    private void DrawVaultSnapshotCard(ForgeVaultSnapshotInfo snapshot, bool cloud)
    {
        var theme = plugin.CurrentTheme;
        ImGui.PushID($"vault-{(cloud ? "cloud" : "local")}-{snapshot.Id}");
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(theme.PanelAlt, 0.48f));
        if (ImGui.BeginChild("##vault-card", new Vector2(0f, 92f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, cloud ? theme.AccentStrong : theme.Success);
            ImGui.TextUnformatted(snapshot.Name);
            ImGui.PopStyleColor();
            ImGui.TextDisabled($"{snapshot.CreatedUtc.ToLocalTime():g} · {snapshot.DeviceName} · {FormatByteCount(snapshot.SizeBytes)}");

            var busy = forgeVaultListTask is not null || forgeVaultActionTask is not null;
            if (busy)
                ImGui.BeginDisabled();
            if (ImGui.Button("RESTORE", new Vector2(100f, 28f)))
            {
                if (cloud)
                    forgeVaultActionTask = plugin.ForgeVault.RestoreCloudSnapshotAsync(snapshot.Id, forgeVaultCancellation.Token);
                else
                    forgeVaultStatus = plugin.ForgeVault.RestoreLocalSnapshot(snapshot.Id).Message;
            }
            ImGui.SameLine();
            if (ImGui.Button("DELETE", new Vector2(100f, 28f)))
            {
                if (cloud)
                    forgeVaultActionTask = plugin.ForgeVault.DeleteCloudSnapshotAsync(snapshot.Id, forgeVaultCancellation.Token);
                else
                    forgeVaultStatus = plugin.ForgeVault.DeleteLocalSnapshot(snapshot.Id).Message;
            }
            if (busy)
                ImGui.EndDisabled();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();
    }

    private void CompleteVaultTasks()
    {
        if (forgeVaultListTask is { IsCompleted: true } listTask)
        {
            forgeVaultListTask = null;
            try
            {
                var result = listTask.GetAwaiter().GetResult();
                forgeCloudSnapshots = result.Snapshots;
                forgeVaultStatus = result.Result.Message;
            }
            catch (Exception ex)
            {
                forgeVaultStatus = $"Cloud Vault refresh failed: {ex.Message}";
            }
        }

        if (forgeVaultActionTask is { IsCompleted: true } actionTask)
        {
            forgeVaultActionTask = null;
            try
            {
                var result = actionTask.GetAwaiter().GetResult();
                forgeVaultStatus = result.Message;
                if (result.Success && plugin.ForgeAccess.HasAccess)
                    forgeVaultListTask = plugin.ForgeVault.ListCloudSnapshotsAsync(forgeVaultCancellation.Token);
            }
            catch (Exception ex)
            {
                forgeVaultStatus = $"Cloud Vault operation failed: {ex.Message}";
            }
        }
    }

    private void DrawForgeMembershipWorkspace(Configuration configuration)
    {
        DrawForgeServiceTabs(
            ref forgeMembershipPage,
            (ForgeMembershipPage.PreviewLab, "PREVIEW LAB", "Early previews"),
            (ForgeMembershipPage.MemberVoting, "MEMBER VOTING", "Shape the next drop"),
            (ForgeMembershipPage.Community, "COMMUNITY", "Badge · Discord · Ko-fi"));
        ImGui.Spacing();

        var theme = plugin.CurrentTheme;
        var settings = configuration.ForgePremium;
        settings.EnsureValid();
        var changed = false;

        switch (forgeMembershipPage)
        {
            case ForgeMembershipPage.MemberVoting:
                UiStyles.SectionLabel("Member Voting", theme);
                ImGui.TextWrapped("Save the collection direction you would most like Ruby Blaire to develop next. This preference is included in your Vault snapshots.");
                ImGui.Spacing();
                foreach (var collection in ForgeWorkshopThemeCatalog.All)
                    changed |= DrawMemberVote(settings, collection.Name, collection.VoteDescription);
                break;

            case ForgeMembershipPage.Community:
                UiStyles.SectionLabel("Member Identity", theme);
                ImGui.TextWrapped("Carry your RE:Forge identity into the suite while keeping the underlying HUD useful and complete for every RE:Frame player.");
                ImGui.Spacing();
                var badge = settings.ShowReforgeMemberBadge;
                if (ImGui.Checkbox("Show RE:Forge member badge##forge-member-badge", ref badge))
                {
                    settings.ShowReforgeMemberBadge = badge;
                    changed = true;
                }
                var title = settings.FounderTitle;
                ImGui.SetNextItemWidth(MathF.Min(480f, ImGui.GetContentRegionAvail().X));
                if (ImGui.InputText("Member title##forge-member-title", ref title, 60))
                {
                    settings.FounderTitle = title;
                    changed = true;
                }
                ImGui.Spacing();
                if (ImGui.Button("OPEN MEMBER DISCORD", new Vector2(210f, 36f)))
                    plugin.OpenSupport();
                ImGui.SameLine();
                if (ImGui.Button("OPEN RE:FORGE ON KO-FI", new Vector2(230f, 36f)))
                    plugin.OpenExternalResource(plugin.ForgeAccess.MembershipUrl, "RE:Forge Ko-fi membership");
                ImGui.Spacing();
                DrawForgeFeatureCard(
                    plugin.ForgeAccess.HasAccess ? "MEMBERSHIP VERIFIED" : "MEMBERSHIP NOT CONNECTED",
                    plugin.ForgeAccess.HasAccess
                        ? $"{(string.IsNullOrWhiteSpace(plugin.ForgeAccess.DiscordDisplayName) ? "Discord member" : plugin.ForgeAccess.DiscordDisplayName)} is connected to RE:Forge."
                        : "Use the membership panel at the top of The Forge to subscribe or connect Discord.",
                    plugin.ForgeAccess.HasAccess ? theme.Success : theme.Warning);
                break;

            case ForgeMembershipPage.PreviewLab:
            default:
                UiStyles.SectionLabel("Forge Preview Lab", theme);
                ImGui.TextWrapped("Opt into member-facing previews of cosmetic and presentation tools before their normal release. Compatibility fixes and safety repairs remain part of free RE:Frame updates.");
                ImGui.Spacing();
                var preview = settings.PreviewLabEnabled;
                if (ImGui.Checkbox("Enable Preview Lab features##forge-preview-lab", ref preview))
                {
                    settings.PreviewLabEnabled = preview;
                    changed = true;
                }
                ImGui.Spacing();
                DrawForgeFeatureCard("CURRENT PREVIEW TRACK", "Workshop creator profiles, richer AFK transitions, and additional mode-directed voice events can be introduced through this track as their runtime integrations are completed.", theme.AccentStrong);
                break;
        }

        if (changed)
        {
            settings.EnsureValid();
            plugin.SaveConfiguration();
        }
    }

    private bool DrawMemberVote(ForgePremiumSettings settings, string choice, string description)
    {
        var selected = string.Equals(settings.MemberVoteChoice, choice, StringComparison.OrdinalIgnoreCase);
        ImGui.PushID($"vote-{choice}");
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(selected ? plugin.CurrentTheme.NavigationSelected : plugin.CurrentTheme.PanelAlt, selected ? 0.76f : 0.46f));
        var changed = false;
        if (ImGui.BeginChild("##vote-card", new Vector2(0f, 76f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextUnformatted(choice);
            ImGui.TextDisabled(description);
            ImGui.SameLine();
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - 96f));
            if (ImGui.SmallButton(selected ? "SELECTED" : "VOTE"))
            {
                settings.MemberVoteChoice = choice;
                changed = true;
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();
        return changed;
    }

    private void DrawWorkshopCollection(
        Configuration configuration,
        ForgeWorkshopThemeDefinition collection,
        List<string> favorites)
    {
        var name = collection.Name;
        var favorite = favorites.Contains(name, StringComparer.OrdinalIgnoreCase);
        var installed = configuration.ForgeThemes.Any(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase));
        var preview = collection.Palette;

        ImGui.PushID($"workshop-{name}");
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(preview.PanelAlt, 0.82f));
        ImGui.PushStyleColor(ImGuiCol.Border, UiStyles.WithAlpha(preview.AccentMid, 0.68f));
        if (ImGui.BeginChild("##workshop-card", new Vector2(0f, 118f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, preview.AccentMid);
            ImGui.TextUnformatted(name.ToUpperInvariant());
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.Text, preview.Text);
            ImGui.TextWrapped(collection.Description);
            ImGui.PopStyleColor();

            var installLabel = installed ? "REFRESH THEME" : "INSTALL THEME";
            if (ImGui.Button(installLabel, new Vector2(145f, 28f)))
            {
                InstallWorkshopTheme(configuration, collection);
                forgePage = ForgePage.Create;
                forgeCreatePage = ForgeCreatePage.ThemeStudio;
            }
            ImGui.SameLine();
            if (ImGui.Button(favorite ? "★ FAVORITE" : "☆ FAVORITE", new Vector2(120f, 28f)))
            {
                if (favorite)
                    favorites.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
                else
                    favorites.Add(name);
                plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.PopID();
    }

    private void InstallWorkshopTheme(Configuration configuration, ForgeWorkshopThemeDefinition collection)
    {
        var existing = configuration.ForgeThemes.FirstOrDefault(theme =>
            string.Equals(theme.Name, collection.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = ForgeThemeDefinition.FromPalette(
                collection.Name,
                collection.Palette,
                collection.SourcePreset);
            configuration.ForgeThemes.Add(existing);
        }
        else
        {


            existing.SourcePreset = collection.SourcePreset;
            existing.ApplyPalette(collection.Palette);
        }

        ForgeThemeLibrary.Activate(configuration, existing);
        forgeSelectedThemeId = existing.Id;
        plugin.SaveConfiguration();
    }

    private void ApplyDisplayProfile(Configuration configuration, ForgePremiumSettings settings, ForgeDisplayProfile profile)
    {
        configuration.InterfaceScale = profile.InterfaceScale;
        configuration.TextScale = profile.TextScale;
        settings.ActiveDisplayProfileId = profile.Id;
        plugin.FitHudToViewport(HudCanvas.Current().Size);
    }

    private bool DrawRuleConditionCombo(string label, ref ForgeRuleCondition value)
    {
        var changed = false;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo(label, value.ToString()))
        {
            foreach (var candidate in Enum.GetValues<ForgeRuleCondition>())
            {
                if (ImGui.Selectable(candidate.ToString(), candidate == value))
                {
                    value = candidate;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private static string DescribeAutomationRule(ForgeAutomationRule rule)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.JobAbbreviation)) parts.Add(rule.JobAbbreviation);
        if (!string.IsNullOrWhiteSpace(rule.TerritoryContains)) parts.Add(rule.TerritoryContains);
        if (rule.Combat != ForgeRuleCondition.Any) parts.Add($"Combat {rule.Combat}");
        if (rule.Party != ForgeRuleCondition.Any) parts.Add($"Party {rule.Party}");
        if (rule.MinimumPartySize > 0) parts.Add($"{rule.MinimumPartySize}+ players");
        return parts.Count == 0 ? "Any player state" : string.Join(" · ", parts);
    }

    private void DrawForgeMiniFeature(string title, string text, Vector4 accent, float width)
    {
        ImGui.PushID($"mini-{title}-{text}");
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.46f));
        if (ImGui.BeginChild("##mini-feature", new Vector2(width, 78f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, accent);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            ImGui.TextWrapped(text);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();
    }

    private void DrawForgeServiceTabs<T>(ref T selected, params (T Page, string Label, string Subtitle)[] tabs)
        where T : struct, Enum
    {
        var theme = plugin.CurrentTheme;
        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var preferredWidth = 174f;
        var columns = Math.Max(1, Math.Min(tabs.Length, (int)MathF.Floor((available + gap) / (preferredWidth + gap))));
        var width = MathF.Min(210f, MathF.Max(138f, (available - gap * (columns - 1)) / columns));

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 6f));
        for (var index = 0; index < tabs.Length; index++)
        {
            var tab = tabs[index];
            var active = EqualityComparer<T>.Default.Equals(selected, tab.Page);
            ImGui.PushID($"forge-service-tab-{typeof(T).Name}-{index}");
            ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(
                active ? theme.ResolvedNavigationSelected : theme.ResolvedNavigation,
                active ? 0.96f : 0.24f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiStyles.WithAlpha(theme.ResolvedNavigationHovered, 0.92f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ResolvedNavigationActive);
            ImGui.PushStyleColor(ImGuiCol.Text, active ? theme.AccentStrong : theme.Text);
            if (ImGui.Button(tab.Label, new Vector2(width, 36f)))
                selected = tab.Page;
            if (active)
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddRectFilled(
                    new Vector2(min.X + 10f, max.Y - 3f),
                    new Vector2(max.X - 10f, max.Y),
                    ImGui.GetColorU32(theme.AccentStrong),
                    2f);
            }
            if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(tab.Subtitle))
                ImGui.SetTooltip(tab.Subtitle);
            ImGui.PopStyleColor(4);
            ImGui.PopID();
            if ((index + 1) % columns != 0 && index < tabs.Length - 1)
                ImGui.SameLine();
        }
        ImGui.PopStyleVar(2);
    }

    private static string FormatByteCount(int bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:0.0} KB";
        return $"{bytes / (1024f * 1024f):0.0} MB";
    }

    private enum ForgeCreatePage { ThemeStudio, MapStudio, DockStudio }
    private enum ForgeImmersionPage { AfkDirector, VoiceDirector }
    private enum ForgeAutomationPage { SceneAutomation, DisplayLab }
    private enum ForgeSharePage { Workshop, ProfileFusion, ForgeLinks }
    private enum ForgeMembershipPage { PreviewLab, MemberVoting, Community }
}
