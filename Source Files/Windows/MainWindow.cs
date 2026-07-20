using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Localization;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly bool forgeStandalone;
    private MainPage page = MainPage.Docks;
    private string selectedJobTheme = string.Empty;
    private string selectedDockButtonEditor = DockButtonCatalog.Leisure;
    private string presetName = "My HUD";
    private string copySourceJob = string.Empty;
    private string copySourcePreset = string.Empty;
    private string importCode = string.Empty;
    private string importName = string.Empty;
    private string shareCode = string.Empty;
    private string offlineShareCode = string.Empty;
    private string presetStatus = string.Empty;
    private Task<ShortCodeResult>? shortCodeTask;
    private ShortCodeTaskKind shortCodeTaskKind;
    private CancellationTokenSource? shortCodeCancellation;
    private string pendingImportName = string.Empty;
    private readonly FileDialogManager voicePackFileDialog = new();
    private readonly string[] newVoicePackFiles = new string[GreetingVoicePackService.MaxGreetingSets * 3];
    private string newVoicePackName = string.Empty;
    private int newVoicePackSetCount = 1;
    private string voicePackStatus = string.Empty;

    public MainWindow(Plugin plugin, bool forgeStandalone = false)
        : base(
            forgeStandalone ? "RE:Forge###REFrameForge" : "RE:Frame XIV###REFrameMain",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.forgeStandalone = forgeStandalone;
        Size = forgeStandalone ? new Vector2(1160f, 800f) : new Vector2(1180f, 780f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = forgeStandalone ? new Vector2(900f, 640f) : new Vector2(960f, 640f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        AllowBackgroundBlur = true;
    }

    public override void PreDraw() => UiStyles.PushWindowStyle(plugin.CurrentTheme, plugin.CurrentThemeStyle);
    public override void PostDraw() => UiStyles.PopWindowStyle();

    public override void Draw()
    {
        if (forgeStandalone)
        {
            DrawStandaloneForgeWindow();
            voicePackFileDialog.Draw();
            return;
        }

        DrawSidebar();
        ImGui.SameLine();
        if (ImGui.BeginChild("##reframe-main-content", Vector2.Zero, false))
        {
            DrawHeader();
            ImGui.Spacing();
            UiStyles.Divider(plugin.CurrentTheme);
            ImGui.Spacing();

            switch (page)
            {
                case MainPage.Docks: DrawDocks(); break;
                case MainPage.JobPresets: DrawJobPresets(); break;
                case MainPage.Halo: DrawHalo(); break;
                case MainPage.Integrations: DrawIntegrations(); break;
                case MainPage.Safety: DrawSafety(); break;
                case MainPage.Interface: ConfigWindow.DrawInterfaceOwnership(plugin); break;
                case MainPage.Settings: ConfigWindow.DrawEmbedded(plugin, ref selectedJobTheme); break;
                default: DrawDocks(); break;
            }
        }
        ImGui.EndChild();
    }

    internal void ShowIntegrationsPage()
    {
        page = MainPage.Integrations;
        IsOpen = true;
        BringToFront();
    }

    internal void ShowForgeOverviewPage()
    {
        forgePage = ForgePage.Overview;
        IsOpen = true;
        BringToFront();
    }

    internal void ShowForgeImmersionPage()
    {
        forgePage = ForgePage.Immersion;
        forgeImmersionPage = ForgeImmersionPage.VoiceDirector;
        IsOpen = true;
        BringToFront();
    }

    private void DrawSidebar()
    {
        var theme = plugin.CurrentTheme;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(theme.PanelAlt, 0.76f));
        if (ImGui.BeginChild("##reframe-sidebar", new Vector2(228f, 0f), true))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, theme.AccentStrong);
            ImGui.SetWindowFontScale(1.22f);
            ImGui.TextUnformatted("RE:FRAME XIV");
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();
            ImGui.TextDisabled("A Ruby Blaire Overhaul");
            ImGui.Spacing();
            UiStyles.Divider(theme);
            ImGui.Spacing();

            DrawNav(MainPage.Docks, L("main.nav.docks", "Docks"));
            DrawNav(MainPage.JobPresets, L("main.nav.presets", "Job Presets"));
            DrawNav(MainPage.Halo, L("main.nav.halo", "Twin Arc Halos"));
            DrawNav(MainPage.Integrations, L("main.nav.integrations", "Integrations"));
            DrawNav(MainPage.Interface, L("main.nav.interface", "Interface"));
            DrawNav(MainPage.Safety, L("main.nav.safety", "HUD Safety"));


            ImGui.SetCursorPosY(MathF.Max(ImGui.GetCursorPosY(), ImGui.GetWindowHeight() - 184f));
            UiStyles.Divider(theme);
            ImGui.Spacing();
            DrawNav(MainPage.Settings, L("main.nav.settings", "Settings"));
            ImGui.Spacing();
            UiStyles.Divider(theme);
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, theme.AccentStrong);
            if (UiStyles.NavButton(L("main.nav.forge", "THE FORGE"), false, theme, -1f))
                plugin.OpenForgeWindow();
            ImGui.PopStyleColor();
            ImGui.Spacing();

            if (ImGui.Button(L("main.nav.support", "SUPPORT"), new Vector2(-1, 40f)))
                plugin.OpenSupport();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(L("main.nav.support.tip", "Open the Ruby Blaire Collective support Discord."));
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawNav(MainPage target, string label)
    {
        if (UiStyles.NavButton(label, page == target, plugin.CurrentTheme, -1f))
            page = target;
    }

    private void DrawHeader()
    {
        var title = page switch
        {
            MainPage.Docks => L("main.nav.docks", "Docks"),
            MainPage.JobPresets => L("main.nav.presets", "Job Presets"),
            MainPage.Halo => L("main.nav.halo", "Twin Arc Halos"),
            MainPage.Integrations => L("main.nav.integrations", "Integrations"),
            MainPage.Safety => L("main.header.safety", "HUD Safety & Recovery"),
            MainPage.Interface => L("main.header.interface", "Interface Ownership"),
            MainPage.Settings => L("main.nav.settings", "Settings"),
            _ => L("main.nav.docks", "Docks"),
        };

        ImGui.SetWindowFontScale(1.34f);
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);

        ImGui.TextDisabled($"{plugin.GetJobAbbreviation()}  •  {plugin.GetLocationName()}  •  {plugin.AdaptiveState.ActivityLabel}");
    }

    private void DrawDocks()
    {
        UiStyles.SectionLabel(L("main.nav.docks", "Docks"), plugin.CurrentTheme);
        ImGui.TextDisabled(L("main.docks.help", "Choose a HUD layout, or let RE:Frame switch automatically between Leisure, Quest, Raid, and Work. Roleplay remains a deliberate manual choice."));
        ImGui.Spacing();

        DrawDockCard(UiMode.Leisure, L("mode.leisure", "Leisure").ToUpperInvariant(), L("main.dock.leisure.description", "A relaxed layout for exploration, travel, appearance tools, and everyday play."), plugin.CurrentTheme.Accent);
        DrawDockCard(UiMode.Roleplay, L("mode.roleplay", "Roleplay").ToUpperInvariant(), L("main.dock.roleplay.description", "A social layout focused on chat channels, emotes, Scenekeeper, and a calm roleplay presentation."), plugin.CurrentTheme.AccentStrong);
        DrawDockCard(UiMode.Quest, L("mode.quest", "Quest").ToUpperInvariant(), L("main.dock.quest.description", "A focused adventuring layout with combat information, quests, objectives, and FATE priority kept visible."), plugin.CurrentTheme.Success);
        DrawDockCard(UiMode.RaidReady, L("mode.raid", "Raid").ToUpperInvariant(), L("main.dock.raid.description", "A complete duty layout with party information, waymarks, countdown tools, strategy access, and Twin Arc."), plugin.CurrentTheme.Warning);
        DrawDockCard(UiMode.Work, L("mode.work", "Work").ToUpperInvariant(), L("main.dock.work.description", "A dedicated crafting and gathering layout with CP/GP, workstation tools, consumables, and spiritbond tracking."), plugin.CurrentTheme.AccentStrong);

        var stickyQuest = plugin.Configuration.StickyQuestMode;
        if (ImGui.Checkbox(L("main.dock.stickyquest", "Sticky Quest"), ref stickyQuest))
        {
            plugin.Configuration.StickyQuestMode = stickyQuest;
            if (stickyQuest) plugin.Configuration.ModeOverride = UiMode.Auto;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        var automatic = plugin.Configuration.ModeOverride == UiMode.Auto;
        ImGui.PushStyleColor(ImGuiCol.Button, automatic ? UiStyles.WithAlpha(plugin.CurrentTheme.AccentStrong, 0.52f) : UiStyles.WithAlpha(plugin.CurrentTheme.Accent, 0.16f));
        if (ImGui.Button(automatic ? L("main.dock.automatic.active", "AUTOMATIC ACTIVE") : L("main.dock.automatic.use", "USE AUTOMATIC SWITCHING"), new Vector2(240f, 38f)))
            plugin.SetMode(UiMode.Auto);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        if (ImGui.Button(plugin.Configuration.ShowHudOverlay ? L("main.dock.hide", "HIDE HUD") : L("main.dock.show", "SHOW HUD"), new Vector2(130f, 38f)))
            plugin.SetHudVisible(!plugin.Configuration.ShowHudOverlay);

        ImGui.Spacing();
        DrawDockButtonEditor();
        ImGui.Spacing();
        UiStyles.SectionLabel(L("main.dock.preview", "HUD preview"), plugin.CurrentTheme);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.Panel, 0.86f));
        if (ImGui.BeginChild("##replacement-preview", new Vector2(0f, 360f), true, ImGuiWindowFlags.NoScrollbar))
        {
            var draw = ImGui.GetWindowDrawList();
            var p = ImGui.GetWindowPos() + new Vector2(5f, 5f);
            var s = ImGui.GetWindowSize() - new Vector2(10f, 10f);
            HudRenderer.Draw(plugin, draw, p, s, true, HudModeProfileService.IsCalmMode(plugin.AdaptiveState.EffectiveMode) ? 0f : 1f);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawDockButtonEditor()
    {
        var theme = plugin.CurrentTheme;
        UiStyles.Divider(theme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Dock button actions", theme);
        ImGui.TextWrapped("Every button slot is replaceable, and every built-in action is available on every dock. Choose an RE:Frame action or assign any registered slash command from another plugin.");
        ImGui.Spacing();

        var dockKeys = DockButtonCatalog.DockKeys;
        var selectedIndex = Math.Max(0, dockKeys.ToList().FindIndex(key => key.Equals(selectedDockButtonEditor, StringComparison.OrdinalIgnoreCase)));
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("Dock##dock-button-editor", dockKeys[selectedIndex]))
        {
            for (var index = 0; index < dockKeys.Count; index++)
            {
                var selected = index == selectedIndex;
                if (ImGui.Selectable(dockKeys[index], selected)) selectedDockButtonEditor = dockKeys[index];
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        var buttons = DockButtonCatalog.Resolve(plugin.Configuration, selectedDockButtonEditor);
        var choices = DockButtonCatalog.GetActionChoices(selectedDockButtonEditor);
        var changed = false;
        var moveFrom = -1; var moveTo = -1; var removeAt = -1;
        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            ImGui.PushID($"dock-button-{selectedDockButtonEditor}-{button.Id}");
            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(theme.PanelAlt, 0.44f));
            if (ImGui.BeginChild("##row", new Vector2(0f, button.Action == DockButtonCatalog.CustomCommand ? 116f : 82f), true, ImGuiWindowFlags.NoScrollbar))
            {
                var visible = button.Visible;
                if (ImGui.Checkbox("##visible", ref visible)) { button.Visible = visible; changed = true; }
                ImGui.SameLine();
                var label = button.Label;
                ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - 270f));
                if (ImGui.InputText("##label", ref label, 24)) { button.Label = label; changed = true; }
                ImGui.SameLine();
                var current = DockButtonCatalog.Definition(selectedDockButtonEditor, button.Action);
                ImGui.SetNextItemWidth(180f);
                if (ImGui.BeginCombo("##action", current.Label))
                {
                    foreach (var choice in choices)
                    {
                        var selected = choice.Id.Equals(button.Action, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(choice.Label, selected))
                        {
                            button.Action = choice.Id;
                            if (string.IsNullOrWhiteSpace(button.Label) || button.Label == current.Label) button.Label = choice.Label;
                            changed = true;
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(choice.Tooltip);
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                ImGui.BeginDisabled(index == 0); if (ImGui.Button("↑", new Vector2(30f, 26f))) { moveFrom=index; moveTo=index-1; } ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(index == buttons.Count-1); if (ImGui.Button("↓", new Vector2(30f, 26f))) { moveFrom=index; moveTo=index+1; } ImGui.EndDisabled();
                ImGui.SameLine(); if (ImGui.Button("×", new Vector2(30f,26f))) removeAt=index;

                if (button.Action == DockButtonCatalog.CustomCommand)
                {
                    ImGui.SetCursorPosY(48f);
                    ImGui.TextDisabled("Slash command"); ImGui.SameLine();
                    var command = button.Command;
                    ImGui.SetNextItemWidth(MathF.Max(180f, ImGui.GetContentRegionAvail().X));
                    if (ImGui.InputText("##command", ref command, 128)) { button.Command = command; changed = true; }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Examples: /scenekeeper, /mare, /roleplayplugin open");
                }
            }
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopID(); ImGui.Spacing();
        }
        if (moveFrom>=0 && moveTo>=0) { var moving=buttons[moveFrom]; buttons.RemoveAt(moveFrom); buttons.Insert(moveTo,moving); changed=true; }
        if (removeAt>=0 && removeAt<buttons.Count) { buttons.RemoveAt(removeAt); changed=true; }
        if (ImGui.Button("+ Add button", new Vector2(130f,32f))) { DockButtonCatalog.Add(plugin.Configuration, selectedDockButtonEditor); changed=true; }
        ImGui.SameLine();
        if (ImGui.Button("Reset this dock", new Vector2(160f,32f))) { DockButtonCatalog.Reset(plugin.Configuration, selectedDockButtonEditor); changed=true; }
        ImGui.SameLine(); ImGui.TextDisabled("Custom commands can open any installed plugin that registers a slash command.");
        if (changed) plugin.SaveConfiguration();
    }

    private void DrawDockCard(UiMode mode, string title, string description, Vector4 accent)
    {
        var selected = plugin.Configuration.ModeOverride == mode;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, selected ? 0.72f : 0.42f));
        if (ImGui.BeginChild($"##dock-{mode}", new Vector2(0f, 74f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, accent);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            ImGui.TextDisabled(description);
            ImGui.SameLine(ImGui.GetWindowWidth() - 130f);
            ImGui.SetCursorPosY(18f);
            if (ImGui.Button(selected ? L("common.active", "ACTIVE") : L("common.activate", "ACTIVATE"), new Vector2(105f, 34f)))
                plugin.SetMode(mode);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void DrawJobPresets()
    {
        CompleteShortCodeTask();
        var currentJob = plugin.GetJobAbbreviation().ToUpperInvariant();
        UiStyles.SectionLabel($"{currentJob} job presets", plugin.CurrentTheme);
        ImGui.TextDisabled("Save layouts for individual jobs or create general presets you can use anywhere.");
        ImGui.Spacing();

        ImGui.TextUnformatted("Preset name");
        var available = ImGui.GetContentRegionAvail().X;
        const float saveButtonWidth = 150f;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var nameWidth = MathF.Max(240f, available - (saveButtonWidth * 2f) - (gap * 2f));
        ImGui.SetNextItemWidth(nameWidth);
        DrawBrightInputText("##preset-name", ref presetName, 80);
        ImGui.SameLine();
        if (ImGui.Button("SAVE FOR JOB", new Vector2(saveButtonWidth, 32f)))
            plugin.HudPresets.SaveForCurrentJob(presetName, out presetStatus);
        ImGui.SameLine();
        if (ImGui.Button("SAVE GENERAL", new Vector2(saveButtonWidth, 32f)))
            plugin.HudPresets.SaveGeneral(presetName, out presetStatus);

        ImGui.Spacing();
        DrawPresetCollection(currentJob, plugin.HudPresets.GetJobPresets(currentJob), false);

        ImGui.Spacing();
        UiStyles.Divider(plugin.CurrentTheme);
        ImGui.Spacing();
        UiStyles.SectionLabel("General presets", plugin.CurrentTheme);
        DrawPresetCollection("GENERAL", plugin.HudPresets.GeneralPresets, true);

        ImGui.Spacing();
        UiStyles.Divider(plugin.CurrentTheme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Copy from another job", plugin.CurrentTheme);
        var sourceJobs = plugin.Configuration.JobHudPresets.Keys
            .Where(job => !string.Equals(job, currentJob, StringComparison.OrdinalIgnoreCase))
            .OrderBy(job => job)
            .ToArray();
        if (sourceJobs.Length == 0)
        {
            ImGui.TextDisabled("Save a preset on another job first; it will appear here.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(copySourceJob) || !sourceJobs.Contains(copySourceJob, StringComparer.OrdinalIgnoreCase))
                copySourceJob = sourceJobs[0];
            ImGui.SetNextItemWidth(180f);
            if (ImGui.BeginCombo("##copy-source-job", copySourceJob))
            {
                foreach (var job in sourceJobs)
                {
                    var selected = string.Equals(job, copySourceJob, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(job, selected))
                    {
                        copySourceJob = job;
                        copySourcePreset = string.Empty;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            var sourcePresets = plugin.HudPresets.GetJobPresets(copySourceJob);
            var names = sourcePresets.Keys.OrderBy(name => name).ToArray();
            if (names.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(copySourcePreset) || !names.Contains(copySourcePreset, StringComparer.OrdinalIgnoreCase))
                    copySourcePreset = names[0];
                ImGui.SetNextItemWidth(260f);
                if (ImGui.BeginCombo("##copy-source-preset", copySourcePreset))
                {
                    foreach (var name in names)
                    {
                        var selected = string.Equals(name, copySourcePreset, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(name, selected)) copySourcePreset = name;
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                if (ImGui.Button($"COPY TO {currentJob}", new Vector2(150f, 30f)))
                    plugin.HudPresets.CopyJobPreset(copySourceJob, copySourcePreset, currentJob, presetName, out presetStatus);
            }
        }

        ImGui.Spacing();
        UiStyles.Divider(plugin.CurrentTheme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Share your HUD", plugin.CurrentTheme);
        ImGui.TextDisabled("Create a short RF4 code to share your HUD with another RE:Frame user.");
        ImGui.TextDisabled("Older RF3, RF2, and RFHUD1 codes can still be imported or converted.");
        ImGui.Spacing();

        var shortCodeBusy = shortCodeTask is { IsCompleted: false };
        if (shortCodeBusy) ImGui.BeginDisabled();
        if (ImGui.Button("CREATE SHORT CODE", new Vector2(190f, 32f)))
        {
            if (plugin.HudPresets.TryExportCurrent(presetName, out var portableCode, out _))
                StartShortCodePublish(portableCode);
        }
        if (shortCodeBusy) ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled(shortCodeBusy
            ? "Creating your short code…"
            : "Creates a shareable code without changing your active HUD.");
        ImGui.Spacing();

        ImGui.TextUnformatted("Your share code");
        var copyWidth = 150f;
        var codeWidth = MathF.Max(240f, ImGui.GetContentRegionAvail().X - copyWidth - gap);
        ImGui.SetNextItemWidth(codeWidth);
        DrawBrightInputText("##share-code", ref shareCode, 4096, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        var codeEmpty = string.IsNullOrWhiteSpace(shareCode);
        if (codeEmpty) ImGui.BeginDisabled();
        if (ImGui.Button("COPY CODE", new Vector2(copyWidth, 30f)))
        {
            ImGui.SetClipboardText(shareCode);
            presetStatus = "Short share code copied to the clipboard.";
        }
        if (codeEmpty) ImGui.EndDisabled();
        ImGui.TextDisabled(codeEmpty
            ? "No short code generated yet."
            : shareCode.StartsWith(HudPresetShortCodeService.Prefix, StringComparison.OrdinalIgnoreCase)
                ? $"Online RF4 short code • {shareCode.Length} characters"
                : $"Offline preset code • {shareCode.Length} characters");

        if (!string.IsNullOrWhiteSpace(offlineShareCode))
        {
            if (ImGui.Button("COPY OFFLINE RF3 BACKUP", new Vector2(230f, 28f)))
            {
                ImGui.SetClipboardText(offlineShareCode);
                presetStatus = "Offline RF3 backup copied. It is long, but it does not depend on an online lookup service.";
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Optional offline backup for use when the short-code service is unavailable.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Name this imported preset (optional)");
        ImGui.SetNextItemWidth(-1f);
        DrawBrightInputText("##import-name", ref importName, 80);

        ImGui.TextUnformatted("Paste a RE:Frame share code");
        ImGui.SetNextItemWidth(-1f);
        DrawBrightInputText("##import-code", ref importCode, 4096);

        if (shortCodeBusy) ImGui.BeginDisabled();
        if (ImGui.Button("TURN OLD CODE INTO RF4", new Vector2(-1f, 30f)))
            ConvertPastedCodeToRf4();
        if (shortCodeBusy) ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Convert an older RE:Frame code into a short RF4 share code without applying it.");

        ImGui.Spacing();
        const float importButtonWidth = 190f;
        if (shortCodeBusy) ImGui.BeginDisabled();
        if (ImGui.Button($"IMPORT FOR {currentJob}", new Vector2(importButtonWidth, 32f)))
            BeginImport(importAsGeneral: false);
        ImGui.SameLine();
        if (ImGui.Button("IMPORT AS GENERAL", new Vector2(importButtonWidth, 32f)))
            BeginImport(importAsGeneral: true);
        if (shortCodeBusy) ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(presetStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(presetStatus);
        }
    }

    private void StartShortCodePublish(string portableCode)
    {
        CancelShortCodeTask();
        offlineShareCode = portableCode;
        shareCode = string.Empty;
        presetStatus = "Creating a clean RF4 short code…";
        shortCodeTaskKind = ShortCodeTaskKind.Publish;
        shortCodeCancellation = new CancellationTokenSource();
        shortCodeTask = HudPresetShortCodeService.PublishAsync(portableCode, shortCodeCancellation.Token);
    }

    private void ConvertPastedCodeToRf4()
    {
        var value = importCode.Trim();
        if (HudPresetShortCodeService.IsShortCode(value))
        {
            shareCode = value;
            offlineShareCode = string.Empty;
            presetStatus = $"That is already a clean RF4 short code ({value.Length} characters).";
            return;
        }

        if (!plugin.HudPresets.TryConvertToRf3(value, out var portableCode, out presetStatus))
            return;

        StartShortCodePublish(portableCode);
    }

    private void BeginImport(bool importAsGeneral)
    {
        var code = importCode.Trim();
        if (!HudPresetShortCodeService.IsShortCode(code))
        {
            if (importAsGeneral)
                plugin.HudPresets.ImportGeneral(code, importName, out presetStatus);
            else
                plugin.HudPresets.ImportForCurrentJob(code, importName, out presetStatus);
            return;
        }

        CancelShortCodeTask();
        pendingImportName = importName;
        presetStatus = "Resolving RF4 short code…";
        shortCodeTaskKind = importAsGeneral ? ShortCodeTaskKind.ImportGeneral : ShortCodeTaskKind.ImportCurrentJob;
        shortCodeCancellation = new CancellationTokenSource();
        shortCodeTask = HudPresetShortCodeService.ResolveAsync(code, shortCodeCancellation.Token);
    }

    private void CompleteShortCodeTask()
    {
        var task = shortCodeTask;
        if (task is null || !task.IsCompleted)
            return;

        shortCodeTask = null;
        shortCodeCancellation?.Dispose();
        shortCodeCancellation = null;

        ShortCodeResult result;
        try
        {
            result = task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            presetStatus = $"The RF4 operation failed: {ex.Message}";
            shortCodeTaskKind = ShortCodeTaskKind.None;
            return;
        }

        var completedKind = shortCodeTaskKind;
        shortCodeTaskKind = ShortCodeTaskKind.None;
        if (!result.Success)
        {
            presetStatus = result.Message;
            return;
        }

        switch (completedKind)
        {
            case ShortCodeTaskKind.Publish:
                shareCode = result.Value;
                presetStatus = result.Message;
                break;
            case ShortCodeTaskKind.ImportCurrentJob:
                plugin.HudPresets.ImportForCurrentJob(result.Value, pendingImportName, out presetStatus);
                break;
            case ShortCodeTaskKind.ImportGeneral:
                plugin.HudPresets.ImportGeneral(result.Value, pendingImportName, out presetStatus);
                break;
        }
    }

    private void CancelShortCodeTask()
    {
        shortCodeCancellation?.Cancel();
        shortCodeCancellation?.Dispose();
        shortCodeCancellation = null;
        shortCodeTask = null;
        shortCodeTaskKind = ShortCodeTaskKind.None;
    }

    private void DrawPresetCollection(string scope, System.Collections.Generic.IReadOnlyDictionary<string, HudPresetData> presets, bool general)
    {
        if (presets.Count == 0)
        {
            ImGui.TextDisabled("No presets saved yet.");
            return;
        }

        string? deleteName = null;
        foreach (var (name, preset) in presets.OrderBy(pair => pair.Key))
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.36f));
            if (ImGui.BeginChild($"##preset-{scope}-{name}", new Vector2(0f, 62f), true, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.SetCursorPos(new Vector2(12f, 9f));
                ImGui.TextUnformatted(name);
                ImGui.SetCursorPosX(12f);
                ImGui.TextDisabled(general ? $"General • from {preset.SourceJob}" : $"{scope} • {preset.CreatedUtc.ToLocalTime():g}");

                const float loadWidth = 78f;
                const float exportWidth = 86f;
                const float copyPresetWidth = 76f;
                const float deleteWidth = 82f;
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                var buttonCount = general ? 4 : 3;
                var totalWidth = loadWidth + exportWidth + deleteWidth + (general ? copyPresetWidth : 0f) + (spacing * (buttonCount - 1));
                var buttonStart = MathF.Max(260f, ImGui.GetWindowWidth() - totalWidth - 14f);
                ImGui.SetCursorPos(new Vector2(buttonStart, 15f));

                if (ImGui.Button($"LOAD##{scope}-{name}", new Vector2(loadWidth, 30f)))
                {
                    if (general) plugin.HudPresets.ApplyGeneral(name, true, out presetStatus);
                    else plugin.HudPresets.ApplyJob(scope, name, scope, true, out presetStatus);
                }
                ImGui.SameLine();
                if (ImGui.Button($"EXPORT##{scope}-{name}", new Vector2(exportWidth, 30f)))
                {
                    string portableCode;
                    var exported = general
                        ? plugin.HudPresets.TryExportGeneral(name, out portableCode, out presetStatus)
                        : plugin.HudPresets.TryExportJob(scope, name, out portableCode, out presetStatus);
                    if (exported)
                        StartShortCodePublish(portableCode);
                }
                if (general)
                {
                    ImGui.SameLine();
                    if (ImGui.Button($"COPY##{scope}-{name}", new Vector2(copyPresetWidth, 30f)))
                        plugin.HudPresets.CopyGeneralToJob(name, plugin.GetJobAbbreviation(), presetName, out presetStatus);
                }
                ImGui.SameLine();
                if (ImGui.Button($"DELETE##{scope}-{name}", new Vector2(deleteWidth, 30f)))
                    deleteName = name;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (deleteName is not null)
        {
            if (general) plugin.HudPresets.DeleteGeneral(deleteName, out presetStatus);
            else plugin.HudPresets.DeleteJob(scope, deleteName, out presetStatus);
        }
    }

    private static bool DrawBrightInputText(string id, ref string value, int maxLength, ImGuiInputTextFlags flags = default)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.94f, 0.96f, 1.00f, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.96f, 0.98f, 1.00f, 0.29f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.98f, 0.99f, 1.00f, 0.36f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.90f, 0.94f, 1.00f, 0.68f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.98f, 0.99f, 1.00f, 1f));
        var changed = ImGui.InputText(id, ref value, maxLength, flags);
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar();
        return changed;
    }

    private void DrawHalo()
    {
        var configuration = plugin.Configuration;
        var telemetry = plugin.CombatTelemetry;
        var theme = plugin.CurrentTheme;
        var changed = false;

        UiStyles.SectionLabel("Twin Arc halo settings", theme);
        changed |= DrawHaloBool("Show Twin Arc halo", configuration.ShowCombatHalo, value => configuration.ShowCombatHalo = value);
        changed |= DrawHaloBool("Keep Twin Arc visible in Raid and Quest Docks", configuration.ShowCombatHaloInRaidReady, value => configuration.ShowCombatHaloInRaidReady = value);
        changed |= DrawHaloBool("Follow the player on screen", configuration.HaloFollowsPlayer, value => configuration.HaloFollowsPlayer = value);
        changed |= DrawHaloFloat("Halo radius", configuration.HaloRadius, 75f, 190f, "%.0f", value => configuration.HaloRadius = value);
        changed |= DrawHaloFloat("Halo thickness", configuration.HaloThickness, 2f, 12f, "%.1f", value => configuration.HaloThickness = value);
        changed |= DrawHaloFloat("Halo vertical offset", configuration.HaloVerticalOffset, -140f, 100f, "%.0f", value => configuration.HaloVerticalOffset = value);

        if (changed)
            plugin.SaveConfiguration();

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Live combat activity", theme);
        ImGui.TextUnformatted($"Damage received: {telemetry.DamageInPerSecond:0}/s");
        ImGui.TextUnformatted($"Healing received: {telemetry.HealingReceivedPerSecond:0}/s");
        ImGui.TextUnformatted($"Target pressure: {telemetry.TargetPressurePerSecond:0}/s");
        ImGui.Spacing();
        ImGui.TextWrapped("The left arc reflects visible damage and healing on your character. The right arc reflects pressure on your current target; it is not a DPS meter.");
        ImGui.Spacing();
        if (ImGui.Button("Reset encounter telemetry", new Vector2(220f, 36f))) telemetry.Reset();
        DrawCard("LEFT ARC", "Outer crimson rail: incoming damage. Inner green rail: healing received.", theme.Danger);
        DrawCard("RIGHT ARC", "Job-colored target-pressure rail.", theme.AccentStrong);
    }

    private static bool DrawHaloBool(string label, bool value, Action<bool> apply)
    {
        var copy = value;
        var changed = ImGui.Checkbox($"##halo-bool-{label}", ref copy);
        ImGui.SameLine();
        ImGui.TextWrapped(label);
        if (!changed)
            return false;

        apply(copy);
        return true;
    }

    private static bool DrawHaloFloat(string label, float value, float min, float max, string format, Action<float> apply)
    {
        ImGui.TextWrapped(label);
        var copy = value;
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.SliderFloat($"##halo-float-{label}", ref copy, min, max, format))
            return false;

        apply(copy);
        return true;
    }

    private void DrawIntegrations()
    {
        var configuration = plugin.Configuration;
        var theme = plugin.CurrentTheme;

        UiStyles.SectionLabel("Built-in integrations", theme);
        ImGui.TextWrapped("Connect RE:Frame to supported Dalamud plugins and adjust the slash commands it uses.");
        ImGui.Spacing();

        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columnWidth = MathF.Max(1f, (available - spacing) * 0.5f);
        var startX = ImGui.GetCursorPosX();

        ImGui.TextUnformatted("Plugin status");
        ImGui.SameLine(startX + columnWidth + spacing);
        ImGui.TextUnformatted("Slash commands");
        ImGui.Spacing();

        var commandsChanged = false;

        DrawIntegrationStatus("Penumbra", configuration.PenumbraCommand, columnWidth);
        ImGui.SameLine();
        commandsChanged |= DrawIntegrationCommandEditor("Penumbra", configuration.PenumbraCommand, value => configuration.PenumbraCommand = value, columnWidth);
        ImGui.Spacing();

        DrawIntegrationStatus("Glamourer", configuration.GlamourerCommand, columnWidth);
        ImGui.SameLine();
        commandsChanged |= DrawIntegrationCommandEditor("Glamourer", configuration.GlamourerCommand, value => configuration.GlamourerCommand = value, columnWidth);
        ImGui.Spacing();

        DrawIntegrationStatus("Lifestream", configuration.LifestreamCommand, columnWidth);
        ImGui.SameLine();
        commandsChanged |= DrawIntegrationCommandEditor("Lifestream", configuration.LifestreamCommand, value => configuration.LifestreamCommand = value, columnWidth);
        ImGui.Spacing();

        DrawIntegrationStatus("BoneSmith", configuration.BoneSmithCommand, columnWidth);
        ImGui.SameLine();
        commandsChanged |= DrawIntegrationCommandEditor("BoneSmith", configuration.BoneSmithCommand, value => configuration.BoneSmithCommand = value, columnWidth);
        ImGui.Spacing();

        DrawIntegrationStatus("Scenekeeper", configuration.ScenekeeperCommand, columnWidth, plugin.OpenScenekeeper);
        ImGui.SameLine();
        commandsChanged |= DrawIntegrationCommandEditor("Scenekeeper", configuration.ScenekeeperCommand, value => configuration.ScenekeeperCommand = value, columnWidth);
        ImGui.Spacing();

        DrawIntegrationStatus("Character Select", plugin.ResolveCharacterSelectCommand(), columnWidth, plugin.OpenCharacterSelect);
        ImGui.SameLine();
        commandsChanged |= DrawIntegrationCommandEditor(
            "Character Select",
            configuration.CharacterSelectCommand,
            value => configuration.CharacterSelectCommand = value,
            columnWidth,
            plugin.ResolveCharacterSelectCommand());

        if (commandsChanged)
            plugin.SaveConfiguration();

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Custom integrations", theme);
        ImGui.TextWrapped("Add other Dalamud plugin commands to RE:Frame. Saved integrations also appear in the Command Center.");
        ImGui.Spacing();

        if (ConfigWindow.DrawCustomIntegrations(plugin, configuration))
            plugin.SaveConfiguration();

    }

    private void DrawGreetingVoicePacks()
    {
        var configuration = plugin.Configuration;
        var theme = plugin.CurrentTheme;
        GreetingVoicePackService.NormalizeConfiguration(configuration);

        if (!plugin.ForgeAccess.HasAccess)
        {
            UiStyles.SectionLabel("Greeting voice packs", theme);
            ImGui.TextWrapped("Custom voice-pack importing is available only with an active RE:Forge membership. Installed voices can still be selected and previewed under RE:Frame Settings → Greeting.");
            return;
        }

        UiStyles.SectionLabel("Greeting voice packs", theme);
        ImGui.TextWrapped("Choose the voice used for Leisure greetings, or import your own Morning, Afternoon, and Evening WAV files.");
        ImGui.Spacing();

        var activeId = configuration.ActiveGreetingVoicePackId;
        var activeName = GreetingVoicePackService.GetDisplayName(configuration, activeId);
        ImGui.TextUnformatted("Active voice pack");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##active-greeting-voice-pack", activeName))
        {
            var jarvinSelected = string.Equals(activeId, GreetingVoicePackService.JarvinPackId, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(GreetingVoicePackService.JarvinPackName, jarvinSelected))
            {
                configuration.ActiveGreetingVoicePackId = GreetingVoicePackService.JarvinPackId;
                plugin.SaveConfiguration();
            }
            if (jarvinSelected)
                ImGui.SetItemDefaultFocus();

            var rubySelected = string.Equals(activeId, GreetingVoicePackService.RubyPackId, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(GreetingVoicePackService.RubyPackName, rubySelected))
            {
                configuration.ActiveGreetingVoicePackId = GreetingVoicePackService.RubyPackId;
                plugin.SaveConfiguration();
            }
            if (rubySelected)
                ImGui.SetItemDefaultFocus();

            foreach (var pack in configuration.CustomGreetingVoicePacks.OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase))
            {
                var selected = string.Equals(activeId, pack.Id, StringComparison.OrdinalIgnoreCase);
                var ready = GreetingVoicePackService.IsPackReady(pack, out _);
                var displayName = GreetingVoicePackService.GetDisplayName(configuration, pack.Id);
                if (!ready) ImGui.BeginDisabled();
                if (ImGui.Selectable(ready ? displayName : $"{displayName} (missing files)", selected))
                {
                    configuration.ActiveGreetingVoicePackId = pack.Id;
                    plugin.SaveConfiguration();
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
                if (!ready) ImGui.EndDisabled();
            }

            ImGui.EndCombo();
        }

        if (ImGui.Button("PREVIEW ACTIVE VOICE PACK", new Vector2(250f, 34f)))
            plugin.PreviewLoginGreeting();

        ImGui.Spacing();
        string? ignoredRemoveId = null;
        DrawVoicePackCard(
            GreetingVoicePackService.JarvinPackId,
            GreetingVoicePackService.JarvinPackName,
            "Built-in • 2 rotating greeting sets",
            true,
            false,
            ref ignoredRemoveId);
        DrawVoicePackCard(
            GreetingVoicePackService.RubyPackId,
            GreetingVoicePackService.RubyPackName,
            "Built-in • 3 rotating greeting sets",
            true,
            false,
            ref ignoredRemoveId);

        string? removeId = null;
        foreach (var pack in configuration.CustomGreetingVoicePacks.OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var ready = GreetingVoicePackService.IsPackReady(pack, out var readiness);
            var displayName = GreetingVoicePackService.GetDisplayName(configuration, pack.Id);
            DrawVoicePackCard(pack.Id, displayName, readiness, ready, true, ref removeId);
        }

        if (removeId is not null)
        {
            GreetingVoiceService.Stop();
            if (GreetingVoicePackService.TryDeletePack(configuration, removeId, out voicePackStatus))
                plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        UiStyles.SectionLabel("Add a voice pack", theme);
        ImGui.TextDisabled("Each set needs one Morning, Afternoon, and Evening WAV file. Voice packs can contain up to three rotating sets.");
        ImGui.Spacing();

        ImGui.TextUnformatted("Voice pack name");
        ImGui.SetNextItemWidth(-1f);
        DrawBrightInputText("##new-voice-pack-name", ref newVoicePackName, 80);
        ImGui.Spacing();

        for (var setIndex = 0; setIndex < newVoicePackSetCount; setIndex++)
            DrawVoicePackSetEditor(setIndex);

        if (newVoicePackSetCount < GreetingVoicePackService.MaxGreetingSets &&
            ImGui.Button("+ ADD GREETING SET", new Vector2(190f, 32f)))
        {
            newVoicePackSetCount++;
        }

        if (newVoicePackSetCount > 1)
        {
            ImGui.SameLine();
            if (ImGui.Button("REMOVE LAST SET", new Vector2(180f, 32f)))
            {
                var removedSet = newVoicePackSetCount - 1;
                for (var periodIndex = 0; periodIndex < 3; periodIndex++)
                    newVoicePackFiles[(removedSet * 3) + periodIndex] = string.Empty;
                newVoicePackSetCount--;
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("IMPORT & ACTIVATE VOICE PACK", new Vector2(280f, 38f)))
        {
            var sets = Enumerable.Range(0, newVoicePackSetCount)
                .Select(index => (
                    Morning: newVoicePackFiles[(index * 3) + 0],
                    Afternoon: newVoicePackFiles[(index * 3) + 1],
                    Evening: newVoicePackFiles[(index * 3) + 2]))
                .ToArray();

            if (GreetingVoicePackService.TryImportPack(
                    configuration,
                    plugin.ForgeAccess.HasAccess,
                    newVoicePackName,
                    sets,
                    out _,
                    out voicePackStatus))
            {
                newVoicePackName = string.Empty;
                Array.Clear(newVoicePackFiles, 0, newVoicePackFiles.Length);
                newVoicePackSetCount = 1;
                plugin.SaveConfiguration();
            }
        }

        if (!string.IsNullOrWhiteSpace(voicePackStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(voicePackStatus);
        }
    }

    private void DrawVoicePackCard(
        string packId,
        string name,
        string status,
        bool ready,
        bool removable,
        ref string? removeId)
    {
        var active = string.Equals(plugin.Configuration.ActiveGreetingVoicePackId, packId, StringComparison.OrdinalIgnoreCase);
        ImGui.PushID($"voice-pack-{packId}");
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, active ? 0.70f : 0.42f));
        if (ImGui.BeginChild("##voice-pack-card", new Vector2(0f, 88f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextUnformatted(name);
            if (ready)
                ImGui.TextDisabled(status);
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, plugin.CurrentTheme.Warning);
                ImGui.TextWrapped(status);
                ImGui.PopStyleColor();
            }

            var buttonY = 25f;
            ImGui.SetCursorPos(new Vector2(MathF.Max(1f, ImGui.GetWindowWidth() - (removable ? 218f : 112f)), buttonY));
            if (!ready) ImGui.BeginDisabled();
            if (ImGui.Button(active ? "ACTIVE" : "ACTIVATE", new Vector2(96f, 32f)) && !active)
            {
                plugin.Configuration.ActiveGreetingVoicePackId = packId;
                plugin.SaveConfiguration();
                voicePackStatus = $"Activated {name}.";
            }
            if (!ready) ImGui.EndDisabled();

            if (removable)
            {
                ImGui.SameLine();
                if (ImGui.Button("REMOVE", new Vector2(96f, 32f)))
                    removeId = packId;
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();
        ImGui.Spacing();
    }

    private void DrawVoicePackSetEditor(int setIndex)
    {
        ImGui.PushID($"new-voice-pack-set-{setIndex}");
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.42f));
        if (ImGui.BeginChild("##voice-pack-set", new Vector2(0f, 196f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextUnformatted($"Greeting set {setIndex + 1}");
            ImGui.Spacing();
            DrawVoiceFilePickerRow(setIndex, 0, "Morning");
            DrawVoiceFilePickerRow(setIndex, 1, "Afternoon");
            DrawVoiceFilePickerRow(setIndex, 2, "Evening");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();
        ImGui.Spacing();
    }

    private void DrawVoiceFilePickerRow(int setIndex, int periodIndex, string periodName)
    {
        var slot = (setIndex * 3) + periodIndex;
        ImGui.PushID($"voice-file-{slot}");
        ImGui.TextUnformatted(periodName);

        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        const float browseWidth = 96f;
        var inputWidth = MathF.Max(80f, available - browseWidth - spacing);
        var displayedPath = newVoicePackFiles[slot] ?? string.Empty;
        ImGui.SetNextItemWidth(inputWidth);
        DrawBrightInputText("##voice-file-path", ref displayedPath, 1024, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("BROWSE", new Vector2(browseWidth, 0f)))
        {
            voicePackFileDialog.OpenFileDialog(
                $"Select {periodName} greeting for set {setIndex + 1}",
                "Wave audio{.wav}",
                (success, path) =>
                {
                    if (success && !string.IsNullOrWhiteSpace(path))
                        newVoicePackFiles[slot] = path;
                });
        }

        ImGui.PopID();
    }

    private void DrawSafety()
    {
        UiStyles.SectionLabel("HUD replacement controls", plugin.CurrentTheme);
        ImGui.TextWrapped("RE:Frame only hides FFXIV HUD elements it can safely replace. Your original interface is restored whenever replacement mode is turned off or the plugin unloads.");
        ImGui.Spacing();
        ImGui.TextUnformatted(plugin.NativeHudVisibility.IsSuppressing
            ? $"Replacement mode is active — {plugin.NativeHudVisibility.HiddenAddonCount} FFXIV HUD elements are currently hidden"
            : "Replacement mode is off — your original FFXIV HUD is visible");
        ImGui.TextDisabled(NativeHudVisibilityService.HotbarDataAvailable ? "Action bar replacement is ready." : "Waiting for FFXIV action bar data; your action bars will remain visible.");
        ImGui.Spacing();
        if (ImGui.Button("ENABLE REPLACEMENT MODE", new Vector2(230f, 38f))) plugin.SetNativeReplacement(true);
        ImGui.SameLine();
        if (ImGui.Button("RESTORE ORIGINAL FFXIV HUD", new Vector2(245f, 38f))) plugin.SetNativeReplacement(false);
        DrawCard("QUEST TRACKING", "Quest Dock keeps FFXIV's Main Scenario guide and objective tracker so FATE timers retain their normal priority.", plugin.CurrentTheme.Success);
        DrawCard("FFXIV ELEMENTS KEPT INTACT", "Chat, minimap content, job gauges, scenario guidance, and quest tracking continue using FFXIV's reliable native behavior.", plugin.CurrentTheme.Warning);
        ImGui.TextWrapped("Recovery command: /reframe restore");
    }

    private void DrawIntegrationStatus(string name, string command, float width, Action? openAction = null)
    {
        var normalized = command?.Trim() ?? string.Empty;
        var root = string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.Split(' ')[0];
        var detected = !string.IsNullOrWhiteSpace(root) && Plugin.CommandManager.Commands.ContainsKey(root);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.42f));
        if (ImGui.BeginChild($"##integration-status-{name}", new Vector2(width, 94f), true, ImGuiWindowFlags.NoScrollbar))
        {
            const float openButtonWidth = 86f;
            const float openButtonHeight = 29f;
            var childStart = ImGui.GetCursorScreenPos();
            var childWidth = ImGui.GetContentRegionAvail().X;

            ImGui.TextUnformatted(name);

            var buttonX = childStart.X + MathF.Max(0f, childWidth - openButtonWidth);
            ImGui.SetCursorScreenPos(new Vector2(buttonX, childStart.Y));
            if (ImGui.Button($"OPEN##{name}", new Vector2(openButtonWidth, openButtonHeight)))
            {
                if (openAction is not null)
                    openAction();
                else
                    plugin.RunIntegrationCommand(normalized, name);
            }

            ImGui.SetCursorScreenPos(new Vector2(childStart.X, childStart.Y + openButtonHeight + 4f));
            ImGui.TextDisabled(detected ? "Ready" : "Not detected");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private bool DrawIntegrationCommandEditor(
        string name,
        string value,
        Action<string> apply,
        float width,
        string? resolvedCommand = null)
    {
        var changed = false;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.42f));
        if (ImGui.BeginChild($"##integration-command-{name}", new Vector2(width, 94f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextUnformatted(name);
            var copy = value ?? string.Empty;
            ImGui.SetNextItemWidth(-1f);
            if (DrawBrightInputText($"##integration-command-input-{name}", ref copy, 128))
            {
                apply(copy);
                changed = true;
            }

            if (resolvedCommand is not null)
            {
                var resolved = string.IsNullOrWhiteSpace(resolvedCommand) ? "not detected" : resolvedCommand;
                ImGui.TextDisabled(string.IsNullOrWhiteSpace(copy)
                    ? $"Blank uses auto-detect: {resolved}"
                    : $"Resolved command: {resolved}");
            }
            else
            {
                var normalized = copy.Trim();
                var root = string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.Split(' ')[0];
                var detected = !string.IsNullOrWhiteSpace(root) && Plugin.CommandManager.Commands.ContainsKey(root);
                ImGui.TextDisabled(detected ? "Ready" : "Not detected");
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        return changed;
    }

    private void DrawCard(string title, string text, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.42f));
        if (ImGui.BeginChild($"##card-{title}", new Vector2(0f, 84f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, accent);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            ImGui.TextWrapped(text);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static string L(string key, string english) => Localizer.Text(key, english);

    public void Dispose()
    {
        forgeVaultCancellation.Cancel();
        forgeVaultCancellation.Dispose();
        CancelShortCodeTask();
        voicePackFileDialog.Reset();
    }

    private enum ShortCodeTaskKind { None, Publish, ImportCurrentJob, ImportGeneral }
    private enum MainPage { Docks, JobPresets, Halo, Integrations, Interface, Safety, Settings }
}
