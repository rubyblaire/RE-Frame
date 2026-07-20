using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Theme;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class PocketDeckWindow : Window, IDisposable
{
    private const double AnimationDuration = 0.18;
    private readonly Plugin plugin;
    private readonly Stopwatch openingAnimation = new();
    private HudBounds lastBounds;

    public PocketDeckWindow(Plugin plugin)
        : base("RE:Frame Pocket###REFramePocketDeck",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = true;
        AllowBackgroundBlur = true;
    }

    public bool ContainsScreenPoint(Vector2 point)
        => IsOpen &&
           point.X >= lastBounds.Position.X &&
           point.Y >= lastBounds.Position.Y &&
           point.X <= lastBounds.Position.X + lastBounds.Size.X &&
           point.Y <= lastBounds.Position.Y + lastBounds.Size.Y;

    public void Open()
    {
        openingAnimation.Restart();
        IsOpen = true;
        BringToFront();
    }

    public void Close()
    {
        IsOpen = false;
        openingAnimation.Reset();
    }

    public void ToggleOpenState()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public override bool DrawConditions()
    {
        if (!IsOpen)
            return false;

        var valid = plugin.Configuration.ShowHudOverlay &&
                    !plugin.IsHudEditMode &&
                    !plugin.HotbarEditing.IsEnabled &&
                    Plugin.ClientState.IsLoggedIn &&
                    !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing &&
                    !plugin.NativeWindows.HasProtectedDutyWindowOpen &&
                    !plugin.NativeContextMenus.IsAnyMenuOpen &&
                    plugin.IsHudElementVisible(HudElementIds.PocketRibbon, plugin.CurrentHudMode);
        if (!valid)
            Close();

        return valid;
    }

    public override void PreDraw()
    {
        var canvas = HudCanvas.Current();
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var ribbon = HudLayout.Resolve(
            plugin.Configuration,
            HudElementIds.PocketRibbon,
            canvas.Origin,
            canvas.Size,
            plugin.CurrentHudMode);

        var width = Math.Clamp(520f * scale, 430f, MathF.Min(720f, canvas.Size.X - 32f));
        var height = Math.Clamp(290f * scale, 245f, MathF.Min(430f, canvas.Size.Y - 32f));
        var windowSize = new Vector2(width, height);
        var gap = 9f * scale;
        var openBelow = ribbon.Position.Y + ribbon.Size.Y + gap + height <= canvas.Origin.Y + canvas.Size.Y - 12f;
        var targetY = openBelow
            ? ribbon.Position.Y + ribbon.Size.Y + gap
            : ribbon.Position.Y - height - gap;
        var targetX = ribbon.Position.X + ribbon.Size.X * 0.5f - width * 0.5f;

        targetX = Math.Clamp(targetX, canvas.Origin.X + 12f, canvas.Origin.X + canvas.Size.X - width - 12f);
        targetY = Math.Clamp(targetY, canvas.Origin.Y + 12f, canvas.Origin.Y + canvas.Size.Y - height - 12f);

        var progress = plugin.Configuration.ReducedMotion
            ? 1f
            : EaseOutCubic((float)Math.Clamp(openingAnimation.Elapsed.TotalSeconds / AnimationDuration, 0d, 1d));
        var slide = (1f - progress) * 12f * scale * (openBelow ? -1f : 1f);
        var position = new Vector2(targetX, targetY + slide);
        lastBounds = new HudBounds(position, windowSize);

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
        UiStyles.PushWindowStyle(plugin.CurrentTheme, plugin.CurrentThemeStyle);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(progress, 0.05f, 1f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        UiStyles.PopWindowStyle();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.TextScale, 0.75f, 1.75f));
        var theme = plugin.CurrentTheme;
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);

        DrawHeader(theme, scale);
        ImGui.SetCursorPosY(76f * scale);

        var available = ImGui.GetContentRegionAvail().X;
        var gap = 10f * scale;
        var cardWidth = (available - gap) * 0.5f;
        var cardHeight = MathF.Max(76f * scale, (ImGui.GetContentRegionAvail().Y - gap) * 0.5f);

        DrawCard("MOUNTS", "Open FFXIV's Mount Guide.", "COLLECTION", new Vector2(cardWidth, cardHeight), theme, scale, plugin.OpenPocketMounts);
        ImGui.SameLine(0f, gap);
        DrawCard("MINIONS", "Open FFXIV's Minion Guide.", "COLLECTION", new Vector2(cardWidth, cardHeight), theme, scale, plugin.OpenPocketMinions);
        DrawCard("HUNT BILLS", "Open Key Items for Hunt and Clan/Nut bills.", "KEY ITEMS", new Vector2(cardWidth, cardHeight), theme, scale, plugin.OpenPocketHuntBills);
        ImGui.SameLine(0f, gap);
        DrawCard("DIG", "Use the native Dig action for treasure maps.", "TREASURE", new Vector2(cardWidth, cardHeight), theme, scale, plugin.UsePocketDig);
    }

    private void DrawHeader(ThemePalette theme, float scale)
    {
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.TextScale * 1.18f, 0.85f, 2f));
        ImGui.TextUnformatted("POCKET");
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.TextScale, 0.75f, 1.75f));
        ImGui.TextDisabled("Collections, hunt bills, and treasure-map tools within reach");

        var closeWidth = 74f * scale;
        var closeHeight = 28f * scale;
        var start = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(new Vector2(start.X + size.X - closeWidth - 14f * scale, start.Y + 14f * scale));
        if (ImGui.Button("CLOSE", new Vector2(closeWidth, closeHeight)))
            Close();

        ImGui.SetCursorScreenPos(new Vector2(start.X + 16f, start.Y + 65f * scale));
        UiStyles.Divider(theme);
    }

    private void DrawCard(
        string title,
        string description,
        string category,
        Vector2 size,
        ThemePalette theme,
        float scale,
        Action action)
    {
        ImGui.PushID(title);
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##pocket-card", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var end = position + size;
        var fill = hovered
            ? UiStyles.WithAlpha(theme.Accent, 0.30f)
            : UiStyles.WithAlpha(theme.PanelAlt, 0.76f);
        var border = hovered
            ? UiStyles.WithAlpha(theme.AccentStrong, 0.96f)
            : UiStyles.WithAlpha(theme.Accent, 0.40f);

        draw.AddRectFilled(position, end, ImGui.GetColorU32(fill), 10f * scale);
        draw.AddRect(position, end, ImGui.GetColorU32(border), 10f * scale, ImDrawFlags.None, MathF.Max(1f, scale));
        draw.AddRectFilledMultiColor(
            position,
            new Vector2(end.X, position.Y + 3f * scale),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.Accent, 0.94f)),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, 0.94f)),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, 0.94f)),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.Accent, 0.94f)));

        draw.AddText(position + new Vector2(12f, 12f) * scale, ImGui.GetColorU32(theme.AccentStrong), category);
        draw.AddText(position + new Vector2(12f, 34f) * scale, ImGui.GetColorU32(theme.Text), title);
        DrawWrappedText(draw, description, position + new Vector2(12f, 59f) * scale, size.X - 24f * scale, theme.Muted, scale);

        if (hovered)
            ImGui.SetTooltip(description);

        if (clicked)
        {
            action();
            Close();
        }

        ImGui.PopID();
    }

    private static void DrawWrappedText(
        ImDrawListPtr draw,
        string text,
        Vector2 position,
        float maximumWidth,
        Vector4 color,
        float scale)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = string.Empty;
        var y = position.Y;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
            if (!string.IsNullOrEmpty(line) && ImGui.CalcTextSize(candidate).X > maximumWidth)
            {
                draw.AddText(new Vector2(position.X, y), ImGui.GetColorU32(color), line);
                line = word;
                y += ImGui.GetTextLineHeight() + 2f * scale;
            }
            else
            {
                line = candidate;
            }
        }

        if (!string.IsNullOrEmpty(line))
            draw.AddText(new Vector2(position.X, y), ImGui.GetColorU32(color), line);
    }

    private static float EaseOutCubic(float value)
    {
        var inverse = 1f - Math.Clamp(value, 0f, 1f);
        return 1f - inverse * inverse * inverse;
    }

    public void Dispose() => Close();
}
