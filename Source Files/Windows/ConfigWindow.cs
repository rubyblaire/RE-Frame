using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Localization;
using REFrameXIV.Services;
using REFrameXIV.Theme;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string selectedJobTheme = string.Empty;

    public ConfigWindow(Plugin plugin)
        : base("RE:Frame Settings###REFrameConfig", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(700f, 760f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560f, 560f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        AllowBackgroundBlur = true;
    }

    public override void PreDraw() => UiStyles.PushWindowStyle(plugin.CurrentTheme, plugin.CurrentThemeStyle);
    public override void PostDraw() => UiStyles.PopWindowStyle();
    public override void Draw() => DrawEmbedded(plugin, ref selectedJobTheme);

    internal static void DrawEmbedded(Plugin plugin, ref string selectedJobTheme)
    {
        var c = plugin.Configuration;
        var changed = false;

        ImGui.TextWrapped(Localizer.Text("settings.intro", "Configure RE:Frame's interface, layout, appearance, and presentation features."));
        TextDisabledWrapped(Localizer.Text("settings.edit.help", "Use Edit HUD for per-mode placement and visibility. Use Edit Bars & Keys to arrange actions and change native or RE:Frame overflow hotbar controls."));
        if (ImGui.Button(Localizer.Text("settings.tour.open", "Open Tour Guide"), new Vector2(MathF.Min(220f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
            plugin.OpenTourGuide();
        TextDisabledWrapped(Localizer.Text("settings.tour.help", "Revisit the friendly RE:Frame tour at any time. It explains the interface without changing your current choices."));

        Section(Localizer.Text("settings.language.section", "Language"), plugin);
        TextDisabledWrapped(Localizer.Text("settings.language.help", "Automatic follows the language reported by FFXIV/Dalamud. English is used whenever a translated entry is unavailable."));
        DrawControlLabel(Localizer.Text("settings.language.label", "RE:Frame interface language"));
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##REFrameLanguage", Localizer.LanguageName(c.LanguagePreference)))
        {
            foreach (var language in Enum.GetValues<PluginLanguage>())
            {
                var selected = c.LanguagePreference == language;
                if (ImGui.Selectable(Localizer.LanguageName(language), selected))
                {
                    c.LanguagePreference = language;
                    Localizer.Reload();
                    changed = true;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        var effectiveLanguage = Localizer.CurrentLanguage;
        TextDisabledWrapped(Localizer.Format("settings.language.effective", "Currently displaying {0}.", Localizer.LanguageName(effectiveLanguage)));
        TextDisabledWrapped(Localizer.Format(
            "settings.language.loaded",
            "Loaded {0} translated interface entries.",
            Localizer.LoadedEntryCount(effectiveLanguage)));

        Section("Interface", plugin);
        TextDisabledWrapped("Native FFXIV UI ownership and third-party compatibility now live on the Interface page in the main sidebar. This keeps Settings focused on general behavior and presentation.");

        Section("Layout", plugin);
        DrawHudLayoutButtons(plugin, c);
        changed |= DrawBool("Show alignment grid in Edit HUD", c.ShowHudEditorGrid, v => c.ShowHudEditorGrid = v);
        if (c.ShowHudEditorGrid)
            changed |= DrawFloat("Grid size", c.HudEditorGridSize, 4f, 64f, "%.0f px", v => c.HudEditorGridSize = v);
        changed |= DrawBool("Apply job presets automatically after changing jobs", c.AutoApplyHudPresets, v => c.AutoApplyHudPresets = v);

        Section("HUD elements", plugin);

        changed |= DrawHotbarManager(plugin, c);

        DrawControlLabel("STATUS EFFECT PRESENTATION");
        var statusDisplayScale = HudRenderer.ResolveRaidStatusDisplayScale(
            c.InterfaceScale,
            HudCanvas.Current().Size);
        TextDisabledWrapped($"Buffs and Debuffs automatically follow the active display ({statusDisplayScale * 100f:0}% effective scale). Interface Scale remains the manual multiplier. Background transparency does not affect icons, timers, titles, or tooltips.");
        changed |= DrawBool("Transparent Buffs background", c.TransparentRaidBuffBackground, v => c.TransparentRaidBuffBackground = v);
        changed |= DrawBool("Transparent Debuffs background", c.TransparentRaidDebuffBackground, v => c.TransparentRaidDebuffBackground = v);

        changed |= DrawBool(
            "ARDYN'S CHANT",
            c.ShowArdynChantButton,
            v =>
            {
                c.ShowArdynChantButton = v;
                if (!v)
                    plugin.StopArdynChant();
            });
        if (c.ShowArdynChantButton)
        {
            TextDisabledWrapped("HE'S THE HIDDEN KING, YEAH");
            DrawControlLabel("Ardyn's Chant volume");
            var ardynVolumePercent = (int)MathF.Round(Math.Clamp(c.ArdynChantVolume, 0f, 1f) * 100f);
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderInt("##ArdynChantVolume", ref ardynVolumePercent, 0, 100, "%d%%"))
            {
                c.ArdynChantVolume = ardynVolumePercent / 100f;
                changed = true;
            }
        }

        DrawControlLabel("CONSUMABLES BAR SETTINGS");
        TextDisabledWrapped("Enter part of an item name to prefer a specific food or potion. Leave either field blank to use the highest item-level matching consumable in your inventory.");
        DrawControlLabel("Preferred food (optional)");
        ImGui.SetNextItemWidth(-1f);
        var foodOverride = c.RaidersKitFoodOverride ?? string.Empty;
        if (ImGui.InputText("##RaidersKitFood", ref foodOverride, 96))
        {
            c.RaidersKitFoodOverride = foodOverride;
            changed = true;
        }
        DrawControlLabel("Preferred potion (optional)");
        ImGui.SetNextItemWidth(-1f);
        var potionOverride = c.RaidersKitPotionOverride ?? string.Empty;
        if (ImGui.InputText("##RaidersKitPotion", ref potionOverride, 96))
        {
            c.RaidersKitPotionOverride = potionOverride;
            changed = true;
        }

        DrawControlLabel("LIMIT BREAK SETTINGS");
        DrawControlLabel("Limit Break gauge layout");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##LimitBreakLayout", c.LimitBreakLayout switch { LimitBreakLayout.Stacked => "Stacked", LimitBreakLayout.Diagonal45 => "45°", _ => "Horizontal" }))
        {
            foreach (var layout in Enum.GetValues<LimitBreakLayout>())
            {
                var label = layout switch { LimitBreakLayout.Stacked => "Stacked", LimitBreakLayout.Diagonal45 => "45°", _ => "Horizontal" };
                var selected = c.LimitBreakLayout == layout;
                if (ImGui.Selectable(label, selected))
                {
                    c.LimitBreakLayout = layout;
                    changed = true;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        Section("Appearance", plugin);
        var activeForge = ForgeThemeLibrary.GetActive(c);
        if (activeForge is not null)
            TextDisabledWrapped($"The Forge is active: {activeForge.Name}. Built-in theme choices below remain your fallback when you return to the standard theme system.");
        changed |= DrawBool("Use job colors", c.FollowJobColors, v => c.FollowJobColors = v);
        if (c.FollowJobColors)
        {
            TextDisabledWrapped("Choose any job and edit both ends of its HUD gradient.");
            if (string.IsNullOrWhiteSpace(selectedJobTheme) || !JobThemeProvider.IsSupportedJob(selectedJobTheme))
            {
                var currentJob = plugin.GetJobAbbreviation();
                selectedJobTheme = JobThemeProvider.IsSupportedJob(currentJob) ? currentJob : JobThemeProvider.AllJobs[0];
            }

            DrawControlLabel("Job to customize");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("##JobToCustomize", JobThemeProvider.JobLabel(selectedJobTheme)))
            {
                foreach (var job in JobThemeProvider.AllJobs)
                {
                    var selected = string.Equals(selectedJobTheme, job, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(JobThemeProvider.JobLabel(job), selected)) selectedJobTheme = job;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            JobThemeProvider.TryGetDefaultJobColors(selectedJobTheme, out var defaultAccent, out var defaultHighlight);
            var hasOverride = c.JobThemeColors.TryGetValue(selectedJobTheme, out var storedOverride) && storedOverride is not null;
            var editable = hasOverride
                ? JobThemeColorOverride.From(storedOverride!.Accent, storedOverride.Highlight)
                : JobThemeColorOverride.From(defaultAccent, defaultHighlight);

            var accent = new Vector3(editable.Accent.X, editable.Accent.Y, editable.Accent.Z);
            var highlight = new Vector3(editable.Highlight.X, editable.Highlight.Y, editable.Highlight.Z);
            DrawControlLabel("Gradient start");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.ColorEdit3("##JobGradientStart", ref accent))
            {
                editable.SetAccent(accent);
                c.JobThemeColors[selectedJobTheme] = editable;
                changed = true;
            }
            DrawControlLabel("Gradient finish");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.ColorEdit3("##JobGradientFinish", ref highlight))
            {
                editable.SetHighlight(highlight);
                c.JobThemeColors[selectedJobTheme] = editable;
                changed = true;
            }
            if (ImGui.Button("Reset selected job to default", new Vector2(MathF.Min(220f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 30f)) && c.JobThemeColors.Remove(selectedJobTheme))
                changed = true;
            DrawThemePreview(JobThemeProvider.Get(selectedJobTheme, true, c.SelectedTheme, c.JobThemeColors));
        }

        if (c.FollowJobColors) ImGui.BeginDisabled();
        DrawControlLabel("Default theme");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##GeneralHudTheme", ThemePresetInfo.Label(c.SelectedTheme)))
        {
            foreach (var preset in ThemePresetInfo.All)
            {
                var selected = c.SelectedTheme == preset;
                if (ImGui.Selectable(ThemePresetInfo.Label(preset), selected))
                {
                    c.SelectedTheme = preset;
                    changed = true;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        if (c.FollowJobColors) ImGui.EndDisabled();
        if (!c.FollowJobColors) DrawThemePreview(JobThemeProvider.GetPreset(c.SelectedTheme));
        changed |= DrawBool("Reduce interface motion", c.ReducedMotion, v => c.ReducedMotion = v);
        changed |= DrawBool("Enhanced action highlights", c.EnhancedActionHighlights, v => c.EnhancedActionHighlights = v);
        changed |= DrawFloat("Interface scale", c.InterfaceScale, 0.60f, 2.50f, "%.2f", v => c.InterfaceScale = v);
        changed |= DrawFloat("Text scale", c.TextScale, 0.75f, 1.75f, "%.2f", v => c.TextScale = v);
        changed |= DrawFloat("HUD opacity", c.HudOpacity, 0.35f, 1f, "%.2f", v => c.HudOpacity = v);
        DrawControlLabel("Display fit");
        var viewport = HudCanvas.Current().Size;
        var layoutWidth = Math.Max(1, (int)MathF.Round(viewport.X));
        var layoutHeight = Math.Max(1, (int)MathF.Round(viewport.Y));
        var detectedDisplay = DisplayResolutionService.Detect(viewport);
        TextDisabledWrapped($"Current FFXIV client: {detectedDisplay.ClientWidth} × {detectedDisplay.ClientHeight}  •  Monitor: {detectedDisplay.MonitorWidth} × {detectedDisplay.MonitorHeight}");
        if (detectedDisplay.ClientWidth != layoutWidth || detectedDisplay.ClientHeight != layoutHeight)
            TextDisabledWrapped($"RE:Frame placement canvas: {layoutWidth} × {layoutHeight}");

        if (ImGui.Button("Copy canvas diagnostics", new Vector2(-1f, 30f)))
        {
            var diagnostic = HudCanvas.DiagnosticText();
            ImGui.SetClipboardText(diagnostic);
            Plugin.ChatGui.Print("RE:Frame copied viewport, DPI, canvas, and mouse diagnostics to the clipboard.");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copies the exact coordinate spaces used by RE:Frame. Useful when the editor bounds or clicks do not match the game window.");

        if (ImGui.Button($"Fit HUD to Current Display · {layoutWidth} × {layoutHeight}", new Vector2(-1f, 34f)))
            plugin.FitHudToViewport(viewport);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Preserve practical element sizes and screen anchoring while fitting every HUD mode to the current FFXIV window.");

        DrawRecentLayoutHistory(plugin);
        DrawControlLabel("Cooldown style");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##CooldownStyle", CooldownDisplayStyleInfo.Label(c.CooldownStyle)))
        {
            foreach (var style in Enum.GetValues<CooldownDisplayStyle>())
            {
                var selected = c.CooldownStyle == style;
                if (ImGui.Selectable(CooldownDisplayStyleInfo.Label(style), selected))
                {
                    c.CooldownStyle = style;
                    changed = true;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        DrawControlLabel("Cooldown number color");
        var cooldownColor = c.CooldownTextColor;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.ColorEdit4("##CooldownTextColor", ref cooldownColor)) { c.CooldownTextColor = cooldownColor; changed = true; }
        Section("Targeting", plugin);
        changed |= DrawBool("Sticky Targeting", c.StickyTargeting, v => c.StickyTargeting = v);
        TextDisabledWrapped("When enabled, Escape keeps your current target selected. Turn this off to let a fresh Escape press clear the current target.");

        Section("Automatic modes", plugin);
        changed |= DrawBool("Keep Quest mode active outside combat or work", c.StickyQuestMode, v => { c.StickyQuestMode = v; if (v) c.ModeOverride = UiMode.Auto; });

        Section("Startup", plugin);
        var splashButtonLabel = c.EnableGlitchSplashScreen
            ? "Disable Glitch Splash"
            : "Enable Glitch Splash";
        if (ImGui.Button(splashButtonLabel, new Vector2(MathF.Min(240f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
            plugin.SetGlitchSplashEnabled(!c.EnableGlitchSplashScreen);
        TextDisabledWrapped(c.EnableGlitchSplashScreen
            ? "The RE:Frame glitch logo transition plays during startup, refreshes, activation, and when opening the main window."
            : "The glitch splash is disabled. RE:Frame opens and refreshes immediately without the full-screen transition.");

        Section("AFK presentation", plugin);
        changed |= DrawBool("Show the AFK screen after inactivity", c.EnableAfkScreen, v => c.EnableAfkScreen = v);
        if (c.EnableAfkScreen)
        {
            DrawControlLabel("Inactivity delay");
            var afkTimeout = Math.Clamp(c.AfkTimeoutMinutes, 1, 120);
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderInt("##AfkTimeoutMinutes", ref afkTimeout, 1, 120, "%d minutes"))
            {
                c.AfkTimeoutMinutes = afkTimeout;
                changed = true;
            }

            changed |= DrawBool("Play AFK audio", c.AfkScreenAudioEnabled, v => c.AfkScreenAudioEnabled = v);
            if (c.AfkScreenAudioEnabled)
                changed |= DrawFloat("AFK screen volume", c.AfkScreenVolume, 0f, 1f, "%.2f", v => c.AfkScreenVolume = v);
        }

        var afkPreviewLabel = plugin.AfkScreen.IsActive ? "Stop AFK Preview" : "Preview AFK Screen";
        if (ImGui.Button(afkPreviewLabel, new Vector2(MathF.Min(220f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
            plugin.ToggleAfkScreenPreview();

        Section("Greeting", plugin);
        changed |= DrawBool("Show a time-aware greeting in Leisure mode", c.ShowLoginGreeting, v => c.ShowLoginGreeting = v);
        changed |= DrawFloat("Greeting voice volume", c.GreetingVoiceVolume, 0f, 1f, "%.2f", v => c.GreetingVoiceVolume = v);
        changed |= DrawGreetingVoicePackSelection(c);
        if (ImGui.Button("Preview Greeting", new Vector2(MathF.Min(220f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
            plugin.PreviewLoginGreeting();

        if (plugin.ForgeAccess.HasAccess)
        {
            ImGui.SameLine();
            if (ImGui.Button("VOICE DIRECTOR & IMPORTS", new Vector2(MathF.Min(250f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
                plugin.OpenForgeImmersionPage();
            TextDisabledWrapped("Custom voice-pack importing is managed in RE:Forge → Immersion → Voice Director.");
        }
        else
        {
            TextDisabledWrapped("Custom voice-pack importing is a RE:Forge membership feature. The free interface can select and preview installed voices, but it cannot add or import new packs.");
        }

        TextDisabledWrapped("Uses the computer's local time and the logged-in character name: Good Morning from 5:00 AM–11:59 AM, Good Afternoon from 12:00 PM–4:59 PM, and Good Evening from 5:00 PM–4:59 AM.");

        if (changed)
        {
            plugin.SaveConfiguration();
            plugin.NativeHudVisibility.RefreshNow();
            plugin.NativeWindows.ApplyConfigurationChange();
            plugin.AfkScreen.ApplyConfigurationChange();
        }
    }


    private static bool DrawGreetingVoicePackSelection(Configuration configuration)
    {
        GreetingVoicePackService.NormalizeConfiguration(configuration);
        var changed = false;
        var activeId = configuration.ActiveGreetingVoicePackId;
        var activeName = GreetingVoicePackService.GetDisplayName(configuration, activeId);

        DrawControlLabel("Active voice pack");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##SettingsActiveGreetingVoicePack", activeName))
        {
            changed |= DrawGreetingVoicePackOption(
                configuration,
                GreetingVoicePackService.JarvinPackId,
                GreetingVoicePackService.JarvinPackName,
                true);
            changed |= DrawGreetingVoicePackOption(
                configuration,
                GreetingVoicePackService.RubyPackId,
                GreetingVoicePackService.RubyPackName,
                true);

            foreach (var pack in configuration.CustomGreetingVoicePacks.OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase))
            {
                var ready = GreetingVoicePackService.IsPackReady(pack, out _);
                var displayName = GreetingVoicePackService.GetDisplayName(configuration, pack.Id);
                changed |= DrawGreetingVoicePackOption(
                    configuration,
                    pack.Id,
                    ready ? $"{displayName} · Imported" : $"{displayName} · Missing files",
                    ready);
            }

            ImGui.EndCombo();
        }

        if (configuration.CustomGreetingVoicePacks.Count > 0)
            TextDisabledWrapped("Existing imported packs remain listed here. Adding, replacing, or removing custom pack files is reserved for the RE:Forge Voice Director.");

        return changed;
    }

    private static bool DrawGreetingVoicePackOption(
        Configuration configuration,
        string packId,
        string label,
        bool ready)
    {
        var selected = string.Equals(configuration.ActiveGreetingVoicePackId, packId, StringComparison.OrdinalIgnoreCase);
        if (!ready)
            ImGui.BeginDisabled();

        var changed = false;
        if (ImGui.Selectable(label, selected) && ready && !selected)
        {
            configuration.ActiveGreetingVoicePackId = packId;
            changed = true;
        }
        if (selected)
            ImGui.SetItemDefaultFocus();

        if (!ready)
            ImGui.EndDisabled();
        return changed;
    }

    private static bool DrawHotbarManager(Plugin plugin, Configuration configuration)
    {
        var changed = false;
        DrawControlLabel("HOTBAR EXPANSION");
        TextDisabledWrapped(
            "FFXIV provides ten native keyboard hotbars with twelve slots each. " +
            "RE:Frame keeps Combat Hotbars 1–3 and Utility Hotbar 1 on their current native bars, " +
            "uses the remaining native bars first, then creates as many RE:Frame overflow hotbars as you need.");

        var remainingNative = plugin.AdditionalHotbars.RemainingNativeCombatBars;
        TextDisabledWrapped(remainingNative > 0
            ? $"{remainingNative} unused native FFXIV hotbar{(remainingNative == 1 ? string.Empty : "s")} remain before new bars become RE:Frame overflow bars."
            : "All ten native FFXIV keyboard hotbars are represented. New combat bars will be RE:Frame overflow bars.");

        if (remainingNative > 0 && ImGui.Button(
                $"ADD ALL {remainingNative} REMAINING NATIVE BARS",
                new Vector2(MathF.Min(320f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
        {
            plugin.AdditionalHotbars.AddAllRemainingNativeCombatHotbars();
            plugin.AdditionalHotbars.EnsureValid();
            plugin.RequestAdditionalHotbarWindowSync();
            changed = true;
        }

        if (ImGui.Button(
                "+ ADD COMBAT HOTBAR",
                new Vector2(MathF.Min(240f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
        {
            plugin.AdditionalHotbars.AddCombatHotbar();
            plugin.AdditionalHotbars.EnsureValid();
            plugin.RequestAdditionalHotbarWindowSync();
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Uses an unused native FFXIV hotbar when possible; otherwise creates a fully interactive RE:Frame overflow bar.");

        ImGui.SameLine();
        if (ImGui.Button(
                "EDIT BARS & KEYS",
                new Vector2(MathF.Min(220f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
            plugin.OpenSkillEdit();

        changed |= DrawBool(
            "Enable Utility Hotbar 2",
            configuration.ShowSecondUtilityBarFrames,
            value => configuration.ShowSecondUtilityBarFrames = value);
        TextDisabledWrapped("Utility Hotbar 2 is an independently movable 4×3 RE:Frame bar with twelve action slots and per-slot keybinds.");
        TextDisabledWrapped("The native Pet Bar is also independently movable, supports 12×1 / 6×2 / 4×3 / 3×4 layouts in Edit HUD, and can receive primary and secondary RE:Frame keybinds from Edit Bars & Keys.");

        uint? removeId = null;
        foreach (var bar in plugin.AdditionalHotbars.CombatBars)
        {
            ImGui.PushID($"AdditionalHotbar{bar.Id}");
            ImGui.Separator();

            var enabled = bar.Enabled;
            if (ImGui.Checkbox("##Enabled", ref enabled))
            {
                bar.Enabled = enabled;
                changed = true;
            }
            ImGui.SameLine();

            var name = bar.Name ?? string.Empty;
            ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - 112f));
            if (ImGui.InputText("##Name", ref name, 64))
            {
                bar.Name = string.IsNullOrWhiteSpace(name)
                    ? (bar.IsNativeBacked ? $"Combat Hotbar {bar.NativeHotbarId + 1}" : $"Overflow Hotbar {bar.Id}")
                    : name.Trim();
                changed = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("REMOVE", new Vector2(96f, 0f)))
                removeId = bar.Id;

            TextDisabledWrapped(bar.IsNativeBacked
                ? $"Native-backed · FFXIV Hotbar {bar.NativeHotbarId + 1} · 12 slots · click, drag/drop, shape, and keybind supported"
                : "RE:Frame overflow · 12 action/crafting-action slots · click, drag/drop, shape, and keybind supported");
            ImGui.PopID();
        }

        if (removeId is { } id && plugin.AdditionalHotbars.RemoveCombatHotbar(id))
        {
            plugin.AdditionalHotbars.EnsureValid();
            plugin.RequestAdditionalHotbarWindowSync();
            changed = true;
        }

        return changed;
    }

    internal static void DrawInterfaceOwnership(Plugin plugin)
    {
        var c = plugin.Configuration;
        var changed = false;

        ImGui.TextWrapped("Choose exactly which parts of FFXIV's native interface RE:Frame is allowed to replace. Disable ownership for any element managed by DelvUI or another plugin.");
        TextDisabledWrapped("Nameplates are preserved by default. Changes apply immediately and do not alter your RE:Frame HUD layouts.");

        if (ImGui.Button("THIRD-PARTY UI COMPATIBILITY", new Vector2(MathF.Min(300f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 38f)))
        {
            c.OverrideNativeNameplates = false;
            c.SkinNativeContextMenus = false;
            c.SkinNativeWindows = false;
            c.NativeWindowGlassEffect = false;
            c.FrameNativeHoldouts = false;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Preserve native nameplates and stop styling native windows so dedicated UI plugins retain control.");

        ImGui.SameLine();
        if (ImGui.Button("RE:FRAME DEFAULTS", new Vector2(MathF.Min(220f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 38f)))
        {
            c.OverrideNativeNameplates = false;
            c.OverrideNativeLocationAndParameter = true;
            c.OverrideNativeCurrencyAndInventory = true;
            c.OverrideNativePlayerCastBar = true;
            c.OverrideNativePartyList = true;
            c.OverrideNativeAllianceLists = true;
            c.OverrideNativeTargetInfo = true;
            c.OverrideNativeFocusTarget = true;
            c.OverrideNativeEnemyList = true;
            c.OverrideNativeStatusEffects = true;
            c.OverrideNativeJobGauges = true;
            c.OverrideNativeQuestElements = true;
            c.HideNativeActionBars = true;
            c.ReplaceNativeCrossHotbar = true;
            changed = true;
        }

        Section("Master control", plugin);
        changed |= DrawBool("Replace supported FFXIV HUD elements", c.ReplaceNativeHud, v => c.ReplaceNativeHud = v);
        if (!c.ReplaceNativeHud)
            TextDisabledWrapped("All native FFXIV HUD elements are currently restored. The ownership choices below are retained for the next time replacement mode is enabled.");

        Section("Compatibility", plugin);
        changed |= DrawBool("Allow RE:Frame to override native nameplates", c.OverrideNativeNameplates, v => c.OverrideNativeNameplates = v);
        TextDisabledWrapped("Leave this disabled when using DelvUI or another plugin for nameplates. RE:Frame will actively release and restore the native nameplate addon.");
        changed |= DrawBool("Style character context menus", c.SkinNativeContextMenus, v => c.SkinNativeContextMenus = v);
        changed |= DrawBool("Style supported FFXIV windows", c.SkinNativeWindows, v => c.SkinNativeWindows = v);
        changed |= DrawBool("Add a glass backdrop to styled windows", c.NativeWindowGlassEffect, v => c.NativeWindowGlassEffect = v);
        if (c.NativeWindowGlassEffect)
            changed |= DrawFloat("Glass opacity", c.NativeWindowGlassOpacity, 0.20f, 1f, "%.2f", v => c.NativeWindowGlassOpacity = v);
        changed |= DrawBool("Frame remaining native UI elements", c.FrameNativeHoldouts, v => c.FrameNativeHoldouts = v);

        Section("Native HUD ownership", plugin);
        changed |= DrawBool("Location, server, experience, and parameter widgets", c.OverrideNativeLocationAndParameter, v => c.OverrideNativeLocationAndParameter = v);
        changed |= DrawBool("Gil display", c.OverrideNativeCurrencyAndInventory, v => c.OverrideNativeCurrencyAndInventory = v);
        changed |= DrawBool("Player cast bar", c.OverrideNativePlayerCastBar, v => c.OverrideNativePlayerCastBar = v);
        changed |= DrawBool("Party list", c.OverrideNativePartyList, v => c.OverrideNativePartyList = v);
        changed |= DrawBool("Alliance lists", c.OverrideNativeAllianceLists, v => c.OverrideNativeAllianceLists = v);
        changed |= DrawBool("Target information and enemy cast bar", c.OverrideNativeTargetInfo, v => c.OverrideNativeTargetInfo = v);
        changed |= DrawBool("Focus target", c.OverrideNativeFocusTarget, v => c.OverrideNativeFocusTarget = v);
        changed |= DrawBool("Enemy list", c.OverrideNativeEnemyList, v => c.OverrideNativeEnemyList = v);
        changed |= DrawBool("Player and target status effects", c.OverrideNativeStatusEffects, v => c.OverrideNativeStatusEffects = v);
        changed |= DrawBool("Job gauges", c.OverrideNativeJobGauges, v => c.OverrideNativeJobGauges = v);
        changed |= DrawBool("Scenario guide, duty list, and quest trackers", c.OverrideNativeQuestElements, v => c.OverrideNativeQuestElements = v);
        changed |= DrawBool("Hide FFXIV hotbars while RE:Frame is active", c.HideNativeActionBars, v => c.HideNativeActionBars = v);
        changed |= DrawBool("Replace the FFXIV controller cross hotbar", c.ReplaceNativeCrossHotbar, v => c.ReplaceNativeCrossHotbar = v);

        Section("Interaction", plugin);
        changed |= DrawBool("Enable mouse controls", c.EnableHudMouseInteraction, v => c.EnableHudMouseInteraction = v);
        changed |= DrawBool("Enable mouseover targeting for party frames", c.EnableMouseoverTargeting, v => c.EnableMouseoverTargeting = v);
        changed |= DrawBool("Open the FFXIV character menu on right-click", c.RightClickOpensNativeContextMenu, v => c.RightClickOpensNativeContextMenu = v);

        if (ImGui.Button("RESTORE ORIGINAL FFXIV UI", new Vector2(MathF.Min(270f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 38f)))
        {
            c.ReplaceNativeHud = false;
            changed = true;
        }

        TextDisabledWrapped(c.ReplaceNativeHud
            ? $"RE:Frame active · {plugin.NativeHudVisibility.HiddenAddonCount} native addons currently replaced"
            : "Original FFXIV UI active");

        if (changed)
        {
            plugin.SaveConfiguration();
            plugin.NativeHudVisibility.RefreshNow();
            plugin.NativeWindows.ApplyConfigurationChange();
        }
    }

    private static void DrawRecentLayoutHistory(Plugin plugin)
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Recent Layout History"))
            return;

        plugin.LayoutRecovery.EnsureValid();
        var snapshots = plugin.LayoutRecovery.Snapshots;
        if (snapshots.Count == 0)
        {
            TextDisabledWrapped("Recovery snapshots will appear here before preset imports, resolution fitting, resets, preset overwrites, and major dock copies.");
            return;
        }

        string? restoreId = null;
        string? deleteId = null;
        foreach (var snapshot in snapshots)
        {
            ImGui.PushID(snapshot.Id);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.36f));
            if (ImGui.BeginChild("##layout-history-entry", new Vector2(0f, 74f), true, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.TextUnformatted(snapshot.Reason);
                ImGui.TextDisabled(snapshot.CreatedUtc.ToLocalTime().ToString("g"));
                if (ImGui.Button("RESTORE", new Vector2(96f, 27f)))
                    restoreId = snapshot.Id;
                ImGui.SameLine();
                if (ImGui.Button("DELETE", new Vector2(86f, 27f)))
                    deleteId = snapshot.Id;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopID();
            ImGui.Spacing();
        }

        if (restoreId is not null)
        {
            if (plugin.LayoutRecovery.Restore(restoreId, out var message))
                Plugin.ChatGui.Print(message);
            else
                Plugin.ChatGui.PrintError(message);
        }
        if (deleteId is not null)
            plugin.LayoutRecovery.Delete(deleteId);
    }

    private static void Section(string label, Plugin plugin)
    {
        if (ImGui.GetCursorPosY() > 4f)
        {
            ImGui.Spacing();
            UiStyles.Divider(plugin.CurrentTheme);
            ImGui.Spacing();
        }

        UiStyles.SectionLabel(label, plugin.CurrentTheme);
    }

    private static bool DrawBool(string label, bool value, Action<bool> apply)
    {
        var copy = value;
        var changed = ImGui.Checkbox($"##bool_{label}", ref copy);
        ImGui.SameLine();
        ImGui.TextWrapped(label);
        if (!changed) return false;
        apply(copy);
        return true;
    }

    private static bool DrawFloat(string label, float value, float min, float max, string format, Action<float> apply)
    {
        DrawControlLabel(label);
        var copy = value;
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.SliderFloat($"##float_{label}", ref copy, min, max, format)) return false;
        apply(copy);
        return true;
    }

    internal static bool DrawCustomIntegrations(Plugin plugin, Configuration configuration)
    {
        var changed = false;
        string? removeId = null;

        for (var index = 0; index < configuration.CustomIntegrations.Count; index++)
        {
            var integration = configuration.CustomIntegrations[index];
            integration.EnsureValid();
            ImGui.PushID(integration.Id);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.42f));
            if (ImGui.BeginChild("##custom-integration", new Vector2(0f, 154f), true, ImGuiWindowFlags.NoScrollbar))
            {
                DrawControlLabel("Display name");
                var name = integration.Name;
                ImGui.SetNextItemWidth(-1f);
                if (DrawTranslucentInput("##custom-name", ref name, 64))
                {
                    integration.Name = name;
                    changed = true;
                }

                DrawControlLabel("Slash command");
                var command = integration.Command;
                ImGui.SetNextItemWidth(-1f);
                if (DrawTranslucentInput("##custom-command", ref command, 128))
                {
                    integration.Command = command;
                    changed = true;
                }

                var displayName = string.IsNullOrWhiteSpace(integration.Name)
                    ? "Custom Integration"
                    : integration.Name.Trim();
                var normalized = integration.Command?.Trim() ?? string.Empty;
                var root = string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.Split(' ')[0];
                var detected = !string.IsNullOrWhiteSpace(root) && Plugin.CommandManager.Commands.ContainsKey(root);
                ImGui.TextDisabled(detected ? "Command detected" : "Command not detected");

                if (ImGui.Button("OPEN", new Vector2(96f, 29f)))
                    plugin.RunIntegrationCommand(normalized, displayName);
                ImGui.SameLine();
                if (ImGui.Button("REMOVE", new Vector2(96f, 29f)))
                    removeId = integration.Id;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopID();
            ImGui.Spacing();
        }

        if (removeId is not null)
        {
            configuration.CustomIntegrations.RemoveAll(integration =>
                string.Equals(integration.Id, removeId, StringComparison.Ordinal));
            changed = true;
        }

        if (ImGui.Button("+ ADD CUSTOM INTEGRATION", new Vector2(MathF.Min(250f, MathF.Max(1f, ImGui.GetContentRegionAvail().X)), 34f)))
        {
            configuration.CustomIntegrations.Add(new CustomIntegration());
            changed = true;
        }

        return changed;
    }

    private static bool DrawTranslucentInput(string id, ref string value, int maxLength)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.94f, 0.96f, 1.00f, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.96f, 0.98f, 1.00f, 0.29f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.98f, 0.99f, 1.00f, 0.36f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.90f, 0.94f, 1.00f, 0.68f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.98f, 0.99f, 1.00f, 1f));
        var edited = ImGui.InputText(id, ref value, maxLength);
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar();
        return edited;
    }

    private static void DrawControlLabel(string label) => ImGui.TextWrapped(label);

    private static void TextDisabledWrapped(string text)
    {
        var wrapAt = ImGui.GetCursorPosX() + MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.PushTextWrapPos(wrapAt);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }

    private static void DrawHudLayoutButtons(Plugin plugin, Configuration configuration)
    {
        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        const float preferredWidth = 220f;
        var sideBySide = available >= preferredWidth * 2f + spacing;
        var buttonWidth = sideBySide ? preferredWidth : available;
        if (ImGui.Button("Edit HUD", new Vector2(buttonWidth, 38f))) plugin.SetHudEditMode(true);
        if (sideBySide) ImGui.SameLine();
        if (ImGui.Button("Edit Bars & Keys", new Vector2(buttonWidth, 38f))) plugin.ToggleBarEditMode();
        if (ImGui.Button("Reset All HUD Layouts", new Vector2(-1f, 38f)))
        {
            plugin.LayoutRecovery.Create("Before Full HUD Reset");
            HudLayout.ResetAll(configuration);
            plugin.LayoutHistory.Clear();
            plugin.SaveConfiguration();
            plugin.NativeHudVisibility.RefreshNow();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Restore the default positions and sizes for Leisure, Roleplay, Quest, Raid, and Work. A recovery snapshot is created first.");
    }

    private static void DrawThemePreview(ThemePalette theme)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        const float height = 18f;
        var mid = start.X + width * 0.5f;
        var end = start + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(start, new Vector2(mid, end.Y), ImGui.GetColorU32(theme.GradientStart), ImGui.GetColorU32(theme.GradientMid), ImGui.GetColorU32(theme.GradientMid), ImGui.GetColorU32(theme.GradientStart));
        draw.AddRectFilledMultiColor(new Vector2(mid, start.Y), end, ImGui.GetColorU32(theme.GradientMid), ImGui.GetColorU32(theme.GradientEnd), ImGui.GetColorU32(theme.GradientEnd), ImGui.GetColorU32(theme.GradientMid));
        draw.AddRect(start, end, ImGui.GetColorU32(UiStyles.WithAlpha(theme.Text, 0.28f)), 5f, ImDrawFlags.None, 1f);
        ImGui.Dummy(new Vector2(width, height + 4f));
    }

    public void Dispose() { }
}
