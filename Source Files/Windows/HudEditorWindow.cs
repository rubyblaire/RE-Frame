using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed partial class HudEditorWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string selectedId = HudElementIds.ActionBarOne;
    private string selectedNativeJobGaugeComponent = string.Empty;
    private bool dirty;

    public HudEditorWindow(Plugin plugin)
        : base("Edit RE:Frame HUD###REFrameHudEditor",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        IsClickthrough = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0f;
    }


    public void BeginSkillEdit() => plugin.SetBarEditMode(true);

    private void EndSkillEdit() => plugin.SetBarEditMode(false);

    public override bool DrawConditions()
        => plugin.IsHudEditMode && Plugin.ClientState.IsLoggedIn && !Plugin.GameGui.GameUiHidden;

    public override void PreDraw()
    {
        var hudCanvas = HudCanvas.Current();
        Position = hudCanvas.Origin;
        PositionCondition = ImGuiCond.Always;
        Size = hudCanvas.Size;
        SizeCondition = ImGuiCond.Always;
        ImGui.SetNextWindowPos(hudCanvas.Origin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(hudCanvas.Size, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw()
    {


        var hudCanvas = HudCanvas.Current();
        var origin = hudCanvas.Origin;
        var canvas = hudCanvas.Size;
        var draw = ImGui.GetWindowDrawList();
        var theme = plugin.CurrentTheme;

        draw.PushClipRect(hudCanvas.Origin, hudCanvas.Max, false);
        try
        {
            draw.AddRectFilled(origin, origin + canvas, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.18f)));
            PrepareLayoutStudioFrame(origin, canvas, draw);
            DrawGrid(draw, origin, canvas);

            foreach (var id in plugin.GetEditableHudElementIds())
            {
                if (!plugin.ShouldExposeHudElement(id))
                    continue;
                DrawElementEditor(id, origin, canvas, draw);
            }


            DrawNativeJobGaugeEditor(origin, canvas, draw);
            DrawNativeStatusEffectsEditor(origin, canvas, draw);
            DrawNativeQuestElementEditor(HudElementIds.NativeScenarioGuide, origin, canvas, draw);
            DrawNativeQuestElementEditor(HudElementIds.NativeQuestList, origin, canvas, draw);
            DrawNativeQuestElementEditor(HudElementIds.NativeDutyInfo, origin, canvas, draw);

            DrawSnapGuides(draw, origin, canvas);
            DrawElementPanel(origin, canvas);
            HandleKeyboardNudge(origin, canvas);
            FinishLayoutStudioFrame(origin, canvas, draw);
        }
        finally
        {
            draw.PopClipRect();
        }

        if (dirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            plugin.SaveConfiguration();
            plugin.NativeHudVisibility.RefreshNow();
            dirty = false;
        }
    }


    private void HandleKeyboardNudge(Vector2 origin, Vector2 canvas)
    {
        if (ImGui.GetIO().WantTextInput) return;
        var delta = Vector2.Zero;
        var step = ImGui.GetIO().KeyShift ? 10f : 1f;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, true)) delta.X -= step;
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow, true)) delta.X += step;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true)) delta.Y -= step;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true)) delta.Y += step;
        if (delta == Vector2.Zero) return;

        if (selectedId == HudElementIds.NativeJobGauge)
        {
            var componentKey = selectedNativeJobGaugeComponent;
            if (string.IsNullOrWhiteSpace(componentKey))
                return;

            plugin.LayoutHistory.Record("Nudge native job gauge component", () =>
                plugin.NativeHudVisibility.MoveVisibleJobGaugeComponent(plugin.GetJobAbbreviation(), componentKey, delta));
            dirty = true;
        }
        else if (selectedId == HudElementIds.NativeStatusEffects)
        {
            plugin.LayoutHistory.Record("Nudge native status effects", () =>
                plugin.NativeHudVisibility.MoveVisibleStatusEffects(delta));
            dirty = true;
        }
        else if (selectedId is HudElementIds.NativeScenarioGuide or HudElementIds.NativeQuestList or HudElementIds.NativeDutyInfo)
        {
            plugin.LayoutHistory.Record($"Nudge {plugin.GetHudElementLabel(selectedId)}", () =>
                plugin.NativeHudVisibility.MoveVisibleQuestElement(selectedId, delta));
            dirty = true;
        }
        else if (HasPrimaryCustomSelection)
        {
            plugin.LayoutHistory.Record("Nudge selected elements", () =>
            {
                var mode = plugin.HudEditPreviewMode;
                foreach (var id in SelectedCustomElements())
                {
                    if (IsLocked(id))
                        continue;
                    var bounds = HudLayout.Resolve(plugin.Configuration, id, origin, canvas, mode);
                    HudLayout.Store(plugin.Configuration, id, new HudBounds(bounds.Position + delta, bounds.Size), origin, canvas, mode);
                }
            });
            dirty = true;
        }
    }

    private void DrawGrid(ImDrawListPtr draw, Vector2 origin, Vector2 canvas)
    {
        if (!plugin.Configuration.ShowHudEditorGrid)
            return;
        var spacing = Math.Clamp(plugin.Configuration.HudEditorGridSize, 4f, 64f);
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.045f));
        for (var x = origin.X; x <= origin.X + canvas.X; x += spacing)
            draw.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y + canvas.Y), color);
        for (var y = origin.Y; y <= origin.Y + canvas.Y; y += spacing)
            draw.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + canvas.X, y), color);
    }

    private void DrawElementEditor(string id, Vector2 origin, Vector2 canvas, ImDrawListPtr draw)
    {
        var mode = plugin.HudEditPreviewMode;
        var bounds = HudLayout.Resolve(plugin.Configuration, id, origin, canvas, mode);
        var visible = plugin.IsHudElementVisible(id, mode);
        var selected = selectedIds.Contains(id);
        var primary = selected && string.Equals(selectedId, id, StringComparison.OrdinalIgnoreCase);
        var locked = IsLocked(id);
        var accent = primary ? plugin.CurrentTheme.AccentStrong : plugin.CurrentTheme.Accent;
        var alpha = primary ? 0.98f : selected ? 0.86f : visible ? 0.68f : 0.52f;
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, alpha));
        var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, selected ? 0.11f : visible ? 0.050f : 0.040f));

        draw.AddRectFilled(bounds.Position, bounds.Position + bounds.Size, fill, 5f);
        draw.AddRect(bounds.Position, bounds.Position + bounds.Size, color, 5f, ImDrawFlags.None, primary ? 3f : selected ? 2f : 1.2f);
        draw.AddRectFilled(bounds.Position, bounds.Position + new Vector2(MathF.Min(bounds.Size.X, 180f), 22f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.72f)), 5f);
        var elementLabel = visible ? plugin.GetHudElementLabel(id) : $"{plugin.GetHudElementLabel(id)}  [HIDDEN]";
        if (primary && selectedIds.Count > 1) elementLabel += "  [PRIMARY]";
        if (locked) elementLabel += "  [LOCKED]";
        draw.AddText(bounds.Position + new Vector2(7f, 3f), color, elementLabel);

        var grip = MathF.Min(22f, MathF.Max(14f, MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.18f));
        var bodySize = Vector2.Max(new Vector2(1f), bounds.Size - new Vector2(grip));
        ImGui.SetCursorPos(bounds.Position - origin);
        ImGui.InvisibleButton($"##hud-move-{id}", bodySize);
        if (ImGui.IsItemHovered())
            anyElementHovered = true;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectCustomElement(id);
            BeginPointerGesture(selectedIds.Count > 1 ? "Move selected elements" : $"Move {plugin.GetHudElementLabel(id)}");
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && !locked)
            MoveSelection(ImGui.GetIO().MouseDelta, origin, canvas);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(locked ? $"{plugin.GetHudElementLabel(id)} is locked in this dock." : $"Drag to move {plugin.GetHudElementLabel(id)}. Shift-click to multi-select.");

        var gripMin = bounds.Position + bounds.Size - new Vector2(grip);
        draw.AddTriangleFilled(
            bounds.Position + bounds.Size,
            bounds.Position + new Vector2(bounds.Size.X - grip, bounds.Size.Y),
            bounds.Position + new Vector2(bounds.Size.X, bounds.Size.Y - grip),
            color);
        ImGui.SetCursorPos(gripMin - origin);
        ImGui.InvisibleButton($"##hud-resize-{id}", new Vector2(grip));
        if (ImGui.IsItemHovered())
            anyElementHovered = true;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectCustomElement(id);
            BeginPointerGesture($"Resize {plugin.GetHudElementLabel(id)}");
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && !locked)
        {
            var minimum = HudLayout.MinimumSize(plugin.Configuration, id);
            var requested = Vector2.Max(minimum, bounds.Size + ImGui.GetIO().MouseDelta);
            var nextSize = Vector2.Max(minimum, ApplyResizeSnapping(id, bounds, requested, origin, canvas));
            HudLayout.Store(plugin.Configuration, id, new HudBounds(bounds.Position, nextSize), origin, canvas, mode);
            if (id == HudElementIds.CombatHalo)
                plugin.Configuration.HaloRadius = MathF.Min(nextSize.X, nextSize.Y) * 0.34f;
            MarkPointerGestureChanged();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(locked ? $"{plugin.GetHudElementLabel(id)} is locked in this dock." : $"Drag to resize {plugin.GetHudElementLabel(id)}");
    }


    private void DrawNativeJobGaugeEditor(Vector2 origin, Vector2 canvas, ImDrawListPtr draw)
    {
        var job = plugin.GetJobAbbreviation();
        var components = plugin.NativeHudVisibility.GetVisibleJobGaugeComponents(job);
        if (components.Count == 0)
            return;

        if (selectedId == HudElementIds.NativeJobGauge &&
            (string.IsNullOrWhiteSpace(selectedNativeJobGaugeComponent) ||
             !components.Any(component => string.Equals(component.Key, selectedNativeJobGaugeComponent, StringComparison.OrdinalIgnoreCase))))
        {
            selectedNativeJobGaugeComponent = components[0].Key;
        }

        foreach (var component in components)
        {
            var padding = new Vector2(9f, 9f);
            var bounds = new HudBounds(
                component.Position - padding,
                Vector2.Max(new Vector2(80f, 52f), component.Size + padding * 2f));
            var selected = selectedId == HudElementIds.NativeJobGauge &&
                           string.Equals(selectedNativeJobGaugeComponent, component.Key, StringComparison.OrdinalIgnoreCase);
            var accent = selected ? plugin.CurrentTheme.AccentStrong : plugin.CurrentTheme.Accent;
            var alpha = selected ? 0.98f : 0.72f;
            var color = ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, alpha));
            var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, selected ? 0.10f : 0.045f));

            draw.AddRectFilled(bounds.Position, bounds.Position + bounds.Size, fill, 5f);
            draw.AddRect(bounds.Position, bounds.Position + bounds.Size, color, 5f, ImDrawFlags.None, selected ? 2.5f : 1.5f);
            draw.AddRectFilled(bounds.Position, bounds.Position + new Vector2(MathF.Min(bounds.Size.X, 230f), 22f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.78f)), 5f);
            draw.AddText(bounds.Position + new Vector2(7f, 3f), color, $"{component.Label}  ·  {job}");

            ImGui.SetCursorPos(bounds.Position - origin);
            ImGui.InvisibleButton($"##hud-move-native-job-gauge-{component.Key}", bounds.Size);
            if (ImGui.IsItemHovered())
                anyElementHovered = true;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                SelectNativeJobGaugeComponent(component.Key);
                BeginPointerGesture($"Move {component.Label}");
            }
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = ApplyMoveSnapping(
                    $"{HudElementIds.NativeJobGauge}:{component.Key}",
                    bounds,
                    ImGui.GetIO().MouseDelta,
                    origin,
                    canvas);
                if (plugin.NativeHudVisibility.MoveVisibleJobGaugeComponent(job, component.Key, delta))
                    MarkPointerGestureChanged();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Drag to move {component.Label} independently. Gauge scale remains controlled by FFXIV.");
        }
    }

    private void DrawNativeStatusEffectsEditor(Vector2 origin, Vector2 canvas, ImDrawListPtr draw)
    {
        if (!plugin.NativeHudVisibility.TryGetVisibleStatusEffectsBounds(out var nativePosition, out var nativeSize)) return;
        var bounds = new HudBounds(nativePosition - new Vector2(7f), Vector2.Max(new Vector2(120f, 42f), nativeSize + new Vector2(14f)));
        var selected = selectedId == HudElementIds.NativeStatusEffects;
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(plugin.CurrentTheme.AccentStrong.X, plugin.CurrentTheme.AccentStrong.Y, plugin.CurrentTheme.AccentStrong.Z, selected ? .98f : .78f));
        draw.AddRect(bounds.Position, bounds.Position + bounds.Size, color, 5f, ImDrawFlags.None, selected ? 2.5f : 1.5f);
        draw.AddText(bounds.Position + new Vector2(6f, 3f), color, "Native Status Effects");

        var grip = 18f;
        var bodySize = Vector2.Max(new Vector2(1f), bounds.Size - new Vector2(grip));
        ImGui.SetCursorPos(bounds.Position - origin);
        ImGui.InvisibleButton("##move-native-status", bodySize);
        if (ImGui.IsItemHovered()) anyElementHovered = true;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) { SelectNativeElement(HudElementIds.NativeStatusEffects); BeginPointerGesture("Move native status effects"); }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ApplyMoveSnapping(HudElementIds.NativeStatusEffects, bounds, ImGui.GetIO().MouseDelta, origin, canvas);
            if (plugin.NativeHudVisibility.MoveVisibleStatusEffects(delta)) MarkPointerGestureChanged();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag to move your native player status effects.");

        var gripMin = bounds.Position + bounds.Size - new Vector2(grip);
        draw.AddTriangleFilled(gripMin + new Vector2(grip, 0f), gripMin + new Vector2(grip, grip), gripMin + new Vector2(0f, grip), color);
        ImGui.SetCursorPos(gripMin - origin);
        ImGui.InvisibleButton("##resize-native-status", new Vector2(grip));
        if (ImGui.IsItemHovered()) anyElementHovered = true;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) { SelectNativeElement(HudElementIds.NativeStatusEffects); BeginPointerGesture("Resize native status effects"); }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var requested = bounds.Size + ImGui.GetIO().MouseDelta;
            var snapped = ApplyResizeSnapping(HudElementIds.NativeStatusEffects, bounds, requested, origin, canvas);
            if (plugin.NativeHudVisibility.ResizeVisibleStatusEffects(snapped - bounds.Size)) MarkPointerGestureChanged();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag to resize status effects proportionally.");
    }


    private void DrawNativeQuestElementEditor(string elementId, Vector2 origin, Vector2 canvas, ImDrawListPtr draw)
    {
        if (!plugin.NativeHudVisibility.TryGetVisibleQuestElementBounds(elementId, out var nativePosition, out var nativeSize))
            return;

        var padding = new Vector2(7f, 7f);
        var bounds = new HudBounds(
            nativePosition - padding,
            Vector2.Max(new Vector2(130f, 44f), nativeSize + padding * 2f));
        var selected = selectedId == elementId;
        var accent = selected ? plugin.CurrentTheme.AccentStrong : plugin.CurrentTheme.Accent;
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, selected ? 0.98f : 0.78f));
        var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, selected ? 0.10f : 0.04f));

        draw.AddRectFilled(bounds.Position, bounds.Position + bounds.Size, fill, 5f);
        draw.AddRect(bounds.Position, bounds.Position + bounds.Size, color, 5f, ImDrawFlags.None, selected ? 2.5f : 1.5f);
        draw.AddRectFilled(
            bounds.Position,
            bounds.Position + new Vector2(MathF.Min(bounds.Size.X, 210f), 22f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.76f)),
            5f);
        draw.AddText(bounds.Position + new Vector2(7f, 3f), color, plugin.GetHudElementLabel(elementId));

        var grip = MathF.Min(22f, MathF.Max(16f, MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.18f));
        var bodySize = Vector2.Max(new Vector2(1f), bounds.Size - new Vector2(grip));
        ImGui.SetCursorPos(bounds.Position - origin);
        ImGui.InvisibleButton($"##move-{elementId}", bodySize);
        if (ImGui.IsItemHovered()) anyElementHovered = true;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectNativeElement(elementId);
            BeginPointerGesture($"Move {plugin.GetHudElementLabel(elementId)}");
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ApplyMoveSnapping(elementId, bounds, ImGui.GetIO().MouseDelta, origin, canvas);
            if (plugin.NativeHudVisibility.MoveVisibleQuestElement(elementId, delta))
                MarkPointerGestureChanged();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Drag to move {plugin.GetHudElementLabel(elementId)}.");

        var gripMin = bounds.Position + bounds.Size - new Vector2(grip);
        draw.AddTriangleFilled(
            bounds.Position + bounds.Size,
            bounds.Position + new Vector2(bounds.Size.X - grip, bounds.Size.Y),
            bounds.Position + new Vector2(bounds.Size.X, bounds.Size.Y - grip),
            color);
        ImGui.SetCursorPos(gripMin - origin);
        ImGui.InvisibleButton($"##resize-{elementId}", new Vector2(grip));
        if (ImGui.IsItemHovered()) anyElementHovered = true;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectNativeElement(elementId);
            BeginPointerGesture($"Resize {plugin.GetHudElementLabel(elementId)}");
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var requested = bounds.Size + ImGui.GetIO().MouseDelta;
            var snapped = ApplyResizeSnapping(elementId, bounds, requested, origin, canvas);
            if (plugin.NativeHudVisibility.ResizeVisibleQuestElement(elementId, snapped - bounds.Size))
                MarkPointerGestureChanged();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Drag to resize {plugin.GetHudElementLabel(elementId)} proportionally.");
    }

    private void DrawElementPanel(Vector2 origin, Vector2 canvas)
    {
        const float panelWidth = 350f;
        var panelHeight = MathF.Min(800f, MathF.Max(440f, canvas.Y - 36f));
        var panelSize = new Vector2(panelWidth, panelHeight);
        var panelPosition = ResolveEditorChromePosition(
            plugin.Configuration.HudEditorPanelX,
            plugin.Configuration.HudEditorPanelY,
            canvas,
            panelSize,
            new Vector2(canvas.X - panelWidth - 14f, 18f));

        ImGui.SetCursorPos(panelPosition);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.Panel, 0.91f));
        if (ImGui.BeginChild("##hud-editor-elements", panelSize, true))
        {
            editorPanelHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
            DrawEditorChromeDragHandle(
                "##hud-editor-panel-drag",
                "::  EDIT HUD",
                new Vector2(ImGui.GetContentRegionAvail().X, 30f),
                panelPosition,
                panelSize,
                canvas,
                next =>
                {
                    plugin.Configuration.HudEditorPanelX = next.X / MathF.Max(1f, canvas.X);
                    plugin.Configuration.HudEditorPanelY = next.Y / MathF.Max(1f, canvas.Y);
                });

            ImGui.TextDisabled("Shift-click to multi-select. Drag frames directly; use the corner grip to resize. Arrow keys nudge 1 px; Shift + Arrow nudges 10 px.");
            ImGui.Spacing();
            DrawHistoryControls();
            ImGui.Spacing();

            ImGui.TextUnformatted("Editing dock");
            DrawDockSelector();
            ImGui.TextDisabled("Each frame keeps its own visibility and positions. Saving a preset includes Leisure, Roleplay, Quest, Raid, and Work together.");
            DrawDockCopyControls();
            ImGui.Spacing();

            DrawHotbarEditingToggle();
            {
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var resetSelectedWidth = MathF.Max(128f, availableWidth * 0.56f);
                var resetDockWidth = MathF.Max(84f, availableWidth - resetSelectedWidth - ImGui.GetStyle().ItemSpacing.X);
                if (string.IsNullOrWhiteSpace(selectedId))
                    ImGui.BeginDisabled();
                if (ImGui.Button("Reset Selected", new Vector2(resetSelectedWidth, 30f)))
                {
                    plugin.LayoutHistory.Record("Reset selected element", () =>
                    {
                        if (selectedId == HudElementIds.NativeJobGauge && !string.IsNullOrWhiteSpace(selectedNativeJobGaugeComponent))
                            plugin.NativeHudVisibility.ResetJobGaugeComponentPosition(plugin.GetJobAbbreviation(), selectedNativeJobGaugeComponent);
                        else if (selectedId == HudElementIds.NativeStatusEffects)
                            plugin.NativeHudVisibility.ResetStatusEffectsPosition();
                        else if (selectedId is HudElementIds.NativeScenarioGuide or HudElementIds.NativeQuestList or HudElementIds.NativeDutyInfo)
                            plugin.NativeHudVisibility.ResetQuestElementPlacement(selectedId);
                        else if (!string.IsNullOrWhiteSpace(selectedId))
                            HudModeProfileService.ResetElement(plugin.Configuration, plugin.HudEditPreviewMode, selectedId);
                    });
                    plugin.SaveConfiguration();
                    plugin.NativeHudVisibility.RefreshNow();
                }
                if (string.IsNullOrWhiteSpace(selectedId))
                    ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.Button("Reset Dock", new Vector2(resetDockWidth, 30f)))
                {
                    plugin.LayoutRecovery.Create("Before Dock Reset");
                    plugin.LayoutHistory.Record("Reset dock", () =>
                        HudModeProfileService.ResetMode(plugin.Configuration, plugin.HudEditPreviewMode));
                    plugin.SaveConfiguration();
                    plugin.NativeHudVisibility.RefreshNow();
                }
                if (ImGui.Button("RESET ENTIRE HUD", new Vector2(-1f, 28f)))
                {
                    plugin.LayoutRecovery.Create("Before Full HUD Reset");
                    plugin.LayoutHistory.Record("Reset entire HUD", () =>
                    {
                        HudLayout.ResetAll(plugin.Configuration);
                        plugin.Configuration.HudModeProfiles.Clear();
                        HudModeProfileService.EnsureCollections(plugin.Configuration);
                        plugin.NativeHudVisibility.ResetAllJobGaugePositions();
                        plugin.NativeHudVisibility.ResetStatusEffectsPosition();
                        plugin.NativeHudVisibility.ResetAllQuestElementPlacements();
                    });
                    plugin.SaveConfiguration();
                    plugin.NativeHudVisibility.RefreshNow();
                }
            }

            if (ImGui.Button("SAVE & LOCK HUD", new Vector2(-1f, 32f)))
            {
                plugin.LayoutRecovery.Create("Manual Editor Save");
                plugin.SetHudEditMode(false);
            }

            var hasActiveJobPreset = plugin.HudPresets.TryGetActiveForCurrentJob(out var activeJob, out var activePresetName);
            if (!hasActiveJobPreset)
                ImGui.BeginDisabled();
            if (ImGui.Button("UPDATE ACTIVE JOB PRESET & LOCK", new Vector2(-1f, 32f)))
            {
                if (plugin.HudPresets.UpdateActiveForCurrentJob(out var message))
                {
                    Plugin.ChatGui.Print(message);
                    plugin.SetHudEditMode(false);
                }
                else
                {
                    Plugin.ChatGui.PrintError(message);
                }
            }
            if (!hasActiveJobPreset)
                ImGui.EndDisabled();

            if (hasActiveJobPreset)
            {
                ImGui.TextDisabled($"Active preset: {activeJob} • {activePresetName}");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Overwrite this active job preset with all five dock layouts, their visibility choices, scale, and presentation settings.");
            }
            else
            {
                var currentJob = plugin.GetJobAbbreviation();
                ImGui.TextDisabled($"No active {currentJob} job preset. Save or activate one on the Job Presets page first.");
            }

            ImGui.Spacing();
            ImGui.Separator();
            DrawLayoutStudioControls(origin, canvas);
            ImGui.Spacing();
            ImGui.Separator();
            DrawStandardElementControls();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private bool DrawHotbarEditingToggle()
    {
        ImGui.TextUnformatted("Action buttons and keybinds");
        if (ImGui.Button("EDIT BARS & KEYS  •  /ref bars", new Vector2(-1f, 36f)))
            plugin.SetBarEditMode(true);
        ImGui.TextDisabled("Closes Layout Studio and opens the isolated bar workspace. Use Actions to arrange supported buttons or Keybinds to assign direct primary and secondary keys to combat, Pet Bar, and utility slots.");
        return false;
    }

    private void DrawStandardElementControls()
    {
        if (!ImGui.CollapsingHeader("Visibility and Selection", ImGuiTreeNodeFlags.DefaultOpen))
            return;
        ImGui.TextDisabled("Select, show, hide, move, and resize.");
        ImGui.Separator();
        foreach (var id in plugin.GetEditableHudElementIds())
        {
            if (!plugin.ShouldExposeHudElement(id))
                continue;

            var visible = plugin.IsHudElementVisible(id, plugin.HudEditPreviewMode);
            if (ImGui.Checkbox($"##visible-{id}", ref visible))
            {
                var nextVisible = visible;
                plugin.LayoutHistory.Record("Change element visibility", () =>
                    plugin.SetHudElementVisible(id, nextVisible, plugin.HudEditPreviewMode));
                dirty = false;
            }
            ImGui.SameLine();
            var selected = selectedIds.Contains(id);
            if (selected) ImGui.PushStyleColor(ImGuiCol.Text, plugin.CurrentTheme.AccentStrong);
            if (ImGui.Selectable(plugin.GetHudElementLabel(id), selected))
                SelectCustomElement(id);
            if (selected) ImGui.PopStyleColor();
        }

        var job = plugin.GetJobAbbreviation();
        var nativeGaugeComponents = plugin.NativeHudVisibility.GetVisibleJobGaugeComponents(job);
        if (nativeGaugeComponents.Count == 0)
        {
            var nativeGaugeVisible = false;
            ImGui.BeginDisabled();
            ImGui.Checkbox("##visible-native-job-gauge-none", ref nativeGaugeVisible);
            ImGui.SameLine();
            ImGui.TextDisabled("Native Job Gauge (not currently visible)");
            ImGui.EndDisabled();
        }
        else
        {
            foreach (var component in nativeGaugeComponents)
            {
                var nativeGaugeVisible = true;
                ImGui.BeginDisabled();
                ImGui.Checkbox($"##visible-native-job-gauge-{component.Key}", ref nativeGaugeVisible);
                ImGui.EndDisabled();
                ImGui.SameLine();
                var nativeGaugeSelected = selectedId == HudElementIds.NativeJobGauge &&
                                          string.Equals(selectedNativeJobGaugeComponent, component.Key, StringComparison.OrdinalIgnoreCase);
                if (nativeGaugeSelected)
                    ImGui.PushStyleColor(ImGuiCol.Text, plugin.CurrentTheme.AccentStrong);
                if (ImGui.Selectable($"{component.Label}##native-job-gauge-select-{component.Key}", nativeGaugeSelected))
                    SelectNativeJobGaugeComponent(component.Key);
                if (nativeGaugeSelected)
                    ImGui.PopStyleColor();
            }
        }

        var nativeStatusVisible = plugin.NativeHudVisibility.TryGetVisibleStatusEffectsBounds(out _, out _);
        ImGui.BeginDisabled();
        ImGui.Checkbox("##visible-native-status-effects", ref nativeStatusVisible);
        ImGui.EndDisabled();
        ImGui.SameLine();
        var nativeStatusSelected = selectedId == HudElementIds.NativeStatusEffects;
        if (nativeStatusSelected) ImGui.PushStyleColor(ImGuiCol.Text, plugin.CurrentTheme.AccentStrong);
        if (ImGui.Selectable(plugin.GetHudElementLabel(HudElementIds.NativeStatusEffects), nativeStatusSelected))
            SelectNativeElement(HudElementIds.NativeStatusEffects);
        if (nativeStatusSelected) ImGui.PopStyleColor();

        DrawNativeQuestElementSelector(HudElementIds.NativeScenarioGuide);
        DrawNativeQuestElementSelector(HudElementIds.NativeQuestList);
        DrawNativeQuestElementSelector(HudElementIds.NativeDutyInfo);

        ImGui.Spacing();
        ImGui.Separator();
        var grid = plugin.Configuration.ShowHudEditorGrid;
        if (ImGui.Checkbox("Show alignment grid", ref grid))
        {
            plugin.Configuration.ShowHudEditorGrid = grid;
            dirty = true;
        }
        var gridSize = plugin.Configuration.HudEditorGridSize;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##grid-size", ref gridSize, 4f, 64f, "Grid %.0f px"))
        {
            plugin.Configuration.HudEditorGridSize = gridSize;
            dirty = true;
        }

        if (selectedId == HudElementIds.CombatHalo)
        {
            var follow = plugin.Configuration.HaloFollowsPlayer;
            if (ImGui.Checkbox("Halo follows player", ref follow))
            {
                plugin.Configuration.HaloFollowsPlayer = follow;
                dirty = true;
            }
        }
        else if (selectedId == HudElementIds.NativeJobGauge)
        {
            var componentKey = selectedNativeJobGaugeComponent;
            var component = plugin.NativeHudVisibility.GetVisibleJobGaugeComponents(job)
                .FirstOrDefault(item => string.Equals(item.Key, componentKey, StringComparison.OrdinalIgnoreCase));
            ImGui.Spacing();
            if (string.IsNullOrWhiteSpace(component.Key))
            {
                ImGui.TextDisabled("No native job-gauge component is currently selected.");
            }
            else
            {
                ImGui.TextWrapped($"Drag {component.Label} independently. Its position is saved separately from the other {job} gauges; scale and simple/normal display remain controlled by FFXIV.");
                if (plugin.NativeHudVisibility.HasJobGaugeComponentPlacement(job, component.Key) &&
                    ImGui.Button($"Reset {component.Label} Position"))
                {
                    plugin.NativeHudVisibility.ResetJobGaugeComponentPosition(job, component.Key);
                    dirty = true;
                }
            }
        }
        else if (selectedId is HudElementIds.NativeScenarioGuide or HudElementIds.NativeQuestList or HudElementIds.NativeDutyInfo)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("This is a live FFXIV Quest Dock element. Drag its outline to move it and use the lower-right grip to resize it proportionally.");
            if (!plugin.NativeHudVisibility.TryGetVisibleQuestElementBounds(selectedId, out _, out _))
                ImGui.TextDisabled("This element is not currently visible. Enter Quest Dock, or enter a duty for Duty Information, then reopen Edit HUD.");
            else if (plugin.NativeHudVisibility.HasQuestElementPlacement(selectedId))
            {
                var available = ImGui.GetContentRegionAvail().X;
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                var buttonWidth = MathF.Max(110f, (available - spacing) * 0.5f);
                if (ImGui.Button("Restore Native Size", new Vector2(buttonWidth, 0f)))
                {
                    if (plugin.NativeHudVisibility.ResetQuestElementScale(selectedId))
                        dirty = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Restore FFXIV's original scale without moving this element.");

                ImGui.SameLine();
                if (ImGui.Button($"Reset Position + Size##{selectedId}", new Vector2(buttonWidth, 0f)))
                {
                    plugin.NativeHudVisibility.ResetQuestElementPlacement(selectedId);
                    dirty = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Return {plugin.GetHudElementLabel(selectedId)} to its original FFXIV position and size.");
            }
        }
    }


    private void DrawDockSelector()
    {
        var modes = HudModeProfileService.EditableModes;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = MathF.Max(1f, (ImGui.GetContentRegionAvail().X - spacing * (modes.Length - 1)) / modes.Length);

        for (var i = 0; i < modes.Length; i++)
        {
            var mode = modes[i];
            if (i > 0)
                ImGui.SameLine();

            var selected = plugin.HudEditPreviewMode == mode;
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(plugin.CurrentTheme.AccentStrong, 0.42f));
                ImGui.PushStyleColor(ImGuiCol.Border, UiStyles.WithAlpha(plugin.CurrentTheme.AccentStrong, 0.95f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            }

            if (ImGui.Button($"{HudModeProfileService.Label(mode).ToUpperInvariant()}##edit-dock-{mode}", new Vector2(width, 30f)))
            {
                plugin.SetHudEditPreviewMode(mode);
                selectedIds.Clear();
                if (!string.IsNullOrWhiteSpace(selectedId) && plugin.GetEditableHudElementIds().Contains(selectedId))
                    selectedIds.Add(selectedId);
            }

            if (selected)
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(2);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Edit only the {HudModeProfileService.Label(mode)} dock. The saved HUD preset still contains every dock.");
        }
    }


    private void DrawDockCopyControls()
    {
        var destination = HudModeProfileService.Normalize(plugin.HudEditPreviewMode);
        if (destination == UiMode.Leisure)
            return;

        ImGui.Spacing();
        ImGui.TextUnformatted("Copy dock layout from");

        var sources = HudModeProfileService.EditableModes
            .Where(mode => mode != UiMode.Leisure && mode != destination)
            .ToArray();

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttonWidth = MathF.Max(1f,
            (ImGui.GetContentRegionAvail().X - spacing * Math.Max(0, sources.Length - 1)) /
            Math.Max(1, sources.Length));
        for (var index = 0; index < sources.Length; index++)
        {
            if (index > 0)
                ImGui.SameLine();

            var source = sources[index];
            var label = HudModeProfileService.Label(source).ToUpperInvariant();
            if (ImGui.Button($"COPY {label}##copy-dock-{source}-to-{destination}", new Vector2(buttonWidth, 28f)))
            {
                plugin.LayoutRecovery.Create("Before Cross-Dock Copy");
                plugin.LayoutHistory.Record("Copy dock layout", () =>
                    HudModeProfileService.CopyMode(plugin.Configuration, source, destination));
                plugin.SaveConfiguration();
                plugin.NativeHudVisibility.RefreshNow();
                dirty = false;
                Plugin.ChatGui.Print($"Copied the {HudModeProfileService.Label(source)} dock layout into {HudModeProfileService.Label(destination)}.");
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Replace the {HudModeProfileService.Label(destination)} dock's element positions and visibility with the {HudModeProfileService.Label(source)} dock. Leisure remains the shared baseline and is never copied into another dock.");
        }
    }


    private void DrawNativeQuestElementSelector(string elementId)
    {
        var visible = plugin.NativeHudVisibility.TryGetVisibleQuestElementBounds(elementId, out _, out _);
        ImGui.BeginDisabled();
        ImGui.Checkbox($"##visible-{elementId}", ref visible);
        ImGui.EndDisabled();
        ImGui.SameLine();
        var selected = selectedId == elementId;
        if (selected)
            ImGui.PushStyleColor(ImGuiCol.Text, plugin.CurrentTheme.AccentStrong);
        if (ImGui.Selectable(plugin.GetHudElementLabel(elementId), selected))
            SelectNativeElement(elementId);
        if (selected)
            ImGui.PopStyleColor();
    }

    private void DrawEditorChromeDragHandle(
        string id,
        string label,
        Vector2 size,
        Vector2 currentPosition,
        Vector2 chromeSize,
        Vector2 canvas,
        Action<Vector2> storePosition)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(plugin.CurrentTheme.Panel, 0.26f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiStyles.WithAlpha(plugin.CurrentTheme.Accent, 0.32f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiStyles.WithAlpha(plugin.CurrentTheme.AccentStrong, 0.42f));
        ImGui.Button($"{label}{id}", size);
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var next = ClampEditorChromePosition(currentPosition + ImGui.GetIO().MouseDelta, canvas, chromeSize);
            storePosition(next);
            dirty = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Drag to move this Edit HUD control panel.");
    }

    private static Vector2 ResolveEditorChromePosition(
        float normalizedX,
        float normalizedY,
        Vector2 canvas,
        Vector2 chromeSize,
        Vector2 fallback)
    {
        var position = new Vector2(normalizedX * canvas.X, normalizedY * canvas.Y);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            position = fallback;
        return ClampEditorChromePosition(position, canvas, chromeSize);
    }

    private static Vector2 ClampEditorChromePosition(Vector2 position, Vector2 canvas, Vector2 chromeSize)
        => new(
            Math.Clamp(position.X, 0f, MathF.Max(0f, canvas.X - chromeSize.X)),
            Math.Clamp(position.Y, 0f, MathF.Max(0f, canvas.Y - chromeSize.Y)));

    public void Dispose() { }
}
