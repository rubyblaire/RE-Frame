using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed partial class HudEditorWindow
{
    private readonly HashSet<string> selectedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SnapGuide> activeSnapGuides = new();
    private bool anyElementHovered;
    private bool editorPanelHovered;
    private bool pointerGestureActive;
    private bool pointerGestureChanged;
    private bool numericEditChanged;
    private bool numericTransactionActive;

    private readonly record struct SnapGuide(bool Vertical, float Position);

    private bool HasCustomSelection => selectedIds.Count > 0;
    private bool HasPrimaryCustomSelection => !string.IsNullOrWhiteSpace(selectedId) && selectedIds.Contains(selectedId);

    private void PrepareLayoutStudioFrame(Vector2 origin, Vector2 canvas, ImDrawListPtr draw)
    {
        activeSnapGuides.Clear();
        anyElementHovered = false;
        editorPanelHovered = false;

        if (!string.IsNullOrWhiteSpace(selectedId) &&
            plugin.GetEditableHudElementIds().Contains(selectedId) &&
            plugin.ShouldExposeHudElement(selectedId))
        {
            if (selectedIds.Count == 0)
                selectedIds.Add(selectedId);
        }
        else if (!IsNativeEditorElement(selectedId))
        {
            selectedId = string.Empty;
            selectedIds.Clear();
        }

        DrawSafeAreaOverlays(draw, origin, canvas);
    }

    private void FinishLayoutStudioFrame(Vector2 origin, Vector2 canvas, ImDrawListPtr draw)
    {
        HandleHistoryShortcuts();

        if (pointerGestureActive && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (pointerGestureChanged)
                plugin.LayoutHistory.CommitTransaction();
            else
                plugin.LayoutHistory.CancelTransaction();
            pointerGestureActive = false;
            pointerGestureChanged = false;
        }

        if (numericTransactionActive && !ImGui.IsAnyItemActive())
        {
            if (numericEditChanged)
                plugin.LayoutHistory.CommitTransaction();
            else
                plugin.LayoutHistory.CancelTransaction();
            numericTransactionActive = false;
            numericEditChanged = false;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
            !anyElementHovered &&
            !editorPanelHovered &&
            !ImGui.IsAnyItemHovered())
        {
            selectedIds.Clear();
            selectedId = string.Empty;
            selectedNativeJobGaugeComponent = string.Empty;
            plugin.HotbarEditing.SetActiveElement(string.Empty);
        }
    }

    private void BeginPointerGesture(string label)
    {
        if (pointerGestureActive)
            return;
        plugin.LayoutHistory.BeginTransaction(label);
        pointerGestureActive = true;
        pointerGestureChanged = false;
    }

    private void MarkPointerGestureChanged()
    {
        pointerGestureChanged = true;
        dirty = true;
    }

    private void SelectCustomElement(string id)
    {
        if (!plugin.ShouldExposeHudElement(id))
            return;

        if (ImGui.GetIO().KeyShift)
        {
            if (selectedIds.Contains(id))
            {
                selectedIds.Remove(id);
                if (string.Equals(selectedId, id, StringComparison.OrdinalIgnoreCase))
                    selectedId = selectedIds.FirstOrDefault() ?? string.Empty;
            }
            else
            {
                selectedIds.Add(id);
                selectedId = id;
            }
        }
        else
        {
            if (selectedIds.Contains(id) && selectedIds.Count > 1)
            {

                selectedId = id;
            }
            else
            {
                selectedIds.Clear();
                selectedIds.Add(id);
                selectedId = id;
            }
        }

        selectedNativeJobGaugeComponent = string.Empty;
        plugin.HotbarEditing.SetActiveElement(selectedId);
    }

    private void SelectNativeElement(string id)
    {
        selectedIds.Clear();
        selectedId = id;
        if (id != HudElementIds.NativeJobGauge)
            selectedNativeJobGaugeComponent = string.Empty;
        plugin.HotbarEditing.SetActiveElement(string.Empty);
    }

    private void SelectNativeJobGaugeComponent(string componentKey)
    {
        selectedIds.Clear();
        selectedId = HudElementIds.NativeJobGauge;
        selectedNativeJobGaugeComponent = componentKey ?? string.Empty;
        plugin.HotbarEditing.SetActiveElement(string.Empty);
    }

    private string SelectedNativeElementLabel()
    {
        if (selectedId != HudElementIds.NativeJobGauge)
            return plugin.GetHudElementLabel(selectedId);

        var job = plugin.GetJobAbbreviation();
        var component = plugin.NativeHudVisibility.GetVisibleJobGaugeComponents(job)
            .FirstOrDefault(item => string.Equals(item.Key, selectedNativeJobGaugeComponent, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(component.Label)
            ? plugin.GetHudElementLabel(HudElementIds.NativeJobGauge)
            : component.Label;
    }

    private static bool IsNativeEditorElement(string id)
        => id is HudElementIds.NativeJobGauge or
            HudElementIds.NativeStatusEffects or
            HudElementIds.NativeScenarioGuide or
            HudElementIds.NativeQuestList or
            HudElementIds.NativeDutyInfo;

    private bool IsLocked(string id)
        => HudModeProfileService.IsLocked(plugin.Configuration, plugin.HudEditPreviewMode, id);

    private IReadOnlyList<string> SelectedCustomElements()
        => selectedIds.Where(plugin.ShouldExposeHudElement).ToArray();

    private void MoveSelection(Vector2 delta, Vector2 origin, Vector2 canvas)
    {
        if (!HasPrimaryCustomSelection)
            return;

        var mode = plugin.HudEditPreviewMode;
        var primary = HudLayout.Resolve(plugin.Configuration, selectedId, origin, canvas, mode);
        delta = ApplyMoveSnapping(selectedId, primary, delta, origin, canvas);

        foreach (var id in SelectedCustomElements())
        {
            if (IsLocked(id))
                continue;
            var bounds = HudLayout.Resolve(plugin.Configuration, id, origin, canvas, mode);
            HudLayout.Store(plugin.Configuration, id, new HudBounds(bounds.Position + delta, bounds.Size), origin, canvas, mode);
            if (id == HudElementIds.CombatHalo)
                plugin.Configuration.HaloFollowsPlayer = false;
        }

        MarkPointerGestureChanged();
    }

    private Vector2 ApplyMoveSnapping(string movingId, HudBounds bounds, Vector2 delta, Vector2 origin, Vector2 canvas)
    {
        if (!plugin.Configuration.HudEditorSnappingEnabled || ImGui.GetIO().KeyCtrl)
            return delta;

        var tolerance = Math.Clamp(plugin.Configuration.HudEditorSnapTolerance, 2f, 32f);
        var proposed = new HudBounds(bounds.Position + delta, bounds.Size);
        var xTargets = BuildSnapTargets(true, origin, canvas, movingId);
        var yTargets = BuildSnapTargets(false, origin, canvas, movingId);

        var xSources = new[] { proposed.Position.X, proposed.Position.X + proposed.Size.X * 0.5f, proposed.Position.X + proposed.Size.X };
        var ySources = new[] { proposed.Position.Y, proposed.Position.Y + proposed.Size.Y * 0.5f, proposed.Position.Y + proposed.Size.Y };
        var snapX = FindSnapAdjustment(xSources, xTargets, tolerance, out var guideX);
        var snapY = FindSnapAdjustment(ySources, yTargets, tolerance, out var guideY);
        if (guideX.HasValue)
            activeSnapGuides.Add(new SnapGuide(true, guideX.Value));
        if (guideY.HasValue)
            activeSnapGuides.Add(new SnapGuide(false, guideY.Value));
        return delta + new Vector2(snapX, snapY);
    }

    private Vector2 ApplyResizeSnapping(string resizingId, HudBounds bounds, Vector2 requestedSize, Vector2 origin, Vector2 canvas)
    {
        if (!plugin.Configuration.HudEditorSnappingEnabled || ImGui.GetIO().KeyCtrl)
            return requestedSize;

        var tolerance = Math.Clamp(plugin.Configuration.HudEditorSnapTolerance, 2f, 32f);
        var right = bounds.Position.X + requestedSize.X;
        var bottom = bounds.Position.Y + requestedSize.Y;
        var xAdjustment = FindSnapAdjustment(new[] { right }, BuildSnapTargets(true, origin, canvas, resizingId), tolerance, out var guideX);
        var yAdjustment = FindSnapAdjustment(new[] { bottom }, BuildSnapTargets(false, origin, canvas, resizingId), tolerance, out var guideY);
        if (guideX.HasValue)
            activeSnapGuides.Add(new SnapGuide(true, guideX.Value));
        if (guideY.HasValue)
            activeSnapGuides.Add(new SnapGuide(false, guideY.Value));
        return requestedSize + new Vector2(xAdjustment, yAdjustment);
    }

    private List<float> BuildSnapTargets(bool vertical, Vector2 origin, Vector2 canvas, string movingId)
    {
        var targets = new List<float>(64);
        var start = vertical ? origin.X : origin.Y;
        var extent = vertical ? canvas.X : canvas.Y;
        targets.Add(start);
        targets.Add(start + extent * 0.05f);
        targets.Add(start + extent * 0.5f);
        targets.Add(start + extent * 0.95f);
        targets.Add(start + extent);

        foreach (var id in plugin.GetEditableHudElementIds())
        {
            if (selectedIds.Contains(id) || string.Equals(id, movingId, StringComparison.OrdinalIgnoreCase) || !plugin.ShouldExposeHudElement(id))
                continue;

            var bounds = HudLayout.Resolve(plugin.Configuration, id, origin, canvas, plugin.HudEditPreviewMode);
            if (vertical)
            {
                targets.Add(bounds.Position.X);
                targets.Add(bounds.Position.X + bounds.Size.X * 0.5f);
                targets.Add(bounds.Position.X + bounds.Size.X);
            }
            else
            {
                targets.Add(bounds.Position.Y);
                targets.Add(bounds.Position.Y + bounds.Size.Y * 0.5f);
                targets.Add(bounds.Position.Y + bounds.Size.Y);
            }
        }

        return targets;
    }

    private static float FindSnapAdjustment(IEnumerable<float> sources, IEnumerable<float> targets, float tolerance, out float? guide)
    {
        var best = tolerance + 1f;
        var adjustment = 0f;
        guide = null;
        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                var difference = target - source;
                var distance = MathF.Abs(difference);
                if (distance > tolerance || distance >= best)
                    continue;
                best = distance;
                adjustment = difference;
                guide = target;
            }
        }
        return guide.HasValue ? adjustment : 0f;
    }

    private void DrawSnapGuides(ImDrawListPtr draw, Vector2 origin, Vector2 canvas)
    {
        if (activeSnapGuides.Count == 0)
            return;
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(
            plugin.CurrentTheme.AccentStrong.X,
            plugin.CurrentTheme.AccentStrong.Y,
            plugin.CurrentTheme.AccentStrong.Z,
            0.82f));
        foreach (var guide in activeSnapGuides.Distinct())
        {
            if (guide.Vertical)
                draw.AddLine(new Vector2(guide.Position, origin.Y), new Vector2(guide.Position, origin.Y + canvas.Y), color, 1.5f);
            else
                draw.AddLine(new Vector2(origin.X, guide.Position), new Vector2(origin.X + canvas.X, guide.Position), color, 1.5f);
        }
    }

    private void DrawSafeAreaOverlays(ImDrawListPtr draw, Vector2 origin, Vector2 canvas)
    {
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(
            plugin.CurrentTheme.Accent.X,
            plugin.CurrentTheme.Accent.Y,
            plugin.CurrentTheme.Accent.Z,
            0.22f));
        var strong = ImGui.ColorConvertFloat4ToU32(new Vector4(
            plugin.CurrentTheme.AccentStrong.X,
            plugin.CurrentTheme.AccentStrong.Y,
            plugin.CurrentTheme.AccentStrong.Z,
            0.34f));

        if (plugin.Configuration.ShowHudEditorScreenBounds)
            draw.AddRect(origin + Vector2.One, origin + canvas - Vector2.One, strong, 0f, ImDrawFlags.None, 1.5f);

        if (plugin.Configuration.ShowHudEditorGeneralSafeArea)
        {
            var inset = canvas * 0.05f;
            draw.AddRect(origin + inset, origin + canvas - inset, color, 0f, ImDrawFlags.None, 1.25f);
        }

        if (plugin.Configuration.ShowHudEditorStreamSafeArea)
        {
            var safe = FitAspect(canvas * 0.90f, 16f / 9f);
            var topLeft = origin + (canvas - safe) * 0.5f;
            draw.AddRect(topLeft, topLeft + safe, color, 0f, ImDrawFlags.None, 1.25f);
        }

        if (plugin.Configuration.ShowHudEditorUltrawideSafeArea)
        {
            var safe = FitAspect(canvas, 16f / 9f);
            var topLeft = origin + (canvas - safe) * 0.5f;
            draw.AddRect(topLeft, topLeft + safe, strong, 0f, ImDrawFlags.None, 1.25f);
        }

        if (plugin.Configuration.ShowHudEditorCenterGuides)
        {
            draw.AddLine(new Vector2(origin.X + canvas.X * 0.5f, origin.Y), new Vector2(origin.X + canvas.X * 0.5f, origin.Y + canvas.Y), color);
            draw.AddLine(new Vector2(origin.X, origin.Y + canvas.Y * 0.5f), new Vector2(origin.X + canvas.X, origin.Y + canvas.Y * 0.5f), color);
        }
    }

    private static Vector2 FitAspect(Vector2 available, float aspect)
    {
        var width = available.X;
        var height = width / aspect;
        if (height > available.Y)
        {
            height = available.Y;
            width = height * aspect;
        }
        return new Vector2(MathF.Max(1f, width), MathF.Max(1f, height));
    }

    private void HandleHistoryShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput || !io.KeyCtrl)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.Z, false))
        {
            if (io.KeyShift)
                PerformRedo();
            else
                PerformUndo();
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Y, false))
        {
            PerformRedo();
        }
    }

    private void PerformUndo()
    {
        FinalizePendingHistoryTransaction();
        if (plugin.LayoutHistory.Undo(out var message))
        {
            dirty = false;
            Plugin.ChatGui.Print(message);
        }
    }

    private void PerformRedo()
    {
        FinalizePendingHistoryTransaction();
        if (plugin.LayoutHistory.Redo(out var message))
        {
            dirty = false;
            Plugin.ChatGui.Print(message);
        }
    }

    private void FinalizePendingHistoryTransaction()
    {
        if (pointerGestureActive)
        {
            if (pointerGestureChanged)
                plugin.LayoutHistory.CommitTransaction();
            else
                plugin.LayoutHistory.CancelTransaction();
            pointerGestureActive = false;
            pointerGestureChanged = false;
        }

        if (numericTransactionActive)
        {
            if (numericEditChanged)
                plugin.LayoutHistory.CommitTransaction();
            else
                plugin.LayoutHistory.CancelTransaction();
            numericTransactionActive = false;
            numericEditChanged = false;
        }
    }

    private void DrawHistoryControls()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = MathF.Max(1f, (ImGui.GetContentRegionAvail().X - spacing) * 0.5f);


        var canUndo = plugin.LayoutHistory.CanUndo;
        var undoLabel = plugin.LayoutHistory.UndoLabel;
        if (!canUndo)
            ImGui.BeginDisabled();
        if (ImGui.Button("UNDO", new Vector2(width, 30f)) && canUndo)
            PerformUndo();
        if (ImGui.IsItemHovered() && canUndo)
            ImGui.SetTooltip($"Undo {undoLabel} (Ctrl+Z)");
        if (!canUndo)
            ImGui.EndDisabled();

        ImGui.SameLine();
        var canRedo = plugin.LayoutHistory.CanRedo;
        var redoLabel = plugin.LayoutHistory.RedoLabel;
        if (!canRedo)
            ImGui.BeginDisabled();
        if (ImGui.Button("REDO", new Vector2(width, 30f)) && canRedo)
            PerformRedo();
        if (ImGui.IsItemHovered() && canRedo)
            ImGui.SetTooltip($"Redo {redoLabel} (Ctrl+Y)");
        if (!canRedo)
            ImGui.EndDisabled();
    }

    private void DrawLayoutStudioControls(Vector2 origin, Vector2 canvas)
    {
        if (!HasPrimaryCustomSelection)
        {
            if (IsNativeEditorElement(selectedId))
            {
                if (ImGui.CollapsingHeader("Selection", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.TextUnformatted(SelectedNativeElementLabel());
                    ImGui.TextDisabled("This is a supported live FFXIV element. Exact changes update its native placement without replacing the native window.");
                }

                if (ImGui.CollapsingHeader("Position and Size", ImGuiTreeNodeFlags.DefaultOpen))
                    DrawNativeExactPositionSizeControls();
            }
            else
            {
                ImGui.TextDisabled("Select one or more RE:Frame elements to use precision tools.");
            }

            DrawSnappingAndGuideControls();
            return;
        }

        if (ImGui.CollapsingHeader("Selection", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var selected = SelectedCustomElements();
            ImGui.TextUnformatted(selected.Count == 1
                ? plugin.GetHudElementLabel(selectedId)
                : $"{selected.Count} elements selected");
            ImGui.TextDisabled("Shift-click frames or names to add or remove them. The most recently selected element is the alignment anchor.");

            var locked = IsLocked(selectedId);
            if (ImGui.Checkbox("Lock primary element in this dock", ref locked))
            {
                plugin.LayoutHistory.Record(locked ? "Lock element" : "Unlock element", () =>
                    HudModeProfileService.SetLocked(plugin.Configuration, plugin.HudEditPreviewMode, selectedId, locked));
                plugin.SaveConfiguration();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("A locked element remains selectable and can still be shown or hidden, but it cannot be moved or resized in this dock.");
        }

        if (ImGui.CollapsingHeader("Position and Size", ImGuiTreeNodeFlags.DefaultOpen))
            DrawExactPositionSizeControls(origin, canvas);

        if (SelectedCustomElements().Count == 1 && HotbarGridLayouts.IsConfigurableHotbar(selectedId) &&
            ImGui.CollapsingHeader("Hotbar Layout", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawHotbarLayoutControls(canvas);
        }

        if (ImGui.CollapsingHeader("Alignment"))
            DrawAlignmentControls(origin, canvas);

        if (ImGui.CollapsingHeader("Distribution"))
            DrawDistributionControls(origin, canvas);

        if (ImGui.CollapsingHeader("Dock Mirroring"))
            DrawElementDockMirroring();

        DrawSnappingAndGuideControls();
    }


    private void DrawHotbarLayoutControls(Vector2 canvas)
    {
        var currentColumns = HotbarGridLayouts.GetColumns(plugin.Configuration, selectedId);
        var supported = HotbarGridLayouts.Supported;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = MathF.Max(1f, (ImGui.GetContentRegionAvail().X - spacing * (supported.Length - 1)) / supported.Length);

        for (var i = 0; i < supported.Length; i++)
        {
            var shape = supported[i];
            if (i > 0)
                ImGui.SameLine();

            var selected = currentColumns == shape.Columns;
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(plugin.CurrentTheme.AccentStrong, 0.42f));
                ImGui.PushStyleColor(ImGuiCol.Border, UiStyles.WithAlpha(plugin.CurrentTheme.AccentStrong, 0.95f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            }

            if (ImGui.Button($"{shape.Label}##hotbar-shape-{selectedId}-{shape.Columns}", new Vector2(width, 30f)) && !selected)
            {
                var elementId = selectedId;
                var nextColumns = shape.Columns;
                plugin.LayoutHistory.Record($"Change {plugin.GetHudElementLabel(elementId)} to {shape.Label}", () =>
                    HotbarGridLayouts.ChangeShape(plugin.Configuration, elementId, nextColumns, canvas));
                dirty = true;
            }

            if (selected)
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(2);
            }
        }

        ImGui.TextDisabled("Choose 12×1, 6×2, 4×3, or 3×4. Slots fill left-to-right, then top-to-bottom. RE:Frame preserves the current slot size and recenters the bar in every dock where it has a custom position. Pet Bar commands remain native to FFXIV.");
    }

    private void DrawNativeExactPositionSizeControls()
    {
        Vector2 position;
        Vector2 size;
        var movable = true;
        var resizable = true;

        if (selectedId == HudElementIds.NativeJobGauge)
        {
            if (string.IsNullOrWhiteSpace(selectedNativeJobGaugeComponent) ||
                !plugin.NativeHudVisibility.TryGetVisibleJobGaugeComponentBounds(
                    plugin.GetJobAbbreviation(),
                    selectedNativeJobGaugeComponent,
                    out position,
                    out size))
            {
                ImGui.TextDisabled("The selected native job-gauge component is not currently visible.");
                return;
            }
            resizable = false;
        }
        else if (selectedId == HudElementIds.NativeStatusEffects)
        {
            if (!plugin.NativeHudVisibility.TryGetVisibleStatusEffectsBounds(out position, out size))
            {
                ImGui.TextDisabled("Native status effects are not currently visible.");
                return;
            }
        }
        else if (selectedId is HudElementIds.NativeScenarioGuide or HudElementIds.NativeQuestList or HudElementIds.NativeDutyInfo)
        {
            if (!plugin.NativeHudVisibility.TryGetVisibleQuestElementBounds(selectedId, out position, out size))
            {
                ImGui.TextDisabled("This native Quest Dock element is not currently visible.");
                return;
            }
        }
        else
        {
            movable = false;
            position = Vector2.Zero;
            size = Vector2.Zero;
        }

        if (!movable)
            return;

        var x = position.X;
        var y = position.Y;
        var width = size.X;
        var height = size.Y;
        var fieldWidth = MathF.Max(80f, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f);

        var nativeSelectionKey = selectedId == HudElementIds.NativeJobGauge
            ? $"{selectedId}-{selectedNativeJobGaugeComponent}"
            : selectedId;

        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"X##native-exact-{nativeSelectionKey}", ref x, -4096f, 16384f))
            MoveNativeExact(new Vector2(x - position.X, 0f));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"Y##native-exact-{nativeSelectionKey}", ref y, -4096f, 16384f))
            MoveNativeExact(new Vector2(0f, y - position.Y));

        if (!resizable)
        {
            ImGui.TextDisabled("Scale and simple/normal display remain controlled by FFXIV for native job gauges.");
            return;
        }

        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"Width##native-exact-{nativeSelectionKey}", ref width, 20f, 8192f))
            ResizeNativeExact(new Vector2(width - size.X, 0f));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"Height##native-exact-{nativeSelectionKey}", ref height, 20f, 8192f))
            ResizeNativeExact(new Vector2(0f, height - size.Y));
        ImGui.TextDisabled("Native width and height remain proportional because FFXIV owns the underlying element scale.");
    }

    private void MoveNativeExact(Vector2 delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y))
            return;

        var changed = selectedId switch
        {
            HudElementIds.NativeJobGauge when !string.IsNullOrWhiteSpace(selectedNativeJobGaugeComponent)
                => plugin.NativeHudVisibility.MoveVisibleJobGaugeComponent(
                    plugin.GetJobAbbreviation(),
                    selectedNativeJobGaugeComponent,
                    delta),
            HudElementIds.NativeStatusEffects => plugin.NativeHudVisibility.MoveVisibleStatusEffects(delta),
            HudElementIds.NativeScenarioGuide or HudElementIds.NativeQuestList or HudElementIds.NativeDutyInfo
                => plugin.NativeHudVisibility.MoveVisibleQuestElement(selectedId, delta),
            _ => false,
        };
        if (changed)
        {
            numericEditChanged = true;
            dirty = true;
        }
    }

    private void ResizeNativeExact(Vector2 delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y))
            return;

        var changed = selectedId switch
        {
            HudElementIds.NativeStatusEffects => plugin.NativeHudVisibility.ResizeVisibleStatusEffects(delta),
            HudElementIds.NativeScenarioGuide or HudElementIds.NativeQuestList or HudElementIds.NativeDutyInfo
                => plugin.NativeHudVisibility.ResizeVisibleQuestElement(selectedId, delta),
            _ => false,
        };
        if (changed)
        {
            numericEditChanged = true;
            dirty = true;
        }
    }

    private void DrawExactPositionSizeControls(Vector2 origin, Vector2 canvas)
    {
        if (!HasPrimaryCustomSelection)
            return;

        var mode = plugin.HudEditPreviewMode;
        var bounds = HudLayout.Resolve(plugin.Configuration, selectedId, origin, canvas, mode);
        var local = bounds.Position - origin;
        var x = local.X;
        var y = local.Y;
        var widthValue = bounds.Size.X;
        var heightValue = bounds.Size.Y;
        var locked = IsLocked(selectedId);
        if (locked)
            ImGui.BeginDisabled();

        var fieldWidth = MathF.Max(80f, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f);
        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"X##exact-{selectedId}", ref x, -canvas.X * 0.5f, canvas.X * 1.5f))
            StoreExactBounds(new HudBounds(origin + new Vector2(x, local.Y), bounds.Size), origin, canvas);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"Y##exact-{selectedId}", ref y, -canvas.Y * 0.5f, canvas.Y * 1.5f))
            StoreExactBounds(new HudBounds(origin + new Vector2(local.X, y), bounds.Size), origin, canvas);

        var minimum = HudLayout.MinimumSize(plugin.Configuration, selectedId);
        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"Width##exact-{selectedId}", ref widthValue, minimum.X, canvas.X * 2f))
            StoreExactBounds(new HudBounds(bounds.Position, new Vector2(widthValue, bounds.Size.Y)), origin, canvas);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(fieldWidth);
        if (DrawTrackedDragFloat($"Height##exact-{selectedId}", ref heightValue, minimum.Y, canvas.Y * 2f))
            StoreExactBounds(new HudBounds(bounds.Position, new Vector2(bounds.Size.X, heightValue)), origin, canvas);

        if (locked)
            ImGui.EndDisabled();
    }

    private bool DrawTrackedDragFloat(string label, ref float value, float minimum, float maximum)
    {
        var changed = ImGui.DragFloat(label, ref value, 1f, minimum, maximum, "%.0f px");
        if (ImGui.IsItemActivated() && !numericTransactionActive)
        {
            plugin.LayoutHistory.BeginTransaction("Edit exact position or size");
            numericTransactionActive = true;
            numericEditChanged = false;
        }
        if (changed)
            numericEditChanged = true;
        return changed;
    }

    private void StoreExactBounds(HudBounds bounds, Vector2 origin, Vector2 canvas)
    {
        if (!float.IsFinite(bounds.Position.X) || !float.IsFinite(bounds.Position.Y) ||
            !float.IsFinite(bounds.Size.X) || !float.IsFinite(bounds.Size.Y))
            return;

        HudLayout.Store(plugin.Configuration, selectedId, bounds, origin, canvas, plugin.HudEditPreviewMode);
        if (selectedId == HudElementIds.CombatHalo)
            plugin.Configuration.HaloFollowsPlayer = false;
        dirty = true;
    }

    private void DrawAlignmentControls(Vector2 origin, Vector2 canvas)
    {
        if (SelectedCustomElements().Count < 2)
        {
            ImGui.TextDisabled("Select at least two elements.");
            return;
        }

        DrawThreeButtonRow(
            ("LEFT", () => AlignSelection(Alignment.Left, origin, canvas)),
            ("H-CENTER", () => AlignSelection(Alignment.HorizontalCenter, origin, canvas)),
            ("RIGHT", () => AlignSelection(Alignment.Right, origin, canvas)));
        DrawThreeButtonRow(
            ("TOP", () => AlignSelection(Alignment.Top, origin, canvas)),
            ("V-MIDDLE", () => AlignSelection(Alignment.VerticalMiddle, origin, canvas)),
            ("BOTTOM", () => AlignSelection(Alignment.Bottom, origin, canvas)));
        ImGui.TextDisabled("The primary selection is the anchor. Locked elements remain in place.");
    }

    private enum Alignment { Left, HorizontalCenter, Right, Top, VerticalMiddle, Bottom }

    private void AlignSelection(Alignment alignment, Vector2 origin, Vector2 canvas)
    {
        var selected = SelectedCustomElements();
        if (selected.Count < 2 || !HasPrimaryCustomSelection)
            return;
        var mode = plugin.HudEditPreviewMode;
        var anchor = HudLayout.Resolve(plugin.Configuration, selectedId, origin, canvas, mode);
        plugin.LayoutHistory.Record($"Align {alignment}", () =>
        {
            foreach (var id in selected)
            {
                if (string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase) || IsLocked(id))
                    continue;
                var bounds = HudLayout.Resolve(plugin.Configuration, id, origin, canvas, mode);
                var position = bounds.Position;
                position = alignment switch
                {
                    Alignment.Left => new Vector2(anchor.Position.X, position.Y),
                    Alignment.HorizontalCenter => new Vector2(anchor.Position.X + anchor.Size.X * 0.5f - bounds.Size.X * 0.5f, position.Y),
                    Alignment.Right => new Vector2(anchor.Position.X + anchor.Size.X - bounds.Size.X, position.Y),
                    Alignment.Top => new Vector2(position.X, anchor.Position.Y),
                    Alignment.VerticalMiddle => new Vector2(position.X, anchor.Position.Y + anchor.Size.Y * 0.5f - bounds.Size.Y * 0.5f),
                    Alignment.Bottom => new Vector2(position.X, anchor.Position.Y + anchor.Size.Y - bounds.Size.Y),
                    _ => position,
                };
                HudLayout.Store(plugin.Configuration, id, new HudBounds(position, bounds.Size), origin, canvas, mode);
            }
        });
        SaveLayoutOperation();
    }

    private void DrawDistributionControls(Vector2 origin, Vector2 canvas)
    {
        if (SelectedCustomElements().Count < 3)
        {
            ImGui.TextDisabled("Select at least three elements.");
            return;
        }

        DrawTwoButtonRow(
            ("DISTRIBUTE H", () => DistributeSelection(true, false, origin, canvas)),
            ("DISTRIBUTE V", () => DistributeSelection(false, false, origin, canvas)));
        DrawTwoButtonRow(
            ("EQUAL H SPACING", () => DistributeSelection(true, true, origin, canvas)),
            ("EQUAL V SPACING", () => DistributeSelection(false, true, origin, canvas)));
        ImGui.TextDisabled("Distribution preserves element sizes. Locked elements are skipped.");
    }

    private void DistributeSelection(bool horizontal, bool equalSpacing, Vector2 origin, Vector2 canvas)
    {
        var mode = plugin.HudEditPreviewMode;
        var items = SelectedCustomElements()
            .Where(id => !IsLocked(id))
            .Select(id => (Id: id, Bounds: HudLayout.Resolve(plugin.Configuration, id, origin, canvas, mode)))
            .OrderBy(item => horizontal ? item.Bounds.Position.X : item.Bounds.Position.Y)
            .ToList();
        if (items.Count < 3)
            return;

        plugin.LayoutHistory.Record(equalSpacing
            ? (horizontal ? "Equal horizontal spacing" : "Equal vertical spacing")
            : (horizontal ? "Distribute horizontally" : "Distribute vertically"), () =>
        {
            if (!equalSpacing)
            {
                var firstCenter = horizontal
                    ? items[0].Bounds.Position.X + items[0].Bounds.Size.X * 0.5f
                    : items[0].Bounds.Position.Y + items[0].Bounds.Size.Y * 0.5f;
                var lastCenter = horizontal
                    ? items[^1].Bounds.Position.X + items[^1].Bounds.Size.X * 0.5f
                    : items[^1].Bounds.Position.Y + items[^1].Bounds.Size.Y * 0.5f;
                var step = (lastCenter - firstCenter) / (items.Count - 1);
                for (var i = 1; i < items.Count - 1; i++)
                {
                    var item = items[i];
                    var targetCenter = firstCenter + step * i;
                    var position = horizontal
                        ? new Vector2(targetCenter - item.Bounds.Size.X * 0.5f, item.Bounds.Position.Y)
                        : new Vector2(item.Bounds.Position.X, targetCenter - item.Bounds.Size.Y * 0.5f);
                    HudLayout.Store(plugin.Configuration, item.Id, new HudBounds(position, item.Bounds.Size), origin, canvas, mode);
                }
            }
            else
            {
                var firstStart = horizontal ? items[0].Bounds.Position.X : items[0].Bounds.Position.Y;
                var lastEnd = horizontal
                    ? items[^1].Bounds.Position.X + items[^1].Bounds.Size.X
                    : items[^1].Bounds.Position.Y + items[^1].Bounds.Size.Y;
                var totalSize = items.Sum(item => horizontal ? item.Bounds.Size.X : item.Bounds.Size.Y);
                var gap = (lastEnd - firstStart - totalSize) / (items.Count - 1);
                var cursor = firstStart;
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var position = horizontal
                        ? new Vector2(cursor, item.Bounds.Position.Y)
                        : new Vector2(item.Bounds.Position.X, cursor);
                    HudLayout.Store(plugin.Configuration, item.Id, new HudBounds(position, item.Bounds.Size), origin, canvas, mode);
                    cursor += (horizontal ? item.Bounds.Size.X : item.Bounds.Size.Y) + gap;
                }
            }
        });
        SaveLayoutOperation();
    }

    private void DrawElementDockMirroring()
    {
        ImGui.TextWrapped($"Mirror {plugin.GetHudElementLabel(selectedId)} without replacing unrelated elements.");
        var current = HudModeProfileService.Normalize(plugin.HudEditPreviewMode);
        foreach (var destination in HudModeProfileService.EditableModes)
        {
            if (destination == current)
                continue;
            if (ImGui.Button($"COPY TO {HudModeProfileService.Label(destination).ToUpperInvariant()}##mirror-{selectedId}-{destination}", new Vector2(-1f, 28f)))
                MirrorSelectedElement(destination, false);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Copy only this element's position, size, visibility, and lock state into the destination dock.");
        }
        if (ImGui.Button("MIRROR THIS ELEMENT ACROSS ALL DOCKS", new Vector2(-1f, 30f)))
            MirrorSelectedElement(UiMode.Auto, true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy only this element into every other dock without replacing unrelated layout data.");
    }

    private void MirrorSelectedElement(UiMode destination, bool all)
    {
        if (!HasPrimaryCustomSelection)
            return;
        if (all)
            plugin.LayoutRecovery.Create("Before Cross-Dock Element Copy");
        var source = plugin.HudEditPreviewMode;
        plugin.LayoutHistory.Record("Mirror element across docks", () =>
        {
            if (all)
            {
                foreach (var mode in HudModeProfileService.EditableModes)
                {
                    if (mode != source)
                        HudModeProfileService.CopyElement(plugin.Configuration, source, mode, selectedId);
                }
            }
            else
            {
                HudModeProfileService.CopyElement(plugin.Configuration, source, destination, selectedId);
            }
        });
        SaveLayoutOperation();
    }

    private void DrawSnappingAndGuideControls()
    {
        if (!ImGui.CollapsingHeader("Snapping and Safe Areas"))
            return;

        var snapping = plugin.Configuration.HudEditorSnappingEnabled;
        if (ImGui.Checkbox("Smart snapping", ref snapping))
        {
            plugin.Configuration.HudEditorSnappingEnabled = snapping;
            dirty = true;
        }
        var tolerance = plugin.Configuration.HudEditorSnapTolerance;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##snap-tolerance", ref tolerance, 2f, 32f, "Tolerance %.0f px"))
        {
            plugin.Configuration.HudEditorSnapTolerance = tolerance;
            dirty = true;
        }
        ImGui.TextDisabled("Hold Ctrl while dragging to bypass snapping temporarily.");

        DrawConfigCheckbox("Screen bounds", plugin.Configuration.ShowHudEditorScreenBounds, value => plugin.Configuration.ShowHudEditorScreenBounds = value);
        DrawConfigCheckbox("General safe area", plugin.Configuration.ShowHudEditorGeneralSafeArea, value => plugin.Configuration.ShowHudEditorGeneralSafeArea = value);
        DrawConfigCheckbox("Stream-safe 16:9 area", plugin.Configuration.ShowHudEditorStreamSafeArea, value => plugin.Configuration.ShowHudEditorStreamSafeArea = value);
        DrawConfigCheckbox("Ultrawide-safe center region", plugin.Configuration.ShowHudEditorUltrawideSafeArea, value => plugin.Configuration.ShowHudEditorUltrawideSafeArea = value);
        DrawConfigCheckbox("Horizontal and vertical center", plugin.Configuration.ShowHudEditorCenterGuides, value => plugin.Configuration.ShowHudEditorCenterGuides = value);
    }

    private void DrawConfigCheckbox(string label, bool value, Action<bool> apply)
    {
        var copy = value;
        if (ImGui.Checkbox(label, ref copy))
        {
            apply(copy);
            dirty = true;
        }
    }

    private void DrawThreeButtonRow(params (string Label, Action Action)[] buttons)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = MathF.Max(1f, (ImGui.GetContentRegionAvail().X - spacing * 2f) / 3f);
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            if (ImGui.Button(buttons[i].Label, new Vector2(width, 28f)))
                buttons[i].Action();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(TooltipForStudioCommand(buttons[i].Label));
        }
    }

    private void DrawTwoButtonRow(params (string Label, Action Action)[] buttons)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = MathF.Max(1f, (ImGui.GetContentRegionAvail().X - spacing) * 0.5f);
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            if (ImGui.Button(buttons[i].Label, new Vector2(width, 28f)))
                buttons[i].Action();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(TooltipForStudioCommand(buttons[i].Label));
        }
    }

    private static string TooltipForStudioCommand(string label) => label switch
    {
        "LEFT" => "Align every unlocked selection to the primary element's left edge.",
        "H-CENTER" => "Align every unlocked selection to the primary element's horizontal center.",
        "RIGHT" => "Align every unlocked selection to the primary element's right edge.",
        "TOP" => "Align every unlocked selection to the primary element's top edge.",
        "V-MIDDLE" => "Align every unlocked selection to the primary element's vertical middle.",
        "BOTTOM" => "Align every unlocked selection to the primary element's bottom edge.",
        "DISTRIBUTE H" => "Distribute unlocked element centers evenly from left to right.",
        "DISTRIBUTE V" => "Distribute unlocked element centers evenly from top to bottom.",
        "EQUAL H SPACING" => "Create equal horizontal gaps while preserving every element's size.",
        "EQUAL V SPACING" => "Create equal vertical gaps while preserving every element's size.",
        _ => label,
    };

    private void SaveLayoutOperation()
    {
        plugin.SaveConfiguration();
        plugin.NativeHudVisibility.RefreshNow();
        dirty = false;
    }
}
