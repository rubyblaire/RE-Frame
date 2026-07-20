using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public enum ActorWidgetKind
{
    Party,
    EnemyList,
    Target,
    Focus,
}


public sealed unsafe class ActorInteractionWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly ActorWidgetKind kind;
    private readonly List<IGameObject?> actors = new(8);
    private float scale;

    public ActorInteractionWindow(Plugin plugin, ActorWidgetKind kind)
        : base($"RE:Frame {kind} Inputs###REFrameActorInputs{kind}",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        this.kind = kind;
        IsClickthrough = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0f;
    }

    public override bool DrawConditions()
    {
        if (!plugin.Configuration.ShowHudOverlay ||
            !plugin.Configuration.EnableHudMouseInteraction ||
            plugin.IsHudEditMode ||
            plugin.HotbarEditing.IsEnabled ||
            !Plugin.ClientState.IsLoggedIn ||
            Plugin.GameGui.GameUiHidden ||
            Plugin.ClientState.IsGPosing ||
            plugin.NativeContextMenus.IsAnyMenuOpen)
            return false;

        var mode = plugin.CurrentHudMode;
        return kind switch
        {
            ActorWidgetKind.Party => plugin.IsHudElementVisible(HudElementIds.Party, mode) && Plugin.PartyList.Length > 1,
            ActorWidgetKind.EnemyList => plugin.IsHudElementVisible(HudElementIds.EnemyList, mode) &&
                                         !HudModeProfileService.IsCalmMode(mode) &&
                                         plugin.EnemyList.Snapshot(8, engagedOnly: true).Count > 0,
            ActorWidgetKind.Target => plugin.IsHudElementVisible(HudElementIds.Target, mode) && !HudModeProfileService.IsCalmMode(mode) && Plugin.TargetManager.Target is not null,
            ActorWidgetKind.Focus => plugin.IsHudElementVisible(HudElementIds.Focus, mode) && !HudModeProfileService.IsCalmMode(mode) && Plugin.TargetManager.FocusTarget is not null,
            _ => false,
        };
    }

    public override void PreDraw()
    {
        actors.Clear();
        scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var canvas = HudCanvas.Current();
        var elementId = kind switch
        {
            ActorWidgetKind.Party => HudElementIds.Party,
            ActorWidgetKind.EnemyList => HudElementIds.EnemyList,
            ActorWidgetKind.Target => HudElementIds.Target,
            ActorWidgetKind.Focus => HudElementIds.Focus,
            _ => HudElementIds.Target,
        };
        var bounds = HudLayout.Resolve(plugin.Configuration, elementId, canvas.Origin, canvas.Size, plugin.CurrentHudMode);

        switch (kind)
        {
            case ActorWidgetKind.Party:
                for (var i = 0; i < Math.Min(8, Plugin.PartyList.Length); i++)
                {
                    var actor = Plugin.PartyList[i]?.GameObject;
                    actors.Add(actor is not null && actor.IsValid() ? actor : null);
                }
                break;
            case ActorWidgetKind.EnemyList:
                foreach (var enemy in plugin.EnemyList.Snapshot(
                             8,
                             excludeCurrentTarget: false,
                             excludeFocusTarget: false,
                             engagedOnly: true))
                    actors.Add(enemy);
                break;
            case ActorWidgetKind.Target:
                if (Plugin.TargetManager.Target is { } target && target.IsValid())
                {
                    actors.Add(target);
                    if (target is IBattleChara battleTarget && ResolveTargetsTarget(battleTarget) is { } targetOfTarget && targetOfTarget.IsValid())
                        actors.Add(targetOfTarget);
                }
                break;
            case ActorWidgetKind.Focus:
                if (Plugin.TargetManager.FocusTarget is { } focus && focus.IsValid())
                    actors.Add(focus);
                break;
        }

        Position = bounds.Position;
        PositionCondition = ImGuiCond.Always;
        Size = bounds.Size;
        SizeCondition = ImGuiCond.Always;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw()
    {
        if (actors.Count == 0)
            return;

        if (kind == ActorWidgetKind.Target)
        {
            DrawTargetButtons();
            return;
        }

        if (kind == ActorWidgetKind.Focus)
        {
            if (actors[0] is { } actor)
                DrawActorButton(actor, Vector2.Zero, ImGui.GetWindowSize(), 0);
            return;
        }

        if (kind == ActorWidgetKind.Party)
        {
            DrawPartyButtons();
            return;
        }

        var gap = 4f * scale;
        var rowHeight = MathF.Max(24f, (ImGui.GetWindowSize().Y - gap * (actors.Count - 1)) / actors.Count);
        for (var i = 0; i < actors.Count; i++)
        {
            var local = new Vector2(0f, i * (rowHeight + gap));
            if (actors[i] is { } actor)
                DrawActorButton(actor, local, new Vector2(ImGui.GetWindowSize().X, rowHeight), i);
        }
    }

    private void DrawPartyButtons()
    {
        var gap = 4f * scale;
        var rowHeight = MathF.Max(24f, (ImGui.GetWindowSize().Y - gap * (actors.Count - 1)) / actors.Count);
        for (var i = 0; i < actors.Count; i++)
        {
            if (actors[i] is not { } actor)
                continue;

            var rowLocal = new Vector2(0f, i * (rowHeight + gap));
            var statusBandHeight = actor is IBattleChara battle
                ? DrawPartyStatusButtons(battle, rowLocal, ImGui.GetWindowSize().X, rowHeight, i)
                : 0f;

            var actorHeight = MathF.Max(0f, rowHeight - statusBandHeight);
            if (actorHeight > 1f)
            {
                DrawActorButton(
                    actor,
                    rowLocal + new Vector2(0f, statusBandHeight),
                    new Vector2(ImGui.GetWindowSize().X, actorHeight),
                    i);
            }
        }
    }

    private void DrawTargetButtons()
    {
        if (actors.Count == 0 || actors[0] is not { } primaryTarget)
            return;

        var windowSize = ImGui.GetWindowSize();
        var statusBandHeight = primaryTarget is IBattleChara battleTarget
            ? DrawTargetStatusButtons(battleTarget, windowSize)
            : 0f;


        var lowerTop = windowSize.Y * 0.64f;
        lowerTop = MathF.Min(lowerTop, windowSize.Y - 18f * scale);
        lowerTop = Math.Clamp(lowerTop, 0f, windowSize.Y);
        var splitX = Math.Clamp(windowSize.X * 0.56f, 0f, windowSize.X);


        var primaryTopHeight = MathF.Max(0f, lowerTop - statusBandHeight);
        if (primaryTopHeight > 0f)
        {
            DrawActorButton(
                primaryTarget,
                new Vector2(0f, statusBandHeight),
                new Vector2(windowSize.X, primaryTopHeight),
                0);
        }

        var lowerHeight = MathF.Max(0f, windowSize.Y - lowerTop);
        if (splitX > 0f && lowerHeight > 0f)
            DrawActorButton(primaryTarget, new Vector2(0f, lowerTop), new Vector2(splitX, lowerHeight), 1);

        if (actors.Count > 1 && actors[1] is { } targetOfTarget && windowSize.X - splitX > 0f && lowerHeight > 0f)
        {
            DrawActorButton(
                targetOfTarget,
                new Vector2(splitX, lowerTop),
                new Vector2(windowSize.X - splitX, lowerHeight),
                2,
                "Target's target");
        }
    }

    private float DrawPartyStatusButtons(
        IBattleChara actor,
        Vector2 rowLocal,
        float rowWidth,
        float rowHeight,
        int rowIndex)
    {
        if (rowHeight < 30f * scale)
            return 0f;

        var iconSize = Math.Clamp(rowHeight * 0.25f, 10f * scale, 18f * scale);
        var gap = MathF.Max(1f, 2f * scale);
        var top = rowLocal.Y + 3f * scale;
        var left = rowLocal.X + 6f * scale;
        var right = rowLocal.X + rowWidth - 6f * scale;
        var maxPerSide = Math.Clamp((int)((rowWidth * 0.43f + gap) / (iconSize + gap)), 1, 7);
        var buffCount = 0;
        var debuffCount = 0;
        var statusIndex = 0;

        foreach (var status in actor.StatusList)
        {
            if (status.StatusId == 0 || !TryGetStatusTooltipData(status.StatusId, status.Param, out var data))
                continue;

            Vector2 iconPosition;
            if (data.IsDebuff)
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

            DrawStatusTooltipButton(
                actor,
                status.StatusId,
                status.RemainingTime,
                data,
                iconPosition,
                iconSize,
                $"party-{rowIndex}-{statusIndex++}");
        }

        return buffCount > 0 || debuffCount > 0 ? iconSize + 4f * scale : 0f;
    }

    private float DrawTargetStatusButtons(IBattleChara actor, Vector2 frameSize)
    {
        var iconSize = Math.Clamp(frameSize.Y * 0.19f, 14f * scale, 21f * scale);
        var gap = MathF.Max(1f, 2f * scale);
        var top = 4f * scale;
        var left = 12f * scale;
        var right = frameSize.X - 12f * scale;
        var maxPerSide = Math.Clamp((int)((frameSize.X * 0.44f + gap) / (iconSize + gap)), 1, 12);
        var buffCount = 0;
        var debuffCount = 0;
        var statusIndex = 0;

        foreach (var status in actor.StatusList)
        {
            if (status.StatusId == 0 || !TryGetStatusTooltipData(status.StatusId, status.Param, out var data))
                continue;

            Vector2 iconPosition;
            if (data.IsDebuff)
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

            DrawStatusTooltipButton(
                actor,
                status.StatusId,
                status.RemainingTime,
                data,
                iconPosition,
                iconSize,
                $"target-{statusIndex++}");
        }

        return buffCount > 0 || debuffCount > 0 ? iconSize + 6f * scale : 0f;
    }

    private void DrawStatusTooltipButton(
        IGameObject owner,
        uint statusId,
        float remainingTime,
        StatusTooltipData data,
        Vector2 local,
        float iconSize,
        string uniqueSuffix)
    {
        ImGui.SetCursorPos(local);
        ImGui.InvisibleButton($"##reframe-status-{uniqueSuffix}-{owner.GameObjectId}-{statusId}", new Vector2(iconSize));

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            plugin.HudTargeting.SetTarget(owner);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && plugin.Configuration.RightClickOpensNativeContextMenu)
            plugin.HudTargeting.OpenNativeContextMenu(owner);

        if (!ImGui.IsItemHovered())
            return;

        if (plugin.Configuration.EnableMouseoverTargeting)
            plugin.HudTargeting.TouchMouseover(owner);

        ImGui.BeginTooltip();
        var theme = plugin.CurrentTheme;
        ImGui.TextColored(data.IsDebuff ? theme.Danger : theme.Success, data.Name);
        if (!string.IsNullOrWhiteSpace(data.Description))
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 360f * scale);
            ImGui.TextWrapped(data.Description);
            ImGui.PopTextWrapPos();
        }

        if (remainingTime > 0.05f)
            ImGui.TextDisabled($"Remaining: {FormatStatusDuration(remainingTime)}");
        ImGui.EndTooltip();
    }

    private static bool TryGetStatusTooltipData(uint statusId, uint statusParameter, out StatusTooltipData data)
    {
        data = new StatusTooltipData(string.Empty, string.Empty, false);
        if (!StatusDisplayService.TryResolve(statusId, statusParameter, out var display))
            return false;

        data = new StatusTooltipData(display.Name, display.Description, display.IsDebuff);
        return true;
    }

    private static string FormatStatusDuration(float seconds)
    {
        seconds = MathF.Max(0f, seconds);
        if (seconds >= 86400f)
        {
            var days = (int)(seconds / 86400f);
            var hours = (int)(seconds % 86400f / 3600f);
            return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        }
        if (seconds >= 3600f)
            return $"{(int)(seconds / 3600f)}h {(int)(seconds % 3600f / 60f)}m";
        if (seconds >= 60f)
            return $"{(int)(seconds / 60f)}m {(int)(seconds % 60f)}s";
        return $"{MathF.Ceiling(seconds):0}s";
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
            Plugin.Log.Debug(ex, "RE:Frame could not resolve the target's target for mouse interaction.");
            return null;
        }
    }

    private void DrawActorButton(IGameObject actor, Vector2 local, Vector2 size, int index, string? roleLabel = null)
    {
        ImGui.SetCursorPos(local);
        ImGui.InvisibleButton($"##reframe-actor-{kind}-{index}-{actor.GameObjectId}", size);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        if (hovered && plugin.Configuration.EnableMouseoverTargeting)
            plugin.HudTargeting.TouchMouseover(actor);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            plugin.HudTargeting.SetTarget(actor);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && plugin.Configuration.RightClickOpensNativeContextMenu)
            plugin.HudTargeting.OpenNativeContextMenu(actor);

        if (hovered || held)
        {
            var theme = plugin.CurrentTheme;
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var alpha = held ? 0.34f : 0.18f;
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, alpha)), 5f * scale);
            draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(theme.AccentStrong.X, theme.AccentStrong.Y, theme.AccentStrong.Z, 0.92f)), 5f * scale, ImDrawFlags.None, MathF.Max(1f, 1.5f * scale));
        }

        if (!hovered)
            return;


        if (kind == ActorWidgetKind.Party || plugin.AdaptiveState.EffectiveMode == UiMode.RaidReady)
            return;

        ImGui.BeginTooltip();
        if (!string.IsNullOrWhiteSpace(roleLabel))
            ImGui.TextDisabled(roleLabel);
        ImGui.TextUnformatted(actor.Name.ToString());
        if (plugin.Configuration.EnableMouseoverTargeting)
            ImGui.TextDisabled("Hover: native <mo> target");
        ImGui.TextDisabled("Left-click: target");
        if (plugin.Configuration.RightClickOpensNativeContextMenu)
            ImGui.TextDisabled("Right-click: open FFXIV character menu");
        ImGui.EndTooltip();
    }

    private readonly record struct StatusTooltipData(string Name, string Description, bool IsDebuff);

    public void Dispose() { }
}
