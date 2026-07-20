using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed class CommandPaletteWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string search = string.Empty;
    private bool focusSearch;
    private IReadOnlyList<NativeMenuEntry>? nativeMenuEntries;

    public CommandPaletteWindow(Plugin plugin)
        : base("Command Center###REFrameCommandCenter",
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(720f, 500f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(580f, 390f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        AllowBackgroundBlur = true;
    }

    public void OpenPalette()
    {
        search = string.Empty;
        focusSearch = true;
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw() => UiStyles.PushWindowStyle(plugin.CurrentTheme, plugin.CurrentThemeStyle);
    public override void PostDraw() => UiStyles.PopWindowStyle();

    public override void Draw()
    {
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Command Center", theme);
        ImGui.TextDisabled("Type what you want: switch jobs, open any native FFXIV menu, launch RE:Frame, restore the FFXIV UI…");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        if (focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            focusSearch = false;
        }

        var submitted = ImGui.InputText(
            "##reframe-command-center-search",
            ref search,
            160,
            ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();

        var items = BuildItems().Where(Matches).ToList();
        if (items.Count == 0 && !string.IsNullOrWhiteSpace(search))
        {
            var requestedMenu = NormalizeNativeMenuRequest(search);
            if (!string.IsNullOrWhiteSpace(requestedMenu))
            {
                items.Add(new CommandItem(
                    "native-menu-fallback",
                    $"Open native FFXIV menu: {requestedMenu}",
                    "Pass this exact menu name to FFXIV's native Main Commands system.",
                    $"native ffxiv xiv menu window {requestedMenu}",
                    () => TryOpenNativeMenu(requestedMenu)));
            }
        }

        if (submitted && items.Count > 0)
        {
            items[0].Action();
            IsOpen = false;
            return;
        }

        if (ImGui.BeginChild("##command-center-results", Vector2.Zero, false))
        {
            if (items.Count == 0)
            {
                ImGui.TextDisabled("No matching command. Try a job, plugin, or native FFXIV window name.");
            }
            else
            {
                foreach (var item in items)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(theme.PanelAlt, 0.56f));
                    if (ImGui.Button($"{item.Title}##{item.Id}", new Vector2(-1, 40f)))
                    {
                        item.Action();
                        IsOpen = false;
                    }
                    ImGui.PopStyleColor();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 8f);
                    ImGui.TextDisabled(item.Description);
                    ImGui.Spacing();
                }
            }
        }
        ImGui.EndChild();
    }

    private bool Matches(CommandItem item)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var haystack = $"{item.Title} {item.Description} {item.Keywords}".ToLowerInvariant();
        var tokens = search
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '-', '_', '/', '.', ',', ':', ';' }, StringSplitOptions.RemoveEmptyEntries);

        return tokens.All(token => haystack.Contains(token, StringComparison.Ordinal));
    }

    private IEnumerable<CommandItem> BuildItems()
    {
        yield return new CommandItem(
            "ui-activate",
            "Launch RE:Frame UI",
            "Show the RE:Frame HUD and enable native replacement mode.",
            "launch start enable activate reframe ui replacement on",
            plugin.ActivateReFrameUi);

        yield return new CommandItem(
            "ui-open-main",
            "Open RE:Frame",
            "Open the full RE:Frame control window.",
            "open show launch reframe window dashboard settings home",
            plugin.OpenMainUiImmediate);

        yield return new CommandItem(
            "ui-edit-hud",
            "Edit HUD",
            "Unlock every RE:Frame HUD element for moving, resizing, showing, and hiding.",
            "edit hud layout unlock move resize hide show customize",
            () => plugin.SetHudEditMode(true));

        yield return new CommandItem(
            "ui-bar-edit",
            "Toggle Hotbar Lock",
            "Open the movable current-job Action Palette and unlock RE:Frame hotbar slots for action assignment, moving, swapping, copying, and removal.",
            "bars hotbars unlock lock move swap copy remove ref bars",
            plugin.ToggleBarEditMode);

        yield return new CommandItem(
            "ui-refresh",
            "Refresh RE:Frame",
            "Reapply the HUD and play the RE:Frame glitch transition when enabled.",
            "refresh reload redraw restart reframe glitch",
            plugin.RefreshReFrame);

        yield return new CommandItem(
            "ui-restore-ffxiv",
            "Revert to the Final Fantasy XIV UI",
            "Hide RE:Frame and immediately restore every native HUD element it suppressed.",
            "restore revert return native original default vanilla ff ffxiv xiv ui off disable",
            plugin.RestoreFinalFantasyUi);

        yield return new CommandItem("mode-auto", "Use automatic interface state", "Leisure, Quest, Raid, and Work change with game state; crafting/gathering activates Work and combat activates Raid.", "automatic adaptive mode combat raid work crafting gathering", () => plugin.SetMode(UiMode.Auto));
        yield return new CommandItem("mode-leisure", "Switch to Leisure Frame", "Hide combat density and restore the calm exploration ribbon.", "casual exploration leisure mode", () => plugin.SetMode(UiMode.Leisure));
        yield return new CommandItem("mode-roleplay", "Switch to Roleplay Dock", "Use the calm social layout with chat channels, emotes, Scenekeeper, and dock switching.", "roleplay rp social scene scenes emotes chat scenekeeper dock", () => plugin.SetMode(UiMode.Roleplay));
        yield return new CommandItem("mode-quest", "Switch to Quest Dock", "Use the raid-grade HUD without raid utility buttons and keep native quest tracking visible.", "quest fate scenario objectives dock", () => plugin.SetMode(UiMode.Quest));
        yield return new CommandItem("mode-raid", "Switch to Raid Dock", "Show preparation information and raid utilities before the pull.", "raid ready preparation mode", () => plugin.SetMode(UiMode.RaidReady));
        yield return new CommandItem("mode-work", "Switch to Work Frame", "Show CP/GP, the Workstation Dock, crafting buffs, navigation, and utility actions.", "work craft crafting gather gathering cp gp teamcraft garland ffxivcrafting resources workstation mode", () => plugin.SetMode(UiMode.Work));
        yield return new CommandItem("settings", "Open RE:Frame settings", "Theme, adaptive state, scale, integrations, and safety.", "config configuration options", plugin.ToggleConfigUi);
        yield return new CommandItem("native-audit", "Show remaining native UI", "Report which known native systems are still being integrated rather than fully replaced.", "audit remaining native holdout ui elements visible", plugin.ReportIntegratedNativeUi);


        yield return new CommandItem(
            "native-emotes",
            "Open Emotes",
            "Open FFXIV's native list of available emotes.",
            "native ffxiv xiv emote emotes expression gesture gestures list window menu",
            () => TryExecuteNativeCommand("/emotelist", "Emotes"));

        yield return new CommandItem(
            "native-waymarks",
            "Open Waymarks",
            "Open FFXIV's native Waymarks window.",
            "native ffxiv xiv waymark waymarks field marker markers raid menu window",
            () => TryOpenRaidTool(NativeRaidTool.Waymarks, "Waymarks"));
        yield return new CommandItem(
            "native-countdown",
            "Open Countdown",
            "Open FFXIV's native Countdown window.",
            "native ffxiv xiv countdown timer raid menu window",
            () => TryOpenRaidTool(NativeRaidTool.Countdown, "Countdown"));
        yield return new CommandItem(
            "native-strategy-board",
            "Open Strategy Board",
            "Open FFXIV's native Strategy Board list.",
            "native ffxiv xiv strategy board raidplan raid plan menu window",
            () => TryOpenRaidTool(NativeRaidTool.StrategyBoard, "Strategy Board"));


        foreach (var nativeMenu in GetNativeMenuEntries())
        {
            yield return new CommandItem(
                nativeMenu.Id,
                $"Open {nativeMenu.Name}",
                $"Open FFXIV's native {nativeMenu.Name} window or menu.",
                $"native ffxiv xiv menu window panel {nativeMenu.Name}",
                () => TryOpenMainCommand(nativeMenu.CommandId, nativeMenu.Name));
        }

        foreach (var gearset in plugin.Gearsets.GetSavedJobs())
        {
            var title = $"Switch to {gearset.JobName} ({gearset.Abbreviation})";
            var description = $"Equip saved gearset {gearset.GearsetId + 1}. Rapid repeat changes are blocked until FFXIV finishes the previous swap.";
            var keywords = $"switch change swap equip become job class to {gearset.JobName} {gearset.Abbreviation}";
            yield return new CommandItem(
                $"job-{gearset.ClassJobId}-{gearset.GearsetId}",
                title,
                description,
                keywords,
                () => plugin.EquipSavedJob(gearset));
        }

        foreach (var integration in IntegrationItems())
            yield return integration;
    }

    private IReadOnlyList<NativeMenuEntry> GetNativeMenuEntries()
    {
        if (nativeMenuEntries is not null)
            return nativeMenuEntries;

        var entries = new List<NativeMenuEntry>();
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<MainCommand>();
            var index = 0;
            foreach (var row in sheet)
            {
                var name = row.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name) ||
                    string.Equals(name, "Emotes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(new NativeMenuEntry(
                    $"native-main-command-{index++}",
                    name,
                    row.RowId));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not enumerate FFXIV's native Main Commands.");
        }

        entries.Sort((left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.CommandId.CompareTo(right.CommandId);
        });
        nativeMenuEntries = entries;
        return nativeMenuEntries;
    }

    private static string NormalizeNativeMenuRequest(string request)
    {
        var normalized = request.Trim();
        var prefixes = new[] { "please ", "open ", "show ", "launch ", "display " };
        var removedPrefix = true;
        while (removedPrefix && normalized.Length > 0)
        {
            removedPrefix = false;
            foreach (var prefix in prefixes)
            {
                if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                normalized = normalized[prefix.Length..].TrimStart();
                removedPrefix = true;
                break;
            }
        }

        return normalized.Trim().Trim('"', '\'');
    }

    private static void TryOpenNativeMenu(string requestedMenu)
    {
        var normalized = NormalizeNativeMenuRequest(requestedMenu);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (string.Equals(normalized, "emote", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "emotes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "emote list", StringComparison.OrdinalIgnoreCase))
        {
            TryExecuteNativeCommand("/emotelist", "Emotes");
            return;
        }

        var safeName = normalized.Replace('"', '\'').Trim();
        TryExecuteNativeCommand($"/maincommand \"{safeName}\"", safeName);
    }


    private static void TryOpenMainCommand(uint commandId, string label)
    {
        if (!NativeMainCommandService.TryOpen(commandId))
            Plugin.ChatGui.PrintError($"RE:Frame could not open the native {label} window.");
    }

    private static void TryExecuteNativeCommand(string command, string label)
    {
        if (!NativeChatCommandService.TryExecute(command))
            Plugin.ChatGui.PrintError($"RE:Frame could not open the native {label} window.");
    }

    private static void TryOpenRaidTool(NativeRaidTool tool, string label)
    {
        if (!NativeRaidToolService.TryOpen(tool))
            Plugin.ChatGui.PrintError($"RE:Frame could not open the native {label} window.");
    }

    private IEnumerable<CommandItem> IntegrationItems()
    {
        yield return CreateIntegration("appearance", "Open Appearance Workspace", "Open Penumbra and Glamourer together", "appearance mods glamour design", plugin.OpenAppearanceWorkspace);
        yield return CreateIntegration("penumbra", "Open Penumbra", "Appearance and mod management", "pen mods mod manager", plugin.Configuration.PenumbraCommand, editOnIntegrationsPage: true);
        yield return CreateIntegration("glamourer", "Open Glamourer", "Appearance designs and character presentation", "glam designs appearance", plugin.Configuration.GlamourerCommand, editOnIntegrationsPage: true);
        yield return CreateIntegration("lifestream", "Open Lifestream", "Travel and destination tools", "travel teleport world house", plugin.Configuration.LifestreamCommand, editOnIntegrationsPage: true);
        yield return CreateIntegration("bonesmith", "Open BoneSmith", "Skeleton and pose studio", "bones pose skeleton studio", plugin.Configuration.BoneSmithCommand, editOnIntegrationsPage: true);
        yield return CreateIntegration("scenekeeper", "Open Scenekeeper", "Roleplay scene and performance tools", "roleplay rp scene scenes scenekeeper performance", plugin.Configuration.ScenekeeperCommand, editOnIntegrationsPage: true);
        var characterSelectCommand = plugin.ResolveCharacterSelectCommand();
        var characterSelectDetected = !string.IsNullOrWhiteSpace(characterSelectCommand);
        var characterSelectStatus = characterSelectDetected ? "Detected" : "Command not detected; editable in Integrations";
        yield return new CommandItem(
            "character-select",
            "Open Character Select",
            $"Character switching and login tools  •  {characterSelectStatus}",
            "character select switch login alt alternate character swap",
            plugin.OpenCharacterSelect);

        foreach (var integration in plugin.Configuration.CustomIntegrations)
        {
            integration.EnsureValid();
            if (string.IsNullOrWhiteSpace(integration.Name) && string.IsNullOrWhiteSpace(integration.Command))
                continue;

            var displayName = string.IsNullOrWhiteSpace(integration.Name)
                ? "Custom Integration"
                : integration.Name.Trim();
            yield return CreateIntegration(
                $"custom-{integration.Id}",
                $"Open {displayName}",
                "User-defined plugin bridge",
                $"custom integration plugin {displayName}",
                integration.Command,
                editOnIntegrationsPage: true);
        }
    }

    private CommandItem CreateIntegration(
        string id,
        string title,
        string description,
        string keywords,
        string command,
        bool editOnIntegrationsPage = false)
    {
        var normalized = command?.Trim() ?? string.Empty;
        var detected = !string.IsNullOrWhiteSpace(normalized) && Plugin.CommandManager.Commands.ContainsKey(normalized.Split(' ')[0]);
        var editLocation = editOnIntegrationsPage ? "Integrations" : "Settings";
        var status = detected ? "Detected" : $"Command not detected — editable in {editLocation}";
        return new CommandItem(id, title, $"{description}  •  {status}", keywords, () =>
        {
            if (detected)
                Plugin.CommandManager.ProcessCommand(normalized);
            else if (editOnIntegrationsPage)
                plugin.OpenIntegrationsPage();
            else
                plugin.ToggleConfigUi();
        });
    }

    private static CommandItem CreateIntegration(string id, string title, string description, string keywords, System.Action action)
        => new(id, title, description, keywords, action);

    public void Dispose() { }

    private sealed record CommandItem(string Id, string Title, string Description, string Keywords, System.Action Action);
    private sealed record NativeMenuEntry(string Id, string Name, uint CommandId);
}
