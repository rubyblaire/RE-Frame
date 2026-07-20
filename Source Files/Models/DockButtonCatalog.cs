using System;
using System.Collections.Generic;
using System.Linq;

namespace REFrameXIV.Models;

public readonly record struct DockButtonDefinition(string Id, string Label, string Tooltip);

public static class DockButtonCatalog
{
    public const string Leisure = "Leisure";
    public const string Roleplay = "Roleplay";
    public const string Quest = "Quest";
    public const string Work = "Work";
    public const string CustomCommand = "custom-command";

    private static readonly DockButtonDefinition[] AllActions =
    {
        Def("command", "COMMAND", "Open RE:Frame Command Center."),
        Def("travel", "TRAVEL", "Choose Teleport or Lifestream."),
        Def("appearance", "APPEARANCE", "Choose Character Select or Penumbra/Glamourer."),
        Def("chat", "CHAT", "Choose a chat channel."),
        Def("emotes", "EMOTES", "Open FFXIV's native Emote list."),
        Def("scenes", "SCENES", "Open Scenekeeper through its configured slash command."),
        Def("questlog", "QUEST LOG", "Open FFXIV's native Quest Log."),
        Def("dutyfinder", "DUTY FINDER", "Open FFXIV's native Duty Finder."),
        Def("resources", "RESOURCES", "Open crafting resources."),
        Def("docks", "DOCKS", "Choose another dock or Automatic mode."),
        Def(CustomCommand, "PLUGIN / COMMAND", "Run any registered slash command, including another plugin."),
    };

    private static readonly IReadOnlyDictionary<string, DockButtonDefinition[]> Defaults =
        new Dictionary<string, DockButtonDefinition[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Leisure] = Pick("command", "travel", "appearance", "docks"),
            [Roleplay] = Pick("chat", "emotes", "scenes", "docks"),
            [Quest] = Pick("command", "questlog", "dutyfinder"),
            [Work] = Pick("command", "resources", "docks"),
        };

    private static DockButtonDefinition Def(string id, string label, string tip) => new(id, label, tip);
    private static DockButtonDefinition[] Pick(params string[] ids)
        => ids.Select(id => AllActions.First(action => action.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();

    public static IReadOnlyList<string> DockKeys { get; } = new[] { Leisure, Roleplay, Quest, Work };
    public static IReadOnlyList<DockButtonDefinition> GetDefaults(string dockKey)
        => Defaults.TryGetValue(dockKey, out var value) ? value : Array.Empty<DockButtonDefinition>();


    public static IReadOnlyList<DockButtonDefinition> GetActionChoices(string dockKey)
    {
        var choices = new List<DockButtonDefinition>();
        choices.AddRange(GetDefaults(dockKey));
        foreach (var action in AllActions)
            AddUnique(choices, action);
        return choices;
    }

    private static void AddUnique(List<DockButtonDefinition> list, DockButtonDefinition item)
    {
        if (!list.Any(existing => existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
            list.Add(item);
    }

    public static List<DockButtonConfig> Resolve(Configuration configuration, string dockKey)
    {
        configuration.DockButtonLayouts ??= new(StringComparer.OrdinalIgnoreCase);
        if (!configuration.DockButtonLayouts.TryGetValue(dockKey, out var saved) || saved is null || saved.Count == 0)
            saved = GetDefaults(dockKey).Select((definition, index) => NewDefault(definition, index)).ToList();

        var result = new List<DockButtonConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in saved)
        {
            if (item is null)
                continue;

            var id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
            if (!seen.Add(id))
                id = Guid.NewGuid().ToString("N");

            var action = NormalizeAction(string.IsNullOrWhiteSpace(item.Action) ? LegacyAction(item.Id) : item.Action);
            var fallback = Definition(dockKey, action).Label;
            result.Add(new DockButtonConfig
            {
                Id = id,
                Label = NormalizeLabel(item.Label, fallback),
                Visible = item.Visible,
                Action = action,
                Command = (item.Command ?? string.Empty).Trim(),
            });
        }

        configuration.DockButtonLayouts[dockKey] = result;
        return result;
    }

    private static DockButtonConfig NewDefault(DockButtonDefinition definition, int index)
        => new() { Id = $"default-{index}-{definition.Id}", Label = definition.Label, Visible = true, Action = definition.Id };

    private static string LegacyAction(string? id)
    {
        var value = (id ?? string.Empty).Trim();
        if (value.StartsWith("default-", StringComparison.OrdinalIgnoreCase))
            return value.Split('-').Last();
        return string.IsNullOrWhiteSpace(value) ? "command" : value;
    }

    private static string NormalizeAction(string? action)
    {
        var value = (action ?? string.Empty).Trim();

        if (value.Equals("dock", StringComparison.OrdinalIgnoreCase))
            return "docks";
        return string.IsNullOrWhiteSpace(value) ? "command" : value;
    }

    public static IReadOnlyList<DockButtonConfig> Visible(Configuration configuration, string key)
        => Resolve(configuration, key).Where(button => button.Visible).ToArray();

    public static DockButtonDefinition Definition(string dockKey, string action)
    {
        var normalized = NormalizeAction(action);
        var definition = AllActions.FirstOrDefault(candidate => candidate.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrEmpty(definition.Id)
            ? definition
            : Def(CustomCommand, "CUSTOM", "Run a custom slash command.");
    }

    public static void Reset(Configuration configuration, string key)
    {
        configuration.DockButtonLayouts ??= new(StringComparer.OrdinalIgnoreCase);
        configuration.DockButtonLayouts[key] = GetDefaults(key).Select((definition, index) => NewDefault(definition, index)).ToList();
    }

    public static DockButtonConfig Add(Configuration configuration, string key)
    {
        var list = Resolve(configuration, key);
        var button = new DockButtonConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = "NEW BUTTON",
            Visible = true,
            Action = CustomCommand,
            Command = "/",
        };
        list.Add(button);
        return button;
    }

    private static string NormalizeLabel(string? value, string fallback)
    {
        var label = (value ?? string.Empty).Trim();
        return string.IsNullOrEmpty(label) ? fallback : label.Length <= 24 ? label : label[..24];
    }
}
