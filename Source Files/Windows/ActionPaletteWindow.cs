using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class ActionPaletteWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string search = string.Empty;
    private bool focusSearch;
    private bool positionInitialized;
    private Vector2 lastPosition;
    private Vector2 lastSize;

    public ActionPaletteWindow(Plugin plugin)
        : base("Action Palette###REFrameActionPalette",
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        Size = new Vector2(660f, 520f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500f, 360f),
            MaximumSize = new Vector2(920f, 820f),
        };
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        AllowPinning = false;
        AllowClickthrough = false;
        AllowBackgroundBlur = true;
        IsClickthrough = false;
        IsOpen = false;
    }

    public void OpenPalette()
    {
        search = string.Empty;
        focusSearch = true;
        IsClickthrough = false;
        IsPinned = false;
        IsOpen = true;
        BringToFront();
    }

    public override bool DrawConditions()
        => plugin.HotbarEditing.IsEnabled &&
           Plugin.ClientState.IsLoggedIn &&
           !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing;

    public override void PreDraw()
    {


        IsClickthrough = false;

        if (!positionInitialized)
        {
            var canvas = HudCanvas.Current();
            Position = canvas.Origin + (canvas.Size - Size) * 0.5f;
            PositionCondition = ImGuiCond.FirstUseEver;
            positionInitialized = true;
        }

        UiStyles.PushWindowStyle(plugin.CurrentTheme, plugin.CurrentThemeStyle);
    }
    public override void PostDraw() => UiStyles.PopWindowStyle();

    public override void Draw()
    {
        plugin.HotbarEditing.BeginInputFrame();
        try
        {
            DrawPaletteContents();
        }
        finally
        {


            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                plugin.HotbarEditing.FinalizePointerRelease(ContainsPoint(ImGui.GetMousePos()));
        }
    }

    private void DrawPaletteContents()
    {
        lastPosition = ImGui.GetWindowPos();
        lastSize = ImGui.GetWindowSize();
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Action Palette", theme);
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.HotbarEditing.CurrentJobLabel);
        ImGui.TextDisabled("Drag an action—or click it, then click a slot—to assign it.");
        ImGui.Spacing();

        var lockWidth = 116f;
        ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - lockWidth - 10f));
        if (focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            focusSearch = false;
        }
        ImGui.InputText("##reframe-action-search", ref search, 96);
        ImGui.SameLine();
        if (ImGui.Button("LOCK BARS", new Vector2(lockWidth, 0f)))
        {
            plugin.SetBarEditMode(false);
            return;
        }

        ImGui.Spacing();
        UiStyles.Divider(theme);
        ImGui.Spacing();

        var actions = plugin.HotbarEditing.SearchActions(search, 200);
        if (actions.Count == 0)
        {
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(search)
                ? "No learned player actions were found for the current job."
                : "No current-job actions match that search.");
            return;
        }

        if (ImGui.BeginChild("##reframe-action-palette-scroll", Vector2.Zero, false))
        {
            var availableWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
            const float desiredCardWidth = 104f;
            var columns = Math.Clamp((int)MathF.Floor((availableWidth + 8f) / (desiredCardWidth + 8f)), 3, 8);
            var cardWidth = MathF.Max(86f, (availableWidth - (columns - 1) * 8f) / columns);
            var cardHeight = MathF.Max(96f, cardWidth * 0.98f);

            for (var index = 0; index < actions.Count; index++)
            {
                DrawActionCard(actions[index], new Vector2(cardWidth, cardHeight));
                if ((index + 1) % columns != 0)
                    ImGui.SameLine(0f, 8f);
            }
        }
        ImGui.EndChild();
    }

    private void DrawActionCard(HotbarActionOption action, Vector2 size)
    {
        var theme = plugin.CurrentTheme;
        var start = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##reframe-action-{action.CommandType}-{action.ActionId}", size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var selectedForDrag = plugin.HotbarEditing.IsDraggingAction &&
                              plugin.HotbarEditing.DraggedActionType == action.CommandType &&
                              plugin.HotbarEditing.DraggedActionId == action.ActionId;

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            plugin.HotbarEditing.BeginActionDrag(action);
        else if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            plugin.HotbarEditing.BeginActionDrag(action);

        var draw = ImGui.GetWindowDrawList();
        var end = start + size;
        var background = hovered || active
            ? UiStyles.WithAlpha(theme.Accent, 0.24f)
            : UiStyles.WithAlpha(theme.PanelAlt, 0.82f);
        var border = selectedForDrag
            ? UiStyles.WithAlpha(theme.Success, 0.98f)
            : hovered
                ? UiStyles.WithAlpha(theme.AccentStrong, 0.92f)
                : UiStyles.WithAlpha(theme.Accent, 0.34f);

        draw.AddRectFilled(start, end, ImGui.ColorConvertFloat4ToU32(background), 9f);
        draw.AddRect(start, end, ImGui.ColorConvertFloat4ToU32(border), 9f, ImDrawFlags.None, selectedForDrag ? 2.4f : 1.2f);

        var iconSize = Math.Clamp(size.X * 0.48f, 38f, 58f);
        var iconMin = start + new Vector2((size.X - iconSize) * 0.5f, 8f);
        var iconMax = iconMin + new Vector2(iconSize);
        var wrap = HudRenderer.GetGameIcon(action.IconId);
        if (wrap is not null)
        {
            draw.AddImage(wrap.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One, 0xFFFFFFFF);
        }
        else
        {
            draw.AddRectFilled(iconMin, iconMax, ImGui.ColorConvertFloat4ToU32(UiStyles.WithAlpha(theme.Panel, 0.92f)), 6f);
            var missing = "◇";
            var missingSize = ImGui.CalcTextSize(missing);
            draw.AddText(iconMin + (new Vector2(iconSize) - missingSize) * 0.5f,
                ImGui.ColorConvertFloat4ToU32(theme.Muted), missing);
        }

        var label = Ellipsize(action.Name, MathF.Max(40f, size.X - 12f));
        var labelSize = ImGui.CalcTextSize(label);
        var labelY = iconMax.Y + 7f;
        draw.AddText(new Vector2(start.X + (size.X - labelSize.X) * 0.5f, labelY),
            ImGui.ColorConvertFloat4ToU32(theme.Text), label);

        var levelText = action.IsRoleAction ? $"ROLE • LV {action.RequiredLevel}" : $"LV {action.RequiredLevel}";
        var levelSize = ImGui.CalcTextSize(levelText);
        draw.AddText(new Vector2(start.X + (size.X - levelSize.X) * 0.5f, end.Y - levelSize.Y - 7f),
            ImGui.ColorConvertFloat4ToU32(theme.Muted), levelText);

        if (hovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(action.Name);
            ImGui.TextDisabled(action.IsRoleAction
                ? $"Role action • Learned at level {action.RequiredLevel}"
                : $"Job action • Learned at level {action.RequiredLevel}");
            ImGui.TextDisabled("Drag onto a slot, or click once and then click the destination slot.");
            ImGui.EndTooltip();
        }
    }

    private static string Ellipsize(string text, float maximumWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maximumWidth)
            return text;

        var value = text;
        while (value.Length > 1 && ImGui.CalcTextSize(value + "…").X > maximumWidth)
            value = value[..^1];
        return value + "…";
    }

    public bool ContainsPoint(Vector2 point)
        => IsOpen &&
           point.X >= lastPosition.X && point.X <= lastPosition.X + lastSize.X &&
           point.Y >= lastPosition.Y && point.Y <= lastPosition.Y + lastSize.Y;

    public void Dispose()
    {
        plugin.HotbarEditing.CancelActionDrag();
    }
}
