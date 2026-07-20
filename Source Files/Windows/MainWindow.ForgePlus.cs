using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed partial class MainWindow
{
    private ForgePlusPage forgePlusPage = ForgePlusPage.MotionLibrary;
    private string forgePlusSelectedSceneId = string.Empty;
    private string forgePlusNewSceneName = "My Scene";
    private string forgePlusSelectedPresetId = string.Empty;
    private string forgePlusNewPresetName = "My Character";
    private string forgePlusSelectedVenueId = string.Empty;
    private string forgePlusNewVenueName = "My Venue";
    private string forgePlusSelectedEventId = string.Empty;
    private string forgePlusNewEventName = "New Event";

    private void DrawForgePlusWorkspace(Configuration configuration)
    {
        if (!plugin.ForgeAccess.HasPlusAccess)
        {
            DrawForgePlusUpgradeGate();
            return;
        }

        DrawForgeServiceTabs(
            ref forgePlusPage,
            (ForgePlusPage.MotionLibrary, "MOTION", "Installed animations"),
            (ForgePlusPage.SceneBuilder, "SCENES", "Sequence builder"),
            (ForgePlusPage.Dancer, "DANCER", "Spotify reactions"),
            (ForgePlusPage.CharacterPresets, "CHARACTERS", "Deep personalization"),
            (ForgePlusPage.VenueProfiles, "VENUES", "Venue identities"),
            (ForgePlusPage.EventScheduler, "SCHEDULE", "Timed events"),
            (ForgePlusPage.AnimationWheel, "WHEEL", "Instant favorites"),
            (ForgePlusPage.Roulette, "ROULETTE", "Living animation rotation"));
        ImGui.Spacing();

        switch (forgePlusPage)
        {
            case ForgePlusPage.SceneBuilder: DrawForgePlusSceneBuilder(configuration); break;
            case ForgePlusPage.Dancer: DrawForgePlusDancer(configuration); break;
            case ForgePlusPage.CharacterPresets: DrawForgePlusCharacterPresets(configuration); break;
            case ForgePlusPage.VenueProfiles: DrawForgePlusVenueProfiles(configuration); break;
            case ForgePlusPage.EventScheduler: DrawForgePlusEventScheduler(configuration); break;
            case ForgePlusPage.AnimationWheel: DrawForgePlusAnimationWheel(configuration); break;
            case ForgePlusPage.Roulette: DrawForgePlusRoulette(configuration); break;
            case ForgePlusPage.MotionLibrary:
            default:
                DrawForgeAnimationLibrary(configuration);
                break;
        }
    }

    private void DrawForgePlusUpgradeGate()
    {
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("RE:Forge+", theme);
        ImGui.SetWindowFontScale(1.34f);
        ImGui.TextUnformatted("MAKE YOUR CHARACTER FEEL ALIVE");
        ImGui.SetWindowFontScale(1f);
        ImGui.TextWrapped("Forge+ is the complete $5.99 creative tier: Motion Library, Scene Builder, Dancer, Character Presets, Venue Profiles, Event Scheduler, Animation Wheel, and Animation Roulette.");
        ImGui.Spacing();

        var features = new[]
        {
            "Motion Library and instant animation launching",
            "Scene sequences built from animations, themes, profiles, and commands",
            "Spotify-aware Dancer that changes motion when the song changes",
            "Deep character personalization presets",
            "Venue profiles with automatic location recognition",
            "Recurring event scheduling",
            "Eight-slot radial Animation Wheel",
            "Customizable Animation Roulette",
        };
        foreach (var feature in features)
            ImGui.BulletText(feature);

        ImGui.Spacing();
        if (ImGui.Button("VIEW FORGE+ MEMBERSHIP", new Vector2(250f, 40f)))
            plugin.OpenExternalResource(plugin.ForgeAccess.MembershipUrl, "RE:Forge+ membership");
        ImGui.SameLine();
        if (ImGui.Button("CHECK MEMBERSHIP", new Vector2(190f, 40f)))
            _ = RefreshForgeMembershipAsync();
        ImGui.Spacing();
        ImGui.TextDisabled("Cloud Vault and cloud sync remain included with the base RE:Forge subscription.");
    }

    private void DrawForgePlusSceneBuilder(Configuration configuration)
    {
        var settings = configuration.ForgePremium;
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Scene Builder", theme);
        ImGui.TextWrapped("Build reusable sequences from animations, waits, UI modes, themes, character presets, venue profiles, and any command already installed on your system.");
        ImGui.TextDisabled(plugin.ForgePlus.RuntimeStatus);
        ImGui.Spacing();

        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var listWidth = width > 850f ? 255f : 215f;
        if (ImGui.BeginChild("##forge-plus-scene-list", new Vector2(listWidth, 0f), true))
        {
            foreach (var scene in settings.Scenes)
            {
                var selected = scene.Id == forgePlusSelectedSceneId;
                var running = plugin.ForgePlus.RunningSceneId == scene.Id;
                if (ImGui.Selectable($"{(running ? "▶ " : string.Empty)}{scene.Name}##scene-{scene.Id}", selected))
                    forgePlusSelectedSceneId = scene.Id;
            }
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##new-forge-scene", ref forgePlusNewSceneName, 80);
            if (ImGui.Button("+ CREATE SCENE", new Vector2(-1f, 34f)))
            {
                var scene = new ForgeScene { Name = forgePlusNewSceneName };
                scene.EnsureValid();
                settings.Scenes.Add(scene);
                forgePlusSelectedSceneId = scene.Id;
                forgePlusNewSceneName = "My Scene";
                plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("##forge-plus-scene-editor", Vector2.Zero, true))
        {
            var scene = settings.Scenes.FirstOrDefault(item => item.Id == forgePlusSelectedSceneId);
            if (scene is null)
            {
                ImGui.TextWrapped("Create or select a scene to begin building its sequence.");
            }
            else
            {
                var changed = false;
                var name = scene.Name;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText("##scene-name", ref name, 100)) { scene.Name = name; changed = true; }
                var description = scene.Description;
                if (ImGui.InputTextMultiline("##scene-description", ref description, 400, new Vector2(-1f, 64f))) { scene.Description = description; changed = true; }
                var loop = scene.Loop;
                if (ImGui.Checkbox("Loop scene##scene-loop", ref loop)) { scene.Loop = loop; changed = true; }

                var half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
                var running = plugin.ForgePlus.RunningSceneId == scene.Id;
                if (ImGui.Button(running ? "STOP SCENE" : "PLAY SCENE", new Vector2(half, 36f)))
                {
                    if (running) plugin.ForgePlus.StopScene(); else plugin.ForgePlus.StartScene(scene.Id);
                }
                ImGui.SameLine();
                if (ImGui.Button("+ ADD STEP", new Vector2(half, 36f)))
                {
                    scene.Steps.Add(new ForgeSceneStep());
                    changed = true;
                }

                ImGui.Spacing();
                UiStyles.Divider(theme);
                ImGui.Spacing();
                for (var index = 0; index < scene.Steps.Count; index++)
                {
                    var step = scene.Steps[index];
                    ImGui.PushID(step.Id);
                    if (ImGui.BeginChild("##scene-step", new Vector2(0f, 122f), true))
                    {
                        ImGui.TextDisabled($"STEP {index + 1}");
                        ImGui.SameLine();
                        var action = step.Action;
                        ImGui.SetNextItemWidth(170f);
                        if (ImGui.BeginCombo("##step-action", SceneActionLabel(action)))
                        {
                            foreach (var option in Enum.GetValues<ForgeSceneActionType>())
                            {
                                if (!ImGui.Selectable(SceneActionLabel(option), option == action)) continue;
                                step.Action = option;
                                step.Value = string.Empty;
                                changed = true;
                            }
                            ImGui.EndCombo();
                        }
                        ImGui.SameLine();
                        var delay = step.DelaySeconds;
                        ImGui.SetNextItemWidth(150f);
                        if (ImGui.SliderFloat("##step-delay", ref delay, 0.1f, 30f, "%.1f sec")) { step.DelaySeconds = delay; changed = true; }
                        ImGui.SameLine();
                        if (ImGui.SmallButton("↑") && index > 0)
                        {
                            (scene.Steps[index - 1], scene.Steps[index]) = (scene.Steps[index], scene.Steps[index - 1]);
                            changed = true;
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton("↓") && index < scene.Steps.Count - 1)
                        {
                            (scene.Steps[index + 1], scene.Steps[index]) = (scene.Steps[index], scene.Steps[index + 1]);
                            changed = true;
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton("REMOVE"))
                        {
                            scene.Steps.RemoveAt(index);
                            changed = true;
                            ImGui.EndChild();
                            ImGui.PopID();
                            index--;
                            continue;
                        }

                        ImGui.SetNextItemWidth(-1f);
                        changed |= DrawSceneStepValue(configuration, step);
                    }
                    ImGui.EndChild();
                    ImGui.PopID();
                    ImGui.Spacing();
                }

                if (ImGui.Button("DELETE SCENE", new Vector2(150f, 32f)))
                {
                    if (running) plugin.ForgePlus.StopScene();
                    settings.Scenes.Remove(scene);
                    forgePlusSelectedSceneId = string.Empty;
                    plugin.SaveConfiguration();
                    return;
                }
                if (changed) plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
    }

    private bool DrawSceneStepValue(Configuration configuration, ForgeSceneStep step)
    {
        var changed = false;
        var value = step.Value;
        switch (step.Action)
        {
            case ForgeSceneActionType.Animation:
                changed |= DrawAnimationIdCombo("##scene-animation", ref value,
                    item => !item.Hidden && !string.IsNullOrWhiteSpace(item.TriggerCommand) &&
                            (configuration.ForgePremium.AnimationLibraryShowMature || !item.Mature));
                break;
            case ForgeSceneActionType.UiMode:
                var mode = Enum.TryParse<UiMode>(value, true, out var parsedMode) ? parsedMode : UiMode.Leisure;
                if (ImGui.BeginCombo("##scene-mode", mode.ToString()))
                {
                    foreach (var option in Enum.GetValues<UiMode>())
                    {
                        if (!ImGui.Selectable(option.ToString(), option == mode)) continue;
                        value = option.ToString(); changed = true;
                    }
                    ImGui.EndCombo();
                }
                break;
            case ForgeSceneActionType.Theme:
                changed |= DrawNamedIdCombo("##scene-theme", ref value, configuration.ForgeThemes.Select(item => (item.Id, item.Name)), "Built-in / none");
                break;
            case ForgeSceneActionType.CharacterPreset:
                changed |= DrawNamedIdCombo("##scene-preset", ref value, configuration.ForgePremium.CharacterPresets.Select(item => (item.Id, item.Name)), "Choose character preset");
                break;
            case ForgeSceneActionType.VenueProfile:
                changed |= DrawNamedIdCombo("##scene-venue", ref value, configuration.ForgePremium.VenueProfiles.Select(item => (item.Id, item.Name)), "Choose venue profile");
                break;
            case ForgeSceneActionType.Command:
                if (ImGui.InputTextWithHint("##scene-command", "/emote or plugin command", ref value, 200)) changed = true;
                break;
            case ForgeSceneActionType.Wait:
                ImGui.TextDisabled("Waits for the step delay before continuing.");
                break;
        }
        if (changed) step.Value = value;
        return changed;
    }

    private void DrawForgePlusDancer(Configuration configuration)
    {
        var settings = configuration.ForgePremium;
        var dancer = settings.Dancer;
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Dancer", theme);
        ImGui.TextWrapped("Dancer reads the currently playing Spotify desktop song and changes to one of your installed dance animations whenever the track changes.");
        ImGui.Spacing();

        var changed = false;
        var enabled = dancer.Enabled;
        if (ImGui.Checkbox("Enable Spotify Dancer##dancer-enabled", ref enabled)) { dancer.Enabled = enabled; changed = true; }
        ImGui.SameLine();
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(plugin.ForgePlus.CurrentSpotifyTrack) ? "Spotify track not detected" : plugin.ForgePlus.CurrentSpotifyTrack);
        var poll = dancer.PollSeconds;
        if (ImGui.SliderInt("Spotify check interval##dancer-poll", ref poll, 2, 15, "%d sec")) { dancer.PollSeconds = poll; changed = true; }
        var favorites = dancer.FavoritesOnly;
        if (ImGui.Checkbox("Use favorites only##dancer-favorites", ref favorites)) { dancer.FavoritesOnly = favorites; changed = true; }
        ImGui.SameLine();
        var music = dancer.IncludeMusicCategory;
        if (ImGui.Checkbox("Include Music category##dancer-music", ref music)) { dancer.IncludeMusicCategory = music; changed = true; }
        if (ImGui.Button("DANCE NOW", new Vector2(160f, 34f))) plugin.ForgePlus.TriggerDancerNow();
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.ForgePlus.RuntimeStatus);

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Dance pool", theme);
        ImGui.TextDisabled("Select specific animations, or leave every item unchecked to use all Dance and Music animations.");
        if (ImGui.BeginChild("##dancer-pool", new Vector2(0f, 190f), true))
        {
            foreach (var animation in settings.AnimationLibraryEntries.Where(item =>
                         !item.Hidden &&
                         !string.IsNullOrWhiteSpace(item.TriggerCommand) &&
                         (settings.AnimationLibraryShowMature || !item.Mature) &&
                         (item.Category == ForgeAnimationCategory.Dance || item.Category == ForgeAnimationCategory.Music)))
            {
                var selected = dancer.AnimationIds.Contains(animation.Id, StringComparer.OrdinalIgnoreCase);
                if (ImGui.Checkbox($"{animation.Name}##dancer-animation-{animation.Id}", ref selected))
                {
                    if (selected) dancer.AnimationIds.Add(animation.Id);
                    else dancer.AnimationIds.RemoveAll(id => id.Equals(animation.Id, StringComparison.OrdinalIgnoreCase));
                    changed = true;
                }
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();
        UiStyles.SectionLabel("Song rules", theme);
        ImGui.TextDisabled("A rule can force a particular animation when the Spotify title contains matching text.");
        foreach (var rule in dancer.SongRules.ToArray())
        {
            ImGui.PushID(rule.Id);
            var match = rule.MatchText;
            ImGui.SetNextItemWidth(MathF.Max(160f, ImGui.GetContentRegionAvail().X * 0.38f));
            if (ImGui.InputTextWithHint("##song-match", "artist, song, or keyword", ref match, 160)) { rule.MatchText = match; changed = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(MathF.Max(180f, ImGui.GetContentRegionAvail().X - 90f));
            var ruleAnimationId = rule.AnimationId;
            if (DrawAnimationIdCombo("##song-animation", ref ruleAnimationId,
                    item => !item.Hidden && !string.IsNullOrWhiteSpace(item.TriggerCommand) &&
                            (settings.AnimationLibraryShowMature || !item.Mature) &&
                            (item.Category is ForgeAnimationCategory.Dance or ForgeAnimationCategory.Music)))
            {
                rule.AnimationId = ruleAnimationId;
                changed = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("REMOVE")) { dancer.SongRules.Remove(rule); changed = true; }
            ImGui.PopID();
        }
        if (ImGui.Button("+ ADD SONG RULE", new Vector2(170f, 32f))) { dancer.SongRules.Add(new ForgeDancerSongRule()); changed = true; }
        if (changed) plugin.SaveConfiguration();
    }

    private void DrawForgePlusCharacterPresets(Configuration configuration)
    {
        var settings = configuration.ForgePremium;
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Character Presets", theme);
        ImGui.TextWrapped("Capture a complete presentation identity: HUD mode, Forge theme, scaling, voice pack, AFK scene, HUD geometry, visibility profiles, and dock layouts.");
        ImGui.TextDisabled(plugin.ForgePlus.RuntimeStatus);
        ImGui.Spacing();

        var width = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginChild("##character-preset-list", new Vector2(width > 800f ? 250f : 210f, 0f), true))
        {
            foreach (var preset in settings.CharacterPresets)
                if (ImGui.Selectable($"{preset.Name}##character-{preset.Id}", preset.Id == forgePlusSelectedPresetId)) forgePlusSelectedPresetId = preset.Id;
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##new-character-preset", ref forgePlusNewPresetName, 100);
            if (ImGui.Button("CAPTURE CURRENT", new Vector2(-1f, 34f)))
            {
                var preset = plugin.ForgePlus.CaptureCharacterPreset(forgePlusNewPresetName);
                forgePlusSelectedPresetId = preset.Id;
                forgePlusNewPresetName = "My Character";
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("##character-preset-editor", Vector2.Zero, true))
        {
            var preset = settings.CharacterPresets.FirstOrDefault(item => item.Id == forgePlusSelectedPresetId);
            if (preset is null) ImGui.TextWrapped("Capture or select a character preset.");
            else
            {
                var changed = false;
                var name = preset.Name;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText("##character-name", ref name, 100)) { preset.Name = name; changed = true; }
                var notes = preset.Notes;
                if (ImGui.InputTextMultiline("##character-notes", ref notes, 500, new Vector2(-1f, 70f))) { preset.Notes = notes; changed = true; }

                ImGui.TextDisabled("Choose which parts this preset restores:");
                var includeMode = preset.IncludeMode;
                if (PresetFlag("HUD mode", ref includeMode)) { preset.IncludeMode = includeMode; changed = true; }
                ImGui.SameLine();
                var includeTheme = preset.IncludeTheme;
                if (PresetFlag("Theme", ref includeTheme)) { preset.IncludeTheme = includeTheme; changed = true; }
                ImGui.SameLine();
                var includeScale = preset.IncludeScale;
                if (PresetFlag("Scale", ref includeScale)) { preset.IncludeScale = includeScale; changed = true; }
                var includeVoice = preset.IncludeVoice;
                if (PresetFlag("Voice pack", ref includeVoice)) { preset.IncludeVoice = includeVoice; changed = true; }
                ImGui.SameLine();
                var includeAfk = preset.IncludeAfk;
                if (PresetFlag("AFK scene", ref includeAfk)) { preset.IncludeAfk = includeAfk; changed = true; }
                ImGui.SameLine();
                var includeHud = preset.IncludeHudLayout;
                if (PresetFlag("HUD layout", ref includeHud)) { preset.IncludeHudLayout = includeHud; changed = true; }
                var includeDocks = preset.IncludeDockLayouts;
                if (PresetFlag("Dock layouts", ref includeDocks)) { preset.IncludeDockLayouts = includeDocks; changed = true; }

                ImGui.Spacing();
                var third = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
                if (ImGui.Button("APPLY PRESET", new Vector2(third, 38f))) plugin.ForgePlus.ApplyCharacterPreset(preset.Id);
                ImGui.SameLine();
                if (ImGui.Button("RECAPTURE CURRENT", new Vector2(third, 38f))) plugin.ForgePlus.RecaptureCharacterPreset(preset.Id);
                ImGui.SameLine();
                if (ImGui.Button("DELETE", new Vector2(third, 38f)))
                {
                    settings.CharacterPresets.Remove(preset);
                    forgePlusSelectedPresetId = string.Empty;
                    plugin.SaveConfiguration();
                    return;
                }
                if (changed) plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
    }

    private static bool PresetFlag(string label, ref bool value)
    {
        var original = value;
        ImGui.Checkbox(label, ref value);
        return original != value;
    }

    private void DrawForgePlusVenueProfiles(Configuration configuration)
    {
        var settings = configuration.ForgePremium;
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Venue Profiles", theme);
        ImGui.TextWrapped("Combine a character identity, opening scene, Dancer state, and Roulette state into one venue-ready profile. Optional territory matching can activate it when you arrive.");
        ImGui.Spacing();

        var width = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginChild("##venue-list", new Vector2(width > 800f ? 250f : 210f, 0f), true))
        {
            foreach (var venue in settings.VenueProfiles)
                if (ImGui.Selectable($"{venue.Name}##venue-{venue.Id}", venue.Id == forgePlusSelectedVenueId)) forgePlusSelectedVenueId = venue.Id;
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##new-venue", ref forgePlusNewVenueName, 100);
            if (ImGui.Button("+ CREATE VENUE", new Vector2(-1f, 34f)))
            {
                var venue = new ForgeVenueProfile { Name = forgePlusNewVenueName };
                venue.EnsureValid();
                settings.VenueProfiles.Add(venue);
                forgePlusSelectedVenueId = venue.Id;
                forgePlusNewVenueName = "My Venue";
                plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("##venue-editor", Vector2.Zero, true))
        {
            var venue = settings.VenueProfiles.FirstOrDefault(item => item.Id == forgePlusSelectedVenueId);
            if (venue is null) ImGui.TextWrapped("Create or select a venue profile.");
            else
            {
                var changed = false;
                var name = venue.Name;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText("##venue-name", ref name, 100)) { venue.Name = name; changed = true; }
                var territory = venue.TerritoryContains;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputTextWithHint("##venue-territory", "Territory or location name contains...", ref territory, 160)) { venue.TerritoryContains = territory; changed = true; }
                var auto = venue.AutoActivate;
                if (ImGui.Checkbox("Activate automatically when territory matches##venue-auto", ref auto)) { venue.AutoActivate = auto; changed = true; }

                ImGui.TextUnformatted("Character preset");
                ImGui.SetNextItemWidth(-1f);
                var venueCharacterId = venue.CharacterPresetId;
                if (DrawNamedIdCombo("##venue-character", ref venueCharacterId, settings.CharacterPresets.Select(item => (item.Id, item.Name)), "None"))
                {
                    venue.CharacterPresetId = venueCharacterId;
                    changed = true;
                }
                ImGui.TextUnformatted("Opening scene");
                ImGui.SetNextItemWidth(-1f);
                var venueSceneId = venue.SceneId;
                if (DrawNamedIdCombo("##venue-scene", ref venueSceneId, settings.Scenes.Select(item => (item.Id, item.Name)), "None"))
                {
                    venue.SceneId = venueSceneId;
                    changed = true;
                }
                var dancer = venue.EnableDancer;
                if (ImGui.Checkbox("Enable Dancer##venue-dancer", ref dancer)) { venue.EnableDancer = dancer; changed = true; }
                ImGui.SameLine();
                var roulette = venue.EnableRoulette;
                if (ImGui.Checkbox("Enable Animation Roulette##venue-roulette", ref roulette)) { venue.EnableRoulette = roulette; changed = true; }

                ImGui.Spacing();
                var half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
                if (ImGui.Button("ACTIVATE VENUE", new Vector2(half, 38f))) plugin.ForgePlus.ActivateVenueProfile(venue.Id);
                ImGui.SameLine();
                if (ImGui.Button("DELETE VENUE", new Vector2(half, 38f)))
                {
                    settings.VenueProfiles.Remove(venue);
                    forgePlusSelectedVenueId = string.Empty;
                    plugin.SaveConfiguration();
                    return;
                }
                ImGui.TextDisabled(plugin.ForgePlus.RuntimeStatus);
                if (changed) plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
    }

    private void DrawForgePlusEventScheduler(Configuration configuration)
    {
        var settings = configuration.ForgePremium;
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Event Scheduler", theme);
        ImGui.TextWrapped("Schedule animations, scenes, character presets, venue profiles, Dancer, or Roulette using your local clock while FFXIV is running. Events repeat on the selected weekdays.");
        ImGui.Spacing();

        var width = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginChild("##event-list", new Vector2(width > 800f ? 250f : 210f, 0f), true))
        {
            foreach (var scheduled in settings.ScheduledEvents)
                if (ImGui.Selectable($"{(scheduled.Enabled ? "● " : "○ ")}{scheduled.Name}##event-{scheduled.Id}", scheduled.Id == forgePlusSelectedEventId)) forgePlusSelectedEventId = scheduled.Id;
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##new-event", ref forgePlusNewEventName, 100);
            if (ImGui.Button("+ CREATE EVENT", new Vector2(-1f, 34f)))
            {
                var scheduled = new ForgeScheduledEvent { Name = forgePlusNewEventName };
                scheduled.EnsureValid();
                settings.ScheduledEvents.Add(scheduled);
                forgePlusSelectedEventId = scheduled.Id;
                forgePlusNewEventName = "New Event";
                plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("##event-editor", Vector2.Zero, true))
        {
            var scheduled = settings.ScheduledEvents.FirstOrDefault(item => item.Id == forgePlusSelectedEventId);
            if (scheduled is null) ImGui.TextWrapped("Create or select a scheduled event.");
            else
            {
                var changed = false;
                var name = scheduled.Name;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText("##event-name", ref name, 100)) { scheduled.Name = name; changed = true; }
                var enabled = scheduled.Enabled;
                if (ImGui.Checkbox("Enabled##event-enabled", ref enabled)) { scheduled.Enabled = enabled; changed = true; }

                var hour = scheduled.MinuteOfDay / 60;
                var minute = scheduled.MinuteOfDay % 60;
                ImGui.SetNextItemWidth(90f);
                if (ImGui.InputInt("Hour##event-hour", ref hour)) { hour = Math.Clamp(hour, 0, 23); scheduled.MinuteOfDay = hour * 60 + minute; changed = true; }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(90f);
                if (ImGui.InputInt("Minute##event-minute", ref minute)) { minute = Math.Clamp(minute, 0, 59); scheduled.MinuteOfDay = hour * 60 + minute; changed = true; }

                ImGui.TextUnformatted("Days");
                for (var dayIndex = 0; dayIndex < ScheduledDayOptions.Length; dayIndex++)
                {
                    var day = ScheduledDayOptions[dayIndex];
                    var active = scheduled.Days.HasFlag(day.Flag);
                    if (ImGui.Checkbox($"{day.Label}##event-day-{day.Label}", ref active))
                    {
                        if (active) scheduled.Days |= day.Flag; else scheduled.Days &= ~day.Flag;
                        if (scheduled.Days == ForgeScheduleDays.None) scheduled.Days = day.Flag;
                        changed = true;
                    }
                    if (dayIndex < ScheduledDayOptions.Length - 1) ImGui.SameLine();
                }

                var action = scheduled.Action;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("##event-action", ScheduledActionLabel(action)))
                {
                    foreach (var option in Enum.GetValues<ForgeScheduledActionType>())
                    {
                        if (!ImGui.Selectable(ScheduledActionLabel(option), option == action)) continue;
                        scheduled.Action = option; scheduled.TargetId = string.Empty; changed = true;
                    }
                    ImGui.EndCombo();
                }
                ImGui.SetNextItemWidth(-1f);
                changed |= DrawScheduledTarget(settings, scheduled);

                ImGui.Spacing();
                var half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
                if (ImGui.Button("RUN NOW", new Vector2(half, 38f))) plugin.ForgePlus.RunScheduledEventNow(scheduled);
                ImGui.SameLine();
                if (ImGui.Button("DELETE EVENT", new Vector2(half, 38f)))
                {
                    settings.ScheduledEvents.Remove(scheduled);
                    forgePlusSelectedEventId = string.Empty;
                    plugin.SaveConfiguration();
                    return;
                }
                ImGui.TextDisabled(plugin.ForgePlus.RuntimeStatus);
                if (changed) plugin.SaveConfiguration();
            }
        }
        ImGui.EndChild();
    }

    private bool DrawScheduledTarget(ForgePremiumSettings settings, ForgeScheduledEvent scheduled)
    {
        var targetId = scheduled.TargetId;
        var changed = scheduled.Action switch
        {
            ForgeScheduledActionType.Animation => DrawAnimationIdCombo("##event-target-animation", ref targetId,
                item => !item.Hidden && !string.IsNullOrWhiteSpace(item.TriggerCommand) &&
                        (settings.AnimationLibraryShowMature || !item.Mature)),
            ForgeScheduledActionType.Scene => DrawNamedIdCombo("##event-target-scene", ref targetId, settings.Scenes.Select(item => (item.Id, item.Name)), "Choose scene"),
            ForgeScheduledActionType.CharacterPreset => DrawNamedIdCombo("##event-target-character", ref targetId, settings.CharacterPresets.Select(item => (item.Id, item.Name)), "Choose character preset"),
            ForgeScheduledActionType.VenueProfile => DrawNamedIdCombo("##event-target-venue", ref targetId, settings.VenueProfiles.Select(item => (item.Id, item.Name)), "Choose venue profile"),
            _ => DrawNoTargetRequired(),
        };
        if (changed) scheduled.TargetId = targetId;
        return changed;
    }

    private static bool DrawNoTargetRequired()
    {
        ImGui.TextDisabled("This action does not require a target.");
        return false;
    }

    private void DrawForgePlusAnimationWheel(Configuration configuration)
    {
        var settings = configuration.ForgePremium;
        var wheel = settings.AnimationWheel;
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Animation Wheel", theme);
        ImGui.TextWrapped("Assign eight favorite motions to a radial wheel. Press the configured key anywhere in normal gameplay, or use /ref wheel.");
        ImGui.Spacing();
        var changed = false;
        var enabled = wheel.Enabled;
        if (ImGui.Checkbox("Enable Animation Wheel##wheel-enabled", ref enabled)) { wheel.Enabled = enabled; changed = true; }

        var keyLabel = VirtualKeyLabel(wheel.VirtualKey);
        ImGui.SetNextItemWidth(150f);
        if (ImGui.BeginCombo("Activation key##wheel-key", keyLabel))
        {
            for (var key = 0x70; key <= 0x7B; key++)
            {
                if (!ImGui.Selectable(VirtualKeyLabel(key), wheel.VirtualKey == key)) continue;
                wheel.VirtualKey = key; changed = true;
            }
            ImGui.EndCombo();
        }
        if (ImGui.Button("OPEN WHEEL", new Vector2(170f, 36f))) plugin.ToggleForgeAnimationWheel();
        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();

        var slotWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        for (var index = 0; index < 8; index++)
        {
            ImGui.PushID($"wheel-slot-{index}");
            ImGui.BeginGroup();
            ImGui.TextDisabled($"SLOT {index + 1}");
            ImGui.SetNextItemWidth(slotWidth);
            var id = wheel.Slots[index];
            if (DrawAnimationIdCombo("##wheel-animation", ref id,
                    item => !item.Hidden && !string.IsNullOrWhiteSpace(item.TriggerCommand) &&
                            (settings.AnimationLibraryShowMature || !item.Mature)))
            {
                wheel.Slots[index] = id;
                changed = true;
            }
            ImGui.EndGroup();
            if (index % 2 == 0) ImGui.SameLine();
            ImGui.PopID();
        }
        if (changed) plugin.SaveConfiguration();
    }

    private void DrawForgePlusRoulette(Configuration configuration)
    {
        var roulette = configuration.ForgePremium.AnimationRoulette;
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Animation Roulette", theme);
        ImGui.TextWrapped("Let RE:Forge+ periodically choose from the motion categories you allow. Creator files stay untouched; Roulette only launches animations already installed and configured by the player.");
        ImGui.Spacing();
        var changed = false;
        var enabled = roulette.Enabled;
        if (ImGui.Checkbox("Enable Animation Roulette##roulette-enabled", ref enabled)) { roulette.Enabled = enabled; changed = true; }
        var interval = roulette.IntervalSeconds;
        if (ImGui.SliderInt("Change interval##roulette-interval", ref interval, 10, 600, "%d sec")) { roulette.IntervalSeconds = interval; changed = true; }
        var favorites = roulette.FavoritesOnly;
        if (ImGui.Checkbox("Favorites only##roulette-favorites", ref favorites)) { roulette.FavoritesOnly = favorites; changed = true; }
        ImGui.SameLine();
        var avoid = roulette.AvoidImmediateRepeat;
        if (ImGui.Checkbox("Avoid immediate repeats##roulette-repeat", ref avoid)) { roulette.AvoidImmediateRepeat = avoid; changed = true; }
        ImGui.SameLine();
        var pause = roulette.PauseInCombat;
        if (ImGui.Checkbox("Pause in combat##roulette-combat", ref pause)) { roulette.PauseInCombat = pause; changed = true; }

        ImGui.Spacing();
        UiStyles.SectionLabel("Allowed categories", theme);
        foreach (var category in Enum.GetValues<ForgeAnimationCategory>().Where(category =>
                     category != ForgeAnimationCategory.Unsorted &&
                     (configuration.ForgePremium.AnimationLibraryShowMature || category != ForgeAnimationCategory.Mature)))
        {
            var selected = roulette.Categories.Contains(category);
            if (ImGui.Checkbox($"{AnimationCategoryLabel(category)}##roulette-{category}", ref selected))
            {
                if (selected) roulette.Categories.Add(category);
                else if (roulette.Categories.Count > 1) roulette.Categories.Remove(category);
                changed = true;
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();
        if (ImGui.Button("ROLL NOW", new Vector2(160f, 36f))) plugin.ForgePlus.RollNow();
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.ForgePlus.RuntimeStatus);
        if (changed) plugin.SaveConfiguration();
    }

    private bool DrawAnimationIdCombo(string label, ref string selectedId, Func<ForgeAnimationEntry, bool>? predicate = null)
    {
        var entries = plugin.Configuration.ForgePremium.AnimationLibraryEntries
            .Where(item => predicate is null || predicate(item))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currentSelectedId = selectedId;
        var selected = entries.FirstOrDefault(item => item.Id == currentSelectedId);
        var changed = false;
        if (ImGui.BeginCombo(label, selected?.Name ?? "Choose animation"))
        {
            if (ImGui.Selectable("None", string.IsNullOrWhiteSpace(selectedId))) { selectedId = string.Empty; changed = true; }
            foreach (var item in entries)
            {
                if (!ImGui.Selectable(item.Name, item.Id == selectedId)) continue;
                selectedId = item.Id; changed = true;
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private static bool DrawNamedIdCombo(string label, ref string selectedId, IEnumerable<(string Id, string Name)> options, string emptyLabel)
    {
        var list = options.ToList();
        var currentSelectedId = selectedId;
        var selected = list.FirstOrDefault(item => item.Id == currentSelectedId);
        var preview = string.IsNullOrWhiteSpace(selected.Id) ? emptyLabel : selected.Name;
        var changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            if (ImGui.Selectable(emptyLabel, string.IsNullOrWhiteSpace(selectedId))) { selectedId = string.Empty; changed = true; }
            foreach (var option in list)
            {
                if (!ImGui.Selectable(option.Name, option.Id == selectedId)) continue;
                selectedId = option.Id; changed = true;
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private static string SceneActionLabel(ForgeSceneActionType action) => action switch
    {
        ForgeSceneActionType.UiMode => "UI Mode",
        ForgeSceneActionType.CharacterPreset => "Character Preset",
        ForgeSceneActionType.VenueProfile => "Venue Profile",
        _ => action.ToString(),
    };

    private static string ScheduledActionLabel(ForgeScheduledActionType action) => action switch
    {
        ForgeScheduledActionType.Animation => "Play Animation",
        ForgeScheduledActionType.CharacterPreset => "Apply Character Preset",
        ForgeScheduledActionType.VenueProfile => "Activate Venue Profile",
        ForgeScheduledActionType.DancerOn => "Enable Dancer",
        ForgeScheduledActionType.DancerOff => "Disable Dancer",
        ForgeScheduledActionType.RouletteOn => "Enable Roulette",
        ForgeScheduledActionType.RouletteOff => "Disable Roulette",
        _ => $"Play {action}",
    };

    private static string VirtualKeyLabel(int key) => key is >= 0x70 and <= 0x87 ? $"F{key - 0x6F}" : $"Key {key}";

    private static readonly (ForgeScheduleDays Flag, string Label)[] ScheduledDayOptions =
    {
        (ForgeScheduleDays.Sunday, "Sun"),
        (ForgeScheduleDays.Monday, "Mon"),
        (ForgeScheduleDays.Tuesday, "Tue"),
        (ForgeScheduleDays.Wednesday, "Wed"),
        (ForgeScheduleDays.Thursday, "Thu"),
        (ForgeScheduleDays.Friday, "Fri"),
        (ForgeScheduleDays.Saturday, "Sat"),
    };

    private enum ForgePlusPage
    {
        MotionLibrary,
        SceneBuilder,
        Dancer,
        CharacterPresets,
        VenueProfiles,
        EventScheduler,
        AnimationWheel,
        Roulette,
    }
}
