using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.Theme;

namespace REFrameXIV.UI;

public static unsafe class HudRenderer
{


    public static void DrawSkillEditBar(Plugin plugin, ImDrawListPtr draw, HudBounds bounds)
    {
        switch (plugin.HotbarEditing.ActiveElementId)
        {
            case HudElementIds.ActionBarOne:
                DrawEditableActionBar(plugin, draw, bounds, 0u);
                break;
            case HudElementIds.ActionBarTwo:
                DrawEditableActionBar(plugin, draw, bounds, 1u);
                break;
            case HudElementIds.ActionBarThree:
                DrawEditableActionBar(plugin, draw, bounds, 2u);
                break;
            case HudElementIds.CrossHotbar:
                DrawEditableCrossHotbar(plugin, draw, bounds, plugin.HotbarEditing.CrossHotbarSet);
                break;
            case HudElementIds.PetBar:
                DrawEditableActionBar(plugin, draw, bounds, ReframeHotbarIds.PetBar);
                break;
            case HudElementIds.UtilityBars:
                DrawEditableUtilityBar(plugin, draw, bounds);
                break;
            case HudElementIds.UtilityBarsTwo:
                DrawEditableSecondUtilityBar(plugin, draw, bounds);
                break;
            default:
                if (plugin.AdditionalHotbars.TryGetByElementId(plugin.HotbarEditing.ActiveElementId, out var bar))
                    DrawEditableAdditionalActionBar(plugin, draw, bounds, bar);
                break;
        }
    }

    public static void DrawEditableActionBar(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, uint hotbarId)
    {
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var shape = HotbarGridLayouts.Resolve(plugin.Configuration, hotbarId);
        DrawActionBar(draw, bounds, hotbarId, shape.Columns, shape.Rows, plugin.CurrentTheme, scale, 1f);
    }

    public static void DrawEditableAdditionalActionBar(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        ReframeAdditionalHotbar bar)
    {
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var shape = HotbarGridLayouts.Resolve(plugin.Configuration, bar.ElementId);
        if (bar.IsNativeBacked)
            DrawActionBar(draw, bounds, bar.RuntimeHotbarId, shape.Columns, shape.Rows, plugin.CurrentTheme, scale, 1f);
        else
            DrawVirtualActionBar(plugin, draw, bounds, bar.RuntimeHotbarId, shape.Columns, shape.Rows, plugin.CurrentTheme, scale, 1f);
    }

    public static void DrawEditableUtilityBar(Plugin plugin, ImDrawListPtr draw, HudBounds bounds)
    {
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        DrawUtilityBars(draw, bounds, plugin.CurrentTheme, scale, 1f);
    }

    public static void DrawEditableSecondUtilityBar(Plugin plugin, ImDrawListPtr draw, HudBounds bounds)
    {
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        DrawVirtualActionBar(plugin, draw, bounds, ReframeHotbarIds.SecondUtility, 4, 3, plugin.CurrentTheme, scale, 1f);
    }

    public static void DrawEditableCrossHotbar(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, int setNumber)
    {
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        setNumber = Math.Clamp(setNumber, 1, 8);
        DrawCrossHotbar(
            draw,
            bounds,
            new CrossHotbarState((uint)(9 + setNumber), setNumber, false, false, false),
            plugin.CurrentTheme,
            scale,
            1f);
    }


    internal static float ResolveRaidStatusDisplayScale(float interfaceScale, Vector2 viewportSize)
    {
        var manualScale = Math.Clamp(interfaceScale, 0.60f, 2.50f);
        return Math.Clamp(manualScale * HudCanvas.ReferenceDisplayScale(viewportSize), 0.50f, 3.25f);
    }

    private static readonly HashSet<ulong> MissingIconVariants = new();
    private static readonly HashSet<string> LoggedSlotFailures = new();
    private static readonly Dictionary<Type, Func<object, uint>> ClassJobReaders = new();
    private static readonly Dictionary<uint, uint> ClassJobIconIds = new();
    private static readonly Dictionary<uint, string> ClassJobAbbreviations = new();
    private static readonly HashSet<uint> LoggedMissingClassJobIcons = new();
    private static readonly object ClassJobCacheLock = new();
    private static readonly Dictionary<Type, Func<object, uint>> CastActionReaders = new();
    private static readonly Dictionary<uint, string> CastActionNames = new();
    private static readonly object CastActionCacheLock = new();

    public static void Draw(
        Plugin plugin,
        ImDrawListPtr draw,
        Vector2 origin,
        Vector2 size,
        bool preview,
        float combatBlend = 1f,
        bool editMode = false)
    {
        var config = plugin.Configuration;
        var theme = plugin.CurrentTheme;
        var mode = plugin.CurrentHudMode;
        var showUnitFrameJobIcons = mode is UiMode.RaidReady or UiMode.Quest or UiMode.Work;
        var sortPartyFramesByRole = !editMode && (plugin.AllianceFrames.IsAlliance || mode == UiMode.RaidReady || mode == UiMode.Quest);
        var scale = Math.Clamp(config.InterfaceScale, 0.60f, 2.50f);

        if (preview)
            scale *= MathF.Min(size.X / 1920f, size.Y / 1080f) * 1.45f;


        var raidStatusScale = preview
            ? scale
            : ResolveRaidStatusDisplayScale(config.InterfaceScale, size);

        var opacity = Math.Clamp(config.HudOpacity, 0.35f, 1f);
        combatBlend = editMode ? 1f : Math.Clamp(combatBlend, 0f, 1f);
        var skillEditing = plugin.HotbarEditing.IsEnabled;
        var keyboardBarEditing = skillEditing && !plugin.CrossHotbarState.IsControllerUser;
        var barMode = skillEditing && HudModeProfileService.IsCalmMode(mode) ? UiMode.RaidReady : mode;


        var combatVisible = editMode ||
                            ((!HudModeProfileService.IsCalmMode(mode) || skillEditing) &&
                             (skillEditing || combatBlend > 0.01f));
        var combatOpacity = editMode ? opacity : opacity * combatBlend;


        bool PreviewVisible(string id) => editMode || plugin.IsHudElementVisible(id, mode);
        if (PreviewVisible(HudElementIds.Location))
            DrawLocationFrame(
                draw,
                HudLayout.Resolve(config, HudElementIds.Location, origin, size, mode),
                plugin.GetLocationName(),
                plugin.AdaptiveState.ActivityLabel,
                plugin.GetCurrentWorldName(),
                plugin.GetClockTimeLabel(),
                config.ShowArdynChantButton,
                theme,
                scale,
                opacity);
        if (PreviewVisible(HudElementIds.JobRibbon))
            DrawJobRibbon(draw, HudLayout.Resolve(config, HudElementIds.JobRibbon, origin, size, mode), plugin.GetJobAbbreviation(), plugin.GetLevel(), theme, scale, opacity);
        if (PreviewVisible(HudElementIds.PocketRibbon))
            DrawPocketRibbon(draw, HudLayout.Resolve(config, HudElementIds.PocketRibbon, origin, size, mode), plugin.IsPocketDeckOpen, theme, scale, opacity);
        if (PreviewVisible(HudElementIds.Minimap))
            DrawMinimapFrame(plugin, draw, HudLayout.Resolve(config, HudElementIds.Minimap, origin, size, mode), theme, scale, opacity);
        if (plugin.ShouldUseForgeSquareMinimap && PreviewVisible(HudElementIds.ForgeCoordinates))
            DrawForgeCoordinates(plugin, draw, HudLayout.Resolve(config, HudElementIds.ForgeCoordinates, origin, size, mode), theme, scale, opacity);
        if (PreviewVisible(HudElementIds.Chat))
            DrawChatFrame(draw, HudLayout.Resolve(config, HudElementIds.Chat, origin, size, mode), theme, scale, opacity);
        if (PreviewVisible(HudElementIds.Party) && (Plugin.PartyList.Length > 1 || editMode))
            DrawPartyFrames(plugin, draw, HudLayout.Resolve(config, HudElementIds.Party, origin, size, mode), theme, scale, opacity, editMode, showUnitFrameJobIcons, sortPartyFramesByRole);
        if (plugin.AllianceFrames.IsAlliance || editMode)
        {
            if (PreviewVisible(HudElementIds.AllianceOne))
                DrawAllianceGroup(plugin, draw, HudLayout.Resolve(config, HudElementIds.AllianceOne, origin, size, mode), 0, plugin.AllianceFrames.GetGroupLabel(0), theme, scale, opacity, editMode);
            if (PreviewVisible(HudElementIds.AllianceTwo))
                DrawAllianceGroup(plugin, draw, HudLayout.Resolve(config, HudElementIds.AllianceTwo, origin, size, mode), 1, plugin.AllianceFrames.GetGroupLabel(1), theme, scale, opacity, editMode);
        }

        var hasLiveCrossHotbar = plugin.CrossHotbarState.TryGetState(out var crossHotbar);


        var showCrossHotbarPreview = editMode && config.ReplaceNativeCrossHotbar;
        var editingCrossHotbar = skillEditing && plugin.CrossHotbarState.IsControllerUser;
        if (editingCrossHotbar)
        {
            var editSet = Math.Clamp(plugin.HotbarEditing.CrossHotbarSet, 1, 8);
            crossHotbar = new CrossHotbarState((uint)(9 + editSet), editSet, false, false, false);
            hasLiveCrossHotbar = true;
        }

        if (config.ReplaceNativeCrossHotbar &&
            (PreviewVisible(HudElementIds.CrossHotbar) || editingCrossHotbar) &&
            (hasLiveCrossHotbar || showCrossHotbarPreview) &&
            (editingCrossHotbar || !hasLiveCrossHotbar || !crossHotbar.PetHotbarActive))
        {
            if (!hasLiveCrossHotbar)
                crossHotbar = new CrossHotbarState(10u, 1, false, false, false);

            DrawCrossHotbar(
                draw,
                HudLayout.Resolve(config, HudElementIds.CrossHotbar, origin, size, barMode),
                crossHotbar,
                theme,
                scale,
                opacity);
        }

        if (combatVisible)
        {
            if (PreviewVisible(HudElementIds.EnemyList))
                DrawEnemyList(plugin, draw, HudLayout.Resolve(config, HudElementIds.EnemyList, origin, size, mode), theme, scale, combatOpacity, editMode);
            if (PreviewVisible(HudElementIds.ActionBarOne) || keyboardBarEditing)
                DrawActionBar(draw, HudLayout.ActionBarOne(config, origin, size, barMode), 0u, HotbarGridLayouts.Resolve(config, 0u).Columns, HotbarGridLayouts.Resolve(config, 0u).Rows, theme, scale, combatOpacity);
            if (PreviewVisible(HudElementIds.ActionBarTwo) || keyboardBarEditing)
                DrawActionBar(draw, HudLayout.ActionBarTwo(config, origin, size, barMode), 1u, HotbarGridLayouts.Resolve(config, 1u).Columns, HotbarGridLayouts.Resolve(config, 1u).Rows, theme, scale, combatOpacity);
            if (PreviewVisible(HudElementIds.ActionBarThree) || keyboardBarEditing)
                DrawActionBar(draw, HudLayout.ActionBarThree(config, origin, size, barMode), 2u, HotbarGridLayouts.Resolve(config, 2u).Columns, HotbarGridLayouts.Resolve(config, 2u).Rows, theme, scale, combatOpacity);
            foreach (var bar in plugin.AdditionalHotbars.CombatBars)
            {
                if (!PreviewVisible(bar.ElementId) && !keyboardBarEditing)
                    continue;
                var barBounds = HudLayout.Resolve(config, bar.ElementId, origin, size, barMode);
                var shape = HotbarGridLayouts.Resolve(config, bar.ElementId);
                if (bar.IsNativeBacked)
                    DrawActionBar(draw, barBounds, bar.RuntimeHotbarId, shape.Columns, shape.Rows, theme, scale, combatOpacity);
                else
                    DrawVirtualActionBar(plugin, draw, barBounds, bar.RuntimeHotbarId, shape.Columns, shape.Rows, theme, scale, combatOpacity);
            }
            if (PreviewVisible(HudElementIds.PetBar) && (editMode || plugin.PetBarState.IsActive))
                DrawPetBar(plugin, draw, HudLayout.PetBar(config, origin, size, mode), theme, scale, combatOpacity);
            if (PreviewVisible(HudElementIds.UtilityBars))
            {
                var utilityBounds = HudLayout.UtilityBars(config, origin, size, mode);
                DrawUtilityBars(draw, utilityBounds, theme, scale, combatOpacity);
                if (config.FrameNativeHoldouts)
                    DrawUtilityBarDecal(draw, utilityBounds, theme, scale, combatOpacity);
            }
            if (PreviewVisible(HudElementIds.UtilityBarsTwo))
            {
                var utilityBounds = HudLayout.UtilityBarsTwo(config, origin, size, mode);
                DrawVirtualActionBar(plugin, draw, utilityBounds, ReframeHotbarIds.SecondUtility, 4, 3, theme, scale, combatOpacity);
            }
            if (PreviewVisible(HudElementIds.RaidTools) && (editMode || mode == UiMode.RaidReady))
                DrawRaidTools(draw, HudLayout.RaidTools(config, origin, size, mode), theme, scale, combatOpacity);
            if ((editMode || mode == UiMode.RaidReady) && PreviewVisible(HudElementIds.RaidBuffs))
                DrawRaidStatusPanel(draw, HudLayout.Resolve(config, HudElementIds.RaidBuffs, origin, size, mode), Plugin.ObjectTable.LocalPlayer as IBattleChara, false, config.TransparentRaidBuffBackground, theme, raidStatusScale, combatOpacity, editMode);
            if ((editMode || mode == UiMode.RaidReady) && PreviewVisible(HudElementIds.RaidDebuffs))
                DrawRaidStatusPanel(draw, HudLayout.Resolve(config, HudElementIds.RaidDebuffs, origin, size, mode), Plugin.ObjectTable.LocalPlayer as IBattleChara, true, config.TransparentRaidDebuffBackground, theme, raidStatusScale, combatOpacity, editMode);
            if ((editMode || mode == UiMode.RaidReady) && PreviewVisible(HudElementIds.RaidersKit))
                DrawRaidersKit(draw, HudLayout.Resolve(config, HudElementIds.RaidersKit, origin, size, mode), plugin, theme, scale, combatOpacity, editMode);
            if (PreviewVisible(HudElementIds.Player))
                DrawPlayerFrame(plugin, draw, HudLayout.Resolve(config, HudElementIds.Player, origin, size, mode), theme, scale, combatOpacity, editMode, showUnitFrameJobIcons, mode);
            if (PreviewVisible(HudElementIds.Target))
                DrawTargetFrame(draw, HudLayout.Resolve(config, HudElementIds.Target, origin, size, mode), theme, scale, combatOpacity, editMode, showUnitFrameJobIcons);
            if (PreviewVisible(HudElementIds.TargetOfTarget))
                DrawTargetOfTargetFrame(draw, HudLayout.Resolve(config, HudElementIds.TargetOfTarget, origin, size, mode), theme, scale, combatOpacity, editMode, showUnitFrameJobIcons);
            if (PreviewVisible(HudElementIds.CastBar))
                DrawCastBar(draw, HudLayout.Resolve(config, HudElementIds.CastBar, origin, size, mode), Plugin.TargetManager.Target as IBattleChara, theme, scale, combatOpacity, editMode, true);
            if (PreviewVisible(HudElementIds.PlayerCastBar))
                DrawCastBar(draw, HudLayout.Resolve(config, HudElementIds.PlayerCastBar, origin, size, mode), Plugin.ObjectTable.LocalPlayer as IBattleChara, theme, scale, combatOpacity, editMode, false);
            if (PreviewVisible(HudElementIds.LimitBreak))
                DrawLimitBreakGauge(draw, HudLayout.Resolve(config, HudElementIds.LimitBreak, origin, size, mode), theme, scale, combatOpacity, editMode, config.LimitBreakLayout);
            if (PreviewVisible(HudElementIds.Focus))
                DrawFocusFrame(draw, HudLayout.Resolve(config, HudElementIds.Focus, origin, size, mode), theme, scale, combatOpacity, editMode);
            var haloActive = editMode ||
                             plugin.AdaptiveState.CombatPresentationActive ||
                             ((mode is UiMode.RaidReady or UiMode.Quest) && config.ShowCombatHaloInRaidReady);
            if (PreviewVisible(HudElementIds.CombatHalo) && haloActive)
                DrawCombatHalo(plugin, draw, HudLayout.Resolve(config, HudElementIds.CombatHalo, origin, size, mode), theme, scale, combatOpacity, preview, editMode);
        }


        if (mode == UiMode.Roleplay && !combatVisible)
        {
            if (PreviewVisible(HudElementIds.ActionBarOne))
                DrawActionBar(draw, HudLayout.ActionBarOne(config, origin, size, mode), 0u, HotbarGridLayouts.Resolve(config, 0u).Columns, HotbarGridLayouts.Resolve(config, 0u).Rows, theme, scale, opacity);
            if (PreviewVisible(HudElementIds.ActionBarTwo))
                DrawActionBar(draw, HudLayout.ActionBarTwo(config, origin, size, mode), 1u, HotbarGridLayouts.Resolve(config, 1u).Columns, HotbarGridLayouts.Resolve(config, 1u).Rows, theme, scale, opacity);
            if (PreviewVisible(HudElementIds.ActionBarThree))
                DrawActionBar(draw, HudLayout.ActionBarThree(config, origin, size, mode), 2u, HotbarGridLayouts.Resolve(config, 2u).Columns, HotbarGridLayouts.Resolve(config, 2u).Rows, theme, scale, opacity);

            foreach (var bar in plugin.AdditionalHotbars.CombatBars)
            {
                if (!PreviewVisible(bar.ElementId))
                    continue;

                var barBounds = HudLayout.Resolve(config, bar.ElementId, origin, size, mode);
                var shape = HotbarGridLayouts.Resolve(config, bar.ElementId);
                if (bar.IsNativeBacked)
                    DrawActionBar(draw, barBounds, bar.RuntimeHotbarId, shape.Columns, shape.Rows, theme, scale, opacity);
                else
                    DrawVirtualActionBar(plugin, draw, barBounds, bar.RuntimeHotbarId, shape.Columns, shape.Rows, theme, scale, opacity);
            }

            if (PreviewVisible(HudElementIds.PetBar) && plugin.PetBarState.IsActive)
                DrawPetBar(plugin, draw, HudLayout.PetBar(config, origin, size, mode), theme, scale, opacity);

            if (PreviewVisible(HudElementIds.UtilityBars))
            {
                var utilityBounds = HudLayout.UtilityBars(config, origin, size, mode);
                DrawUtilityBars(draw, utilityBounds, theme, scale, opacity);
                if (config.FrameNativeHoldouts)
                    DrawUtilityBarDecal(draw, utilityBounds, theme, scale, opacity);
            }

            if (PreviewVisible(HudElementIds.UtilityBarsTwo))
            {
                var utilityBounds = HudLayout.UtilityBarsTwo(config, origin, size, mode);
                DrawVirtualActionBar(plugin, draw, utilityBounds, ReframeHotbarIds.SecondUtility, 4, 3, theme, scale, opacity);
            }
        }

        if (PreviewVisible(HudElementIds.LeisureDock) &&
            (editMode || (mode is UiMode.Leisure or UiMode.Roleplay or UiMode.Quest or UiMode.Work) || plugin.ShouldUseWorkstationDock(mode)))
        {
            var dockBounds = HudLayout.LeisureDock(config, origin, size, mode);
            if (mode == UiMode.Quest)
                DrawQuestDock(config, draw, dockBounds, theme, scale, opacity);
            else if (plugin.ShouldUseWorkstationDock(mode))
                DrawWorkstationDock(config, draw, dockBounds, theme, scale, opacity);
            else if (mode == UiMode.Roleplay)
                DrawRoleplayDock(config, draw, dockBounds, theme, scale, opacity);
            else
                DrawLeisureDock(config, draw, dockBounds, theme, scale, opacity);


            if (!editMode && plugin.ActiveLeisureDockPopup != LeisureDockPopup.None)
            {
                var popupBlend = plugin.GetLeisureDockPopupBlend();
                var popupBounds = HudLayout.ResolveLeisureDockPopup(
                    dockBounds,
                    plugin.ActiveLeisureDockPopup,
                    origin,
                    size,
                    scale,
                    popupBlend);
                DrawLeisureDockPopup(draw, popupBounds, plugin.ActiveLeisureDockPopup, theme, scale, opacity * popupBlend);
            }
            else if (!editMode && plugin.ActiveRoleplayDockPopup != RoleplayDockPopup.None)
            {
                var popupBlend = plugin.GetRoleplayDockPopupBlend();
                var popupBounds = HudLayout.ResolveRoleplayDockPopup(
                    dockBounds,
                    plugin.ActiveRoleplayDockPopup,
                    origin,
                    size,
                    scale,
                    popupBlend);
                DrawRoleplayDockPopup(draw, popupBounds, plugin.ActiveRoleplayDockPopup, theme, scale, opacity * popupBlend);
            }
            else if (!editMode && plugin.ActiveWorkstationDockPopup != WorkstationDockPopup.None)
            {
                var popupBlend = plugin.GetWorkstationDockPopupBlend();
                var popupBounds = HudLayout.ResolveWorkstationDockPopup(
                    dockBounds,
                    plugin.ActiveWorkstationDockPopup,
                    origin,
                    size,
                    scale,
                    popupBlend);
                DrawWorkstationDockPopup(draw, popupBounds, plugin.ActiveWorkstationDockPopup, theme, scale, opacity * popupBlend);
            }
        }
    }

    private static void DrawLocationFrame(
        ImDrawListPtr draw,
        HudBounds bounds,
        string location,
        string activity,
        string world,
        string eorzeaTime,
        bool showArdynChantButton,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity, 9f * scale);
        DrawThemeGradient(
            draw,
            p,
            p + new Vector2(MathF.Max(3f, s.X * 0.014f), s.Y),
            theme,
            opacity,
            vertical: true);

        var textClipMax = p + s;
        if (showArdynChantButton)
        {
            var button = HudLayout.LocationChantButton(bounds, scale);
            textClipMax.X = MathF.Max(p.X + 48f * scale, button.Position.X - 7f * scale);
        }

        draw.PushClipRect(p, textClipMax, true);
        draw.AddText(p + new Vector2(15f, MathF.Max(7f, s.Y * 0.10f)) * scale, Color(theme.Text, opacity), location.ToUpperInvariant());
        draw.AddText(p + new Vector2(15f, MathF.Max(28f, s.Y * 0.40f)) * scale, Color(theme.AccentStrong, opacity), $"RE:FRAME  /  {activity}");
        draw.AddText(p + new Vector2(15f, MathF.Max(48f, s.Y * 0.68f)) * scale, Color(theme.Muted, opacity), $"{world.ToUpperInvariant()}   •   {eorzeaTime}");
        draw.PopClipRect();

        if (!showArdynChantButton)
            return;

        var playButton = HudLayout.LocationChantButton(bounds, scale);
        var minimum = playButton.Position;
        var maximum = playButton.Position + playButton.Size;
        var center = playButton.Position + playButton.Size * 0.5f;
        var radius = playButton.Size.X * 0.18f;
        DrawGlassPanel(draw, minimum, playButton.Size, theme, opacity * 0.92f, 6f * scale);
        draw.AddRect(
            minimum,
            maximum,
            Color(theme.AccentStrong, opacity * 0.72f),
            6f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, 1.1f * scale));
        draw.AddTriangleFilled(
            center + new Vector2(-radius * 0.72f, -radius),
            center + new Vector2(-radius * 0.72f, radius),
            center + new Vector2(radius, 0f),
            Color(theme.AccentStrong, opacity));
    }

    private static void DrawJobRibbon(ImDrawListPtr draw, HudBounds bounds, string job, int level, ThemePalette theme, float scale, float opacity)
    {
        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.66f, 5f * scale);
        var label = $"{job}   LV {level}";
        var textSize = ImGui.CalcTextSize(label);
        var chevronWidth = MathF.Max(6f, 7f * scale);
        var contentWidth = textSize.X + chevronWidth + 11f * scale;
        var textPosition = new Vector2(
            p.X + MathF.Max(8f * scale, (s.X - contentWidth) * 0.5f),
            p.Y + (s.Y - textSize.Y) * 0.5f);
        draw.AddText(textPosition, Color(theme.AccentStrong, opacity), label);

        var chevronCenter = new Vector2(
            textPosition.X + textSize.X + 7f * scale,
            p.Y + s.Y * 0.5f + 1f * scale);
        draw.AddTriangleFilled(
            chevronCenter + new Vector2(-chevronWidth * 0.5f, -chevronWidth * 0.28f),
            chevronCenter + new Vector2(chevronWidth * 0.5f, -chevronWidth * 0.28f),
            chevronCenter + new Vector2(0f, chevronWidth * 0.42f),
            Color(theme.Muted, opacity * 0.88f));

        draw.AddLine(p + new Vector2(10f, s.Y - 3f), p + new Vector2(s.X - 10f, s.Y - 3f), Color(theme.Accent, opacity * 0.65f), 1f);
    }

    private static void DrawPocketRibbon(
        ImDrawListPtr draw,
        HudBounds bounds,
        bool deckOpen,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.66f, 5f * scale);

        var label = "POCKET";
        var textSize = ImGui.CalcTextSize(label);
        var chevronWidth = MathF.Max(6f, 7f * scale);
        var contentWidth = textSize.X + chevronWidth + 11f * scale;
        var textPosition = new Vector2(
            p.X + MathF.Max(8f * scale, (s.X - contentWidth) * 0.5f),
            p.Y + (s.Y - textSize.Y) * 0.5f);
        draw.AddText(textPosition, Color(deckOpen ? theme.Text : theme.AccentStrong, opacity), label);

        var chevronCenter = new Vector2(
            textPosition.X + textSize.X + 7f * scale,
            p.Y + s.Y * 0.5f + 1f * scale);
        if (deckOpen)
        {
            draw.AddTriangleFilled(
                chevronCenter + new Vector2(-chevronWidth * 0.5f, chevronWidth * 0.28f),
                chevronCenter + new Vector2(chevronWidth * 0.5f, chevronWidth * 0.28f),
                chevronCenter + new Vector2(0f, -chevronWidth * 0.42f),
                Color(theme.AccentStrong, opacity * 0.95f));
        }
        else
        {
            draw.AddTriangleFilled(
                chevronCenter + new Vector2(-chevronWidth * 0.5f, -chevronWidth * 0.28f),
                chevronCenter + new Vector2(chevronWidth * 0.5f, -chevronWidth * 0.28f),
                chevronCenter + new Vector2(0f, chevronWidth * 0.42f),
                Color(theme.Muted, opacity * 0.88f));
        }

        draw.AddLine(
            p + new Vector2(10f, s.Y - 3f),
            p + new Vector2(s.X - 10f, s.Y - 3f),
            Color(theme.Accent, opacity * 0.65f),
            1f);
    }

    private static void DrawMinimapFrame(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
    {
        if (plugin.ShouldUseForgeSquareMinimap)
        {
            DrawForgeSquareMinimapFrame(plugin, draw, bounds, theme, scale, opacity);
            return;
        }

        var center = bounds.Position + bounds.Size * 0.5f;
        var outerRadius = HudLayout.MinimapOuterRadius(bounds, scale);
        var apertureRadius = HudLayout.MinimapApertureRadius(bounds, scale);


        var maskOuterRadius = outerRadius + 2.5f * scale;
        var maskThickness = MathF.Max(1f, maskOuterRadius - apertureRadius);
        var maskCenterRadius = apertureRadius + maskThickness * 0.5f;
        var maskOpacity = MathF.Max(0.97f, opacity);
        draw.AddCircle(
            center,
            maskCenterRadius,
            Color(theme.Panel, maskOpacity),
            160,
            maskThickness + 1.5f * scale);


        var topCapHalfWidth = MathF.Max(33f * scale, outerRadius * 0.30f);
        draw.AddRectFilled(
            center + new Vector2(-topCapHalfWidth, -outerRadius - 18f * scale),
            center + new Vector2(topCapHalfWidth, -outerRadius + 10f * scale),
            Color(theme.Panel, maskOpacity),
            5f * scale);


        var westCoverCenter = center + new Vector2(-outerRadius - 3.5f * scale, 0f);
        draw.AddCircleFilled(
            westCoverCenter,
            11.5f * scale,
            Color(theme.Panel, maskOpacity),
            48);


        draw.AddCircle(center, apertureRadius, Color(theme.PanelAlt, MathF.Max(0.90f, opacity)), 128, 2.2f * scale);
        draw.AddCircle(center, apertureRadius + 1.6f * scale, Color(theme.AccentStrong, opacity * 0.48f), 128, 1.05f * scale);
        draw.AddCircle(center, outerRadius + 3f * scale, Color(theme.Accent, opacity * 0.78f), 128, 1.7f * scale);
        draw.AddCircle(center, outerRadius - 7f * scale, Color(theme.AccentStrong, opacity * 0.34f), 128, 1f * scale);

        var sideTick = MathF.Max(7f, 10f * scale);
        draw.AddLine(center - new Vector2(outerRadius + 3f * scale, 0f), center - new Vector2(outerRadius + sideTick, 0f), Color(theme.Accent, opacity * 0.60f), 1.2f * scale);
        draw.AddLine(center + new Vector2(outerRadius + 3f * scale, 0f), center + new Vector2(outerRadius + sideTick, 0f), Color(theme.AccentStrong, opacity * 0.60f), 1.2f * scale);
        DrawDiamond(draw, center + new Vector2(0f, -outerRadius - 10f * scale), 6f * scale, Color(theme.AccentStrong, opacity));

        if (plugin.NativeHudVisibility.TryGetIntegratedMinimapZoomBounds(out var zoomOut, out var zoomIn))
        {
            DrawMinimapZoomButton(draw, zoomOut, "-", theme, scale, opacity);
            DrawMinimapZoomButton(draw, zoomIn, "+", theme, scale, opacity);
        }
    }

    private static void DrawForgeSquareMinimapFrame(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var outer = HudLayout.ForgeSquareMinimapOuterBounds(bounds, scale);
        var aperture = HudLayout.ForgeSquareMinimapAperture(bounds, scale);
        var outerMin = outer.Position;
        var outerMax = outer.Position + outer.Size;
        var apertureMin = aperture.Position;
        var apertureMax = aperture.Position + aperture.Size;
        var maskOpacity = MathF.Max(0.97f, opacity);
        var panel = Color(theme.Panel, maskOpacity);
        var panelAlt = Color(theme.PanelAlt, MathF.Max(0.94f, opacity));


        draw.AddRectFilled(outerMin, new Vector2(apertureMin.X, outerMax.Y), panel);
        draw.AddRectFilled(new Vector2(apertureMax.X, outerMin.Y), outerMax, panel);
        draw.AddRectFilled(new Vector2(apertureMin.X, outerMin.Y), new Vector2(apertureMax.X, apertureMin.Y), panel);
        draw.AddRectFilled(new Vector2(apertureMin.X, apertureMax.Y), new Vector2(apertureMax.X, outerMax.Y), panel);

        var rounding = Math.Clamp(plugin.CurrentThemeStyle.FrameRounding * scale, 0f, 11f * scale);
        draw.AddRect(outerMin, outerMax, Color(theme.Accent, opacity * 0.88f), rounding, ImDrawFlags.None, 2f * scale);
        draw.AddRect(
            outerMin + new Vector2(4f * scale),
            outerMax - new Vector2(4f * scale),
            Color(theme.AccentStrong, opacity * 0.34f),
            MathF.Max(0f, rounding - 2f * scale),
            ImDrawFlags.None,
            1f * scale);
        draw.AddRect(apertureMin, apertureMax, panelAlt, 1.5f * scale, ImDrawFlags.None, 2.4f * scale);
        draw.AddRect(
            apertureMin - new Vector2(1.5f * scale),
            apertureMax + new Vector2(1.5f * scale),
            Color(theme.AccentStrong, opacity * 0.55f),
            2f * scale,
            ImDrawFlags.None,
            1f * scale);

        var corner = Math.Clamp(18f * scale, 12f, outer.Size.X * 0.18f);
        var cornerColor = Color(theme.AccentStrong, opacity * 0.96f);
        var cornerThickness = 2.3f * scale;
        draw.AddLine(outerMin, outerMin + new Vector2(corner, 0f), cornerColor, cornerThickness);
        draw.AddLine(outerMin, outerMin + new Vector2(0f, corner), cornerColor, cornerThickness);
        draw.AddLine(new Vector2(outerMax.X, outerMin.Y), new Vector2(outerMax.X - corner, outerMin.Y), cornerColor, cornerThickness);
        draw.AddLine(new Vector2(outerMax.X, outerMin.Y), new Vector2(outerMax.X, outerMin.Y + corner), cornerColor, cornerThickness);
        draw.AddLine(new Vector2(outerMin.X, outerMax.Y), new Vector2(outerMin.X + corner, outerMax.Y), cornerColor, cornerThickness);
        draw.AddLine(new Vector2(outerMin.X, outerMax.Y), new Vector2(outerMin.X, outerMax.Y - corner), cornerColor, cornerThickness);
        draw.AddLine(outerMax, outerMax - new Vector2(corner, 0f), cornerColor, cornerThickness);
        draw.AddLine(outerMax, outerMax - new Vector2(0f, corner), cornerColor, cornerThickness);

        var topCenter = new Vector2((outerMin.X + outerMax.X) * 0.5f, outerMin.Y);
        DrawDiamond(draw, topCenter, 5.2f * scale, Color(theme.AccentStrong, opacity));


        var compassInset = Math.Clamp(12f * scale, 9f, 18f);
        var compassCenter = aperture.Position + aperture.Size * 0.5f;
        DrawForgeCompassLabel(draw, "N", new Vector2(compassCenter.X, apertureMin.Y + compassInset), CompassAnchor.TopCenter, theme.AccentStrong, scale, opacity);
        DrawForgeCompassLabel(draw, "S", new Vector2(compassCenter.X, apertureMax.Y - compassInset), CompassAnchor.BottomCenter, theme.Text, scale, opacity);
        DrawForgeCompassLabel(draw, "W", new Vector2(apertureMin.X + compassInset, compassCenter.Y), CompassAnchor.MiddleLeft, theme.Text, scale, opacity);
        DrawForgeCompassLabel(draw, "E", new Vector2(apertureMax.X - compassInset, compassCenter.Y), CompassAnchor.MiddleRight, theme.Text, scale, opacity);

        var badgeSize = new Vector2(24f * scale, 20f * scale);
        var badgeMin = new Vector2(outerMax.X - badgeSize.X - 7f * scale, outerMax.Y - badgeSize.Y - 7f * scale);
        var badgeMax = badgeMin + badgeSize;
        draw.AddRectFilled(badgeMin, badgeMax, Color(theme.PanelAlt, maskOpacity), 3f * scale);
        draw.AddRect(badgeMin, badgeMax, Color(theme.Accent, opacity * 0.74f), 3f * scale, ImDrawFlags.None, 1f * scale);
        var badgeText = "M";
        var badgeTextSize = ImGui.CalcTextSize(badgeText);
        draw.AddText(badgeMin + (badgeSize - badgeTextSize) * 0.5f, Color(theme.Text, opacity), badgeText);

        if (!plugin.ForgeSquareMap.IsEmbedded)
        {
            draw.AddRectFilled(apertureMin, apertureMax, Color(theme.Panel, opacity * 0.48f), 1f * scale);
            var label = plugin.ForgeSquareMap.IsFullMapMode ? "FULL MAP OPEN" : "OPENING LIVE MAP";
            var labelSize = ImGui.CalcTextSize(label);
            draw.AddText(
                aperture.Position + (aperture.Size - labelSize) * 0.5f,
                Color(theme.Muted, opacity * 0.92f),
                label);
        }
    }

    private enum CompassAnchor
    {
        TopCenter,
        BottomCenter,
        MiddleLeft,
        MiddleRight,
    }

    private static void DrawForgeCompassLabel(
        ImDrawListPtr draw,
        string label,
        Vector2 anchor,
        CompassAnchor alignment,
        Vector4 foreground,
        float scale,
        float opacity)
    {
        var textSize = ImGui.CalcTextSize(label);
        var position = alignment switch
        {
            CompassAnchor.TopCenter => anchor - new Vector2(textSize.X * 0.5f, 0f),
            CompassAnchor.BottomCenter => anchor - new Vector2(textSize.X * 0.5f, textSize.Y),
            CompassAnchor.MiddleLeft => anchor - new Vector2(0f, textSize.Y * 0.5f),
            CompassAnchor.MiddleRight => anchor - new Vector2(textSize.X, textSize.Y * 0.5f),
            _ => anchor,
        };

        var shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, Math.Clamp(opacity * 0.92f, 0f, 1f)));
        var offset = MathF.Max(1f, 1.2f * scale);
        draw.AddText(position + new Vector2(-offset, 0f), shadow, label);
        draw.AddText(position + new Vector2(offset, 0f), shadow, label);
        draw.AddText(position + new Vector2(0f, -offset), shadow, label);
        draw.AddText(position + new Vector2(0f, offset), shadow, label);
        draw.AddText(position, Color(foreground, opacity), label);
    }

    private static void DrawForgeCoordinates(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var min = bounds.Position;
        var max = bounds.Position + bounds.Size;
        var rounding = Math.Clamp(plugin.CurrentThemeStyle.FrameRounding * scale, 2f, bounds.Size.Y * 0.35f);
        draw.AddRectFilled(min, max, Color(theme.Panel, MathF.Max(0.94f, opacity)), rounding);
        draw.AddRect(min, max, Color(theme.Accent, opacity * 0.72f), rounding, ImDrawFlags.None, 1.1f * scale);
        draw.AddLine(min + new Vector2(8f * scale, 2f * scale), new Vector2(max.X - 8f * scale, min.Y + 2f * scale), Color(theme.AccentStrong, opacity * 0.58f), 1f * scale);

        var label = string.IsNullOrWhiteSpace(plugin.ForgeSquareMap.Coordinates)
            ? "X: --.-   Y: --.-"
            : plugin.ForgeSquareMap.Coordinates;
        var textSize = ImGui.CalcTextSize(label);
        var textPosition = min + (bounds.Size - textSize) * 0.5f;
        draw.AddText(textPosition, Color(theme.ResolvedDockText, opacity), label);
    }

    private static void DrawMinimapZoomButton(
        ImDrawListPtr draw,
        HudBounds bounds,
        string label,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var min = bounds.Position;
        var max = bounds.Position + bounds.Size;
        var mouse = ImGui.GetIO().MousePos;
        var hovered = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
        var fill = hovered ? theme.PanelAlt : theme.Panel;
        var border = hovered ? theme.AccentStrong : theme.Accent;

        draw.AddRectFilled(min, max, Color(fill, MathF.Max(0.96f, opacity)), 4f * scale);
        draw.AddRect(min, max, Color(border, opacity * (hovered ? 0.95f : 0.72f)), 4f * scale, ImDrawFlags.None, 1.2f * scale);

        var textSize = ImGui.CalcTextSize(label);
        var textPosition = min + (bounds.Size - textSize) * 0.5f;
        if (label == "-")
            textPosition.Y -= 1f * scale;
        draw.AddText(textPosition, Color(theme.ResolvedDockText, opacity), label);
    }

    private static void DrawChatFrame(ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
    {
        var p = bounds.Position;
        var s = bounds.Size;


        draw.AddRectFilled(p, p + s, Color(theme.Panel, opacity * 0.035f), 6f * scale);
        draw.AddRectFilled(p + new Vector2(1f), p + new Vector2(s.X - 1f, MathF.Max(12f, s.Y * 0.16f)), Color(theme.PanelAlt, opacity * 0.05f), 5f * scale);
        draw.AddRect(p, p + s, Color(theme.Accent, opacity * 0.64f), 6f * scale, ImDrawFlags.None, 1.2f * scale);
        DrawThemeGradient(
            draw,
            p,
            p + new Vector2(MathF.Min(190f * scale, s.X), 2f * scale),
            theme,
            opacity);
        draw.AddLine(p + new Vector2(0f, s.Y), p + new Vector2(s.X, s.Y), Color(theme.AccentStrong, opacity * 0.38f), 1f);
        draw.AddText(p + new Vector2(8f, -18f) * scale, Color(theme.AccentStrong, opacity), "COMMS");
    }

    private static void DrawPartyFrames(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode,
        bool showJobIcons,
        bool sortByRole)
    {
        IReadOnlyList<IPartyMember?> orderedMembers = editMode
            ? Array.Empty<IPartyMember?>()
            : GetOrderedPartyMembers(sortByRole);
        var count = editMode ? Math.Max(4, Math.Min(8, Plugin.PartyList.Length)) : Math.Min(8, orderedMembers.Count);
        if (count <= 0)
            return;

        var isAlliance = plugin.AllianceFrames.IsAlliance;
        var contentPosition = bounds.Position;
        var contentHeight = bounds.Size.Y;
        if (isAlliance)
        {
            var headerHeight = Math.Clamp(22f * scale, 19f, 29f);
            draw.AddRectFilled(
                bounds.Position,
                bounds.Position + new Vector2(bounds.Size.X, headerHeight),
                Color(theme.PanelAlt, opacity * 0.88f),
                7f * scale);
            draw.AddLine(
                bounds.Position + new Vector2(0f, headerHeight),
                bounds.Position + new Vector2(bounds.Size.X, headerHeight),
                Color(theme.AccentStrong, opacity * 0.72f),
                MathF.Max(1f, scale));
            var headerText = $"{plugin.AllianceFrames.GetLocalGroupLabel()}   {count}/8";
            draw.AddText(
                bounds.Position + new Vector2(8f * scale, MathF.Max(2f, (headerHeight - ImGui.GetTextLineHeight()) * 0.5f)),
                Color(theme.AccentStrong, opacity),
                headerText);

            contentPosition = bounds.Position + new Vector2(0f, headerHeight + 2f * scale);
            contentHeight = MathF.Max(1f, bounds.Size.Y - headerHeight - 2f * scale);
        }

        var gap = (isAlliance ? 2f : 4f) * scale;
        var minimumRowHeight = isAlliance ? 15f : 24f;
        var rowHeight = MathF.Max(minimumRowHeight, (contentHeight - gap * (count - 1)) / count);
        var showResourceNumbers = plugin.AdaptiveState.EffectiveMode is UiMode.RaidReady or UiMode.Quest or UiMode.Work || editMode;

        for (var i = 0; i < count; i++)
        {
            var p = contentPosition + new Vector2(0f, i * (rowHeight + gap));
            var member = editMode
                ? GetLocalPartyMember(i)
                : orderedMembers[i];
            var actor = member?.GameObject as IBattleChara;
            var name = member?.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = editMode ? $"Party Member {i + 1}" : $"Party {i + 1}";


            var isOffline = !editMode
                && member is not null
                && plugin.PartyConnections.IsOffline(name);
            DrawGlassPanel(draw, p, new Vector2(bounds.Size.X, rowHeight), theme, opacity * (isOffline ? 0.40f : 0.64f), 5f * scale);
            if (isOffline)
            {
                var offlineTint = new Vector4(0.22f, 0.23f, 0.26f, opacity * 0.58f);
                draw.AddRectFilled(
                    p + new Vector2(1f * scale),
                    p + new Vector2(bounds.Size.X - 1f * scale, rowHeight - 1f * scale),
                    Color(offlineTint, opacity),
                    5f * scale);
                draw.AddRect(
                    p,
                    p + new Vector2(bounds.Size.X, rowHeight),
                    Color(theme.Muted, opacity * 0.78f),
                    5f * scale,
                    ImDrawFlags.None,
                    1f * scale);
            }

            var statusBandHeight = 0f;
            if (!isOffline && actor is not null)
                statusBandHeight = DrawPartyStatusIcons(draw, actor, p, bounds.Size.X, rowHeight, theme, scale, opacity);

            var ratio = isOffline
                ? 0f
                : member is not null && member.MaxHP > 0
                    ? Math.Clamp(member.CurrentHP / (float)member.MaxHP, 0f, 1f)
                    : 0.78f;

            var isLocalPlayer = IsLocalPartyMember(member, actor);
            var barColor = isOffline ? theme.Muted : isLocalPlayer ? theme.AccentStrong : theme.Success;
            var barWidth = isOffline ? bounds.Size.X - 6f * scale : (bounds.Size.X - 6f * scale) * ratio;
            draw.AddRectFilled(
                p + new Vector2(3f * scale, rowHeight - 5f * scale),
                p + new Vector2(3f * scale + barWidth, rowHeight - 2f * scale),
                Color(barColor, opacity * (isOffline ? 0.35f : 0.88f)),
                2f * scale);

            var textHeight = ImGui.GetTextLineHeight();
            var availableTop = statusBandHeight > 0f ? statusBandHeight + 1f * scale : 0f;
            var availableHeight = MathF.Max(textHeight, rowHeight - availableTop - 7f * scale);
            var textY = availableTop + MathF.Max(1f * scale, (availableHeight - textHeight) * 0.5f);


            var iconDrawn = false;
            var jobIconSize = Math.Clamp(rowHeight - 8f * scale, 14f * scale, 20f * scale);
            var jobIconPosition = new Vector2(
                p.X + 5f * scale,
                p.Y + availableTop + MathF.Max(0f, (availableHeight - jobIconSize) * 0.5f));
            if (showJobIcons)
            {
                var jobIconSource = (object?)member ?? (editMode && i == 0 ? Plugin.ObjectTable.LocalPlayer : null);
                if (TryGetPartyJobIcon(jobIconSource, actor, out var jobIconId, out var jobAbbreviation))
                {
                    DrawPartyJobIcon(
                        draw,
                        jobIconId,
                        jobAbbreviation,
                        jobIconPosition,
                        jobIconSize,
                        theme,
                        scale,
                        opacity * (isOffline ? 0.55f : 1f));
                    iconDrawn = true;
                }
                else if (editMode)
                {
                    DrawPartyJobIcon(
                        draw,
                        0,
                        i == 0 ? "JOB" : "?",
                        jobIconPosition,
                        jobIconSize,
                        theme,
                        scale,
                        opacity * 0.72f);
                    iconDrawn = true;
                }
            }

            var nameX = p.X + (iconDrawn ? 5f * scale + jobIconSize + 5f * scale : 9f * scale);
            var namePos = new Vector2(nameX, p.Y + textY);
            var displayName = name.ToUpperInvariant();

            if (isOffline)
            {
                draw.AddText(namePos, Color(theme.Muted, opacity), displayName);
                const string offlineLabel = "OFFLINE";
                var offlineSize = ImGui.CalcTextSize(offlineLabel);
                var offlinePos = new Vector2(
                    p.X + bounds.Size.X - offlineSize.X - 9f * scale,
                    p.Y + textY);
                var minimumOfflineX = namePos.X + ImGui.CalcTextSize(displayName).X + 8f * scale;
                offlinePos.X = MathF.Max(minimumOfflineX, offlinePos.X);
                draw.AddText(offlinePos, Color(new Vector4(0.72f, 0.74f, 0.78f, 1f), opacity), offlineLabel);
            }
            else if (showResourceNumbers && member is not null && member.MaxHP > 0)
            {
                var currentMp = actor is not null ? (ulong)actor.CurrentMp : 0UL;
                var maxMp = actor is not null ? (ulong)actor.MaxMp : 0UL;
                DrawPartyResourceSummary(
                    draw,
                    p,
                    bounds.Size.X,
                    rowHeight,
                    namePos,
                    displayName,
                    member.CurrentHP,
                    member.MaxHP,
                    currentMp,
                    maxMp,
                    theme,
                    scale,
                    opacity);
            }
            else
            {
                draw.AddText(namePos, Color(theme.Text, opacity), displayName);
            }
        }
    }

    private static void DrawAllianceGroup(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        int groupIndex,
        string label,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode)
    {
        DrawGlassPanel(draw, bounds.Position, bounds.Size, theme, opacity * 0.52f, 7f * scale);

        var headerHeight = Math.Clamp(22f * scale, 19f, 29f);
        var groupAccent = groupIndex == 0 ? theme.AccentStrong : theme.Accent;
        draw.AddRectFilled(
            bounds.Position,
            bounds.Position + new Vector2(bounds.Size.X, headerHeight),
            Color(theme.PanelAlt, opacity * 0.88f),
            7f * scale);
        draw.AddLine(
            bounds.Position + new Vector2(0f, headerHeight),
            bounds.Position + new Vector2(bounds.Size.X, headerHeight),
            Color(groupAccent, opacity * 0.72f),
            MathF.Max(1f, scale));

        var memberCount = editMode ? 8 : plugin.AllianceFrames.CountMembers(groupIndex);
        var headerText = $"{label}   {memberCount}/8";
        draw.AddText(
            bounds.Position + new Vector2(8f * scale, MathF.Max(2f, (headerHeight - ImGui.GetTextLineHeight()) * 0.5f)),
            Color(groupAccent, opacity),
            headerText);

        const int slotCount = 8;
        var rowGap = MathF.Max(1f, 2f * scale);
        var bodyHeight = MathF.Max(1f, bounds.Size.Y - headerHeight - 4f * scale);
        var rowHeight = MathF.Max(15f, (bodyHeight - rowGap * (slotCount - 1)) / slotCount);
        var rowWidth = bounds.Size.X - 6f * scale;
        var bodyTop = bounds.Position.Y + headerHeight + 2f * scale;

        for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            var member = editMode ? null : plugin.AllianceFrames.GetMember(groupIndex, slotIndex);
            var actor = editMode ? null : plugin.AllianceFrames.GetActor(groupIndex, slotIndex) as IBattleChara;
            var rowPosition = new Vector2(
                bounds.Position.X + 3f * scale,
                bodyTop + slotIndex * (rowHeight + rowGap));
            var rowSize = new Vector2(rowWidth, rowHeight);
            var rowEnd = rowPosition + rowSize;
            var isEmpty = member is null && !editMode;
            var rowOpacity = opacity * (isEmpty ? 0.20f : 0.62f);

            draw.AddRectFilled(rowPosition, rowEnd, Color(theme.Panel, rowOpacity), 4f * scale);
            draw.AddRect(rowPosition, rowEnd, Color(theme.Accent, opacity * (isEmpty ? 0.16f : 0.38f)), 4f * scale, ImDrawFlags.None, MathF.Max(1f, scale));

            var ratio = member is not null && member.MaxHP > 0
                ? Math.Clamp(member.CurrentHP / (float)member.MaxHP, 0f, 1f)
                : editMode ? 0.82f : 0f;
            var barInset = MathF.Max(2f, 3f * scale);
            var barHeight = MathF.Max(2f, 3f * scale);
            draw.AddRectFilled(
                new Vector2(rowPosition.X + barInset, rowEnd.Y - barHeight - 1f * scale),
                new Vector2(rowPosition.X + barInset + MathF.Max(0f, rowSize.X - barInset * 2f) * ratio, rowEnd.Y - 1f * scale),
                Color(groupAccent, opacity * (isEmpty ? 0.12f : 0.86f)),
                1.5f * scale);

            if (isEmpty)
                continue;

            var maximumIconSize = MathF.Max(10f, MathF.Min(20f * scale, rowHeight - 4f));
            var iconSize = Math.Clamp(rowHeight - 5f * scale, 10f, maximumIconSize);
            var iconPosition = new Vector2(
                rowPosition.X + 3f * scale,
                rowPosition.Y + MathF.Max(1f, (rowHeight - iconSize) * 0.5f));
            var jobIconSource = (object?)member;
            if (TryGetPartyJobIcon(jobIconSource, actor, out var jobIconId, out var jobAbbreviation))
            {
                DrawPartyJobIcon(draw, jobIconId, jobAbbreviation, iconPosition, iconSize, theme, scale, opacity);
            }
            else
            {
                DrawPartyJobIcon(
                    draw,
                    0,
                    editMode ? new[] { "PLD", "WHM", "SCH", "DRG", "NIN", "BRD", "BLM", "RDM" }[slotIndex] : "?",
                    iconPosition,
                    iconSize,
                    theme,
                    scale,
                    opacity * 0.82f);
            }

            var name = member?.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = editMode ? $"Alliance {groupIndex + 2} Member {slotIndex + 1}" : "Unknown";
            var displayName = name.ToUpperInvariant();
            var textY = rowPosition.Y + MathF.Max(1f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f - 1f * scale);
            var namePosition = new Vector2(iconPosition.X + iconSize + 5f * scale, textY);

            var hpText = member is not null && member.MaxHP > 0
                ? $"{MathF.Round(member.CurrentHP / (float)member.MaxHP * 100f):0}%"
                : editMode ? "100%" : string.Empty;
            var hpSize = string.IsNullOrEmpty(hpText) ? Vector2.Zero : ImGui.CalcTextSize(hpText);
            var rightPadding = 6f * scale;
            var hpPosition = new Vector2(rowEnd.X - rightPadding - hpSize.X, textY);
            var nameClipMaxX = string.IsNullOrEmpty(hpText)
                ? rowEnd.X - rightPadding
                : hpPosition.X - 5f * scale;

            draw.PushClipRect(
                new Vector2(namePosition.X, rowPosition.Y),
                new Vector2(MathF.Max(namePosition.X + 1f, nameClipMaxX), rowEnd.Y),
                true);
            draw.AddText(namePosition, Color(theme.Text, opacity), displayName);
            draw.PopClipRect();

            if (!string.IsNullOrEmpty(hpText))
                draw.AddText(hpPosition, Color(theme.Muted, opacity), hpText);
        }
    }

    private static void DrawPartyJobIcon(
        ImDrawListPtr draw,
        uint iconId,
        string abbreviation,
        Vector2 position,
        float iconSize,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var end = position + new Vector2(iconSize);
        draw.AddRectFilled(position, end, Color(theme.Panel, opacity * 0.92f), 3f * scale);

        var wrap = iconId != 0 ? TryGetGameIcon(iconId) : null;
        if (wrap is not null)
        {
            var inset = MathF.Max(1f, scale);
            draw.AddImage(
                wrap.Handle,
                position + new Vector2(inset),
                end - new Vector2(inset),
                Vector2.Zero,
                Vector2.One,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity)));
        }
        else
        {
            var fallback = string.IsNullOrWhiteSpace(abbreviation) ? "?" : abbreviation.Trim().ToUpperInvariant();
            if (fallback.Length > 3)
                fallback = fallback[..3];
            var fallbackSize = ImGui.CalcTextSize(fallback);
            var fallbackPosition = position + (new Vector2(iconSize) - fallbackSize) * 0.5f;
            draw.AddText(fallbackPosition, Color(theme.Text, opacity * 0.94f), fallback);
        }

        draw.AddRect(
            position,
            end,
            Color(theme.Accent, opacity * 0.72f),
            3f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, scale));
    }

    internal static IPartyMember? GetLocalPartyMember(int index)
    {
        if (index is < 0 or > 7)
            return null;

        try
        {
            var address = Plugin.PartyList.GetPartyMemberAddress(index);
            if (address != 0)
                return Plugin.PartyList.CreatePartyMemberReference(address);
        }
        catch
        {

        }

        return index < Plugin.PartyList.Length ? Plugin.PartyList[index] : null;
    }

    internal static IReadOnlyList<IPartyMember?> GetOrderedPartyMembers(bool sortByRole)
    {
        var entries = new List<(IPartyMember? Member, int OriginalIndex, int RoleOrder)>();
        var count = Math.Min(8, Plugin.PartyList.Length);
        for (var index = 0; index < count; index++)
        {
            var member = GetLocalPartyMember(index);
            var classJobId = ReadClassJobId(member);
            if (classJobId == 0)
                classJobId = ReadClassJobId(member?.GameObject);

            entries.Add((member, index, GetPartyRoleOrder(classJobId)));
        }

        if (sortByRole)
        {
            entries.Sort(static (left, right) =>
            {
                var roleComparison = left.RoleOrder.CompareTo(right.RoleOrder);
                return roleComparison != 0
                    ? roleComparison
                    : left.OriginalIndex.CompareTo(right.OriginalIndex);
            });
        }

        return entries.Select(static entry => entry.Member).ToArray();
    }

    internal static int GetPartyRoleOrder(uint classJobId)
        => classJobId switch
        {

            1 or 3 or 19 or 21 or 32 or 37 => 0,


            6 or 24 or 28 or 33 or 40 => 1,


            2 or 4 or 20 or 22 or 29 or 30 or 34 or 39 or 41 => 2,


            5 or 23 or 31 or 38 => 3,


            7 or 25 or 26 or 27 or 35 or 36 or 42 => 4,

            _ => 5,
        };

    private static bool IsLocalPartyMember(IPartyMember? member, IBattleChara? actor)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer is null)
            return false;

        if (actor is not null && actor.GameObjectId != 0 && actor.GameObjectId == localPlayer.GameObjectId)
            return true;

        if (member is not null && member.EntityId != 0 && member.EntityId == localPlayer.EntityId)
            return true;

        var memberName = member?.Name.ToString();
        var localName = localPlayer.Name.ToString();
        return !string.IsNullOrWhiteSpace(memberName)
            && !string.IsNullOrWhiteSpace(localName)
            && string.Equals(memberName, localName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPartyJobIcon(
        object? partyMember,
        IBattleChara? actor,
        out uint iconId,
        out string abbreviation)
    {
        iconId = 0;
        abbreviation = string.Empty;

        var classJobId = ReadClassJobId(partyMember);
        if (classJobId == 0)
            classJobId = ReadClassJobId(actor);
        if (classJobId == 0)
            return false;

        lock (ClassJobCacheLock)
        {
            if (ClassJobIconIds.TryGetValue(classJobId, out iconId))
            {
                ClassJobAbbreviations.TryGetValue(classJobId, out abbreviation!);
                return iconId != 0;
            }
        }


        iconId = ResolveClassJobIconId(classJobId);

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
            if (sheet.TryGetRow(classJobId, out var classJob))
                abbreviation = classJob.Abbreviation.ToString();
        }
        catch (Exception ex)
        {
            lock (ClassJobCacheLock)
            {
                if (LoggedMissingClassJobIcons.Add(classJobId))
                    Plugin.Log.Verbose(ex, "RE:Frame could not read the abbreviation for ClassJob {ClassJobId}.", classJobId);
            }
        }

        if (string.IsNullOrWhiteSpace(abbreviation))
            abbreviation = $"J{classJobId}";

        lock (ClassJobCacheLock)
        {
            ClassJobIconIds[classJobId] = iconId;
            ClassJobAbbreviations[classJobId] = abbreviation;
        }

        return iconId != 0;
    }

    private static uint ResolveClassJobIconId(uint classJobId)
    {
        if (classJobId == 0 || classJobId > 255)
            return 0;


        var isBaseClass = classJobId <= 18 || classJobId is 26 or 29;
        return (isBaseClass ? 62_000u : 62_100u) + classJobId;
    }

    internal static uint ReadClassJobId(object? source)
    {
        if (source is null)
            return 0;

        Func<object, uint> reader;
        var sourceType = source.GetType();
        lock (ClassJobCacheLock)
        {
            if (ClassJobReaders.TryGetValue(sourceType, out var cachedReader))
            {
                reader = cachedReader;
            }
            else
            {
                reader = CreateClassJobReader(sourceType);
                ClassJobReaders[sourceType] = reader;
            }
        }

        try
        {
            return reader(source);
        }
        catch
        {
            return 0;
        }
    }

    private static Func<object, uint> CreateClassJobReader(Type sourceType)
    {
        static PropertyInfo? FindProperty(Type type)
        {
            foreach (var propertyName in new[] { "ClassJob", "ClassJobId", "ClassJobID", "JobId", "JobID" })
            {
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property is not null)
                    return property;
            }
            return null;
        }

        var classJobProperty = FindProperty(sourceType)
            ?? sourceType
                .GetInterfaces()
                .Select(FindProperty)
                .FirstOrDefault(property => property is not null);
        if (classJobProperty is not null)
        {
            return source =>
            {
                var value = classJobProperty.GetValue(source);
                return TryExtractClassJobId(value, 0, out var classJobId) ? classJobId : 0;
            };
        }

        var classJobField = sourceType.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(field => field.Name is "ClassJob" or "ClassJobId" or "ClassJobID" or "JobId" or "JobID");
        if (classJobField is null)
            return static _ => 0u;

        return source =>
        {
            var value = classJobField.GetValue(source);
            return TryExtractClassJobId(value, 0, out var classJobId) ? classJobId : 0;
        };
    }

    private static bool TryExtractClassJobId(object? value, int depth, out uint classJobId)
    {
        classJobId = 0;
        if (value is null || depth > 3)
            return false;
        if (TryReadUnsignedValue(value, out classJobId))
            return classJobId != 0;

        var valueType = value.GetType();
        foreach (var propertyName in new[] { "RowId", "ClassJobId", "Id" })
        {
            try
            {
                var property = valueType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property is not null && TryReadUnsignedValue(property.GetValue(value), out classJobId))
                    return classJobId != 0;
            }
            catch
            {

            }
        }

        try
        {
            var nestedValueProperty = valueType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (nestedValueProperty is not null)
                return TryExtractClassJobId(nestedValueProperty.GetValue(value), depth + 1, out classJobId);
        }
        catch
        {

        }

        return false;
    }

    private static bool TryReadUnsignedValue(object? value, out uint result)
    {
        result = 0;
        switch (value)
        {
            case byte number:
                result = number;
                return true;
            case sbyte number when number >= 0:
                result = (uint)number;
                return true;
            case ushort number:
                result = number;
                return true;
            case short number when number >= 0:
                result = (uint)number;
                return true;
            case uint number:
                result = number;
                return true;
            case int number when number >= 0:
                result = (uint)number;
                return true;
            case ulong number when number <= uint.MaxValue:
                result = (uint)number;
                return true;
            case long number when number is >= 0 and <= uint.MaxValue:
                result = (uint)number;
                return true;
            default:
                return false;
        }
    }

    private static float DrawPartyStatusIcons(
        ImDrawListPtr draw,
        IBattleChara actor,
        Vector2 rowPosition,
        float rowWidth,
        float rowHeight,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        if (rowHeight < 30f * scale)
            return 0f;

        var iconSize = Math.Clamp(rowHeight * 0.25f, 10f * scale, 18f * scale);
        var gap = MathF.Max(1f, 2f * scale);
        var top = rowPosition.Y + 3f * scale;
        var left = rowPosition.X + 6f * scale;
        var right = rowPosition.X + rowWidth - 6f * scale;
        var maxPerSide = Math.Clamp((int)((rowWidth * 0.43f + gap) / (iconSize + gap)), 1, 7);
        var buffCount = 0;
        var debuffCount = 0;

        foreach (var status in actor.StatusList)
        {
            if (status.StatusId == 0 || !TryGetStatusVisual(status.StatusId, status.Param, out var iconId, out var isDebuff))
                continue;

            Vector2 iconPosition;
            if (isDebuff)
            {
                if (debuffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(right - iconSize - debuffCount * (iconSize + gap), top);
                debuffCount++;
            }
            else
            {
                if (buffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(left + buffCount * (iconSize + gap), top);
                buffCount++;
            }

            DrawStatusIcon(draw, iconId, isDebuff, iconPosition, iconSize, theme, scale, opacity);
        }

        return buffCount > 0 || debuffCount > 0 ? iconSize + 4f * scale : 0f;
    }

    private static float DrawTargetStatusIcons(
        ImDrawListPtr draw,
        IBattleChara? actor,
        Vector2 framePosition,
        Vector2 frameSize,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        if (actor is null)
            return 0f;

        var iconSize = Math.Clamp(frameSize.Y * 0.19f, 14f * scale, 21f * scale);
        var gap = MathF.Max(1f, 2f * scale);
        var top = framePosition.Y + 4f * scale;
        var left = framePosition.X + 12f * scale;
        var right = framePosition.X + frameSize.X - 12f * scale;
        var maxPerSide = Math.Clamp((int)((frameSize.X * 0.44f + gap) / (iconSize + gap)), 1, 12);
        var buffCount = 0;
        var debuffCount = 0;

        foreach (var status in actor.StatusList)
        {
            if (status.StatusId == 0 || !TryGetStatusVisual(status.StatusId, status.Param, out var iconId, out var isDebuff))
                continue;

            Vector2 iconPosition;
            if (isDebuff)
            {
                if (debuffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(right - iconSize - debuffCount * (iconSize + gap), top);
                debuffCount++;
            }
            else
            {
                if (buffCount >= maxPerSide)
                    continue;
                iconPosition = new Vector2(left + buffCount * (iconSize + gap), top);
                buffCount++;
            }

            DrawStatusIcon(draw, iconId, isDebuff, iconPosition, iconSize, theme, scale, opacity);
        }

        return buffCount > 0 || debuffCount > 0 ? iconSize + 6f * scale : 0f;
    }

    private static bool TryGetStatusVisual(uint statusId, uint statusParameter, out uint iconId, out bool isDebuff)
    {
        iconId = 0;
        isDebuff = false;
        if (!StatusDisplayService.TryResolve(statusId, statusParameter, out var display))
            return false;

        iconId = display.IconId;
        isDebuff = display.IsDebuff;
        return true;
    }

    private static void DrawStatusIcon(
        ImDrawListPtr draw,
        uint iconId,
        bool isDebuff,
        Vector2 position,
        float iconSize,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var end = position + new Vector2(iconSize);
        draw.AddRectFilled(position, end, Color(theme.PanelAlt, opacity * 0.92f), 3f * scale);
        var wrap = TryGetGameIcon(iconId);
        if (wrap is not null)
        {
            var inset = MathF.Max(1f, scale);
            draw.AddImage(
                wrap.Handle,
                position + new Vector2(inset),
                end - new Vector2(inset),
                Vector2.Zero,
                Vector2.One,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity)));
        }
        else
        {
            DrawMissingIconGlyph(draw, position, iconSize, theme, opacity);
        }

        var edge = isDebuff ? theme.Danger : theme.Success;
        draw.AddRect(position, end, Color(edge, opacity * 0.96f), 3f * scale, ImDrawFlags.None, MathF.Max(1f, scale));
    }

    private static void DrawPartyResourceSummary(
        ImDrawListPtr draw,
        Vector2 rowPosition,
        float rowWidth,
        float rowHeight,
        Vector2 namePosition,
        string displayName,
        ulong currentHp,
        ulong maxHp,
        ulong currentMp,
        ulong maxMp,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var deficit = maxHp > currentHp ? maxHp - currentHp : 0UL;
        var hpText = $"{FormatCompactHp(currentHp)}/{FormatCompactHp(maxHp)}";
        var mpText = maxMp > 0
            ? $"{FormatCompactHp(currentMp)}/{FormatCompactHp(maxMp)} MP"
            : string.Empty;
        var deficitText = deficit > 0 ? $"-{FormatCompactHp(deficit)}" : "0";
        var hpSize = ImGui.CalcTextSize(hpText);
        var mpSize = string.IsNullOrEmpty(mpText) ? Vector2.Zero : ImGui.CalcTextSize(mpText);
        var deficitSize = ImGui.CalcTextSize(deficitText);
        var right = rowPosition.X + rowWidth - 8f * scale;
        var gap = 7f * scale;
        var mpColor = new Vector4(0.46f, 0.68f, 1f, 1f);

        if (rowHeight >= 58f * scale)
        {
            var hpPosition = new Vector2(right - hpSize.X, namePosition.Y);
            var secondLineY = MathF.Min(
                rowPosition.Y + rowHeight - ImGui.GetTextLineHeight() - 8f * scale,
                namePosition.Y + ImGui.GetTextLineHeight() + 1f * scale);
            var mpPosition = string.IsNullOrEmpty(mpText)
                ? new Vector2(right, secondLineY)
                : new Vector2(right - mpSize.X, secondLineY);
            var deficitPosition = new Vector2(
                (string.IsNullOrEmpty(mpText) ? right : mpPosition.X - gap) - deficitSize.X,
                secondLineY);
            var statsStart = MathF.Min(hpPosition.X, deficitPosition.X);

            draw.PushClipRect(
                new Vector2(namePosition.X, rowPosition.Y),
                new Vector2(MathF.Max(namePosition.X + 1f, statsStart - gap), rowPosition.Y + rowHeight),
                true);
            draw.AddText(namePosition, Color(theme.Text, opacity), displayName);
            draw.PopClipRect();

            draw.AddText(hpPosition, Color(theme.Muted, opacity), hpText);
            draw.AddText(
                deficitPosition,
                Color(deficit > 0 ? theme.Danger : theme.Success, opacity),
                deficitText);
            if (!string.IsNullOrEmpty(mpText))
                draw.AddText(mpPosition, Color(mpColor, opacity), mpText);
            return;
        }


        var compactMpText = maxMp > 0 ? $"{FormatCompactHp(currentMp)} MP" : string.Empty;
        var compactMpSize = string.IsNullOrEmpty(compactMpText) ? Vector2.Zero : ImGui.CalcTextSize(compactMpText);
        var hpPositionCompact = new Vector2(right - hpSize.X, namePosition.Y);
        var cursorX = hpPositionCompact.X - gap;
        Vector2 mpPositionCompact = default;
        if (!string.IsNullOrEmpty(compactMpText))
        {
            mpPositionCompact = new Vector2(cursorX - compactMpSize.X, namePosition.Y);
            cursorX = mpPositionCompact.X - gap;
        }
        var statsStartCompact = cursorX;

        draw.PushClipRect(
            new Vector2(namePosition.X, rowPosition.Y),
            new Vector2(MathF.Max(namePosition.X + 1f, statsStartCompact), rowPosition.Y + rowHeight),
            true);
        draw.AddText(namePosition, Color(theme.Text, opacity), displayName);
        draw.PopClipRect();

        if (!string.IsNullOrEmpty(compactMpText))
            draw.AddText(mpPositionCompact, Color(mpColor, opacity), compactMpText);
        draw.AddText(hpPositionCompact, Color(theme.Muted, opacity), hpText);
    }

    private static string FormatCompactHp(ulong value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000f:0.##}m";
        if (value >= 100_000)
            return $"{value / 1_000f:0.#}k";
        if (value >= 10_000)
            return $"{value / 1_000f:0.##}k";
        return value.ToString("N0");
    }


    private static void DrawEnemyList(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity, bool editMode)
    {


        var enemies = plugin.EnemyList.Snapshot(
            8,
            excludeCurrentTarget: false,
            excludeFocusTarget: false,
            engagedOnly: true);

        var actualCount = enemies.Count;
        var count = editMode ? Math.Max(4, actualCount) : actualCount;
        if (count <= 0)
            return;

        var gap = 4f * scale;
        var rowHeight = HudLayout.EnemyListRowHeight(scale);
        for (var i = 0; i < count; i++)
        {
            var enemy = i < enemies.Count ? enemies[i] : null;
            var p = bounds.Position + new Vector2(0f, i * (rowHeight + gap));
            var isCurrentTarget = enemy?.GameObjectId == (Plugin.TargetManager.Target?.GameObjectId ?? 0UL);
            var isFocus = enemy?.GameObjectId == (Plugin.TargetManager.FocusTarget?.GameObjectId ?? 0UL);
            DrawGlassPanel(draw, p, new Vector2(bounds.Size.X, rowHeight), theme, opacity * (isCurrentTarget ? 0.96f : 0.62f), 5f * scale);

            var ratio = enemy is not null && enemy.MaxHp > 0
                ? Math.Clamp(enemy.CurrentHp / (float)enemy.MaxHp, 0f, 1f)
                : 0.72f;
            var barColor = isCurrentTarget ? theme.AccentStrong : isFocus ? theme.Warning : theme.Danger;
            draw.AddRectFilled(
                p + new Vector2(3f * scale, rowHeight - 5f * scale),
                p + new Vector2(3f * scale + (bounds.Size.X - 6f * scale) * ratio, rowHeight - 2f * scale),
                Color(barColor, opacity * 0.92f),
                2f * scale);

            var name = enemy?.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = $"Enemy {i + 1}";
            if (isCurrentTarget)
                name = $"TARGET  •  {name}";
            else if (isFocus)
                name = $"◆ {name}";
            draw.AddText(
                p + new Vector2(9f * scale, MathF.Max(4f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)),
                Color(theme.Text, opacity),
                name);

            var percent = $"{ratio * 100f:0}%";
            var textSize = ImGui.CalcTextSize(percent);
            draw.AddText(
                p + new Vector2(bounds.Size.X - textSize.X - 9f * scale, MathF.Max(4f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)),
                Color(barColor, opacity),
                percent);
        }
    }

    private static void DrawCrossHotbar(
        ImDrawListPtr draw,
        HudBounds bounds,
        CrossHotbarState state,
        ThemePalette theme,
        float scale,
        float opacity)
    {


        var module = RaptureHotbarModule.Instance();
        var padding = MathF.Max(2f, 4f * scale);
        var slotGap = MathF.Max(1f, 2f * scale);
        var clusterGap = MathF.Max(7f, 10f * scale);
        var centerGap = MathF.Max(20f, 34f * scale);
        var triggerHeight = Math.Clamp(18f * scale, 14f, MathF.Max(14f, bounds.Size.Y * 0.16f));
        var footerHeight = Math.Clamp(22f * scale, 18f, MathF.Max(18f, bounds.Size.Y * 0.18f));
        var availableHeight = MathF.Max(1f, bounds.Size.Y - triggerHeight - footerHeight);


        var slotFromWidth = (bounds.Size.X - padding * 2f - slotGap * 8f - clusterGap * 2f - centerGap) / 12f;
        var slotFromHeight = (availableHeight - slotGap * 2f) / 3f;
        var slotSize = MathF.Max(14f, MathF.Min(slotFromWidth, slotFromHeight));
        var clusterSize = slotSize * 3f + slotGap * 2f;
        var halfWidth = clusterSize * 2f + clusterGap;
        var contentWidth = halfWidth * 2f + centerGap;
        var contentHeight = clusterSize;
        var contentStart = new Vector2(
            bounds.Position.X + (bounds.Size.X - contentWidth) * 0.5f,
            bounds.Position.Y + triggerHeight + MathF.Max(0f, (availableHeight - contentHeight) * 0.5f));

        DrawCrossHotbarHalf(
            draw,
            module,
            state.HotbarId,
            0u,
            contentStart,
            halfWidth,
            clusterSize,
            slotSize,
            slotGap,
            clusterGap,
            "LT",
            state.LeftFocused,
            bounds.Position.Y,
            triggerHeight,
            theme,
            scale,
            opacity);

        DrawCrossHotbarHalf(
            draw,
            module,
            state.HotbarId,
            8u,
            contentStart + new Vector2(halfWidth + centerGap, 0f),
            halfWidth,
            clusterSize,
            slotSize,
            slotGap,
            clusterGap,
            "RT",
            state.RightFocused,
            bounds.Position.Y,
            triggerHeight,
            theme,
            scale,
            opacity);

        var separatorX = bounds.Position.X + bounds.Size.X * 0.5f;
        draw.AddLine(
            new Vector2(separatorX, contentStart.Y + slotSize * 0.70f),
            new Vector2(separatorX, contentStart.Y + contentHeight - slotSize * 0.70f),
            Color(theme.Accent, opacity * 0.44f),
            MathF.Max(1f, scale));

        var setLabel = $"SET {state.SetNumber}";
        var setSize = ImGui.CalcTextSize(setLabel);
        var setPosition = new Vector2(
            bounds.Position.X + (bounds.Size.X - setSize.X) * 0.5f,
            MathF.Min(
                bounds.Position.Y + bounds.Size.Y - setSize.Y - 2f * scale,
                contentStart.Y + contentHeight + MathF.Max(3f, 5f * scale)));
        draw.AddText(setPosition + new Vector2(1f, 1f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.90f)), setLabel);
        draw.AddText(setPosition, Color(theme.Text, opacity), setLabel);
        draw.AddLine(
            new Vector2(setPosition.X, setPosition.Y + setSize.Y + 1f * scale),
            new Vector2(setPosition.X + setSize.X, setPosition.Y + setSize.Y + 1f * scale),
            Color(theme.AccentStrong, opacity * 0.82f),
            MathF.Max(1f, scale));
    }

    private static void DrawCrossHotbarHalf(
        ImDrawListPtr draw,
        RaptureHotbarModule* module,
        uint hotbarId,
        uint firstSlot,
        Vector2 start,
        float halfWidth,
        float clusterSize,
        float slotSize,
        float slotGap,
        float clusterGap,
        string triggerLabel,
        bool focused,
        float boundsTop,
        float triggerHeight,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        if (focused)
        {
            var glowPadding = MathF.Max(5f, 8f * scale);
            var glowMin = start - new Vector2(glowPadding);
            var glowMax = start + new Vector2(halfWidth, clusterSize) + new Vector2(glowPadding);
            draw.AddRectFilled(
                glowMin - new Vector2(5f * scale),
                glowMax + new Vector2(5f * scale),
                Color(theme.AccentStrong, opacity * 0.055f),
                18f * scale);
            draw.AddRectFilled(
                glowMin,
                glowMax,
                Color(theme.AccentStrong, opacity * 0.105f),
                13f * scale);
            draw.AddRect(
                glowMin,
                glowMax,
                Color(theme.AccentStrong, opacity * 0.58f),
                13f * scale,
                ImDrawFlags.None,
                MathF.Max(1f, 1.2f * scale));
        }

        var labelSize = ImGui.CalcTextSize(triggerLabel);
        draw.AddText(
            new Vector2(start.X + (halfWidth - labelSize.X) * 0.5f, boundsTop + MathF.Max(1f, (triggerHeight - labelSize.Y) * 0.5f)),
            Color(focused ? theme.AccentStrong : theme.Text, opacity * (focused ? 1f : 0.84f)),
            triggerLabel);

        DrawCrossHotbarCluster(
            draw,
            module,
            hotbarId,
            firstSlot,
            start,
            slotSize,
            slotGap,
            theme,
            scale,
            opacity);
        DrawCrossHotbarCluster(
            draw,
            module,
            hotbarId,
            firstSlot + 4u,
            start + new Vector2(clusterSize + clusterGap, 0f),
            slotSize,
            slotGap,
            theme,
            scale,
            opacity);
    }

    private static void DrawCrossHotbarCluster(
        ImDrawListPtr draw,
        RaptureHotbarModule* module,
        uint hotbarId,
        uint firstSlot,
        Vector2 start,
        float slotSize,
        float gap,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var step = slotSize + gap;
        for (var index = 0; index < 4; index++)
        {


            var offset = index switch
            {


                0 => new Vector2(0f, step),
                1 => new Vector2(step, 0f),
                2 => new Vector2(step * 2f, step),
                _ => new Vector2(step, step * 2f),
            };

            DrawHotbarSlotSafe(
                draw,
                module,
                hotbarId,
                firstSlot + (uint)index,
                start + offset,
                slotSize,
                theme,
                scale,
                opacity,
                string.Empty);
        }
    }

    private static void DrawActionBar(
        ImDrawListPtr draw,
        HudBounds bounds,
        uint hotbarId,
        int columns,
        int rows,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        columns = HotbarGridLayouts.NormalizeColumns(columns);
        rows = Math.Max(1, 12 / columns);
        var gap = MathF.Max(1f, 3f * scale);
        var slotSize = MathF.Max(18f, MathF.Min(
            (bounds.Size.X - gap * (columns - 1)) / columns,
            (bounds.Size.Y - gap * (rows - 1)) / rows));
        var content = new Vector2(
            columns * slotSize + (columns - 1) * gap,
            rows * slotSize + (rows - 1) * gap);
        var start = bounds.Position + (bounds.Size - content) * 0.5f;
        var module = RaptureHotbarModule.Instance();

        for (var slot = 0; slot < 12; slot++)
        {
            var row = slot / columns;
            var column = slot % columns;
            var position = start + new Vector2(column * (slotSize + gap), row * (slotSize + gap));
            var keyLabel = NativeHotbarKeybindService.GetLabel(module, hotbarId, (uint)slot);
            DrawHotbarSlotSafe(draw, module, hotbarId, (uint)slot, position, slotSize, theme, scale, opacity, keyLabel);
        }
    }

    private static void DrawVirtualActionBar(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        uint hotbarId,
        int columns,
        int rows,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        columns = HotbarGridLayouts.NormalizeColumns(columns);
        rows = Math.Max(1, 12 / columns);
        var gap = MathF.Max(1f, 3f * scale);
        var slotSize = MathF.Max(18f, MathF.Min(
            (bounds.Size.X - gap * (columns - 1)) / columns,
            (bounds.Size.Y - gap * (rows - 1)) / rows));
        var content = new Vector2(
            columns * slotSize + (columns - 1) * gap,
            rows * slotSize + (rows - 1) * gap);
        var start = bounds.Position + (bounds.Size - content) * 0.5f;

        for (var slotIndex = 0; slotIndex < ReframeHotbarIds.SlotCount; slotIndex++)
        {
            var row = slotIndex / columns;
            var column = slotIndex % columns;
            var position = start + new Vector2(column * (slotSize + gap), row * (slotSize + gap));
            var reference = new HotbarSlotReference(hotbarId, (uint)slotIndex);
            plugin.AdditionalHotbars.TryGetVirtualSlot(reference, out var slot);
            var keyLabel = NativeHotbarKeybindService.GetBindingLabel(reference, 0, true);
            DrawVirtualHotbarSlot(draw, slot, position, slotSize, theme, scale, opacity, keyLabel);
        }
    }

    private static void DrawVirtualHotbarSlot(
        ImDrawListPtr draw,
        ReframeVirtualHotbarSlot? slot,
        Vector2 p,
        float slotSize,
        ThemePalette theme,
        float scale,
        float opacity,
        string keyLabel)
    {
        var end = p + new Vector2(slotSize);
        if (slot is null || slot.IsEmpty)
        {
            DrawEmptyHotbarSlot(draw, p, end, theme, scale, opacity);
            DrawSlotKey(draw, p, keyLabel, theme, scale, opacity);
            return;
        }

        draw.AddRectFilled(p + new Vector2(2f * scale), end + new Vector2(2f * scale), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.30f)), 5f * scale);
        draw.AddRectFilled(p, end, Color(theme.Panel, opacity * 0.90f), 5f * scale);
        draw.AddRectFilled(p + new Vector2(1f * scale), p + new Vector2(slotSize - 1f * scale, MathF.Max(5f, slotSize * 0.38f)), Color(theme.PanelAlt, opacity * 0.48f), 4f * scale);
        draw.AddRect(p, end, Color(theme.Accent, opacity * 0.70f), 5f * scale, ImDrawFlags.None, 1f);
        draw.AddLine(p + new Vector2(4f * scale, 2f * scale), p + new Vector2(slotSize - 4f * scale, 2f * scale), Color(theme.AccentStrong, opacity * 0.58f), 1f * scale);

        var wrap = slot.IconId != 0 ? TryGetGameIcon(slot.IconId) : null;
        if (wrap is not null)
        {
            var inset = MathF.Max(1f, 2f * scale);
            draw.AddImage(
                wrap.Handle,
                p + new Vector2(inset),
                end - new Vector2(inset),
                Vector2.Zero,
                Vector2.One,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity)));
        }
        else
        {
            DrawMissingIconGlyph(draw, p, slotSize, theme, opacity);
        }

        draw.AddRectFilled(p + new Vector2(3f * scale, slotSize - 4f * scale), end - new Vector2(3f * scale, 2f * scale), Color(theme.AccentStrong, opacity * 0.54f), 1f);
        DrawSlotKey(draw, p, keyLabel, theme, scale, opacity);
    }

    private static void DrawPetBar(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
    {
        var shape = HotbarGridLayouts.Resolve(plugin.Configuration, HudElementIds.PetBar);
        DrawActionBar(
            draw,
            bounds,
            ReframeHotbarIds.PetBar,
            shape.Columns,
            shape.Rows,
            theme,
            scale,
            opacity);
    }

    private static void DrawUtilityBars(ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
    {

        const int columns = 4;
        const int rows = 3;
        var gap = MathF.Max(1f, 3f * scale);
        var slotSize = MathF.Max(18f, MathF.Min(
            (bounds.Size.X - gap * (columns - 1)) / columns,
            (bounds.Size.Y - gap * (rows - 1)) / rows));
        var content = new Vector2(
            columns * slotSize + (columns - 1) * gap,
            rows * slotSize + (rows - 1) * gap);
        var start = bounds.Position + (bounds.Size - content) * 0.5f;
        var module = RaptureHotbarModule.Instance();
        for (var slot = 0; slot < 12; slot++)
        {
            var row = slot / columns;
            var col = slot % columns;
            var p = start + new Vector2(col * (slotSize + gap), row * (slotSize + gap));
            DrawHotbarSlotSafe(draw, module, 5u, (uint)slot, p, slotSize, theme, scale, opacity, string.Empty);
        }
    }


    private static void DrawUtilityBarDecal(ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
    {


        var margin = new Vector2(7f, 7f) * scale;
        var p = bounds.Position - margin;
        var s = bounds.Size + margin * 2f;
        draw.AddRect(p, p + s, Color(theme.Accent, opacity * 0.48f), 6f * scale, ImDrawFlags.None, 1.2f * scale);
        draw.AddLine(p, p + new Vector2(MathF.Min(82f * scale, s.X), 0f), Color(theme.AccentStrong, opacity * 0.92f), 2f * scale);
        DrawDiamond(draw, p + new Vector2(s.X - 8f * scale, 0f), 3.5f * scale, Color(theme.AccentStrong, opacity * 0.86f));
    }

    private static void DrawRaidTools(ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
    {
        DrawGlassPanel(draw, bounds.Position, bounds.Size, theme, opacity, 8f * scale);
        var labels = new[] { "REPAIR", "WAYMARK", "COUNTDOWN", "STRATEGY" };
        var gap = 6f * scale;
        var width = (bounds.Size.X - gap * 5f) / 4f;
        for (var i = 0; i < labels.Length; i++)
        {
            var min = bounds.Position + new Vector2(gap + i * (width + gap), gap);
            var max = min + new Vector2(width, bounds.Size.Y - gap * 2f);
            draw.AddRectFilled(min, max, Color(theme.PanelAlt, opacity * 0.94f), 5f * scale);
            draw.AddRect(min, max, Color(theme.Accent, opacity * 0.78f), 5f * scale, ImDrawFlags.None, MathF.Max(1f, scale));
            var textSize = ImGui.CalcTextSize(labels[i]);
            draw.AddText(min + (max - min - textSize) * 0.5f, Color(theme.Text, opacity), labels[i]);
        }
    }


    private static void DrawRaidStatusPanel(
        ImDrawListPtr draw,
        HudBounds bounds,
        IBattleChara? actor,
        bool debuffs,
        bool transparentBackground,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode)
    {
        var entries = new List<(uint Icon, float Remaining)>();
        if (actor is not null)
        {
            foreach (var status in actor.StatusList)
            {
                if (status.StatusId == 0 ||
                    !TryGetStatusVisual(status.StatusId, status.Param, out var icon, out var isDebuff) ||
                    isDebuff != debuffs)
                    continue;

                entries.Add((icon, MathF.Max(0f, status.RemainingTime)));
            }
        }

        if (editMode && entries.Count == 0)
        {
            entries.Add((0, 120f));
            entries.Add((0, 45f));
            entries.Add((0, 12f));
        }


        var renderBounds = ResolveRaidStatusPanelBounds(bounds, scale, entries.Count);
        if (!transparentBackground)
            DrawGlassPanel(draw, renderBounds.Position, renderBounds.Size, theme, opacity, 7f * scale);

        var title = debuffs ? "DEBUFFS" : "BUFFS";
        var font = ImGui.GetFont();
        var baseFontSize = MathF.Max(1f, ImGui.GetFontSize());
        var titleFontSize = baseFontSize * Math.Clamp(scale, 0.65f, 3.25f);
        draw.AddText(
            font,
            titleFontSize,
            renderBounds.Position + new Vector2(8f, 5f) * scale,
            Color(debuffs ? theme.Danger : theme.AccentStrong, opacity),
            title);

        var slots = ResolveRaidStatusIconBounds(bounds, scale, entries.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var p = slot.Position;
            var iconSize = slot.Size.X;
            if (entries[i].Icon != 0)
                DrawStatusIcon(draw, entries[i].Icon, debuffs, p, iconSize, theme, scale, opacity);
            else
            {
                draw.AddRectFilled(p, p + new Vector2(iconSize), Color(theme.PanelAlt, opacity), 3f * scale);
                draw.AddRect(p, p + new Vector2(iconSize), Color(debuffs ? theme.Danger : theme.Success, opacity), 3f * scale);
            }

            if (entries[i].Remaining > 0f)
            {
                var duration = FormatCompactStatusDuration(entries[i].Remaining);
                var durationFontSize = baseFontSize * Math.Clamp(scale * 0.72f, 0.55f, 2.40f);
                var textScale = durationFontSize / baseFontSize;
                var textSize = ImGui.CalcTextSize(duration) * textScale;
                draw.AddText(
                    font,
                    durationFontSize,
                    p + new Vector2((iconSize - textSize.X) * 0.5f, iconSize + 1f * scale),
                    Color(theme.Text, opacity),
                    duration);
            }
        }
    }


    internal static HudBounds ResolveRaidStatusPanelBounds(
        HudBounds configuredBounds,
        float scale,
        int requestedCount)
    {
        var count = Math.Max(0, requestedCount);
        if (count == 0)
            return configuredBounds;

        ResolveRaidStatusMetrics(
            configuredBounds,
            scale,
            out _,
            out _,
            out var cellHeight,
            out var columns);

        var rows = Math.Max(1, (count + columns - 1) / columns);
        var header = 23f * scale;
        var requiredHeight = header + rows * cellHeight + 4f * scale;
        return new HudBounds(
            configuredBounds.Position,
            new Vector2(configuredBounds.Size.X, MathF.Max(configuredBounds.Size.Y, requiredHeight)));
    }

    internal static IReadOnlyList<HudBounds> ResolveRaidStatusIconBounds(
        HudBounds bounds,
        float scale,
        int requestedCount)
    {
        var count = Math.Max(0, requestedCount);
        if (count == 0)
            return Array.Empty<HudBounds>();

        ResolveRaidStatusMetrics(
            bounds,
            scale,
            out var iconSize,
            out var gap,
            out var cellHeight,
            out var columns);

        var header = 23f * scale;
        var result = new List<HudBounds>(count);
        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var position = bounds.Position + new Vector2(
                6f * scale + column * (iconSize + gap),
                header + row * cellHeight);
            result.Add(new HudBounds(position, new Vector2(iconSize)));
        }

        return result;
    }

    private static void ResolveRaidStatusMetrics(
        HudBounds bounds,
        float scale,
        out float iconSize,
        out float gap,
        out float cellHeight,
        out int columns)
    {
        var header = 23f * scale;
        var availableHeight = MathF.Max(18f * scale, bounds.Size.Y - header - 6f * scale);
        iconSize = Math.Clamp(MathF.Min(30f * scale, availableHeight), 16f * scale, 30f * scale);
        var durationHeight = 12f * scale;
        gap = MathF.Max(2f * scale, iconSize * 0.10f);
        cellHeight = iconSize + durationHeight + gap;
        columns = Math.Max(1, (int)((bounds.Size.X - 12f * scale + gap) / (iconSize + gap)));
    }

    private static void DrawRaidersKit(
        ImDrawListPtr draw,
        HudBounds bounds,
        Plugin plugin,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode)
    {
        DrawGlassPanel(draw, bounds.Position, bounds.Size, theme, opacity, 8f * scale);
        var snapshot = RaidersKitService.Snapshot(plugin.Configuration, Plugin.ObjectTable.LocalPlayer as IBattleChara);
        var gap = 7f * scale;
        var top = bounds.Position.Y + gap;
        var height = MathF.Max(28f * scale, bounds.Size.Y - gap * 2f);
        var width = MathF.Max(1f, (bounds.Size.X - gap * 3f) / 2f);
        DrawRaidersKitSlot(draw, new HudBounds(new Vector2(bounds.Position.X + gap, top), new Vector2(width, height)), "FOOD", snapshot.Food, snapshot.FoodRemaining, false, theme, scale, opacity, editMode);
        DrawRaidersKitSlot(draw, new HudBounds(new Vector2(bounds.Position.X + gap * 2f + width, top), new Vector2(width, height)), "POTION", snapshot.Potion, snapshot.PotionCooldownRemaining, true, theme, scale, opacity, editMode);
    }

    private static void DrawRaidersKitSlot(
        ImDrawListPtr draw,
        HudBounds bounds,
        string label,
        RaidersKitItem? item,
        float timer,
        bool cooldown,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode)
    {
        draw.AddRectFilled(bounds.Position, bounds.Position + bounds.Size, Color(theme.PanelAlt, opacity * 0.95f), 5f * scale);
        draw.AddRect(bounds.Position, bounds.Position + bounds.Size, Color(theme.Accent, opacity * 0.72f), 5f * scale);
        var iconSize = MathF.Min(bounds.Size.Y - 6f * scale, 34f * scale);
        var iconPos = bounds.Position + new Vector2(4f, 3f) * scale;
        var resolved = item;
        if (resolved is { } value && value.IconId != 0)
        {
            var wrap = TryGetGameIcon(value.IconId);
            if (wrap is not null)
                draw.AddImage(wrap.Handle, iconPos, iconPos + new Vector2(iconSize), Vector2.Zero, Vector2.One, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity)));
        }
        else
            DrawMissingIconGlyph(draw, iconPos, iconSize, theme, opacity);

        var textX = iconPos.X + iconSize + 6f * scale;
        draw.AddText(new Vector2(textX, bounds.Position.Y + 4f * scale), Color(theme.Text, opacity), resolved?.Name ?? (editMode ? $"Selected {label.ToLowerInvariant()}" : $"No {label.ToLowerInvariant()} found"));
        var footer = timer > 0f
            ? (cooldown ? $"READY IN {FormatCompactStatusDuration(timer)}" : $"{FormatCompactStatusDuration(timer)} REMAINING")
            : (cooldown ? "READY" : $"x{resolved?.Quantity ?? 0}");
        draw.AddText(new Vector2(textX, bounds.Position.Y + bounds.Size.Y - 17f * scale), Color(timer > 0f ? theme.AccentStrong : theme.Muted, opacity), footer);
    }

    private static void DrawHotbarSlotSafe(ImDrawListPtr draw, RaptureHotbarModule* module, uint hotbarId, uint slotId, Vector2 p, float slotSize, ThemePalette theme, float scale, float opacity, string keyLabel)
    {
        try
        {
            DrawHotbarSlot(draw, module, hotbarId, slotId, p, slotSize, theme, scale, opacity, keyLabel);
        }
        catch (Exception ex)
        {
            var failureKey = $"{hotbarId}:{slotId}:{ex.GetType().FullName}";
            if (LoggedSlotFailures.Add(failureKey))
                Plugin.Log.Warning($"RE:Frame skipped hotbar {hotbarId + 1}, slot {slotId + 1}: {ex.Message}");
            var end = p + new Vector2(slotSize, slotSize);
            draw.AddRectFilled(p, end, Color(theme.Panel, opacity * 0.78f), 5f * scale);
            draw.AddRect(p, end, Color(theme.Danger, opacity * 0.55f), 5f * scale, ImDrawFlags.None, 1f);
            DrawMissingIconGlyph(draw, p, slotSize, theme, opacity);
            DrawSlotKey(draw, p, keyLabel, theme, scale, opacity);
        }
    }

    public static IDalamudTextureWrap? GetGameIcon(uint iconId) => TryGetGameIcon(iconId);

    private static IDalamudTextureWrap? TryGetGameIcon(uint iconId)
    {
        Span<uint> candidates = stackalloc uint[2];
        candidates[0] = iconId;
        var candidateCount = 1;
        if (iconId >= 1_000_000)
        {
            var normalized = iconId - 1_000_000;
            if (normalized != 0 && normalized != iconId)
                candidates[candidateCount++] = normalized;
        }
        for (var i = 0; i < candidateCount; i++)
        {
            var highRes = TryGetGameIconVariant(candidates[i], true);
            if (highRes is not null) return highRes;
            var standard = TryGetGameIconVariant(candidates[i], false);
            if (standard is not null) return standard;
        }
        return null;
    }

    private static IDalamudTextureWrap? TryGetGameIconVariant(uint iconId, bool highResolution)
    {
        var cacheKey = ((ulong)iconId << 1) | (highResolution ? 1UL : 0UL);
        if (MissingIconVariants.Contains(cacheKey))
            return null;
        try
        {
            var lookup = new GameIconLookup { IconId = iconId, HiRes = highResolution };
            return Plugin.TextureProvider.GetFromGameIcon(lookup).GetWrapOrDefault();
        }
        catch (Exception ex)
        {
            MissingIconVariants.Add(cacheKey);
            Plugin.Log.Debug($"RE:Frame could not load icon {iconId} ({(highResolution ? "HiRes" : "standard")}): {ex.Message}");
            return null;
        }
    }

    private static void DrawMissingIconGlyph(ImDrawListPtr draw, Vector2 p, float slotSize, ThemePalette theme, float opacity)
    {
        var center = p + new Vector2(slotSize * 0.5f, slotSize * 0.5f);
        DrawDiamond(draw, center, MathF.Max(3f, slotSize * 0.13f), Color(theme.Muted, opacity * 0.48f));
    }

    private static void DrawHotbarSlot(ImDrawListPtr draw, RaptureHotbarModule* module, uint hotbarId, uint slotId, Vector2 p, float slotSize, ThemePalette theme, float scale, float opacity, string keyLabel)
    {
        var end = p + new Vector2(slotSize, slotSize);
        var nativeSlot = module != null && module->ModuleReady
            ? module->GetSlotById(hotbarId, slotId)
            : null;

        if (nativeSlot == null)
        {
            Plugin.Instance.BarInputDiagnostics.RecordRendererReadback(
                hotbarId,
                slotId,
                "Renderer saw a null native slot pointer");
            DrawEmptyHotbarSlot(draw, p, end, theme, scale, opacity);
            DrawSlotKey(draw, p, keyLabel, theme, scale, opacity);
            return;
        }

        if (nativeSlot->IsEmpty)
        {
            Plugin.Instance.BarInputDiagnostics.RecordRendererReadback(
                hotbarId,
                slotId,
                $"Renderer saw EMPTY — type {nativeSlot->CommandType}; id {nativeSlot->CommandId}; icon {nativeSlot->IconId}");
            DrawEmptyHotbarSlot(draw, p, end, theme, scale, opacity);
            DrawSlotKey(draw, p, keyLabel, theme, scale, opacity);
            return;
        }

        draw.AddRectFilled(p + new Vector2(2f * scale), end + new Vector2(2f * scale), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.30f)), 5f * scale);
        draw.AddRectFilled(p, end, Color(theme.Panel, opacity * 0.90f), 5f * scale);
        draw.AddRectFilled(p + new Vector2(1f * scale), p + new Vector2(slotSize - 1f * scale, MathF.Max(5f, slotSize * 0.38f)), Color(theme.PanelAlt, opacity * 0.48f), 4f * scale);
        draw.AddRect(p, end, Color(theme.Accent, opacity * 0.70f), 5f * scale, ImDrawFlags.None, 1f);
        draw.AddLine(p + new Vector2(4f * scale, 2f * scale), p + new Vector2(slotSize - 4f * scale, 2f * scale), Color(theme.AccentStrong, opacity * 0.58f), 1f * scale);


        var apparentType = nativeSlot->ApparentSlotType;
        var apparentActionId = nativeSlot->ApparentActionId;
        ushort appearanceState = 0;
        RaptureHotbarModule.GetSlotAppearance(
            &apparentType,
            &apparentActionId,
            &appearanceState,
            module,
            nativeSlot);

        if (apparentType == HotbarSlotType.Empty || apparentActionId == 0)
        {
            apparentType = nativeSlot->CommandType;
            apparentActionId = nativeSlot->CommandId;
        }

        var resolvedIcon = nativeSlot->GetIconIdForSlot(apparentType, apparentActionId);
        var iconId = resolvedIcon > 0 ? (uint)resolvedIcon : nativeSlot->IconId;
        if (iconId == 0)
        {
            nativeSlot->LoadIconId();
            iconId = nativeSlot->IconId;
        }

        var tracksUsability = apparentType is HotbarSlotType.Action or HotbarSlotType.CraftAction;
        var isUsable = !tracksUsability || nativeSlot->IsSlotUsable(apparentType, apparentActionId);
        var highlighted = isUsable && nativeSlot->IsActionHighlighted(apparentType, apparentActionId);
        Plugin.Instance.BarInputDiagnostics.RecordRendererReadback(
            hotbarId,
            slotId,
            $"Renderer saw stored {nativeSlot->CommandType}:{nativeSlot->CommandId}; apparent {apparentType}:{apparentActionId}; icon {iconId}; usable={isUsable}; highlighted={highlighted}; empty={nativeSlot->IsEmpty}");
        if (highlighted && Plugin.Instance.Configuration.EnhancedActionHighlights)
        {
            var reducedMotion = Plugin.Instance.Configuration.ReducedMotion;
            var pulse = reducedMotion ? 1f : 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 7.5f);
            var haloInset = MathF.Max(1f, 1.5f * scale);
            var halo = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.68f, 0.08f, opacity * (0.18f + 0.16f * pulse)));
            draw.AddRectFilled(
                p + new Vector2(haloInset),
                end - new Vector2(haloInset),
                halo,
                5f * scale);
        }

        if (iconId != 0)
        {
            var wrap = TryGetGameIcon(iconId);
            if (wrap is not null)
            {
                var inset = MathF.Max(1f, 2f * scale);
                var iconTint = isUsable
                    ? new Vector4(1f, 1f, 1f, opacity)
                    : new Vector4(0.46f, 0.46f, 0.46f, opacity * 0.76f);
                draw.AddImage(
                    wrap.Handle,
                    p + new Vector2(inset),
                    end - new Vector2(inset),
                    Vector2.Zero,
                    Vector2.One,
                    ImGui.ColorConvertFloat4ToU32(iconTint));
            }
            else DrawMissingIconGlyph(draw, p, slotSize, theme, opacity);
        }

        if (!isUsable)
        {
            var veilInset = MathF.Max(1f, 2f * scale);
            draw.AddRectFilled(
                p + new Vector2(veilInset),
                end - new Vector2(veilInset),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.24f)),
                4f * scale);
        }


        if (highlighted)
        {
            var enhanced = Plugin.Instance.Configuration.EnhancedActionHighlights;
            var reducedMotion = Plugin.Instance.Configuration.ReducedMotion;
            var pulse = reducedMotion ? 1f : 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 7.5f);

            if (enhanced)
            {
                var outer = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.72f, 0.10f, opacity * (0.78f + 0.20f * pulse)));
                var inner = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.93f, 0.55f, opacity * (0.58f + 0.24f * pulse)));
                var marker = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.82f, 0.20f, opacity));
                var expansion = (1.5f + 1.5f * pulse) * scale;

                draw.AddRect(
                    p - new Vector2(expansion),
                    end + new Vector2(expansion),
                    outer,
                    7f * scale,
                    ImDrawFlags.None,
                    MathF.Max(2.5f, 3.2f * scale));
                draw.AddRect(
                    p + new Vector2(2f * scale),
                    end - new Vector2(2f * scale),
                    inner,
                    4f * scale,
                    ImDrawFlags.None,
                    MathF.Max(1.2f, 1.5f * scale));

                var arm = Math.Clamp(slotSize * 0.19f, 5f * scale, 10f * scale);
                var tip = Math.Clamp(slotSize * 0.12f, 4f * scale, 7f * scale);
                var left = p.X - expansion;
                var right = end.X + expansion;
                var top = p.Y - expansion;
                var bottom = end.Y + expansion;

                draw.AddTriangleFilled(new Vector2(left, top), new Vector2(left + arm, top), new Vector2(left, top + arm), marker);
                draw.AddTriangleFilled(new Vector2(right, top), new Vector2(right - arm, top), new Vector2(right, top + arm), marker);
                draw.AddTriangleFilled(new Vector2(left, bottom), new Vector2(left + arm, bottom), new Vector2(left, bottom - arm), marker);
                draw.AddTriangleFilled(new Vector2(right, bottom), new Vector2(right - arm, bottom), new Vector2(right, bottom - arm), marker);


                draw.AddCircleFilled(new Vector2(p.X + tip, p.Y + tip), MathF.Max(1.5f, 1.8f * scale), marker);
                draw.AddCircleFilled(new Vector2(end.X - tip, p.Y + tip), MathF.Max(1.5f, 1.8f * scale), marker);
                draw.AddCircleFilled(new Vector2(p.X + tip, end.Y - tip), MathF.Max(1.5f, 1.8f * scale), marker);
                draw.AddCircleFilled(new Vector2(end.X - tip, end.Y - tip), MathF.Max(1.5f, 1.8f * scale), marker);
            }
            else
            {
                var glow = Color(theme.AccentStrong, opacity * (0.72f + 0.20f * pulse));
                draw.AddRect(
                    p - new Vector2(1f * scale),
                    end + new Vector2(1f * scale),
                    glow,
                    6f * scale,
                    ImDrawFlags.None,
                    MathF.Max(2f, 2.2f * scale));
            }
        }

        var cooldownSeconds = 0;
        var cooldownPercent = Math.Clamp(nativeSlot->GetSlotActionCooldownPercentage(&cooldownSeconds), 0, 100);
        if (cooldownPercent > 0)
        {
            var fraction = cooldownPercent / 100f;
            if (Plugin.Instance.Configuration.CooldownStyle == CooldownDisplayStyle.FfxivClock)
            {
                DrawNativeClockCooldown(draw, p, slotSize, fraction, scale, opacity);
            }
            else
            {
                draw.AddRectFilled(
                    p + new Vector2(2f * scale),
                    new Vector2(end.X - 2f * scale, p.Y + 2f * scale + (slotSize - 4f * scale) * fraction),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.68f)),
                    4f * scale);
            }

            if (cooldownSeconds > 0)
            {
                var cooldownLabel = cooldownSeconds >= 60 ? $"{MathF.Ceiling(cooldownSeconds / 60f):0}m" : cooldownSeconds.ToString();
                var baseFontSize = ImGui.GetFontSize();
                var requestedFontSize = MathF.Max(baseFontSize + 1f, baseFontSize * 1.28f);
                var maximumFontSize = MathF.Max(baseFontSize + 1f, slotSize * 0.42f);
                var cooldownFontSize = MathF.Min(requestedFontSize, maximumFontSize);
                var cooldownScale = cooldownFontSize / MathF.Max(1f, baseFontSize);
                var cooldownSize = ImGui.CalcTextSize(cooldownLabel) * cooldownScale;
                var cooldownPos = p + new Vector2((slotSize - cooldownSize.X) * 0.5f, (slotSize - cooldownSize.Y) * 0.5f);
                var cdColor = Plugin.Instance.Configuration.CooldownTextColor;
                var outline = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity));
                var outlineOffset = MathF.Max(1f, cooldownFontSize * 0.065f);
                var font = ImGui.GetFont();
                draw.AddText(font, cooldownFontSize, cooldownPos + new Vector2(-outlineOffset, 0f), outline, cooldownLabel);
                draw.AddText(font, cooldownFontSize, cooldownPos + new Vector2(outlineOffset, 0f), outline, cooldownLabel);
                draw.AddText(font, cooldownFontSize, cooldownPos + new Vector2(0f, -outlineOffset), outline, cooldownLabel);
                draw.AddText(font, cooldownFontSize, cooldownPos + new Vector2(0f, outlineOffset), outline, cooldownLabel);
                draw.AddText(
                    font,
                    cooldownFontSize,
                    cooldownPos,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(cdColor.X, cdColor.Y, cdColor.Z, cdColor.W * opacity)),
                    cooldownLabel);
            }
        }

        if (nativeSlot->CostDisplayMode != 0 && nativeSlot->CostValue > 0)
        {
            var cost = nativeSlot->CostValue >= 1000 ? $"{nativeSlot->CostValue / 1000f:0.#}k" : nativeSlot->CostValue.ToString();
            var costSize = ImGui.CalcTextSize(cost);
            draw.AddText(end - new Vector2(costSize.X + 3f * scale, costSize.Y + 2f * scale), Color(theme.Text, opacity), cost);
        }

        draw.AddRectFilled(p + new Vector2(3f * scale, slotSize - 4f * scale), end - new Vector2(3f * scale, 2f * scale), Color(theme.AccentStrong, opacity * 0.54f), 1f);
        DrawSlotKey(draw, p, keyLabel, theme, scale, opacity);
    }

    private static void DrawEmptyHotbarSlot(
        ImDrawListPtr draw,
        Vector2 p,
        Vector2 end,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        draw.AddRect(
            p,
            end,
            Color(theme.Accent, opacity * 0.30f),
            5f * scale,
            ImDrawFlags.None,
            MathF.Max(1f, 0.8f * scale));
        draw.AddLine(
            p + new Vector2(4f * scale, 2f * scale),
            new Vector2(end.X - 4f * scale, p.Y + 2f * scale),
            Color(theme.AccentStrong, opacity * 0.20f),
            MathF.Max(1f, 0.8f * scale));
    }

    private static void DrawNativeClockCooldown(
        ImDrawListPtr draw,
        Vector2 p,
        float slotSize,
        float fraction,
        float scale,
        float opacity)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction <= 0f)
            return;

        var inset = MathF.Max(1f, 2f * scale);
        var clipMin = p + new Vector2(inset);
        var clipMax = p + new Vector2(slotSize - inset);
        var center = p + new Vector2(slotSize * 0.5f);
        var radius = slotSize * 0.76f;
        var sweep = MathF.PI * 2f * fraction;
        var segments = Math.Max(2, (int)MathF.Ceiling(48f * fraction));
        var startAngle = -MathF.PI * 0.5f;
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.70f));

        draw.PushClipRect(clipMin, clipMax, true);
        for (var segment = 0; segment < segments; segment++)
        {
            var angleOne = startAngle + sweep * segment / segments;
            var angleTwo = startAngle + sweep * (segment + 1) / segments;
            var pointOne = center + new Vector2(MathF.Cos(angleOne), MathF.Sin(angleOne)) * radius;
            var pointTwo = center + new Vector2(MathF.Cos(angleTwo), MathF.Sin(angleTwo)) * radius;
            draw.AddTriangleFilled(center, pointOne, pointTwo, color);
        }
        draw.PopClipRect();
    }

    private static void DrawSlotKey(ImDrawListPtr draw, Vector2 p, string keyLabel, ThemePalette theme, float scale, float opacity)
    {
        keyLabel = SanitizeRenderedKeyLabel(keyLabel);
        if (!string.IsNullOrEmpty(keyLabel))
            draw.AddText(p + new Vector2(3f, 2f) * scale, Color(theme.Text, opacity * 0.92f), keyLabel);
    }

    private static string SanitizeRenderedKeyLabel(string? keyLabel)
    {
        if (string.IsNullOrWhiteSpace(keyLabel))
            return string.Empty;

        var label = keyLabel.Trim();
        if (label.Length > 24 || label.Any(char.IsControl))
            return string.Empty;


        var condensed = new string(label.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (condensed.Length > 1 && ulong.TryParse(condensed, out _))
            return string.Empty;

        foreach (var prefix in new[] { "key", "vk", "virtualkey", "input" })
        {
            if (condensed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(condensed[prefix.Length..], out _))
                return string.Empty;
        }

        if (string.Equals(condensed, uint.MaxValue.ToString(), StringComparison.Ordinal))
            return string.Empty;

        return label;
    }

    private static void DrawPlayerFrame(
        Plugin plugin,
        ImDrawListPtr draw,
        HudBounds bounds,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode,
        bool showJobIcon,
        UiMode mode)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null && !editMode)
            return;

        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.76f, 7f * scale);

        var hp = player is null ? 0.86f : player.MaxHp > 0 ? player.CurrentHp / (float)player.MaxHp : 0f;
        var resourceLabel = plugin.GetHudResourceLabel(mode);
        var resourceValues = player is null
            ? resourceLabel switch
            {
                "CP" => (Current: 540, Maximum: 700, Label: "CP"),
                "GP" => (Current: 720, Maximum: 1000, Label: "GP"),
                _ => (Current: 7200, Maximum: 10000, Label: "MP"),
            }
            : plugin.GetHudResourceValues(mode);
        var resourceFraction = resourceValues.Maximum > 0
            ? Math.Clamp(resourceValues.Current / (float)resourceValues.Maximum, 0f, 1f)
            : 0f;
        var hpBarPosition = p + new Vector2(12f * scale, s.Y * 0.45f);
        var hpBarSize = new Vector2(MathF.Max(10f, s.X - 24f * scale), MathF.Max(5f, s.Y * 0.15f));
        var mpBarPosition = p + new Vector2(12f * scale, s.Y * 0.86f);
        var mpBarSize = new Vector2(MathF.Max(10f, s.X - 24f * scale), MathF.Max(3f, s.Y * 0.07f));
        DrawBar(draw, hpBarPosition, hpBarSize, hp, theme.AccentStrong, theme.PanelAlt, opacity);
        var resourceColor = resourceLabel switch
        {
            "CP" => theme.Warning,
            "GP" => theme.Success,
            _ => new Vector4(0.34f, 0.58f, 0.96f, 1f),
        };
        DrawBar(draw, mpBarPosition, mpBarSize, resourceFraction, resourceColor, theme.PanelAlt, opacity);

        var identityY = p.Y + 6f * scale;
        var iconDrawn = false;
        var iconSize = Math.Clamp(s.Y * 0.34f, 16f * scale, 21f * scale);
        var iconPosition = new Vector2(p.X + 8f * scale, identityY - 1f * scale);
        if (showJobIcon)
        {
            if (TryGetPartyJobIcon(player, player, out var iconId, out var abbreviation))
            {
                DrawPartyJobIcon(draw, iconId, abbreviation, iconPosition, iconSize, theme, scale, opacity);
                iconDrawn = true;
            }
            else if (editMode)
            {
                DrawPartyJobIcon(draw, 0, "JOB", iconPosition, iconSize, theme, scale, opacity * 0.72f);
                iconDrawn = true;
            }
        }

        var name = player?.Name.ToString() ?? "Player Frame";
        var namePosition = new Vector2(
            p.X + (iconDrawn ? 8f * scale + iconSize + 5f * scale : 12f * scale),
            identityY);
        var hpValue = player is null
            ? "86,000 / 100,000 HP"
            : $"{player.CurrentHp:N0} / {player.MaxHp:N0} HP";
        var hpValueSize = ImGui.CalcTextSize(hpValue);
        var hpValuePosition = new Vector2(p.X + s.X - 12f * scale - hpValueSize.X, identityY);
        draw.PushClipRect(
            new Vector2(namePosition.X, p.Y),
            new Vector2(MathF.Max(namePosition.X + 1f, hpValuePosition.X - 7f * scale), p.Y + s.Y),
            true);
        draw.AddText(namePosition, Color(theme.Text, opacity), name);
        draw.PopClipRect();
        draw.AddText(hpValuePosition, Color(theme.Muted, opacity), hpValue);

        var mpValue = $"{resourceValues.Current:N0} / {resourceValues.Maximum:N0} {resourceLabel}";
        var mpValueSize = ImGui.CalcTextSize(mpValue);
        var mpValueY = MathF.Min(
            mpBarPosition.Y - ImGui.GetTextLineHeight() - 1f * scale,
            p.Y + s.Y - ImGui.GetTextLineHeight() - 5f * scale);
        draw.AddText(
            new Vector2(p.X + s.X - 12f * scale - mpValueSize.X, mpValueY),
            Color(resourceColor, opacity),
            mpValue);

        if (mode == UiMode.Work && player is IBattleChara workActor)
            DrawWorkStatusStrip(draw, workActor, bounds, theme, scale, opacity);
    }

    private static void DrawWorkStatusStrip(
        ImDrawListPtr draw,
        IBattleChara actor,
        HudBounds playerBounds,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var entries = WorkStatusService.Snapshot(actor, 8);
        if (entries.Count == 0)
            return;

        var slots = ResolveWorkStatusSlots(playerBounds, scale, entries.Count);
        for (var index = 0; index < Math.Min(entries.Count, slots.Count); index++)
        {
            var entry = entries[index];
            var slot = slots[index];
            DrawStatusIcon(draw, entry.IconId, false, slot.IconBounds.Position, slot.IconBounds.Size.X, theme, scale, opacity);

            var duration = FormatCompactStatusDuration(entry.RemainingTime);
            if (string.IsNullOrEmpty(duration))
                continue;

            var textSize = ImGui.CalcTextSize(duration);
            draw.AddText(
                new Vector2(
                    slot.TextPosition.X,
                    slot.TextPosition.Y + MathF.Max(0f, (slot.IconBounds.Size.Y - textSize.Y) * 0.5f)),
                Color(theme.Muted, opacity),
                duration);
        }
    }

    internal static IReadOnlyList<WorkStatusSlot> ResolveWorkStatusSlots(HudBounds bounds, float scale, int requestedCount)
    {


        var iconSize = Math.Clamp(bounds.Size.Y * 0.225f, 10f, 17f);
        var textWidth = Math.Clamp(24f * scale, 22f, 34f);
        var gap = MathF.Max(2f, 3f * scale);
        var itemWidth = iconSize + textWidth + gap;
        var left = bounds.Position.X + 12f * scale;


        var available = MathF.Max(
            itemWidth,
            MathF.Min(bounds.Size.X * 0.42f, 180f * scale));
        var maximum = Math.Clamp((int)MathF.Floor((available + gap) / (itemWidth + gap)), 1, 8);
        var count = Math.Min(Math.Max(0, requestedCount), maximum);


        var top = bounds.Position.Y + bounds.Size.Y * 0.60f + 1f * scale;
        var result = new List<WorkStatusSlot>(count);
        for (var index = 0; index < count; index++)
        {
            var x = left + index * (itemWidth + gap);
            result.Add(new WorkStatusSlot(
                new HudBounds(new Vector2(x, top), new Vector2(iconSize)),
                new Vector2(x + iconSize + 3f * scale, top)));
        }
        return result;
    }

    private static string FormatCompactStatusDuration(float seconds)
    {
        if (seconds <= 0.05f)
            return string.Empty;
        if (seconds >= 86400f)
            return $"{MathF.Floor(seconds / 86400f):0}d";
        if (seconds >= 3600f)
            return $"{MathF.Floor(seconds / 3600f):0}h";
        if (seconds >= 60f)
            return $"{MathF.Floor(seconds / 60f):0}m";
        return $"{MathF.Ceiling(seconds):0}s";
    }

    private static void DrawTargetFrame(
        ImDrawListPtr draw,
        HudBounds bounds,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode,
        bool showJobIcon)
    {
        var target = Plugin.TargetManager.Target as IBattleChara;
        if ((target is null || target.MaxHp == 0) && !editMode)
            return;

        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.74f, 6f * scale);

        var statusBandHeight = DrawTargetStatusIcons(draw, target, p, s, theme, scale, opacity);
        var hp = target is null ? 0.72f : Math.Clamp(target.CurrentHp / (float)Math.Max(1u, target.MaxHp), 0f, 1f);
        var mp = target is null ? 0.64f : target.MaxMp > 0 ? Math.Clamp(target.CurrentMp / (float)target.MaxMp, 0f, 1f) : 0f;
        var identityY = p.Y + (statusBandHeight > 0f ? statusBandHeight + 1f * scale : 6f * scale);

        var iconDrawn = false;
        var targetIconSize = Math.Clamp(ImGui.GetTextLineHeight() + 2f * scale, 16f * scale, 22f * scale);
        var targetIconPosition = new Vector2(p.X + 9f * scale, identityY - 1f * scale);
        if (showJobIcon)
        {
            if (TryGetPartyJobIcon(target, target, out var targetJobIconId, out var targetJobAbbreviation))
            {
                DrawPartyJobIcon(draw, targetJobIconId, targetJobAbbreviation, targetIconPosition, targetIconSize, theme, scale, opacity);
                iconDrawn = true;
            }
            else if (editMode)
            {
                DrawPartyJobIcon(draw, 0, "JOB", targetIconPosition, targetIconSize, theme, scale, opacity * 0.72f);
                iconDrawn = true;
            }
        }

        var targetNamePosition = new Vector2(
            p.X + (iconDrawn ? 9f * scale + targetIconSize + 5f * scale : 12f * scale),
            identityY);
        var percent = $"{hp * 100f:0.0}%";
        var hpValue = target is null
            ? $"72,000 / 100,000 HP  •  {percent}"
            : $"{target.CurrentHp:N0} / {target.MaxHp:N0} HP  •  {percent}";
        var hpValueSize = ImGui.CalcTextSize(hpValue);
        var hpValuePosition = new Vector2(p.X + s.X - 12f * scale - hpValueSize.X, identityY);
        draw.PushClipRect(
            new Vector2(targetNamePosition.X, p.Y),
            new Vector2(MathF.Max(targetNamePosition.X + 1f, hpValuePosition.X - 7f * scale), p.Y + s.Y),
            true);
        draw.AddText(targetNamePosition, Color(theme.Text, opacity), target?.Name.ToString() ?? "Target Frame");
        draw.PopClipRect();
        draw.AddText(hpValuePosition, Color(theme.Danger, opacity), hpValue);

        var hasMp = editMode || (target is not null && target.MaxMp > 0);
        var resourceLineY = identityY + ImGui.GetTextLineHeight() + 1f * scale;
        if (hasMp)
        {
            var mpValue = target is null
                ? "6,400 / 10,000 MP"
                : $"{target.CurrentMp:N0} / {target.MaxMp:N0} MP";
            var mpValueSize = ImGui.CalcTextSize(mpValue);
            draw.AddText(
                new Vector2(p.X + s.X - 12f * scale - mpValueSize.X, resourceLineY),
                Color(new Vector4(0.46f, 0.68f, 1f, 1f), opacity),
                mpValue);
        }

        var hpPositionY = MathF.Max(
            hasMp ? resourceLineY + ImGui.GetTextLineHeight() + 2f * scale : identityY + ImGui.GetTextLineHeight() + 3f * scale,
            p.Y + s.Y - 12f * scale);
        var hpPosition = new Vector2(p.X + 12f * scale, MathF.Min(hpPositionY, p.Y + s.Y - 9f * scale));
        var hpSize = new Vector2(MathF.Max(10f, s.X - 24f * scale), MathF.Max(4f, s.Y * 0.075f));
        DrawBar(draw, hpPosition, hpSize, hp, theme.Danger, theme.PanelAlt, opacity);

        if (hasMp)
        {
            var mpPosition = hpPosition + new Vector2(0f, hpSize.Y + 2f * scale);
            var mpSize = new Vector2(hpSize.X, MathF.Max(2f, s.Y * 0.03f));
            DrawBar(draw, mpPosition, mpSize, mp, new Vector4(0.34f, 0.58f, 0.96f, 1f), theme.PanelAlt, opacity);
        }
    }

    private static void DrawTargetOfTargetFrame(
        ImDrawListPtr draw,
        HudBounds bounds,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode,
        bool showJobIcon)
    {
        var primaryTarget = Plugin.TargetManager.Target as IBattleChara;
        var targetOfTarget = ResolveTargetsTarget(primaryTarget);
        if ((targetOfTarget is null || !targetOfTarget.IsValid()) && !editMode)
            return;

        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.70f, 5f * scale);
        draw.AddText(p + new Vector2(7f * scale, 2f * scale), Color(theme.Muted, opacity), "TARGET'S TARGET");

        var actor = targetOfTarget as IBattleChara;
        var iconDrawn = false;
        var iconSize = Math.Clamp(s.Y * 0.40f, 14f * scale, 20f * scale);
        var identityY = p.Y + MathF.Max(15f * scale, s.Y * 0.36f);
        var iconPosition = new Vector2(p.X + 7f * scale, identityY - 1f * scale);
        if (showJobIcon)
        {
            if (TryGetPartyJobIcon(targetOfTarget, actor, out var iconId, out var abbreviation))
            {
                DrawPartyJobIcon(draw, iconId, abbreviation, iconPosition, iconSize, theme, scale, opacity);
                iconDrawn = true;
            }
            else if (editMode)
            {
                DrawPartyJobIcon(draw, 0, "JOB", iconPosition, iconSize, theme, scale, opacity * 0.72f);
                iconDrawn = true;
            }
        }

        var namePosition = new Vector2(p.X + (iconDrawn ? 7f * scale + iconSize + 5f * scale : 8f * scale), identityY);
        var displayName = targetOfTarget?.Name.ToString();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = editMode ? "Party Member" : "—";

        var hpRatio = actor is not null && actor.MaxHp > 0
            ? Math.Clamp(actor.CurrentHp / (float)actor.MaxHp, 0f, 1f)
            : editMode ? 0.82f : 0f;
        var hpLabel = actor is not null && actor.MaxHp > 0 ? $"{hpRatio * 100f:0}%" : string.Empty;
        var hpLabelSize = ImGui.CalcTextSize(hpLabel);
        var hpLabelPosition = new Vector2(p.X + s.X - 8f * scale - hpLabelSize.X, identityY);
        draw.PushClipRect(namePosition, new Vector2(MathF.Max(namePosition.X + 1f, hpLabelPosition.X - 5f * scale), p.Y + s.Y), true);
        draw.AddText(namePosition, Color(theme.Text, opacity), displayName);
        draw.PopClipRect();
        if (!string.IsNullOrEmpty(hpLabel))
            draw.AddText(hpLabelPosition, Color(theme.AccentStrong, opacity), hpLabel);

        var barPosition = new Vector2(p.X + 7f * scale, p.Y + s.Y - 6f * scale);
        var barSize = new Vector2(MathF.Max(10f, s.X - 14f * scale), MathF.Max(2f, 3f * scale));
        DrawBar(draw, barPosition, barSize, hpRatio, theme.AccentStrong, theme.PanelAlt, opacity * 0.90f);
    }

    private static void DrawCastBar(
        ImDrawListPtr draw,
        HudBounds bounds,
        IBattleChara? actor,
        ThemePalette theme,
        float scale,
        float opacity,
        bool editMode,
        bool showInterruptState)
    {
        var hasCast = actor is not null && actor.IsCasting && actor.TotalCastTime > 0.01f;
        if (!hasCast && !editMode)
            return;

        var p = bounds.Position;
        var s = bounds.Size;
        var interruptible = showInterruptState && actor?.IsCastInterruptible == true;
        var castColor = interruptible ? theme.Warning : theme.AccentStrong;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.88f, 8f * scale);
        draw.AddRect(p, p + s, Color(castColor, opacity * 0.74f), 8f * scale, ImDrawFlags.None, MathF.Max(1f, scale));

        var leftLabel = showInterruptState
            ? hasCast ? interruptible ? "INTERRUPTIBLE" : "NOT INTERRUPTIBLE" : "TARGET CAST"
            : "CASTING";
        draw.AddText(p + new Vector2(8f * scale, 4f * scale), Color(castColor, opacity), leftLabel);

        var castName = hasCast ? ResolveCastActionName(actor!) : showInterruptState ? "Preview Target Cast" : "Preview Player Cast";
        if (string.IsNullOrWhiteSpace(castName))
            castName = "Casting";


        var remainingCastSeconds = hasCast
            ? MathF.Max(0f, actor!.TotalCastTime - actor.CurrentCastTime)
            : 2.4f;
        var castTimeText = remainingCastSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
        var castTimeSize = ImGui.CalcTextSize(castTimeText);
        var castTimePosition = new Vector2(
            p.X + s.X - 8f * scale - castTimeSize.X,
            p.Y + 4f * scale);

        var minimumCastNameX = p.X + ImGui.CalcTextSize(leftLabel).X + 16f * scale;
        var castNameRight = castTimePosition.X - 8f * scale;
        if (castNameRight > minimumCastNameX)
        {
            var castNameSize = ImGui.CalcTextSize(castName);
            var castNamePosition = new Vector2(
                MathF.Max(minimumCastNameX, castNameRight - castNameSize.X),
                p.Y + 4f * scale);
            draw.PushClipRect(
                new Vector2(minimumCastNameX, p.Y),
                new Vector2(castNameRight, p.Y + s.Y),
                true);
            draw.AddText(castNamePosition, Color(theme.Text, opacity), castName);
            draw.PopClipRect();
        }

        draw.AddText(castTimePosition, Color(castColor, opacity), castTimeText);

        var progress = hasCast ? Math.Clamp(actor!.CurrentCastTime / actor.TotalCastTime, 0f, 1f) : 0.58f;
        var barPosition = new Vector2(p.X + 7f * scale, p.Y + s.Y - 11f * scale);
        var barSize = new Vector2(MathF.Max(20f, s.X - 14f * scale), MathF.Max(5f, 7f * scale));
        draw.AddRectFilled(barPosition, barPosition + barSize, Color(theme.PanelAlt, opacity), 4f * scale);
        draw.AddRectFilled(barPosition, barPosition + new Vector2(barSize.X * progress, barSize.Y), Color(castColor, opacity), 4f * scale);
        draw.AddLine(barPosition + new Vector2(2f * scale, 1f * scale), barPosition + new Vector2(MathF.Max(2f * scale, barSize.X * progress - 2f * scale), 1f * scale), Color(theme.Text, opacity * 0.50f), MathF.Max(1f, scale));
    }

    private static void DrawLimitBreakGauge(ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity, bool editMode, LimitBreakLayout layout)
    {
        var progress = editMode ? 0.72f : 0f;
        var maxBars = 3;
        if (!editMode)
        {


            var readNativeGauge = TryReadNativeLimitBreak(out progress, out maxBars);


            if (!readNativeGauge)
            {
                try
                {
                    var type = Plugin.PartyList.GetType();
                    var currentProperty = type.GetProperty("LimitBreakCurrentValue") ?? type.GetProperty("LimitBreakCurrent") ?? type.GetProperty("LimitBreakValue");
                    var maxProperty = type.GetProperty("LimitBreakMaxValue") ?? type.GetProperty("LimitBreakMax");
                    if (currentProperty?.GetValue(Plugin.PartyList) is IConvertible current && maxProperty?.GetValue(Plugin.PartyList) is IConvertible maximum)
                    {
                        var max = maximum.ToSingle(null);
                        if (max > 0f) progress = Math.Clamp(current.ToSingle(null) / max, 0f, 1f);
                    }
                    var barsProperty = type.GetProperty("LimitBreakBarCount") ?? type.GetProperty("LimitBreakBars");
                    if (barsProperty?.GetValue(Plugin.PartyList) is IConvertible bars)
                        maxBars = Math.Clamp(bars.ToInt32(null), 1, 3);
                }
                catch { }
            }


            progress = Math.Clamp(progress, 0f, 1f);
        }

        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.86f, 7f * scale);
        const float gapBase = 4f;
        var gap = gapBase * scale;
        for (var i = 0; i < maxBars; i++)
        {
            HudBounds segment;
            if (layout == LimitBreakLayout.Stacked)
            {
                var h = (s.Y - gap * (maxBars + 1)) / maxBars;
                segment = new HudBounds(p + new Vector2(gap, gap + i * (h + gap)), new Vector2(s.X - gap * 2f, h));
            }
            else
            {
                var w = (s.X - gap * (maxBars + 1)) / maxBars;
                var y = layout == LimitBreakLayout.Diagonal45 ? (maxBars - 1 - i) * MathF.Min(s.Y * 0.18f, 7f * scale) : 0f;
                segment = new HudBounds(p + new Vector2(gap + i * (w + gap), gap + y), new Vector2(w, MathF.Max(5f, s.Y - gap * 2f - y)));
            }
            var localProgress = Math.Clamp(progress * maxBars - i, 0f, 1f);
            draw.AddRectFilled(segment.Position, segment.Position + segment.Size, Color(theme.PanelAlt, opacity), 3f * scale);
            draw.AddRectFilled(segment.Position, segment.Position + new Vector2(segment.Size.X * localProgress, segment.Size.Y), Color(i == maxBars - 1 ? theme.AccentStrong : theme.Accent, opacity), 3f * scale);
            draw.AddRect(segment.Position, segment.Position + segment.Size, Color(theme.Text, opacity * 0.35f), 3f * scale, ImDrawFlags.None, MathF.Max(1f, scale));
        }
    }


    internal static string BuildLimitBreakDiagnostic()
    {
        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
                return "LB DIAG | UIState=null";

            var controller = (byte*)&uiState->LimitBreakController;
            var raw = new byte[32];
            for (var i = 0; i < raw.Length; i++)
                raw[i] = controller[i];

            static uint U32(byte[] b, int o) =>
                (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
            static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));

            var typedOk = TryReadNativeLimitBreak(out var progress, out var bars);
            var partyCurrent = "n/a";
            var partyMax = "n/a";
            var partyBars = "n/a";
            try
            {
                var type = Plugin.PartyList.GetType();
                object? Read(params string[] names)
                {
                    foreach (var name in names)
                    {
                        var value = type.GetProperty(name)?.GetValue(Plugin.PartyList);
                        if (value != null) return value;
                    }
                    return null;
                }
                partyCurrent = Convert.ToString(Read("LimitBreakCurrentValue", "LimitBreakCurrent", "LimitBreakValue"), System.Globalization.CultureInfo.InvariantCulture) ?? "null";
                partyMax = Convert.ToString(Read("LimitBreakMaxValue", "LimitBreakMax"), System.Globalization.CultureInfo.InvariantCulture) ?? "null";
                partyBars = Convert.ToString(Read("LimitBreakBarCount", "LimitBreakBars"), System.Globalization.CultureInfo.InvariantCulture) ?? "null";
            }
            catch (Exception ex)
            {
                partyCurrent = "err:" + ex.GetType().Name;
            }

            return $"LB DIAG | typedOk={typedOk} progress={progress:F6} bars={bars} | " +
                   $"u32@00={U32(raw,0)} u32@04={U32(raw,4)} u32@08={U32(raw,8)} u32@0C={U32(raw,12)} " +
                   $"u16@00={U16(raw,0)} u16@02={U16(raw,2)} u16@04={U16(raw,4)} u16@06={U16(raw,6)} u16@0A={U16(raw,10)} " +
                   $"b08={raw[8]} b09={raw[9]} b0A={raw[10]} b0B={raw[11]} | " +
                   $"partyCurrent={partyCurrent} partyMax={partyMax} partyBars={partyBars} | " +
                   $"raw={BitConverter.ToString(raw)}";
        }
        catch (Exception ex)
        {
            return $"LB DIAG | exception={ex.GetType().Name}: {ex.Message}";
        }
    }

    private static bool TryReadNativeLimitBreak(out float progress, out int barCount)
    {
        progress = 0f;
        barCount = 3;

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
                return false;


            var controller = (byte*)&uiState->LimitBreakController;
            var bars = *(byte*)(controller + 0x08);
            var current = *(ushort*)(controller + 0x0A);
            var perBar = *(uint*)(controller + 0x0C);

            if (bars is < 1 or > 3 || perBar == 0)
                return false;

            var maximum = (ulong)perBar * bars;
            if (maximum == 0)
                return false;


            if (current > maximum + perBar)
                return false;

            barCount = bars;
            progress = Math.Clamp((float)((double)current / maximum), 0f, 1f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveCastActionName(IBattleChara target)
    {
        var actionId = ReadCastActionId(target);
        if (actionId == 0)
            return string.Empty;

        lock (CastActionCacheLock)
        {
            if (CastActionNames.TryGetValue(actionId, out var cached))
                return cached;
        }

        var name = string.Empty;
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (sheet.TryGetRow(actionId, out var action))
                name = action.Name.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not resolve cast action {ActionId}.", actionId);
        }

        lock (CastActionCacheLock)
            CastActionNames[actionId] = name;
        return name;
    }

    private static uint ReadCastActionId(object source)
    {
        var sourceType = source.GetType();
        Func<object, uint> reader;
        lock (CastActionCacheLock)
        {
            if (!CastActionReaders.TryGetValue(sourceType, out reader!))
            {
                reader = CreateCastActionReader(sourceType);
                CastActionReaders[sourceType] = reader;
            }
        }

        try
        {
            return reader(source);
        }
        catch
        {
            return 0;
        }
    }

    private static Func<object, uint> CreateCastActionReader(Type sourceType)
    {
        static PropertyInfo? FindProperty(Type type)
        {
            foreach (var propertyName in new[] { "CastActionId", "CastActionID", "CurrentCastActionId", "CurrentCastActionID", "CastId", "CastID" })
            {
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property is not null)
                    return property;
            }
            return null;
        }

        var property = FindProperty(sourceType)
            ?? sourceType.GetInterfaces().Select(FindProperty).FirstOrDefault(candidate => candidate is not null);
        if (property is null)
            return static _ => 0u;

        return source =>
        {
            var value = property.GetValue(source);
            return TryReadUnsignedValue(value, out var actionId) ? actionId : 0u;
        };
    }

    private static IGameObject? ResolveTargetsTarget(IBattleChara? target)
    {
        if (target is null || target.Address == nint.Zero)
            return null;

        try
        {
            var nativeTarget = (NativeCharacter*)target.Address;
            var targetId = nativeTarget->GetTargetId().Id;
            if (targetId == 0UL)
                return null;

            return Plugin.ObjectTable.FirstOrDefault(actor => actor.GameObjectId == targetId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "RE:Frame could not resolve the target's target.");
            return null;
        }
    }

    private static void DrawFocusFrame(ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity, bool editMode)
    {
        var target = Plugin.TargetManager.FocusTarget as IBattleChara;
        if ((target is null || target.MaxHp == 0) && !editMode)
            return;
        var p = bounds.Position;
        var s = bounds.Size;
        DrawGlassPanel(draw, p, s, theme, opacity * 0.68f, 5f * scale);
        var hp = target is null ? 0.67f : Math.Clamp(target.CurrentHp / (float)Math.Max(1u, target.MaxHp), 0f, 1f);
        DrawBar(draw, p + new Vector2(8f, s.Y * 0.68f), new Vector2(MathF.Max(10f, s.X - 16f), MathF.Max(4f, s.Y * 0.14f)), hp, theme.Warning, theme.PanelAlt, opacity);
        draw.AddText(p + new Vector2(8f, 4f) * scale, Color(theme.Muted, opacity), target?.Name.ToString() ?? "Focus Target");
    }

    private static void DrawCombatHalo(Plugin plugin, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity, bool preview, bool editMode)
    {
        var center = bounds.Position + bounds.Size * 0.5f;
        if (!preview && !editMode && plugin.Configuration.HaloFollowsPlayer && Plugin.ObjectTable.LocalPlayer is { } player)
        {
            var world = player.Position + new Vector3(0f, 1.35f, 0f);
            if (Plugin.GameGui.WorldToScreen(world, out var screen))
                center = screen + new Vector2(0f, plugin.Configuration.HaloVerticalOffset * scale);
        }
        var radius = MathF.Max(60f, MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.34f);
        var thickness = Math.Clamp(plugin.Configuration.HaloThickness * scale, 2f, MathF.Max(3f, radius * 0.10f));
        var maxHp = Math.Max(1u, plugin.CombatTelemetry.PlayerMaxHp);
        var damageIntensity = editMode ? 0.68f : Math.Clamp((float)(plugin.CombatTelemetry.DamageInPerSecond / (maxHp * 0.18)), 0f, 1f);
        var healIntensity = editMode ? 0.43f : Math.Clamp((float)(plugin.CombatTelemetry.HealingReceivedPerSecond / (maxHp * 0.18)), 0f, 1f);
        var pressureIntensity = editMode ? 0.81f : Math.Clamp((float)(plugin.CombatTelemetry.TargetPressurePerSecond / Math.Max(1d, maxHp * 0.35)), 0f, 1f);
        const float leftStart = 2.02f, leftEnd = 4.27f, rightStart = -1.13f, rightEnd = 1.13f;
        DrawArc(draw, center, radius, leftStart, leftEnd, Color(theme.PanelAlt, opacity * 0.80f), thickness + 4f * scale);
        DrawArc(draw, center, radius, rightStart, rightEnd, Color(theme.PanelAlt, opacity * 0.80f), thickness + 4f * scale);
        DrawArc(draw, center, radius, leftStart, leftStart + (leftEnd - leftStart) * damageIntensity, Color(theme.Danger, opacity), thickness);
        DrawArc(draw, center, radius - 9f * scale, leftStart, leftStart + (leftEnd - leftStart) * healIntensity, Color(theme.Success, opacity), MathF.Max(2f, thickness * 0.55f));
        DrawArc(draw, center, radius, rightStart, rightStart + (rightEnd - rightStart) * pressureIntensity, Color(theme.AccentStrong, opacity), thickness);
        DrawArcTicks(draw, center, radius, leftStart, leftEnd, theme, scale, opacity);
        DrawArcTicks(draw, center, radius, rightStart, rightEnd, theme, scale, opacity);
        var leftAnchor = center + new Vector2(-radius - 80f * scale, -16f * scale);
        draw.AddText(leftAnchor, Color(theme.Danger, opacity), $"IN  {(editMode ? "6.8k" : FormatRate(plugin.CombatTelemetry.DamageInPerSecond))}/s");
        draw.AddText(leftAnchor + new Vector2(0f, 18f) * scale, Color(theme.Success, opacity), $"HEAL  {(editMode ? "4.3k" : FormatRate(plugin.CombatTelemetry.HealingReceivedPerSecond))}/s");
        var rightAnchor = center + new Vector2(radius + 18f * scale, -16f * scale);
        draw.AddText(rightAnchor, Color(theme.AccentStrong, opacity), $"PRESSURE  {(editMode ? "8.1k" : FormatRate(plugin.CombatTelemetry.TargetPressurePerSecond))}/s");
        draw.AddText(rightAnchor + new Vector2(0f, 18f) * scale, Color(theme.Muted, opacity), "TARGET PRESSURE");
        DrawDiamond(draw, center + new Vector2(0f, -radius - 13f * scale), 5f * scale, Color(theme.AccentStrong, opacity));
        DrawDiamond(draw, center + new Vector2(0f, radius + 13f * scale), 4f * scale, Color(theme.Accent, opacity * 0.72f));
    }

    private static void DrawLeisureDock(Configuration config, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
        => DrawConfigurableDock(config, DockButtonCatalog.Leisure, draw, bounds, theme, scale, opacity);

    private static void DrawQuestDock(Configuration config, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
        => DrawConfigurableDock(config, DockButtonCatalog.Quest, draw, bounds, theme, scale, opacity);

    private static void DrawRoleplayDock(Configuration config, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
        => DrawConfigurableDock(config, DockButtonCatalog.Roleplay, draw, bounds, theme, scale, opacity);

    private static void DrawConfigurableDock(
        Configuration config,
        string dockKey,
        ImDrawListPtr draw,
        HudBounds bounds,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        var p = bounds.Position;
        var s = bounds.Size;
        DrawDockPanel(draw, p, s, theme, opacity, 8f * scale);
        DrawThemeGradient(
            draw,
            p + new Vector2(10f * scale, 2f * scale),
            p + new Vector2(s.X - 10f * scale, 4f * scale),
            theme,
            opacity * 0.95f);

        var buttons = DockButtonCatalog.Visible(config, dockKey);
        if (buttons.Count == 0)
        {
            DrawCenteredDockText(draw, p, s.Y, s.X, 0, "DOCK EMPTY", theme, opacity * 0.72f);
            return;
        }

        var segmentWidth = s.X / buttons.Count;
        for (var index = 0; index < buttons.Count; index++)
        {
            var segmentMin = p + new Vector2(segmentWidth * index + 4f * scale, 6f * scale);
            var segmentMax = p + new Vector2(segmentWidth * (index + 1) - 4f * scale, s.Y - 5f * scale);
            var buttonColor = theme.HasExtendedColors
                ? theme.ResolvedDockButton
                : (index % 3) switch
                {
                    0 => new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.10f),
                    1 => new Vector4(theme.Success.X, theme.Success.Y, theme.Success.Z, 0.10f),
                    _ => new Vector4(theme.Warning.X, theme.Warning.Y, theme.Warning.Z, 0.10f),
                };
            draw.AddRectFilled(segmentMin, segmentMax, Color(buttonColor, opacity), 5f * scale);
            if (index > 0)
            {
                var dividerColor = theme.HasExtendedColors
                    ? theme.ResolvedDockDivider
                    : new Vector4(
                        (index % 2 == 0 ? theme.AccentStrong : theme.Accent).X,
                        (index % 2 == 0 ? theme.AccentStrong : theme.Accent).Y,
                        (index % 2 == 0 ? theme.AccentStrong : theme.Accent).Z,
                        0.55f);
                draw.AddLine(
                    p + new Vector2(segmentWidth * index, 8f * scale),
                    p + new Vector2(segmentWidth * index, s.Y - 8f * scale),
                    Color(dividerColor, opacity),
                    1f);
            }
            DrawCenteredDockText(draw, p, s.Y, segmentWidth, index, buttons[index].Label.ToUpperInvariant(), theme, opacity);
        }
    }

    private static void DrawRoleplayDockPopup(
        ImDrawListPtr draw,
        HudBounds bounds,
        RoleplayDockPopup popup,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        if (opacity <= 0.001f)
            return;

        var p = bounds.Position;
        var s = bounds.Size;
        DrawDockPanel(draw, p, s, theme, opacity, 7f * scale);
        DrawThemeGradient(
            draw,
            p + new Vector2(7f * scale, 1f * scale),
            p + new Vector2(s.X - 7f * scale, 3f * scale),
            theme,
            opacity);

        var labels = popup == RoleplayDockPopup.ChatChannels
            ? new[] { "SAY", "PARTY", "FC", "LS1" }
            : new[] { "LEISURE", "QUEST", "RAID", "WORK", "AUTO" };
        var segmentWidth = s.X / labels.Length;
        for (var index = 0; index < labels.Length; index++)
        {
            var min = p + new Vector2(segmentWidth * index + 3f * scale, 5f * scale);
            var max = p + new Vector2(segmentWidth * (index + 1) - 3f * scale, s.Y - 4f * scale);
            var fallbackTint = index == labels.Length - 1 && popup == RoleplayDockPopup.Docks
                ? theme.Warning
                : popup == RoleplayDockPopup.ChatChannels ? theme.Success : theme.AccentStrong;
            var buttonColor = theme.HasExtendedColors
                ? theme.ResolvedDockButton
                : new Vector4(fallbackTint.X, fallbackTint.Y, fallbackTint.Z, 0.10f);
            draw.AddRectFilled(min, max, Color(buttonColor, opacity), 4f * scale);
            if (index > 0)
            {
                var dividerColor = theme.HasExtendedColors
                    ? theme.ResolvedDockDivider
                    : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.60f);
                draw.AddLine(
                    p + new Vector2(segmentWidth * index, 7f * scale),
                    p + new Vector2(segmentWidth * index, s.Y - 7f * scale),
                    Color(dividerColor, opacity),
                    MathF.Max(1f, scale));
            }
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, index, labels[index], theme, opacity);
        }
    }

    private static void DrawWorkstationDock(Configuration config, ImDrawListPtr draw, HudBounds bounds, ThemePalette theme, float scale, float opacity)
        => DrawConfigurableDock(config, DockButtonCatalog.Work, draw, bounds, theme, scale, opacity);

    private static void DrawWorkstationDockPopup(
        ImDrawListPtr draw,
        HudBounds bounds,
        WorkstationDockPopup popup,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        if (opacity <= 0.001f)
            return;

        var p = bounds.Position;
        var s = bounds.Size;
        DrawDockPanel(draw, p, s, theme, opacity, 7f * scale);
        DrawThemeGradient(
            draw,
            p + new Vector2(7f * scale, 1f * scale),
            p + new Vector2(s.X - 7f * scale, 3f * scale),
            theme,
            opacity);

        var segmentCount = popup == WorkstationDockPopup.Resources ? 3 : 5;
        var segmentWidth = s.X / segmentCount;
        for (var index = 0; index < segmentCount; index++)
        {
            var min = p + new Vector2(segmentWidth * index + 3f * scale, 5f * scale);
            var max = p + new Vector2(segmentWidth * (index + 1) - 3f * scale, s.Y - 4f * scale);
            var fallbackTint = popup == WorkstationDockPopup.Resources
                ? theme.Success
                : index == segmentCount - 1 ? theme.Warning : theme.AccentStrong;
            var buttonColor = theme.HasExtendedColors
                ? theme.ResolvedDockButton
                : new Vector4(fallbackTint.X, fallbackTint.Y, fallbackTint.Z, 0.10f);
            draw.AddRectFilled(min, max, Color(buttonColor, opacity), 4f * scale);
            if (index > 0)
            {
                var dividerColor = theme.HasExtendedColors
                    ? theme.ResolvedDockDivider
                    : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.60f);
                draw.AddLine(
                    p + new Vector2(segmentWidth * index, 7f * scale),
                    p + new Vector2(segmentWidth * index, s.Y - 7f * scale),
                    Color(dividerColor, opacity),
                    MathF.Max(1f, scale));
            }
        }

        if (popup == WorkstationDockPopup.Resources)
        {
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 0, "TEAMCRAFT", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 1, "GARLAND", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 2, "FFXIV CRAFTING", theme, opacity);
        }
        else
        {
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 0, "LEISURE", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 1, "ROLEPLAY", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 2, "QUEST", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 3, "RAID", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 4, "AUTO", theme, opacity);
        }
    }

    private static void DrawLeisureDockPopup(
        ImDrawListPtr draw,
        HudBounds bounds,
        LeisureDockPopup popup,
        ThemePalette theme,
        float scale,
        float opacity)
    {
        if (opacity <= 0.001f)
            return;

        var p = bounds.Position;
        var s = bounds.Size;
        DrawDockPanel(draw, p, s, theme, opacity, 7f * scale);
        DrawThemeGradient(
            draw,
            p + new Vector2(7f * scale, 1f * scale),
            p + new Vector2(s.X - 7f * scale, 3f * scale),
            theme,
            opacity);

        if (popup == LeisureDockPopup.Docks)
        {
            const int segmentCount = 5;
            var segmentWidth = s.X / segmentCount;
            for (var index = 0; index < segmentCount; index++)
            {
                var min = p + new Vector2(segmentWidth * index + 3f * scale, 5f * scale);
                var max = p + new Vector2(segmentWidth * (index + 1) - 3f * scale, s.Y - 4f * scale);
                var fallbackTint = index == segmentCount - 1 ? theme.Warning : theme.AccentStrong;
                var buttonColor = theme.HasExtendedColors
                    ? theme.ResolvedDockButton
                    : new Vector4(fallbackTint.X, fallbackTint.Y, fallbackTint.Z, 0.10f);
                draw.AddRectFilled(min, max, Color(buttonColor, opacity), 4f * scale);
                if (index > 0)
                {
                    var dividerColor = theme.HasExtendedColors
                        ? theme.ResolvedDockDivider
                        : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.60f);
                    draw.AddLine(
                        p + new Vector2(segmentWidth * index, 7f * scale),
                        p + new Vector2(segmentWidth * index, s.Y - 7f * scale),
                        Color(dividerColor, opacity),
                        MathF.Max(1f, scale));
                }
            }

            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 0, "ROLEPLAY", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 1, "QUEST", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 2, "RAID", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 3, "WORK", theme, opacity);
            DrawCenteredPopupText(draw, p, s.Y, segmentWidth, 4, "AUTO", theme, opacity);
            return;
        }

        var halfWidth = s.X * 0.5f;
        var leftMin = p + new Vector2(4f * scale, 5f * scale);
        var leftMax = p + new Vector2(halfWidth - 2f * scale, s.Y - 4f * scale);
        var rightMin = p + new Vector2(halfWidth + 2f * scale, 5f * scale);
        var rightMax = p + new Vector2(s.X - 4f * scale, s.Y - 4f * scale);
        var leftButtonColor = theme.HasExtendedColors
            ? theme.ResolvedDockButton
            : new Vector4(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, 0.10f);
        var rightButtonColor = theme.HasExtendedColors
            ? theme.ResolvedDockButton
            : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.10f);
        var popupDividerColor = theme.HasExtendedColors
            ? theme.ResolvedDockDivider
            : new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.62f);
        draw.AddRectFilled(leftMin, leftMax, Color(leftButtonColor, opacity), 4f * scale);
        draw.AddRectFilled(rightMin, rightMax, Color(rightButtonColor, opacity), 4f * scale);
        draw.AddLine(
            p + new Vector2(halfWidth, 7f * scale),
            p + new Vector2(halfWidth, s.Y - 7f * scale),
            Color(popupDividerColor, opacity),
            MathF.Max(1f, scale));

        var leftLabel = popup == LeisureDockPopup.Travel ? "TELEPORT" : "CHARACTER SELECT";
        var rightLabel = popup == LeisureDockPopup.Travel ? "LIFESTREAM" : "PENUMBRA/GLAMOURER";
        DrawCenteredPopupText(draw, p, s.Y, halfWidth, 0, leftLabel, theme, opacity);
        DrawCenteredPopupText(draw, p, s.Y, halfWidth, 1, rightLabel, theme, opacity);
    }

    private static void DrawCenteredPopupText(
        ImDrawListPtr draw,
        Vector2 p,
        float height,
        float segmentWidth,
        int index,
        string label,
        ThemePalette theme,
        float opacity)
    {
        var textSize = ImGui.CalcTextSize(label);
        var x = p.X + segmentWidth * index + (segmentWidth - textSize.X) * 0.5f;
        draw.AddText(
            new Vector2(x, p.Y + (height - textSize.Y) * 0.5f),
            Color(theme.ResolvedDockText, opacity),
            label);
    }

    private static void DrawCenteredDockText(ImDrawListPtr draw, Vector2 p, float height, float segmentWidth, int index, string label, ThemePalette theme, float opacity)
    {
        var textSize = ImGui.CalcTextSize(label);
        var x = p.X + segmentWidth * index + (segmentWidth - textSize.X) * 0.5f;
        draw.AddText(new Vector2(x, p.Y + (height - textSize.Y) * 0.5f), Color(theme.ResolvedDockText, opacity), label);
    }

    private static void DrawNativeHoldoutFrames(Plugin plugin, ImDrawListPtr draw, Vector2 origin, ThemePalette theme, float scale, float opacity)
    {
        var job = plugin.GetJobAbbreviation();
        DrawNativeAddonFrame(plugin, draw, origin, theme, scale, opacity, new[] { $"_JobHud{job}0", $"JobHud{job}0" });
        DrawNativeAddonFrame(plugin, draw, origin, theme, scale, opacity, new[] { $"_JobHud{job}1", $"JobHud{job}1" });
        DrawNativeAddonFrame(plugin, draw, origin, theme, scale, opacity, new[] { $"_JobHud{job}2", $"JobHud{job}2" });
    }

    private static void DrawNativeAddonFrame(Plugin plugin, ImDrawListPtr draw, Vector2 origin, ThemePalette theme, float scale, float opacity, string[] candidateNames)
    {
        if (!plugin.NativeHudVisibility.TryGetVisibleAddonBounds(candidateNames, out var position, out var nativeSize)) return;
        var margin = new Vector2(7f, 7f) * scale;
        var p = origin + position - margin;
        var s = nativeSize + margin * 2f;
        draw.AddRect(p, p + s, Color(theme.Accent, opacity * 0.48f), 6f * scale, ImDrawFlags.None, 1.2f * scale);
        draw.AddLine(p, p + new Vector2(MathF.Min(82f * scale, s.X), 0f), Color(theme.AccentStrong, opacity * 0.92f), 2f * scale);
    }

    private static void DrawDockPanel(ImDrawListPtr draw, Vector2 p, Vector2 s, ThemePalette theme, float opacity, float rounding)
    {
        if (!theme.HasExtendedColors)
        {
            DrawGlassPanel(draw, p, s, theme, opacity, rounding);
            return;
        }

        opacity = Math.Clamp(opacity, 0f, 1f);
        var shadowOffset = new Vector2(3f, 4f);
        draw.AddRectFilled(
            p + shadowOffset,
            p + s + shadowOffset,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.34f)),
            rounding + 1f);
        draw.AddRectFilled(p, p + s, Color(theme.ResolvedDock, opacity), rounding);
        draw.AddRect(
            p,
            p + s,
            Color(theme.ResolvedDockBorder, opacity),
            rounding,
            ImDrawFlags.None,
            1f);
    }

    private static void DrawGlassPanel(ImDrawListPtr draw, Vector2 p, Vector2 s, ThemePalette theme, float opacity, float rounding)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
        var shadowOffset = new Vector2(3f, 4f);
        draw.AddRectFilled(p + shadowOffset, p + s + shadowOffset, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.34f)), rounding + 1f);
        draw.AddRectFilled(p, p + s, Color(theme.Panel, opacity * 0.94f), rounding);
        var glossHeight = MathF.Max(8f, s.Y * 0.44f);
        draw.AddRectFilled(p + new Vector2(1f), p + new Vector2(s.X - 1f, glossHeight), Color(theme.PanelAlt, opacity * 0.54f), MathF.Max(2f, rounding - 1f));
        DrawThemeGradient(
            draw,
            p + new Vector2(8f, 1f),
            p + new Vector2(MathF.Max(8f, s.X - 8f), 3f),
            theme,
            opacity * 0.86f);
        draw.AddLine(p + new Vector2(7f, glossHeight), p + new Vector2(MathF.Max(7f, s.X - 7f), glossHeight), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity * 0.06f)), 1f);
        draw.AddRect(p, p + s, Color(theme.Accent, opacity * 0.58f), rounding, ImDrawFlags.None, 1f);
        draw.AddRect(p + new Vector2(2f), p + s - new Vector2(2f), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity * 0.055f)), MathF.Max(1f, rounding - 2f), ImDrawFlags.None, 1f);
    }

    private static void DrawThemeGradient(
        ImDrawListPtr draw,
        Vector2 min,
        Vector2 max,
        ThemePalette theme,
        float opacity,
        bool vertical = false)
    {
        if (max.X <= min.X || max.Y <= min.Y)
            return;

        if (vertical)
        {
            var middleY = min.Y + (max.Y - min.Y) * 0.5f;
            draw.AddRectFilledMultiColor(
                min,
                new Vector2(max.X, middleY),
                Color(theme.GradientStart, opacity),
                Color(theme.GradientStart, opacity),
                Color(theme.GradientMid, opacity),
                Color(theme.GradientMid, opacity));
            draw.AddRectFilledMultiColor(
                new Vector2(min.X, middleY),
                max,
                Color(theme.GradientMid, opacity),
                Color(theme.GradientMid, opacity),
                Color(theme.GradientEnd, opacity),
                Color(theme.GradientEnd, opacity));
            return;
        }

        var middleX = min.X + (max.X - min.X) * 0.5f;
        draw.AddRectFilledMultiColor(
            min,
            new Vector2(middleX, max.Y),
            Color(theme.GradientStart, opacity),
            Color(theme.GradientMid, opacity),
            Color(theme.GradientMid, opacity),
            Color(theme.GradientStart, opacity));
        draw.AddRectFilledMultiColor(
            new Vector2(middleX, min.Y),
            max,
            Color(theme.GradientMid, opacity),
            Color(theme.GradientEnd, opacity),
            Color(theme.GradientEnd, opacity),
            Color(theme.GradientMid, opacity));
    }

    private static void DrawBar(ImDrawListPtr draw, Vector2 p, Vector2 s, float value, Vector4 fill, Vector4 background, float opacity)
    {
        value = Math.Clamp(value, 0f, 1f);
        draw.AddRectFilled(p + new Vector2(1f, 2f), p + s + new Vector2(1f, 2f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, opacity * 0.28f)), s.Y * 0.5f);
        draw.AddRectFilled(p, p + s, Color(background, opacity * 0.94f), s.Y * 0.5f);
        if (value > 0f)
        {
            var fillEnd = p + new Vector2(s.X * value, s.Y);
            draw.AddRectFilled(p, fillEnd, Color(fill, opacity), s.Y * 0.5f);
            draw.AddLine(p + new Vector2(2f, 1f), new Vector2(MathF.Max(p.X + 2f, fillEnd.X - 2f), p.Y + 1f), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, opacity * 0.24f)), 1f);
        }
    }

    private static void DrawArc(ImDrawListPtr draw, Vector2 center, float radius, float start, float end, uint color, float thickness)
    {
        if (end <= start) return;
        draw.PathClear();
        draw.PathArcTo(center, radius, start, end, 48);
        draw.PathStroke(color, ImDrawFlags.None, thickness);
    }

    private static void DrawArcTicks(ImDrawListPtr draw, Vector2 center, float radius, float start, float end, ThemePalette theme, float scale, float opacity)
    {
        const int ticks = 9;
        for (var i = 0; i <= ticks; i++)
        {
            var angle = start + (end - start) * (i / (float)ticks);
            var a = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius - 9f * scale);
            var b = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius + 9f * scale);
            draw.AddLine(a, b, Color(theme.Accent, opacity * 0.35f), i % 3 == 0 ? 2f * scale : 1f * scale);
        }
    }

    private static void DrawDiamond(ImDrawListPtr draw, Vector2 center, float radius, uint color)
        => draw.AddQuadFilled(center + new Vector2(0f, -radius), center + new Vector2(radius, 0f), center + new Vector2(0f, radius), center + new Vector2(-radius, 0f), color);

    private static uint Color(Vector4 color, float alpha)
        => ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, Math.Clamp(color.W * alpha, 0f, 1f)));

    private static string FormatRate(double value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000d:0.0}m",
        >= 1_000 => $"{value / 1_000d:0.0}k",
        _ => $"{value:0}",
    };

    private static string KeyLabel(int row, int col)
    {
        if (row == 0) return col < 10 ? ((col + 1) % 10).ToString() : col == 10 ? "-" : "=";
        var keys = new[] { "Q", "E", "R", "F", "1", "2", "3", "4", "Z", "X", "C", "V" };
        return row == 1 ? $"S+{keys[col]}" : $"C+{keys[col]}";
    }

    internal readonly record struct WorkStatusSlot(HudBounds IconBounds, Vector2 TextPosition);
}
