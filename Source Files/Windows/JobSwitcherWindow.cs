using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.Theme;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class JobSwitcherWindow : Window, IDisposable
{
    private const double AnimationDuration = 0.18;
    private readonly Plugin plugin;
    private readonly Stopwatch openingAnimation = new();
    private HudBounds lastBounds;
    private UiMode openedMode = UiMode.Leisure;

    public JobSwitcherWindow(Plugin plugin)
        : base("RE:Frame Job Deck###REFrameJobSwitcher",
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
        openedMode = plugin.CurrentHudMode;
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
                    Plugin.ClientState.IsLoggedIn &&
                    !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing &&
                    !plugin.NativeWindows.HasProtectedDutyWindowOpen &&
                    !plugin.NativeContextMenus.IsAnyMenuOpen &&
                    plugin.CurrentHudMode == openedMode &&
                    plugin.IsHudElementVisible(HudElementIds.JobRibbon, plugin.CurrentHudMode);
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
            HudElementIds.JobRibbon,
            canvas.Origin,
            canvas.Size,
            plugin.CurrentHudMode);

        var width = Math.Clamp(610f * scale, 500f, MathF.Min(860f, canvas.Size.X - 32f));
        var height = Math.Clamp(610f * scale, 430f, MathF.Min(900f, canvas.Size.Y - 32f));
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


        ImGui.PopStyleVar(1);
        UiStyles.PopWindowStyle();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.TextScale, 0.75f, 1.75f));
        var theme = plugin.CurrentTheme;
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var gearsets = plugin.Gearsets.GetGearsetOverview();

        DrawHeader(theme, scale);
        ImGui.Spacing();


        var scrollHeight = MathF.Max(1f, ImGui.GetContentRegionAvail().Y);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, MathF.Max(14f, 15f * scale));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, UiStyles.WithAlpha(theme.Panel, 0.54f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.94f, 0.96f, 1.00f, 0.46f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.97f, 0.98f, 1.00f, 0.72f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(1.00f, 1.00f, 1.00f, 0.94f));

        if (ImGui.BeginChild(
                "##job-deck-scroll-region",
                new Vector2(0f, scrollHeight),
                false,
                ImGuiWindowFlags.None))
        {
            DrawScrollableDeckContent(gearsets, theme, scale);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();
    }

    private void DrawScrollableDeckContent(
        IReadOnlyList<JobGearsetOverview> gearsets,
        ThemePalette theme,
        float scale)
    {
        var current = gearsets.FirstOrDefault(gearset => gearset.IsCurrent);
        if (current is not null)
        {
            DrawCurrentJob(current, theme, scale);
            ImGui.Spacing();
            UiStyles.Divider(theme);
            ImGui.Spacing();
        }

        ImGui.TextDisabled("Select any saved gearset to change jobs through FFXIV.");
        ImGui.Spacing();

        var otherGearsets = gearsets.Where(gearset => !gearset.IsCurrent).ToArray();
        if (otherGearsets.Length == 0)
        {
            ImGui.TextWrapped("No other saved gearsets are available. Create gearsets in FFXIV and they will appear here automatically.");
            return;
        }

        foreach (var group in otherGearsets.GroupBy(gearset => gearset.RoleGroup).OrderBy(group => group.Key))
        {
            UiStyles.SectionLabel(GetRoleLabel(group.Key), theme);
            ImGui.Spacing();
            DrawGearsetGrid(group.ToArray(), theme, scale);
            ImGui.Spacing();
        }
    }

    private void DrawHeader(ThemePalette theme, float scale)
    {
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.TextScale * 1.18f, 0.85f, 2f));
        ImGui.TextUnformatted("JOB DECK");
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.TextScale, 0.75f, 1.75f));
        ImGui.TextDisabled("Your current role and every saved FFXIV gearset at a glance");

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

    private void DrawCurrentJob(JobGearsetOverview current, ThemePalette theme, float scale)
    {
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 112f * scale);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##current-job-hero", size);
        var draw = ImGui.GetWindowDrawList();
        var end = position + size;

        draw.AddRectFilled(position, end, ImGui.GetColorU32(UiStyles.WithAlpha(theme.PanelAlt, 0.88f)), 12f * scale);
        draw.AddRect(position, end, ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, 0.82f)), 12f * scale, ImDrawFlags.None, MathF.Max(1f, 1.4f * scale));
        draw.AddRectFilledMultiColor(
            position,
            new Vector2(end.X, position.Y + 4f * scale),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.Accent, 0.95f)),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, 0.95f)),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, 0.95f)),
            ImGui.GetColorU32(UiStyles.WithAlpha(theme.Accent, 0.95f)));

        var iconSize = 70f * scale;
        var iconPosition = position + new Vector2(17f, 20f) * scale;
        DrawJobIcon(draw, current.ClassJobId, iconPosition, iconSize, theme, 1f);

        var textX = iconPosition.X + iconSize + 17f * scale;
        draw.AddText(new Vector2(textX, position.Y + 11f * scale), ImGui.GetColorU32(theme.AccentStrong), "CURRENT JOB");
        draw.AddText(new Vector2(textX, position.Y + 31f * scale), ImGui.GetColorU32(theme.Text), current.JobName);
        draw.AddText(new Vector2(textX, position.Y + 55f * scale), ImGui.GetColorU32(theme.Muted), current.GearsetId >= 0
            ? $"{current.Abbreviation}  •  LEVEL {current.Level}  •  GEARSET {current.GearsetId + 1}"
            : $"{current.Abbreviation}  •  LEVEL {current.Level}  •  UNSAVED LOADOUT");
        var itemLevelLabel = current.ItemLevel > 0 ? $"ILVL {current.ItemLevel}" : "ILVL —";
        var itemLevelSize = ImGui.CalcTextSize(itemLevelLabel);
        draw.AddText(new Vector2(end.X - itemLevelSize.X - 17f * scale, position.Y + 31f * scale), ImGui.GetColorU32(theme.AccentStrong), itemLevelLabel);

        var progressPosition = new Vector2(textX, position.Y + 83f * scale);
        var progressWidth = MathF.Max(80f, end.X - textX - 20f * scale);
        DrawExperienceBar(draw, current, progressPosition, progressWidth, 14f * scale, theme, scale);
    }

    private void DrawGearsetGrid(IReadOnlyList<JobGearsetOverview> gearsets, ThemePalette theme, float scale)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var gap = 10f * scale;
        var columns = available >= 520f * scale ? 2 : 1;
        var cardWidth = columns == 2 ? (available - gap) * 0.5f : available;

        for (var index = 0; index < gearsets.Count; index++)
        {
            if (columns == 2 && index % 2 == 1)
                ImGui.SameLine(0f, gap);

            DrawGearsetCard(gearsets[index], new Vector2(cardWidth, 91f * scale), theme, scale);
        }
    }

    private void DrawGearsetCard(JobGearsetOverview gearset, Vector2 size, ThemePalette theme, float scale)
    {
        ImGui.PushID(gearset.GearsetId);
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##gearset-card", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var end = position + size;
        var fill = hovered
            ? UiStyles.WithAlpha(theme.Accent, 0.30f)
            : UiStyles.WithAlpha(theme.PanelAlt, 0.73f);
        var border = hovered
            ? UiStyles.WithAlpha(theme.AccentStrong, 0.94f)
            : UiStyles.WithAlpha(theme.Accent, 0.37f);

        draw.AddRectFilled(position, end, ImGui.GetColorU32(fill), 9f * scale);
        draw.AddRect(position, end, ImGui.GetColorU32(border), 9f * scale, ImDrawFlags.None, MathF.Max(1f, scale));

        var iconSize = 52f * scale;
        var iconPosition = position + new Vector2(10f, 10f) * scale;
        DrawJobIcon(draw, gearset.ClassJobId, iconPosition, iconSize, theme, hovered ? 1f : 0.90f);

        var textX = iconPosition.X + iconSize + 10f * scale;
        var nameY = position.Y + 10f * scale;
        draw.AddText(new Vector2(textX, nameY), ImGui.GetColorU32(theme.Text), gearset.JobName);
        draw.AddText(new Vector2(textX, nameY + 22f * scale), ImGui.GetColorU32(theme.Muted), $"Lv. {gearset.Level}  •  Set {gearset.GearsetId + 1}");

        var itemLevel = gearset.ItemLevel > 0 ? $"ILVL {gearset.ItemLevel}" : "ILVL —";
        var itemSize = ImGui.CalcTextSize(itemLevel);
        draw.AddText(new Vector2(end.X - itemSize.X - 10f * scale, nameY), ImGui.GetColorU32(theme.AccentStrong), itemLevel);

        DrawExperienceBar(
            draw,
            gearset,
            new Vector2(textX, position.Y + 65f * scale),
            MathF.Max(45f, end.X - textX - 10f * scale),
            11f * scale,
            theme,
            scale);

        if (hovered)
            ImGui.SetTooltip($"Equip Gearset {gearset.GearsetId + 1}: {gearset.JobName}");

        if (clicked && plugin.EquipJobGearset(gearset))
            Close();

        ImGui.PopID();
    }

    private static void DrawExperienceBar(
        ImDrawListPtr draw,
        JobGearsetOverview gearset,
        Vector2 position,
        float width,
        float height,
        ThemePalette theme,
        float scale)
    {
        var end = position + new Vector2(width, height);
        draw.AddRectFilled(position, end, ImGui.GetColorU32(UiStyles.WithAlpha(theme.Panel, 0.92f)), height * 0.5f);

        var progress = gearset.ExperienceProgress;
        if (progress > 0f)
        {
            var fillEnd = new Vector2(position.X + width * Math.Clamp(progress, 0f, 1f), end.Y);
            draw.AddRectFilled(position, fillEnd, ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, 0.92f)), height * 0.5f);
        }

        draw.AddRect(position, end, ImGui.GetColorU32(UiStyles.WithAlpha(theme.Accent, 0.55f)), height * 0.5f, ImDrawFlags.None, MathF.Max(1f, scale));
        var label = gearset.IsLevelCapped
            ? "MAX"
            : gearset.ExperienceAvailable
                ? $"EXP {gearset.ExperiencePercent}%"
                : "EXP —";
        var labelSize = ImGui.CalcTextSize(label);
        draw.AddText(
            new Vector2(end.X - labelSize.X, position.Y - labelSize.Y - 1f * scale),
            ImGui.GetColorU32(theme.Muted),
            label);
    }

    private static void DrawJobIcon(
        ImDrawListPtr draw,
        uint classJobId,
        Vector2 position,
        float size,
        ThemePalette theme,
        float opacity)
    {
        var end = position + new Vector2(size);
        draw.AddRectFilled(position, end, ImGui.GetColorU32(UiStyles.WithAlpha(theme.Panel, 0.88f)), 8f);
        var texture = TryGetJobIcon(classJobId);
        if (texture is not null)
        {
            var inset = MathF.Max(2f, size * 0.06f);
            draw.AddImage(
                texture.Handle,
                position + new Vector2(inset),
                end - new Vector2(inset),
                Vector2.Zero,
                Vector2.One,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, opacity)));
        }
        else
        {
            var abbreviation = classJobId.ToString();
            var textSize = ImGui.CalcTextSize(abbreviation);
            draw.AddText(position + (new Vector2(size) - textSize) * 0.5f, ImGui.GetColorU32(theme.Muted), abbreviation);
        }

        draw.AddRect(position, end, ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, 0.68f)), 8f, ImDrawFlags.None, 1f);
    }

    private static IDalamudTextureWrap? TryGetJobIcon(uint classJobId)
    {
        if (classJobId == 0 || classJobId > byte.MaxValue)
            return null;

        var isBaseClass = classJobId <= 18 || classJobId is 26 or 29;
        var iconId = (isBaseClass ? 62_000u : 62_100u) + classJobId;
        try
        {
            var highResolution = Plugin.TextureProvider
                .GetFromGameIcon(new GameIconLookup { IconId = iconId, HiRes = true })
                .GetWrapOrDefault();
            if (highResolution is not null)
                return highResolution;

            return Plugin.TextureProvider
                .GetFromGameIcon(new GameIconLookup { IconId = iconId, HiRes = false })
                .GetWrapOrDefault();
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not load ClassJob icon {IconId}.", iconId);
            return null;
        }
    }

    private static string GetRoleLabel(JobRoleGroup roleGroup)
        => roleGroup switch
        {
            JobRoleGroup.Tank => "Tanks",
            JobRoleGroup.Healer => "Healers",
            JobRoleGroup.MeleeDps => "Melee DPS",
            JobRoleGroup.PhysicalRangedDps => "Physical Ranged DPS",
            JobRoleGroup.MagicalRangedDps => "Magical Ranged DPS",
            JobRoleGroup.Crafter => "Disciples of the Hand",
            JobRoleGroup.Gatherer => "Disciples of the Land",
            _ => "Other Gearsets",
        };

    private static float EaseOutCubic(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        var inverse = 1f - value;
        return 1f - inverse * inverse * inverse;
    }

    public void Dispose() => Close();
}
