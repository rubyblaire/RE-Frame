using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class HotbarSlotEditorWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly ISharedImmediateTexture logoTexture;
    private readonly List<SlotHitbox> hitboxes = new(64);
    private HotbarSlotReference? pressedSlot;
    private Vector2 pressPosition;
    private bool dragging;
    private HudBounds crossBounds;
    private float scale;

    private Vector2 palettePosition;
    private Vector2 paletteSize;
    private RectF paletteBounds;
    private bool paletteInitialized;
    private bool movingPalette;
    private string paletteSearch = string.Empty;
    private HotbarPaletteCategory paletteCategory = HotbarPaletteCategory.Actions;

    private int combatBarPage;
    private bool keybindMode;
    private int keybindBindingIndex;
    private HotbarSlotReference? keybindCaptureSlot;
    private int keybindCaptureReadyFrame = -1;
    private PendingKeybindChange? pendingKeybindChange;
    private string keybindStatus = "Choose Keybinds, click a combat, pet, or utility slot, then press the key you want.";

    public HotbarSlotEditorWindow(Plugin plugin)
        : base("RE:Frame Bar Edit###REFrameHotbarSlotEditor",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        var logoPath = Path.Combine(
            Plugin.PluginInterface.AssemblyLocation.DirectoryName!,
            "Assets",
            "REFrameLogo.png");
        logoTexture = Plugin.TextureProvider.GetFromFile(logoPath);
        IsClickthrough = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0f;
        IsOpen = false;
    }

    public override bool DrawConditions()
        => plugin.HotbarEditing.IsEnabled &&
           Plugin.ClientState.IsLoggedIn &&
           !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing;

    public override void PreDraw()
    {
        IsClickthrough = false;


        var canvas = plugin.GetRenderedHudCanvas();
        Position = canvas.Origin;
        PositionCondition = ImGuiCond.Always;
        Size = canvas.Size;
        SizeCondition = ImGuiCond.Always;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw()
    {
        var origin = ImGui.GetWindowPos();
        var canvasSize = ImGui.GetWindowSize();
        var draw = ImGui.GetWindowDrawList();
        scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);

        if (keybindMode)
            RequestKeybindInputOwnership();

        PreparePalette(origin, canvasSize);
        var controllerMode = plugin.CrossHotbarState.IsControllerUser;
        var workspace = ResolveWorkspaceStage(origin, canvasSize);
        DrawWorkspaceBackdrop(draw, origin, canvasSize);
        DrawWorkspaceStage(draw, workspace, controllerMode);

        plugin.HotbarEditing.BeginInputFrame();
        var mouse = ImGui.GetMousePos();
        plugin.BarInputDiagnostics.BeginEditorFrame(
            ImGui.GetFrameCount(),
            mouse,
            new HudCanvasInfo(origin, canvasSize),
            IsPointInsidePalette(mouse),
            ImGui.IsMouseDown(ImGuiMouseButton.Left),
            ImGui.IsMouseClicked(ImGuiMouseButton.Left),
            ImGui.IsMouseReleased(ImGuiMouseButton.Left));

        hitboxes.Clear();
        var crossSet = Math.Clamp(plugin.HotbarEditing.CrossHotbarSet, 1, 8);

        if (controllerMode)
        {


            if (!dragging && pressedSlot is null && !plugin.HotbarEditing.IsDraggingAction &&
                plugin.CrossHotbarState.TryGetState(out var liveCrossHotbar))
            {
                plugin.HotbarEditing.SetCrossHotbarSet(liveCrossHotbar.SetNumber);
                crossSet = liveCrossHotbar.SetNumber;
            }

            var utilityBounds = ResolveUtilityWorkspaceBounds(workspace, 0);
            var utilityTwoBounds = ResolveUtilityWorkspaceBounds(workspace, 1);
            crossBounds = ResolveControllerWorkspaceBounds(workspace, utilityBounds);
            HudRenderer.DrawEditableCrossHotbar(plugin, draw, crossBounds, crossSet);
            BuildCrossHotbarHitboxes(crossBounds, origin, crossSet);
            if (keybindMode)
            {
                var petBounds = ResolveControllerPetWorkspaceBounds(workspace, utilityBounds);
                DrawNormalBar(
                    draw,
                    petBounds,
                    new EditorCombatBar("PET BAR · FFXIV COMMANDS", HudElementIds.PetBar, ReframeHotbarIds.PetBar, null),
                    origin);
            }
            DrawUtilityBar(draw, utilityBounds, origin, false);
            DrawUtilityBar(draw, utilityTwoBounds, origin, true);
        }
        else
        {


            var allBars = GetCombatEditorBars();
            var pageCount = Math.Max(1, (int)Math.Ceiling(allBars.Count / 3f));
            combatBarPage = Math.Clamp(combatBarPage, 0, pageCount - 1);
            var pageBars = allBars.Skip(combatBarPage * 3).Take(3).ToArray();
            var utilityBounds = ResolveUtilityWorkspaceBounds(workspace, 0);
            var utilityTwoBounds = ResolveUtilityWorkspaceBounds(workspace, 1);
            var bars = ResolveKeyboardWorkspaceBars(workspace, utilityBounds, pageBars);
            for (var index = 0; index < pageBars.Length; index++)
                DrawNormalBar(draw, bars[index], pageBars[index], origin);
            DrawUtilityBar(draw, utilityBounds, origin, false);
            DrawUtilityBar(draw, utilityTwoBounds, origin, true);
        }


        var hoveredSlot = CaptureSlotInputs(origin);
        var captureInputConsumed = UpdateKeybindCapture();
        if (!captureInputConsumed)
            HandleMouse(hoveredSlot, mouse, origin, draw);
        DrawSlotHighlights(hoveredSlot, origin, draw);

        if (controllerMode && !dragging && pressedSlot is null && !plugin.HotbarEditing.IsDraggingAction &&
            Contains(crossBounds, mouse) && !IsPointInsidePalette(mouse))
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (MathF.Abs(wheel) > 0.01f)
            {
                var direction = wheel > 0f ? 1 : -1;
                var nextSet = ((crossSet - 1 + direction + 8) % 8) + 1;
                plugin.HotbarEditing.SetCrossHotbarSet(nextSet);
                crossSet = nextSet;
            }
        }

        DrawEditBanner(draw, origin, canvasSize, controllerMode, crossSet);
        DrawActionDragPreview(draw, mouse);


        DrawIntegratedPalette(origin, canvasSize, controllerMode);

        plugin.BarInputDiagnostics.RecordSelection(
            plugin.HotbarEditing.DraggedActionId,
            pressedSlot ?? plugin.HotbarEditing.DraggedSlot);
    }

    public bool IsPointInsidePalette(Vector2 point)
        => paletteInitialized && paletteBounds.Contains(point);

    private void PreparePalette(Vector2 origin, Vector2 canvasSize)
    {


        var widthLimit = MathF.Max(360f, canvasSize.X * 0.42f);
        var heightLimit = MathF.Max(340f, canvasSize.Y - 132f);
        paletteSize = new Vector2(
            MathF.Min(MathF.Max(500f, 700f * MathF.Min(scale, 1.35f)), widthLimit),
            MathF.Min(MathF.Max(390f, 500f * MathF.Min(scale, 1.20f)), heightLimit));
        paletteSize = Vector2.Min(paletteSize, Vector2.Max(new Vector2(320f), canvasSize - new Vector2(40f)));

        if (!paletteInitialized)
        {
            palettePosition = plugin.Configuration.BarEditPaletteX >= 0f &&
                              plugin.Configuration.BarEditPaletteY >= 0f
                ? origin + new Vector2(
                    plugin.Configuration.BarEditPaletteX * canvasSize.X,
                    plugin.Configuration.BarEditPaletteY * canvasSize.Y)
                : origin + new Vector2(
                    canvasSize.X - paletteSize.X - 28f,
                    MathF.Max(92f, (canvasSize.Y - paletteSize.Y) * 0.5f));
            paletteInitialized = true;
        }

        ClampPalette(origin, canvasSize);
        paletteBounds = new RectF(palettePosition, palettePosition + paletteSize);
    }

    private void ClampPalette(Vector2 origin, Vector2 canvasSize)
    {
        var margin = 10f;
        var minimum = origin + new Vector2(margin);
        var maximum = origin + canvasSize - paletteSize - new Vector2(margin);
        palettePosition = new Vector2(
            Math.Clamp(palettePosition.X, minimum.X, MathF.Max(minimum.X, maximum.X)),
            Math.Clamp(palettePosition.Y, minimum.Y, MathF.Max(minimum.Y, maximum.Y)));
    }

    private void SavePalettePosition(Vector2 origin, Vector2 canvasSize)
    {
        if (canvasSize.X <= 1f || canvasSize.Y <= 1f)
            return;

        var local = palettePosition - origin;
        plugin.Configuration.BarEditPaletteX = Math.Clamp(local.X / canvasSize.X, 0f, 1f);
        plugin.Configuration.BarEditPaletteY = Math.Clamp(local.Y / canvasSize.Y, 0f, 1f);
        plugin.SaveConfiguration();
    }

    private void DrawIntegratedPalette(Vector2 origin, Vector2 canvasSize, bool controllerMode)
    {
        var theme = plugin.CurrentTheme;
        var lockRequested = false;

        ImGui.SetCursorScreenPos(palettePosition);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f * scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.012f, 0.016f, 0.024f, 0.985f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(
            theme.AccentStrong.X,
            theme.AccentStrong.Y,
            theme.AccentStrong.Z,
            0.78f));

        if (ImGui.BeginChild(
                "##reframe-integrated-action-palette",
                paletteSize,
                true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var lockWidth = 112f * scale;
            var titleHeight = 31f * scale;
            var dragWidth = MathF.Max(180f, ImGui.GetContentRegionAvail().X - lockWidth - 10f * scale);
            var titleStart = ImGui.GetCursorScreenPos();

            ImGui.InvisibleButton("##reframe-palette-drag", new Vector2(dragWidth, titleHeight));
            var titleHovered = ImGui.IsItemHovered();
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f))
            {
                movingPalette = true;
                palettePosition += ImGui.GetIO().MouseDelta;
                ClampPalette(origin, canvasSize);
                paletteBounds = new RectF(palettePosition, palettePosition + paletteSize);
            }

            if (movingPalette && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                movingPalette = false;
                SavePalettePosition(origin, canvasSize);
            }

            var draw = ImGui.GetWindowDrawList();
            var paletteTitle = keybindMode ? "KEYBIND EDITOR" : "HOTBAR PALETTE";
            draw.AddText(
                titleStart + new Vector2(3f * scale, 6f * scale),
                ImGui.ColorConvertFloat4ToU32(theme.AccentStrong),
                paletteTitle);
            var jobText = keybindMode
                ? "RE:Frame keybinds"
                : paletteCategory == HotbarPaletteCategory.Actions
                    ? plugin.HotbarEditing.CurrentJobLabel
                    : PaletteCategoryLabel(paletteCategory);
            var actionTitleSize = ImGui.CalcTextSize(paletteTitle);
            draw.AddText(
                titleStart + new Vector2(actionTitleSize.X + 18f * scale, 6f * scale),
                ImGui.ColorConvertFloat4ToU32(theme.Muted),
                jobText);

            if (titleHovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

            ImGui.SameLine(0f, 10f * scale);
            if (ImGui.Button("LOCK BARS", new Vector2(lockWidth, titleHeight)))
                lockRequested = true;

            DrawEditorModeTabs();
            if (!controllerMode)
                DrawCombatPageControls();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (keybindMode)
            {
                DrawKeybindPanel(controllerMode);
            }
            else
            {
                DrawPaletteCategoryTabs();
                ImGui.Spacing();

                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint(
                    "##reframe-integrated-action-search",
                    PaletteSearchHint(paletteCategory),
                    ref paletteSearch,
                    96);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var commands = plugin.HotbarEditing.SearchCommands(paletteCategory, paletteSearch, 300);
                if (commands.Count == 0)
                {
                    ImGui.TextDisabled(string.IsNullOrWhiteSpace(paletteSearch)
                        ? PaletteEmptyText(paletteCategory)
                        : $"No {PaletteCategoryLabel(paletteCategory).ToLowerInvariant()} match that search.");
                }
                else
                {
                    if (ImGui.BeginChild("##reframe-integrated-action-scroll", Vector2.Zero, false))
                    {
                        var availableWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
                        var desiredCardWidth = 108f * scale;
                        var spacing = 8f * scale;
                        var columns = Math.Clamp(
                            (int)MathF.Floor((availableWidth + spacing) / (desiredCardWidth + spacing)),
                            3,
                            8);
                        var cardWidth = MathF.Max(88f * scale, (availableWidth - (columns - 1) * spacing) / columns);
                        var cardHeight = MathF.Max(100f * scale, cardWidth * 0.96f);

                        for (var index = 0; index < commands.Count; index++)
                        {
                            DrawPaletteActionCard(commands[index], new Vector2(cardWidth, cardHeight));
                            if ((index + 1) % columns != 0)
                                ImGui.SameLine(0f, spacing);
                        }
                    }
                    ImGui.EndChild();
                }
            }
        }
        ImGui.EndChild();

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);

        if (lockRequested)
            plugin.SetBarEditMode(false);
    }

    private List<EditorCombatBar> GetCombatEditorBars()
    {
        var bars = new List<EditorCombatBar>
        {
            new("BAR 1", HudElementIds.ActionBarOne, 0u, null),
            new("BAR 2", HudElementIds.ActionBarTwo, 1u, null),
            new("BAR 3", HudElementIds.ActionBarThree, 2u, null),
        };
        if (keybindMode)
            bars.Add(new EditorCombatBar("PET BAR · FFXIV COMMANDS", HudElementIds.PetBar, ReframeHotbarIds.PetBar, null));
        bars.AddRange(plugin.AdditionalHotbars.CombatBars.Select(bar =>
            new EditorCombatBar(
                bar.IsNativeBacked ? $"{bar.Name} · FFXIV {bar.NativeHotbarId + 1}" : $"{bar.Name} · OVERFLOW",
                bar.ElementId,
                bar.RuntimeHotbarId,
                bar)));
        return bars;
    }

    private void DrawCombatPageControls()
    {
        var bars = GetCombatEditorBars();
        var pageCount = Math.Max(1, (int)Math.Ceiling(bars.Count / 3f));
        combatBarPage = Math.Clamp(combatBarPage, 0, pageCount - 1);
        ImGui.Spacing();
        var buttonWidth = 84f * scale;
        var firstPage = combatBarPage == 0;
        if (firstPage)
            ImGui.BeginDisabled();
        if (ImGui.Button("◀ BARS", new Vector2(buttonWidth, 28f * scale)))
            combatBarPage--;
        if (firstPage)
            ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled($"Bar page {combatBarPage + 1} / {pageCount}  •  {bars.Count} bars");
        ImGui.SameLine();
        var lastPage = combatBarPage >= pageCount - 1;
        if (lastPage)
            ImGui.BeginDisabled();
        if (ImGui.Button("BARS ▶", new Vector2(buttonWidth, 28f * scale)))
            combatBarPage++;
        if (lastPage)
            ImGui.EndDisabled();
    }

    private void DrawEditorModeTabs()
    {
        var spacing = 8f * scale;
        var width = MathF.Max(120f, (ImGui.GetContentRegionAvail().X - spacing) * 0.5f);
        if (DrawEditorModeTab("ACTIONS", !keybindMode, new Vector2(width, 31f * scale)))
            SetKeybindMode(false);
        ImGui.SameLine(0f, spacing);
        if (DrawEditorModeTab("KEYBINDS", keybindMode, new Vector2(width, 31f * scale)))
            SetKeybindMode(true);
    }

    private bool DrawEditorModeTab(string label, bool active, Vector2 size)
    {
        var theme = plugin.CurrentTheme;
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(
                theme.Accent.X,
                theme.Accent.Y,
                theme.Accent.Z,
                0.45f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(
                theme.AccentStrong.X,
                theme.AccentStrong.Y,
                theme.AccentStrong.Z,
                0.52f));
        }

        var pressed = ImGui.Button($"{label}##reframe-editor-mode", size);
        if (active)
            ImGui.PopStyleColor(2);
        return pressed;
    }

    private void SetKeybindMode(bool enabled)
    {
        if (keybindMode == enabled)
            return;

        keybindMode = enabled;
        if (enabled)
            RequestKeybindInputOwnership();
        pressedSlot = null;
        dragging = false;
        plugin.HotbarEditing.CancelActionDrag();
        plugin.HotbarEditing.CancelSlotDrag();
        CancelKeybindCapture(clearPending: true, restoreNative: true);
        keybindStatus = enabled
            ? "Click a combat, pet, or utility slot. Native keyboard bars are safely disarmed first; Pet and overflow slots bind directly in RE:Frame."
            : "Action placement restored.";
    }

    private static void RequestKeybindInputOwnership()
    {


        ImGui.SetNextFrameWantCaptureKeyboard(true);
        ImGui.SetNextFrameWantCaptureMouse(true);
    }

    private void DrawKeybindPanel(bool controllerMode)
    {
        if (ImGui.BeginChild("##reframe-native-keybind-scroll", Vector2.Zero, false))
            DrawKeybindPanelContents(controllerMode);
        ImGui.EndChild();
    }

    private void DrawKeybindPanelContents(bool controllerMode)
    {
        var theme = plugin.CurrentTheme;
        ImGui.TextWrapped("Click a combat, pet, or utility slot to begin. RE:Frame captures the replacement and binds it directly to that displayed slot. Native-backed and Pet Bar slots use FFXIV's exact slot execution path; overflow slots use the same RE:Frame action executor as a deliberate mouse click.");
        ImGui.TextColored(theme.Warning, "For native bars, only the selected slot's matching native column is disarmed. Unrelated FFXIV controls are never removed; Bard Performance bindings are preserved and RE:Frame pauses while Performing.");
        ImGui.Spacing();

        if (controllerMode)
        {
            ImGui.TextColored(theme.Warning, "Controller note");
            ImGui.TextWrapped("Cross hotbars use one shared trigger/directional control layout rather than a separate key for every visible slot. Their controller mapping is left untouched; the Pet Bar and both utility bars can still be bound here.");
            ImGui.Spacing();
        }

        var bindingSpacing = 8f * scale;
        var bindingWidth = MathF.Max(110f, (ImGui.GetContentRegionAvail().X - bindingSpacing) * 0.5f);
        if (DrawEditorModeTab("PRIMARY", keybindBindingIndex == 0, new Vector2(bindingWidth, 30f * scale)))
        {
            keybindBindingIndex = 0;
            CancelKeybindCapture(clearPending: true, restoreNative: true);
            keybindStatus = "Primary binding selected. Click a combat, pet, or utility slot to begin capture.";
        }
        ImGui.SameLine(0f, bindingSpacing);
        if (DrawEditorModeTab("SECONDARY", keybindBindingIndex == 1, new Vector2(bindingWidth, 30f * scale)))
        {
            keybindBindingIndex = 1;
            CancelKeybindCapture(clearPending: true, restoreNative: true);
            keybindStatus = "Secondary binding selected. Click a combat, pet, or utility slot to begin capture.";
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (plugin.HotbarEditing.SelectedSlot is not { } selected)
        {
            ImGui.TextColored(theme.AccentStrong, "NO SLOT SELECTED");
            ImGui.TextWrapped("Click any combat, pet, or utility slot. Empty slots can be bound too. Native keyboard bars show READY after the safe native disarm is verified; Pet and overflow slots begin direct RE:Frame capture immediately.");
        }
        else
        {
            ImGui.TextColored(theme.AccentStrong, selected.Label.ToUpperInvariant());
            if (plugin.HotbarEditing.TryGetSnapshot(selected, out var selectedSnapshot))
                ImGui.TextUnformatted(selectedSnapshot.IsEmpty ? "Empty slot" : selectedSnapshot.DisplayName);
            if (NativeHotbarKeybindService.IsBindableSlot(selected))
            {
                var primary = NativeHotbarKeybindService.GetBindingLabel(selected, 0, false);
                var secondary = NativeHotbarKeybindService.GetBindingLabel(selected, 1, false);
                ImGui.TextUnformatted($"Primary:   {primary}");
                ImGui.TextUnformatted($"Secondary: {secondary}");

                ImGui.Spacing();
                var rebindLabel = keybindCaptureSlot == selected
                    ? "WAITING FOR INPUT..."
                    : $"REBIND {(keybindBindingIndex == 0 ? "PRIMARY" : "SECONDARY")}";
                if (ImGui.Button(rebindLabel, new Vector2(-1f, 34f * scale)))
                    BeginKeybindCapture(selected);

                var half = MathF.Max(110f, (ImGui.GetContentRegionAvail().X - bindingSpacing) * 0.5f);
                if (ImGui.Button("CLEAR SELECTED", new Vector2(half, 31f * scale)))
                    ClearSelectedBinding();
                ImGui.SameLine(0f, bindingSpacing);


                var canUndo = NativeHotbarKeybindService.CanUndo;
                if (!canUndo)
                    ImGui.BeginDisabled();
                if (ImGui.Button("UNDO LAST", new Vector2(half, 31f * scale)) && canUndo)
                    UndoLastKeybindChange();
                if (!canUndo)
                    ImGui.EndDisabled();
            }
            else
            {
                ImGui.TextWrapped("This is a Cross Hotbar slot. FFXIV binds controller directions and triggers globally, so there is no safe one-key-per-XHB-slot record to replace here.");
            }
        }

        ImGui.Spacing();
        DrawKeybindStatusBox();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(theme.AccentStrong, "DIAGNOSTIC TRACE");
        ImGui.TextWrapped("These entries prove capture, storage, physical press detection, and direct slot execution independently. They are event-driven and rate-limited.");
        var trace = plugin.BarInputDiagnostics.KeybindTrace;
        if (trace.Count == 0)
        {
            ImGui.TextDisabled("No keybind diagnostic event recorded yet.");
        }
        else
        {
            foreach (var entry in trace.Skip(Math.Max(0, trace.Count - 8)))
                ImGui.TextWrapped(entry);
        }
        if (ImGui.SmallButton("CLEAR TRACE##reframe-keybind-trace"))
            plugin.BarInputDiagnostics.ClearKeybindTrace();

        if (pendingKeybindChange is { } pending)
        {
            var reframeConflicts = pending.Conflicts.Where(conflict => conflict.CanReplace).ToArray();
            var performanceConflicts = pending.Conflicts.Where(conflict => conflict.IsPreservedPerformance).ToArray();
            var blockedNativeConflicts = pending.Conflicts.Where(conflict => conflict.BlocksSafeBinding).ToArray();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(theme.Warning, blockedNativeConflicts.Length > 0 ? "NATIVE BINDING PROTECTED" : "BINDING CONFLICT");
            ImGui.TextWrapped($"{NativeHotbarKeybindService.FormatChord(pending.Chord, false)} is already assigned to:");
            foreach (var conflict in pending.Conflicts.Take(8))
            {
                var column = conflict.BindingIndex == 0 ? "Primary" : "Secondary";
                var ownership = conflict.Source switch
                {
                    NativeKeybindConflictSource.Reframe => "RE:Frame",
                    NativeKeybindConflictSource.NativePerformance => "FFXIV Performance — preserved",
                    _ => "FFXIV native — protected",
                };
                ImGui.TextUnformatted($"• {conflict.DisplayName} ({column}; {ownership})");
            }
            if (pending.Conflicts.Count > 8)
                ImGui.TextDisabled($"...and {pending.Conflicts.Count - 8} more.");

            if (performanceConflicts.Length > 0)
                ImGui.TextWrapped("Performance controls will remain bound. RE:Frame direct hotbar execution is disabled while the Performing condition is active.");
            if (blockedNativeConflicts.Length > 0)
                ImGui.TextWrapped("RE:Frame will not erase unrelated native controls. Cancel and choose another key, or change that native binding yourself in FFXIV.");

            ImGui.Spacing();
            var half = MathF.Max(110f, (ImGui.GetContentRegionAvail().X - bindingSpacing) * 0.5f);
            if (blockedNativeConflicts.Length == 0)
            {
                var commitLabel = reframeConflicts.Length > 0
                    ? "BIND & REPLACE RE:FRAME"
                    : "BIND & PRESERVE NATIVE";
                if (ImGui.Button(commitLabel, new Vector2(half, 33f * scale)))
                    CommitPendingKeybindChange();
                ImGui.SameLine(0f, bindingSpacing);
            }

            var cancelWidth = blockedNativeConflicts.Length > 0 ? -1f : half;
            if (ImGui.Button("CANCEL & RESTORE", new Vector2(cancelWidth, 33f * scale)))
            {
                pendingKeybindChange = null;
                NativeHotbarKeybindService.CancelPreparedCapture();
                keybindStatus = "Binding change cancelled. Restoring the selected slot's original native binding.";
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Click-to-bind • Wait for READY • Esc cancels/restores • Delete/Backspace clears • Mouse 1 selects only");
    }

    private void DrawKeybindStatusBox()
    {
        var theme = plugin.CurrentTheme;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(
            theme.PanelAlt.X,
            theme.PanelAlt.Y,
            theme.PanelAlt.Z,
            0.88f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(
            theme.Accent.X,
            theme.Accent.Y,
            theme.Accent.Z,
            0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * scale, 9f * scale));
        if (ImGui.BeginChild(
                "##reframe-keybind-status",
                new Vector2(0f, 58f * scale),
                true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextWrapped(keybindStatus);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void DrawPaletteCategoryTabs()
    {
        var categories = Enum.GetValues<HotbarPaletteCategory>();
        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = MathF.Max(66f * scale, (available - spacing * (categories.Length - 1)) / categories.Length);

        for (var index = 0; index < categories.Length; index++)
        {
            var category = categories[index];
            var selected = paletteCategory == category;
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(plugin.CurrentTheme.Accent.X, plugin.CurrentTheme.Accent.Y, plugin.CurrentTheme.Accent.Z, 0.72f));

            if (ImGui.Button(PaletteCategoryTab(category), new Vector2(width, 27f * scale)))
            {
                paletteCategory = category;
                paletteSearch = string.Empty;
                plugin.HotbarEditing.CancelActionDrag();
            }

            if (selected)
                ImGui.PopStyleColor();
            if (index + 1 < categories.Length)
                ImGui.SameLine(0f, spacing);
        }
    }

    private static string PaletteCategoryTab(HotbarPaletteCategory category)
        => category switch
        {
            HotbarPaletteCategory.Actions => "ACTIONS",
            HotbarPaletteCategory.Emotes => "EMOTES",
            HotbarPaletteCategory.Mounts => "MOUNTS",
            HotbarPaletteCategory.Minions => "MINIONS",
            HotbarPaletteCategory.Macros => "MACROS",
            _ => category.ToString().ToUpperInvariant(),
        };

    private static string PaletteCategoryLabel(HotbarPaletteCategory category)
        => category switch
        {
            HotbarPaletteCategory.Actions => "Current-job actions",
            HotbarPaletteCategory.Emotes => "Available emotes",
            HotbarPaletteCategory.Mounts => "Available mounts",
            HotbarPaletteCategory.Minions => "Available minions",
            HotbarPaletteCategory.Macros => "Individual and shared macros",
            _ => "Hotbar commands",
        };

    private string PaletteSearchHint(HotbarPaletteCategory category)
        => category switch
        {
            HotbarPaletteCategory.Actions when plugin.HotbarEditing.CurrentJobLabel.Contains("Shared Crafting Actions", StringComparison.OrdinalIgnoreCase)
                => "Search all crafting actions...",
            HotbarPaletteCategory.Actions => "Search current-job actions...",
            HotbarPaletteCategory.Emotes => "Search emotes...",
            HotbarPaletteCategory.Mounts => "Search mounts...",
            HotbarPaletteCategory.Minions => "Search minions...",
            HotbarPaletteCategory.Macros => "Search named macros...",
            _ => "Search hotbar commands...",
        };

    private static string PaletteEmptyText(HotbarPaletteCategory category)
        => category switch
        {
            HotbarPaletteCategory.Actions => "No learned player actions were found for the current job.",
            HotbarPaletteCategory.Emotes => "No available emotes were found.",
            HotbarPaletteCategory.Mounts => "No available mounts were found.",
            HotbarPaletteCategory.Minions => "No available minions were found.",
            HotbarPaletteCategory.Macros => "No named individual or shared macros were found.",
            _ => "No hotbar commands were found.",
        };

    private void DrawPaletteActionCard(HotbarActionOption action, Vector2 size)
    {
        var theme = plugin.CurrentTheme;
        var start = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##reframe-integrated-action-{action.CommandType}-{action.ActionId}", size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var selected = plugin.HotbarEditing.IsDraggingAction &&
                       plugin.HotbarEditing.DraggedActionType == action.CommandType &&
                       plugin.HotbarEditing.DraggedActionId == action.ActionId;

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) ||
            (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f)))
        {
            plugin.HotbarEditing.BeginActionDrag(action);
        }

        var draw = ImGui.GetWindowDrawList();
        var end = start + size;
        var background = hovered || active
            ? new Vector4(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, 0.30f)
            : new Vector4(theme.PanelAlt.X, theme.PanelAlt.Y, theme.PanelAlt.Z, 0.90f);
        var border = selected
            ? theme.Success
            : hovered
                ? theme.AccentStrong
                : new Vector4(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, 0.42f);

        draw.AddRectFilled(start, end, ImGui.ColorConvertFloat4ToU32(background), 9f * scale);
        draw.AddRect(start, end, ImGui.ColorConvertFloat4ToU32(border), 9f * scale,
            ImDrawFlags.None, selected ? MathF.Max(2f, 2.4f * scale) : MathF.Max(1f, 1.2f * scale));

        var iconSize = Math.Clamp(size.X * 0.46f, 36f * scale, 58f * scale);
        var iconMin = start + new Vector2((size.X - iconSize) * 0.5f, 8f * scale);
        var iconMax = iconMin + new Vector2(iconSize);
        var wrap = HudRenderer.GetGameIcon(action.IconId);
        if (wrap is not null)
            draw.AddImage(wrap.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One, 0xFFFFFFFF);

        var label = Ellipsize(action.Name, MathF.Max(40f, size.X - 12f * scale));
        var labelSize = ImGui.CalcTextSize(label);
        draw.AddText(
            new Vector2(start.X + (size.X - labelSize.X) * 0.5f, iconMax.Y + 6f * scale),
            ImGui.ColorConvertFloat4ToU32(theme.Text),
            label);

        var levelText = action.CommandType switch
        {
            FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType.Emote => "EMOTE",
            FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType.Mount => "MOUNT",
            FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType.Companion => "MINION",
            FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType.Macro => "MACRO",
            _ when action.IsRoleAction => $"ROLE • LV {action.RequiredLevel}",
            _ => $"LV {action.RequiredLevel}",
        };
        var levelSize = ImGui.CalcTextSize(levelText);
        draw.AddText(
            new Vector2(start.X + (size.X - levelSize.X) * 0.5f, end.Y - levelSize.Y - 7f * scale),
            ImGui.ColorConvertFloat4ToU32(theme.Muted),
            levelText);

        if (hovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(action.Name);
            ImGui.TextDisabled("Drag onto any RE:Frame hotbar slot, or click once and then click a slot.");
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

    private void DrawNormalBar(
        ImDrawListPtr draw,
        HudBounds bounds,
        EditorCombatBar bar,
        Vector2 origin)
    {
        if (bar.Additional is { } additional)
            HudRenderer.DrawEditableAdditionalActionBar(plugin, draw, bounds, additional);
        else
            HudRenderer.DrawEditableActionBar(plugin, draw, bounds, bar.RuntimeHotbarId);
        BuildActionBarHitboxes(bounds, origin, bar.RuntimeHotbarId, bar.ElementId);

        var labelSize = ImGui.CalcTextSize(bar.Label);
        var labelPosition = new Vector2(
            bounds.Position.X - labelSize.X - MathF.Max(12f, 16f * scale),
            bounds.Position.Y + (bounds.Size.Y - labelSize.Y) * 0.5f);
        draw.AddText(
            labelPosition,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                plugin.CurrentTheme.AccentStrong.X,
                plugin.CurrentTheme.AccentStrong.Y,
                plugin.CurrentTheme.AccentStrong.Z,
                0.92f)),
            bar.Label);

        if (bar.RuntimeHotbarId == 0u)
        {
            var slotOne = hitboxes.Find(hitbox => hitbox.Slot.HotbarId == 0u && hitbox.Slot.SlotId == 0u);
            if (slotOne.Size > 0f)
            {
                var slotBounds = new HudBounds(origin + slotOne.LocalPosition, new Vector2(slotOne.Size));
                plugin.BarInputDiagnostics.RecordBarOneGeometry(bounds, slotBounds, Contains(slotBounds, ImGui.GetMousePos()));
            }
        }
    }

    private void DrawUtilityBar(ImDrawListPtr draw, HudBounds bounds, Vector2 origin, bool second)
    {
        if (second)
            HudRenderer.DrawEditableSecondUtilityBar(plugin, draw, bounds);
        else
            HudRenderer.DrawEditableUtilityBar(plugin, draw, bounds);
        BuildUtilityBarHitboxes(bounds, origin, second ? ReframeHotbarIds.SecondUtility : 5u);

        var label = second ? "UTILITY 2 · RE:FRAME" : "UTILITY 1 · FFXIV HOTBAR 6";
        var labelSize = ImGui.CalcTextSize(label);
        var labelPosition = new Vector2(
            bounds.Position.X + (bounds.Size.X - labelSize.X) * 0.5f,
            bounds.Position.Y - labelSize.Y - MathF.Max(9f, 12f * scale));
        draw.AddText(
            labelPosition,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                plugin.CurrentTheme.AccentStrong.X,
                plugin.CurrentTheme.AccentStrong.Y,
                plugin.CurrentTheme.AccentStrong.Z,
                0.92f)),
            label);
    }

    private SlotHitbox? CaptureSlotInputs(Vector2 origin)
    {
        SlotHitbox? hovered = null;
        foreach (var hitbox in hitboxes)
        {
            var min = origin + hitbox.LocalPosition;
            var rect = new RectF(min, min + new Vector2(hitbox.Size));


            var isBarOneSlotOne = hitbox.Slot.HotbarId == 0u && hitbox.Slot.SlotId == 0u;
            if (rect.Intersects(paletteBounds))
            {
                if (isBarOneSlotOne)
                    plugin.BarInputDiagnostics.RecordBarOneSlotOneItem(false, false, true);
                continue;
            }

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton(
                $"##reframe-edit-slot-{hitbox.Slot.HotbarId}-{hitbox.Slot.SlotId}",
                new Vector2(hitbox.Size));

            var itemHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            if (isBarOneSlotOne)
                plugin.BarInputDiagnostics.RecordBarOneSlotOneItem(true, itemHovered, false);

            if (!itemHovered)
                continue;

            hovered = hitbox;
            plugin.HotbarEditing.RegisterHoveredSlot(hitbox.Slot);
        }

        return hovered;
    }

    private void BeginKeybindCapture(HotbarSlotReference slot)
    {
        plugin.HotbarEditing.Select(slot);
        pressedSlot = null;
        dragging = false;
        plugin.HotbarEditing.CancelActionDrag();
        plugin.HotbarEditing.CancelSlotDrag();
        pendingKeybindChange = null;

        if (keybindCaptureSlot is { } activeSlot && activeSlot != slot)
        {
            CancelKeybindCapture(clearPending: true, restoreNative: true);
            keybindStatus = $"Restoring {activeSlot.Label}. Click {slot.Label} again when the restore completes.";
            return;
        }

        if (!NativeHotbarKeybindService.IsBindableSlot(slot))
        {
            keybindCaptureSlot = null;
            keybindCaptureReadyFrame = -1;
            keybindStatus = "Cross Hotbar slots share FFXIV's controller trigger/directional bindings and cannot receive an independent keyboard key here.";
            return;
        }

        if (!NativeHotbarKeybindService.TryPrepareCapture(slot, keybindBindingIndex, out var message))
        {
            keybindStatus = message;
            Plugin.ChatGui.PrintError(message);
            return;
        }

        keybindCaptureSlot = slot;
        keybindCaptureReadyFrame = ImGui.GetFrameCount() + 2;
        keybindStatus = message;
        plugin.BarInputDiagnostics.RecordInputEvent($"Native keybind capture preparation queued for {slot.Label}");
        plugin.BarInputDiagnostics.RecordKeybindStage("CAPTURE STARTED", slot.Label);
    }

    private void CancelKeybindCapture(bool clearPending, bool restoreNative = true)
    {
        if (restoreNative && (keybindCaptureSlot is not null || pendingKeybindChange is not null))
            NativeHotbarKeybindService.CancelPreparedCapture();

        keybindCaptureSlot = null;
        keybindCaptureReadyFrame = -1;
        if (clearPending)
            pendingKeybindChange = null;
    }

    private bool UpdateKeybindCapture()
    {
        if (!keybindMode || pendingKeybindChange is not null || ImGui.GetIO().WantTextInput)
            return false;

        if (keybindCaptureSlot is not { } slot)
            return false;


        ImGui.SetNextFrameWantCaptureKeyboard(true);
        ImGui.SetNextFrameWantCaptureMouse(true);

        var phase = NativeHotbarKeybindService.GetCapturePhase(slot, keybindBindingIndex, out var phaseMessage);
        if (!string.IsNullOrWhiteSpace(phaseMessage))
            keybindStatus = phaseMessage;

        if (phase == NativeKeybindCapturePhase.Failed)
        {
            keybindCaptureSlot = null;
            keybindCaptureReadyFrame = -1;
            return false;
        }

        if (phase != NativeKeybindCapturePhase.Ready || ImGui.GetFrameCount() < keybindCaptureReadyFrame)
            return false;

        if (NativeHotbarKeybindService.TryConsumeCaptureControl(out var control))
        {
            if (control == NativeKeybindCaptureControl.Cancel)
            {
                CancelKeybindCapture(clearPending: true, restoreNative: true);
                keybindStatus = "Keybind capture cancelled. Restoring the original native binding.";
            }
            else if (control == NativeKeybindCaptureControl.Clear)
            {
                keybindCaptureSlot = null;
                keybindCaptureReadyFrame = -1;
                ClearSelectedBinding(slot);
            }

            return true;
        }

        if (!NativeHotbarKeybindService.TryCaptureChord(out var chord))
            return false;

        plugin.HotbarEditing.Select(slot);
        keybindCaptureSlot = null;
        keybindCaptureReadyFrame = -1;
        var conflicts = NativeHotbarKeybindService.FindConflicts(slot, keybindBindingIndex, chord);
        if (conflicts.Count > 0)
        {
            pendingKeybindChange = new PendingKeybindChange(slot, keybindBindingIndex, chord, conflicts);
            var blockedNativeCount = conflicts.Count(conflict => conflict.BlocksSafeBinding);
            var performanceCount = conflicts.Count(conflict => conflict.IsPreservedPerformance);
            keybindStatus = blockedNativeCount > 0
                ? $"{NativeHotbarKeybindService.FormatChord(chord, false)} conflicts with {blockedNativeCount} protected native FFXIV binding{(blockedNativeCount == 1 ? string.Empty : "s")}. RE:Frame will not remove them."
                : performanceCount > 0
                    ? $"{NativeHotbarKeybindService.FormatChord(chord, false)} is also used by Bard Performance. That native binding will be preserved and RE:Frame will pause while Performing."
                    : $"{NativeHotbarKeybindService.FormatChord(chord, false)} conflicts with {conflicts.Count} other RE:Frame binding{(conflicts.Count == 1 ? string.Empty : "s")}. The target remains safely disarmed while you review them.";
            return true;
        }

        ApplyCapturedBinding(slot, keybindBindingIndex, chord, Array.Empty<NativeKeybindConflict>());
        return true;
    }

    private void ApplyCapturedBinding(
        HotbarSlotReference slot,
        int bindingIndex,
        NativeKeybindChord chord,
        IReadOnlyList<NativeKeybindConflict> conflicts)
    {
        if (NativeHotbarKeybindService.TryApplyBinding(
                slot,
                bindingIndex,
                chord,
                conflicts,
                out _,
                out var message))
        {
            pendingKeybindChange = null;
            keybindStatus = $"{slot.Label}: {message} Lock the bars, then press it to execute this exact slot.";
            plugin.BarInputDiagnostics.RecordInputEvent($"RE:Frame keybind updated for {slot.Label}: {NativeHotbarKeybindService.FormatChord(chord, false)}");
        }
        else
        {
            keybindStatus = message;
            Plugin.ChatGui.PrintError(message);
        }
    }

    private void CommitPendingKeybindChange()
    {
        if (pendingKeybindChange is not { } pending)
            return;

        var blockedNativeConflicts = pending.Conflicts
            .Where(conflict => conflict.BlocksSafeBinding)
            .ToArray();
        if (blockedNativeConflicts.Length > 0)
        {
            keybindStatus = "RE:Frame refused to remove the protected native binding. Cancel and choose another key.";
            plugin.BarInputDiagnostics.RecordKeybindStage(
                "NATIVE BINDING PROTECTED",
                string.Join(", ", blockedNativeConflicts.Select(conflict => conflict.DisplayName)));
            return;
        }

        var preservedPerformanceConflicts = pending.Conflicts
            .Where(conflict => conflict.IsPreservedPerformance)
            .ToArray();
        if (preservedPerformanceConflicts.Length > 0)
        {
            plugin.BarInputDiagnostics.RecordKeybindStage(
                "NATIVE BINDING PRESERVED",
                string.Join(", ", preservedPerformanceConflicts.Select(conflict => conflict.DisplayName)));
        }

        var replaceableConflicts = pending.Conflicts
            .Where(conflict => conflict.CanReplace)
            .ToArray();
        ApplyCapturedBinding(
            pending.Slot,
            pending.BindingIndex,
            pending.Chord,
            replaceableConflicts);
    }

    private void ClearSelectedBinding(HotbarSlotReference? preferredSlot = null)
    {
        var selected = preferredSlot ?? plugin.HotbarEditing.SelectedSlot;
        if (selected is not { } target)
        {
            keybindStatus = "Click or select a normal hotbar slot before clearing a binding.";
            return;
        }

        plugin.HotbarEditing.Select(target);
        CancelKeybindCapture(clearPending: true, restoreNative: false);
        if (NativeHotbarKeybindService.TryClearBinding(
                target,
                keybindBindingIndex,
                out _,
                out var message))
        {
            keybindStatus = $"{target.Label}: {message}";
            plugin.BarInputDiagnostics.RecordInputEvent($"Native keybind cleared for {target.Label}");
        }
        else
        {
            keybindStatus = message;
            Plugin.ChatGui.PrintError(message);
        }
    }

    private void UndoLastKeybindChange()
    {
        CancelKeybindCapture(clearPending: true, restoreNative: true);
        if (!NativeHotbarKeybindService.CanUndo)
        {
            keybindStatus = "There is no keybind change to undo.";
            return;
        }

        if (NativeHotbarKeybindService.TryUndoLastChange(out var message))
        {
            keybindStatus = $"{message} The native restore will be verified before completion.";
            plugin.BarInputDiagnostics.RecordInputEvent("Last RE:Frame keybind transaction restored");
        }
        else
        {
            keybindStatus = message;
            Plugin.ChatGui.PrintError(message);
        }
    }

    private void HandleKeybindMouse(SlotHitbox? hovered)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            CancelKeybindCapture(clearPending: true, restoreNative: true);
            keybindStatus = "Keybind capture cancelled. Restoring the original native binding.";
            return;
        }

        if (!ImGui.GetIO().WantTextInput &&
            (ImGui.IsKeyPressed(ImGuiKey.Delete, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false)))
        {
            ClearSelectedBinding(hovered?.Slot);
            return;
        }

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;

        if (hovered is { } clicked)
        {
            plugin.BarInputDiagnostics.RecordInputEvent($"Keybind editor selected {clicked.Slot.Label}");
            BeginKeybindCapture(clicked.Slot);
        }
    }

    private void HandleMouse(SlotHitbox? hovered, Vector2 mouse, Vector2 origin, ImDrawListPtr draw)
    {
        if (keybindMode)
        {
            HandleKeybindMouse(hovered);
            return;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !plugin.HotbarEditing.IsDraggingAction)
        {
            if (hovered is { } clicked)
            {
                plugin.BarInputDiagnostics.RecordInputEvent($"Left click detected over {clicked.Slot.Label}");
                plugin.HotbarEditing.Select(clicked.Slot);
                pressedSlot = clicked.Slot;
                pressPosition = mouse;
            }
            else
            {
                plugin.BarInputDiagnostics.RecordInputEvent("Left click detected, but no editable slot was hovered");
                pressedSlot = null;
            }

            dragging = false;
        }

        if (pressedSlot is { } source && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!dragging && Vector2.Distance(mouse, pressPosition) >= MathF.Max(3f, 4f * scale))
            {
                if (plugin.HotbarEditing.TryGetSnapshot(source, out var snapshot) && !snapshot.IsEmpty)
                {
                    dragging = true;
                    plugin.BarInputDiagnostics.RecordInputEvent($"Slot drag threshold crossed for {source.Label}");
                }
            }

            if (dragging)
            {
                var sourceBox = hitboxes.Find(hitbox => hitbox.Slot == source);
                if (sourceBox.Size > 0f)
                {
                    var sourceCenter = origin + sourceBox.LocalPosition + new Vector2(sourceBox.Size * 0.5f);
                    DrawDragLine(draw, sourceCenter, mouse);
                }
            }
        }

        if (!ImGui.GetIO().WantTextInput &&
            plugin.HotbarEditing.SelectedSlot is { } selected &&
            (ImGui.IsKeyPressed(ImGuiKey.Delete, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false)))
        {
            if (!plugin.HotbarEditing.Clear(selected, out var clearMessage))
                Plugin.ChatGui.PrintError(clearMessage);
        }

        if (plugin.HotbarEditing.IsDraggingAction && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            plugin.HotbarEditing.CancelActionDrag();
            return;
        }

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            return;

        plugin.BarInputDiagnostics.RecordInputEvent(hovered is { } releasedOver
            ? $"Left release detected over {releasedOver.Slot.Label}; dispatching edit handler"
            : "Left release detected with no slot hovered");

        if (plugin.HotbarEditing.IsDraggingAction)
        {
            if (hovered is { } destination)
            {
                plugin.BarInputDiagnostics.RecordInputEvent($"Palette assignment handler reached for {destination.Slot.Label}");
                if (!plugin.HotbarEditing.AssignAction(
                        destination.Slot,
                        plugin.HotbarEditing.DraggedActionType,
                        plugin.HotbarEditing.DraggedActionId,
                        out var assignMessage))
                    Plugin.ChatGui.PrintError(assignMessage);
                else
                    plugin.HotbarEditing.Select(destination.Slot);

                plugin.HotbarEditing.CancelActionDrag();
            }
            else if (!plugin.IsPointInsideActionPalette(mouse))
            {


                plugin.HotbarEditing.CancelActionDrag();
            }
        }
        else if (dragging && pressedSlot is { } dragSource)
        {
            if (hovered is { } destination && destination.Slot != dragSource)
            {
                plugin.BarInputDiagnostics.RecordInputEvent($"Slot transfer handler reached: {dragSource.Label} → {destination.Slot.Label}");
                if (!plugin.HotbarEditing.Transfer(
                        dragSource,
                        destination.Slot,
                        ImGui.GetIO().KeyCtrl,
                        out var transferMessage))
                    Plugin.ChatGui.PrintError(transferMessage);
            }
            else if (hovered is null)
            {
                plugin.BarInputDiagnostics.RecordInputEvent($"Drag-away clear handler reached for {dragSource.Label}");
                if (!plugin.HotbarEditing.Clear(dragSource, out var clearMessage))
                    Plugin.ChatGui.PrintError(clearMessage);
            }
        }

        pressedSlot = null;
        dragging = false;
    }

    private void DrawDragLine(ImDrawListPtr draw, Vector2 from, Vector2 to)
    {
        draw.AddLine(
            from,
            to,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                plugin.CurrentTheme.AccentStrong.X,
                plugin.CurrentTheme.AccentStrong.Y,
                plugin.CurrentTheme.AccentStrong.Z,
                0.94f)),
            MathF.Max(1.5f, 2f * scale));
    }

    private void DrawSlotHighlights(SlotHitbox? hovered, Vector2 origin, ImDrawListPtr draw)
    {
        foreach (var hitbox in hitboxes)
        {
            var min = origin + hitbox.LocalPosition;
            var max = min + new Vector2(hitbox.Size);
            var selected = plugin.HotbarEditing.SelectedSlot == hitbox.Slot;
            var isHovered = hovered is { } h && h.Slot == hitbox.Slot;
            var isSource = dragging && pressedSlot == hitbox.Slot;
            var isCapture = keybindMode &&
                            (keybindCaptureSlot == hitbox.Slot ||
                             (isHovered && pendingKeybindChange is null &&
                              NativeHotbarKeybindService.IsBindableSlot(hitbox.Slot)));

            if (isHovered)
            {
                draw.AddRectFilled(
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(
                        plugin.CurrentTheme.AccentStrong.X,
                        plugin.CurrentTheme.AccentStrong.Y,
                        plugin.CurrentTheme.AccentStrong.Z,
                        0.18f)),
                    5f * scale);
            }

            if (selected || isSource || isCapture)
            {
                var outline = isCapture ? plugin.CurrentTheme.Warning : plugin.CurrentTheme.AccentStrong;
                draw.AddRect(
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(
                        outline.X,
                        outline.Y,
                        outline.Z,
                        1f)),
                    5f * scale,
                    ImDrawFlags.None,
                    isCapture ? MathF.Max(3f, 3.2f * scale) : MathF.Max(2f, 2.4f * scale));
            }

            if (keybindMode && NativeHotbarKeybindService.IsBindableSlot(hitbox.Slot))
            {
                var binding = NativeHotbarKeybindService.GetBindingLabel(hitbox.Slot, keybindBindingIndex);
                if (!string.IsNullOrWhiteSpace(binding))
                {
                    var bindingSize = ImGui.CalcTextSize(binding);
                    var padding = new Vector2(4f * scale, 2f * scale);
                    var badgeMax = max - new Vector2(2f * scale);
                    var badgeMin = badgeMax - bindingSize - padding * 2f;
                    draw.AddRectFilled(
                        badgeMin,
                        badgeMax,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.008f, 0.012f, 0.02f, 0.94f)),
                        4f * scale);
                    draw.AddText(
                        badgeMin + padding,
                        ImGui.ColorConvertFloat4ToU32(plugin.CurrentTheme.Text),
                        binding);
                }
            }
        }

        if (keybindMode)
        {
            if (hovered is { } keybindSlot)
            {
                ImGui.BeginTooltip();
                if (plugin.HotbarEditing.TryGetSnapshot(keybindSlot.Slot, out var keybindSnapshot))
                    ImGui.TextUnformatted(keybindSnapshot.IsEmpty ? "Empty slot" : keybindSnapshot.DisplayName);
                ImGui.TextDisabled(keybindSlot.Slot.Label);
                if (NativeHotbarKeybindService.IsBindableSlot(keybindSlot.Slot))
                {
                    ImGui.TextUnformatted($"Primary: {NativeHotbarKeybindService.GetBindingLabel(keybindSlot.Slot, 0, false)}");
                    ImGui.TextUnformatted($"Secondary: {NativeHotbarKeybindService.GetBindingLabel(keybindSlot.Slot, 1, false)}");
                    ImGui.TextDisabled("Press a key now to bind this slot. Click to keep it selected for panel controls.");
                }
                else
                {
                    ImGui.TextDisabled("Cross Hotbar controller slots use shared trigger/directional bindings.");
                }
                ImGui.EndTooltip();
            }
        }
        else if (plugin.HotbarEditing.IsDraggingAction)
        {
            if (hovered is { } destination)
            {
                DrawDestinationOutline(draw, origin, destination);
                ImGui.SetTooltip($"Release to place {plugin.HotbarEditing.DraggedActionName} on {destination.Slot.Label}.");
            }
            else
            {
                ImGui.SetTooltip($"Drag {plugin.HotbarEditing.DraggedActionName} onto an RE:Frame slot.");
            }
        }
        else if (dragging)
        {
            if (hovered is { } destination && pressedSlot != destination.Slot)
            {
                DrawDestinationOutline(draw, origin, destination);
                ImGui.SetTooltip(ImGui.GetIO().KeyCtrl
                    ? "Release to copy this button."
                    : "Release to move or swap this button.");
            }
            else
            {
                ImGui.SetTooltip("Release away from every bar to remove this button.");
            }
        }
        else if (hovered is { } hoveredSlot)
        {
            plugin.HotbarEditing.TryGetSnapshot(hoveredSlot.Slot, out var snapshot);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(snapshot.IsEmpty ? "Empty slot" : snapshot.DisplayName);
            ImGui.TextDisabled(hoveredSlot.Slot.Label);
            ImGui.TextDisabled("Drag to move or swap. Ctrl-drag copies. Drag off the bars to remove.");
            ImGui.EndTooltip();
        }
    }

    private void DrawActionDragPreview(ImDrawListPtr draw, Vector2 mouse)
    {
        if (!plugin.HotbarEditing.IsDraggingAction)
            return;

        var iconSize = Math.Clamp(44f * scale, 36f, 58f);
        var min = mouse + new Vector2(14f, 14f);
        var max = min + new Vector2(iconSize);
        draw.AddRectFilled(min - new Vector2(4f), max + new Vector2(4f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.012f, 0.016f, 0.024f, 0.94f)), 8f);
        draw.AddRect(min - new Vector2(4f), max + new Vector2(4f),
            ImGui.ColorConvertFloat4ToU32(plugin.CurrentTheme.AccentStrong), 8f, ImDrawFlags.None, 1.5f);

        var wrap = HudRenderer.GetGameIcon(plugin.HotbarEditing.DraggedActionIconId);
        if (wrap is not null)
            draw.AddImage(wrap.Handle, min, max, Vector2.Zero, Vector2.One, 0xFFFFFFFF);

        var name = plugin.HotbarEditing.DraggedActionName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var textPosition = new Vector2(min.X, max.Y + 6f);
            draw.AddText(textPosition, ImGui.ColorConvertFloat4ToU32(plugin.CurrentTheme.Text), name);
        }
    }

    private void DrawDestinationOutline(ImDrawListPtr draw, Vector2 origin, SlotHitbox destination)
    {
        var min = origin + destination.LocalPosition;
        var max = min + new Vector2(destination.Size);
        draw.AddRect(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                plugin.CurrentTheme.Success.X,
                plugin.CurrentTheme.Success.Y,
                plugin.CurrentTheme.Success.Z,
                0.98f)),
            5f * scale,
            ImDrawFlags.None,
            MathF.Max(2f, 2.5f * scale));
    }

    private void DrawWorkspaceBackdrop(ImDrawListPtr draw, Vector2 origin, Vector2 canvasSize)
    {
        var maximum = origin + canvasSize;
        var topColor = Blend(plugin.CurrentTheme.Panel, plugin.CurrentTheme.AccentStrong, 0.18f, 1f);
        var bottomColor = Blend(plugin.CurrentTheme.PanelAlt, plugin.CurrentTheme.Accent, 0.12f, 1f);
        var top = ImGui.ColorConvertFloat4ToU32(topColor);
        var bottom = ImGui.ColorConvertFloat4ToU32(bottomColor);
        draw.AddRectFilledMultiColor(origin, maximum, top, top, bottom, bottom);

        var glowColor = ImGui.ColorConvertFloat4ToU32(WithAlpha(plugin.CurrentTheme.AccentStrong, 0.10f));
        var glowRadius = MathF.Max(canvasSize.X, canvasSize.Y) * 0.42f;
        var glowCenter = origin + new Vector2(canvasSize.X * 0.38f, canvasSize.Y * 0.26f);
        draw.AddCircleFilled(glowCenter, glowRadius, glowColor, 96);

        var gridStep = Math.Clamp(68f * scale, 52f, 118f);
        var gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(
            plugin.CurrentTheme.AccentStrong.X,
            plugin.CurrentTheme.AccentStrong.Y,
            plugin.CurrentTheme.AccentStrong.Z,
            0.035f));
        for (var x = origin.X; x <= maximum.X; x += gridStep)
            draw.AddLine(new Vector2(x, origin.Y), new Vector2(x, maximum.Y), gridColor, 1f);
        for (var y = origin.Y; y <= maximum.Y; y += gridStep)
            draw.AddLine(new Vector2(origin.X, y), new Vector2(maximum.X, y), gridColor, 1f);

        var edge = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.34f));
        var edgeSize = Math.Clamp(90f * scale, 70f, 170f);
        draw.AddRectFilledMultiColor(origin, new Vector2(maximum.X, origin.Y + edgeSize), edge, edge, 0x00000000, 0x00000000);
        draw.AddRectFilledMultiColor(new Vector2(origin.X, maximum.Y - edgeSize), maximum, 0x00000000, 0x00000000, edge, edge);
    }

    private HudBounds ResolveWorkspaceStage(Vector2 origin, Vector2 canvasSize)
    {
        var margin = Math.Clamp(28f * scale, 18f, 48f);
        var top = origin.Y + Math.Clamp(104f * scale, 88f, 150f);
        var bottom = origin.Y + canvasSize.Y - margin;
        var paletteOnRight = paletteBounds.Min.X >= origin.X + canvasSize.X * 0.5f;
        var left = paletteOnRight
            ? origin.X + margin
            : paletteBounds.Max.X + margin;
        var right = paletteOnRight
            ? paletteBounds.Min.X - margin
            : origin.X + canvasSize.X - margin;

        if (right - left < MathF.Min(540f, canvasSize.X * 0.52f))
        {
            left = origin.X + margin;
            right = origin.X + canvasSize.X - margin;
            top = MathF.Max(top, paletteBounds.Max.Y + margin);
        }

        return new HudBounds(
            new Vector2(left, top),
            new Vector2(MathF.Max(1f, right - left), MathF.Max(1f, bottom - top)));
    }

    private void DrawWorkspaceStage(ImDrawListPtr draw, HudBounds stage, bool controllerMode)
    {
        var minimum = stage.Position;
        var maximum = stage.Position + stage.Size;
        var rounding = 16f * MathF.Min(scale, 1.4f);
        var stageTop = ImGui.ColorConvertFloat4ToU32(Blend(plugin.CurrentTheme.PanelAlt, plugin.CurrentTheme.AccentStrong, 0.32f, 0.93f));
        var stageBottom = ImGui.ColorConvertFloat4ToU32(Blend(plugin.CurrentTheme.Panel, plugin.CurrentTheme.Accent, 0.18f, 0.90f));
        draw.AddRectFilledMultiColor(
            minimum,
            maximum,
            stageTop,
            stageTop,
            stageBottom,
            stageBottom);
        draw.AddRectFilled(
            minimum,
            maximum,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.10f)),
            rounding);

        DrawStageLogo(draw, minimum, maximum);

        draw.AddRect(
            minimum,
            maximum,
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                plugin.CurrentTheme.AccentStrong.X,
                plugin.CurrentTheme.AccentStrong.Y,
                plugin.CurrentTheme.AccentStrong.Z,
                0.24f)),
            rounding,
            ImDrawFlags.None,
            1.25f);

        var heading = controllerMode ? "CONTROLLER ACTION BARS" : "KEYBOARD / MOUSE ACTION BARS";
        var detail = "Editor-only workspace  •  Your normal HUD visibility and dock settings stay unchanged";
        draw.AddText(
            minimum + new Vector2(20f, 16f),
            ImGui.ColorConvertFloat4ToU32(plugin.CurrentTheme.AccentStrong),
            heading);
        draw.AddText(
            minimum + new Vector2(20f, 20f + ImGui.GetTextLineHeight()),
            ImGui.ColorConvertFloat4ToU32(new Vector4(
                plugin.CurrentTheme.Muted.X,
                plugin.CurrentTheme.Muted.Y,
                plugin.CurrentTheme.Muted.Z,
                0.88f)),
            detail);
    }

    private HudBounds[] ResolveKeyboardWorkspaceBars(
        HudBounds stage,
        HudBounds utilityBounds,
        IReadOnlyList<EditorCombatBar> pageBars)
    {
        var labelSpace = Math.Clamp(132f * scale, 112f, 210f);
        var horizontalPadding = Math.Clamp(28f * scale, 20f, 48f);
        var utilityGap = Math.Clamp(28f * scale, 20f, 52f);
        var utilityReservation = utilityBounds.Size.X + utilityGap;
        var availableWidth = MathF.Max(260f, stage.Size.X - labelSpace - horizontalPadding * 2f - utilityReservation);
        var availableHeight = MathF.Max(240f, stage.Size.Y - 82f);
        var rowGap = Math.Clamp(20f * scale, 16f, 34f);
        var rowRegionHeight = MathF.Max(72f, (availableHeight - rowGap * 2f) / 3f);
        var contentLeft = stage.Position.X + labelSpace + horizontalPadding;
        var contentTop = stage.Position.Y + 66f;
        var result = new HudBounds[Math.Max(1, pageBars.Count)];
        var gap = MathF.Max(1f, 3f * scale);

        for (var i = 0; i < pageBars.Count; i++)
        {
            var shape = HotbarGridLayouts.Resolve(plugin.Configuration, pageBars[i].ElementId);
            var slotFromWidth = (availableWidth - gap * (shape.Columns - 1)) / shape.Columns;
            var slotFromHeight = (rowRegionHeight - gap * (shape.Rows - 1)) / shape.Rows;
            var slotSize = MathF.Max(18f, MathF.Min(
                MathF.Max(34f, 56f * MathF.Min(scale, 1.45f)),
                MathF.Min(slotFromWidth, slotFromHeight)));
            var size = new Vector2(
                shape.Columns * slotSize + (shape.Columns - 1) * gap,
                shape.Rows * slotSize + (shape.Rows - 1) * gap);
            var regionTop = contentTop + i * (rowRegionHeight + rowGap);
            result[i] = new HudBounds(
                new Vector2(
                    contentLeft + MathF.Max(0f, (availableWidth - size.X) * 0.5f),
                    regionTop + MathF.Max(0f, (rowRegionHeight - size.Y) * 0.5f)),
                size);
        }

        return result;
    }

    private HudBounds ResolveControllerWorkspaceBounds(HudBounds stage, HudBounds utilityBounds)
    {
        var utilityGap = Math.Clamp(28f * scale, 20f, 52f);
        var availableWidth = MathF.Max(
            320f,
            utilityBounds.Position.X - utilityGap - stage.Position.X - 28f);
        var availableHeight = MathF.Max(128f, stage.Size.Y - 94f);
        var width = MathF.Min(availableWidth, MathF.Max(720f, 1080f * MathF.Min(scale, 1.35f)));
        var height = MathF.Min(availableHeight, MathF.Max(150f, 235f * MathF.Min(scale, 1.35f)));
        return new HudBounds(
            new Vector2(
                stage.Position.X + 24f + MathF.Max(0f, (availableWidth - width) * 0.5f),
                stage.Position.Y + 66f + MathF.Max(0f, (availableHeight - height) * 0.5f)),
            new Vector2(width, height));
    }

    private HudBounds ResolveUtilityWorkspaceBounds(HudBounds stage, int index)
    {
        var availableHeight = MathF.Max(260f, stage.Size.Y - 96f);
        var maximumWidth = MathF.Max(142f, MathF.Min(238f, stage.Size.X * 0.24f));
        var width = Math.Clamp(178f * scale, 142f, maximumWidth);
        var verticalGap = Math.Clamp(42f * scale, 34f, 62f);
        var height = Math.Clamp((availableHeight - verticalGap) * 0.5f, 104f, 176f);
        var rightPadding = Math.Clamp(30f * scale, 22f, 52f);
        var totalHeight = height * 2f + verticalGap;
        var top = stage.Position.Y + 66f + MathF.Max(0f, (availableHeight - totalHeight) * 0.5f);
        return new HudBounds(
            new Vector2(
                stage.Position.X + stage.Size.X - width - rightPadding,
                top + index * (height + verticalGap)),
            new Vector2(width, height));
    }

    private HudBounds ResolveControllerPetWorkspaceBounds(HudBounds stage, HudBounds utilityBounds)
    {
        var utilityGap = Math.Clamp(28f * scale, 20f, 52f);
        var availableWidth = MathF.Max(
            300f,
            utilityBounds.Position.X - utilityGap - stage.Position.X - 28f);
        var shape = HotbarGridLayouts.Resolve(plugin.Configuration, HudElementIds.PetBar);
        var gap = MathF.Max(1f, 3f * scale);
        var slotFromWidth = (availableWidth - gap * (shape.Columns - 1)) / shape.Columns;
        var slotSize = MathF.Max(18f, MathF.Min(
            MathF.Max(32f, 46f * MathF.Min(scale, 1.35f)),
            slotFromWidth));
        var size = new Vector2(
            shape.Columns * slotSize + (shape.Columns - 1) * gap,
            shape.Rows * slotSize + (shape.Rows - 1) * gap);
        return new HudBounds(
            new Vector2(
                stage.Position.X + 24f + MathF.Max(0f, (availableWidth - size.X) * 0.5f),
                stage.Position.Y + stage.Size.Y - size.Y - Math.Clamp(24f * scale, 18f, 36f)),
            size);
    }

    private void DrawEditBanner(ImDrawListPtr draw, Vector2 origin, Vector2 canvasSize, bool controllerMode, int crossSet)
    {
        var title = keybindMode ? "HOTBAR KEYBIND EDITOR" : "ACTION BAR EDITOR";
        var detail = keybindMode
            ? controllerMode
                ? "Pet + both utility bars support per-slot keys  •  XHB controller bindings remain shared  •  Esc cancels"
                : $"Editing {(keybindBindingIndex == 0 ? "primary" : "secondary")} slot bindings  •  Native + pet + overflow supported  •  Esc cancels"
            : controllerMode
                ? $"Isolated XHB + two Utility bars  •  Set {crossSet}  •  Mouse wheel changes sets  •  /ref bars to lock"
                : $"Combat page {combatBarPage + 1} + two Utility bars  •  Ctrl-drag copies  •  Drag away removes  •  /ref bars to lock";
        var titleSize = ImGui.CalcTextSize(title);
        var detailSize = ImGui.CalcTextSize(detail);
        var width = MathF.Max(titleSize.X, detailSize.X) + 34f;
        var height = titleSize.Y + detailSize.Y + 22f;
        var min = new Vector2(origin.X + (canvasSize.X - width) * 0.5f, origin.Y + 18f);
        var max = min + new Vector2(width, height);

        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.015f, 0.02f, 0.03f, 0.96f)), 8f);
        draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(
            plugin.CurrentTheme.AccentStrong.X,
            plugin.CurrentTheme.AccentStrong.Y,
            plugin.CurrentTheme.AccentStrong.Z,
            0.88f)), 8f, ImDrawFlags.None, 1.5f);
        draw.AddText(
            new Vector2(min.X + (width - titleSize.X) * 0.5f, min.Y + 6f),
            ImGui.ColorConvertFloat4ToU32(plugin.CurrentTheme.AccentStrong),
            title);
        draw.AddText(
            new Vector2(min.X + (width - detailSize.X) * 0.5f, min.Y + 10f + titleSize.Y),
            ImGui.ColorConvertFloat4ToU32(plugin.CurrentTheme.Text),
            detail);
    }

    private void BuildActionBarHitboxes(HudBounds bounds, Vector2 origin, uint hotbarId, string elementId)
    {
        var shape = HotbarGridLayouts.Resolve(plugin.Configuration, elementId);
        var columns = shape.Columns;
        var rows = shape.Rows;
        var gap = MathF.Max(1f, 3f * scale);
        var slotSize = MathF.Max(18f, MathF.Min(
            (bounds.Size.X - gap * (columns - 1)) / columns,
            (bounds.Size.Y - gap * (rows - 1)) / rows));
        var content = new Vector2(
            columns * slotSize + (columns - 1) * gap,
            rows * slotSize + (rows - 1) * gap);
        var start = bounds.Position + (bounds.Size - content) * 0.5f - origin;
        for (var slot = 0; slot < 12; slot++)
        {
            var row = slot / columns;
            var column = slot % columns;
            hitboxes.Add(new SlotHitbox(
                new HotbarSlotReference(hotbarId, (uint)slot),
                start + new Vector2(column * (slotSize + gap), row * (slotSize + gap)),
                slotSize));
        }
    }

    private void BuildUtilityBarHitboxes(HudBounds bounds, Vector2 origin, uint hotbarId)
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
        var start = bounds.Position + (bounds.Size - content) * 0.5f - origin;

        for (var slot = 0; slot < columns * rows; slot++)
        {
            var row = slot / columns;
            var column = slot % columns;
            hitboxes.Add(new SlotHitbox(
                new HotbarSlotReference(hotbarId, (uint)slot),
                start + new Vector2(column * (slotSize + gap), row * (slotSize + gap)),
                slotSize));
        }
    }

    private void BuildCrossHotbarHitboxes(HudBounds bounds, Vector2 origin, int setNumber)
    {
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
        var contentStart = new Vector2(
            bounds.Position.X + (bounds.Size.X - contentWidth) * 0.5f,
            bounds.Position.Y + triggerHeight + MathF.Max(0f, (availableHeight - clusterSize) * 0.5f)) - origin;
        var hotbarId = (uint)(9 + Math.Clamp(setNumber, 1, 8));

        AddCrossHalf(hotbarId, 0u, contentStart, slotSize, slotGap, clusterSize, clusterGap);
        AddCrossHalf(hotbarId, 8u, contentStart + new Vector2(halfWidth + centerGap, 0f), slotSize, slotGap, clusterSize, clusterGap);
    }

    private void AddCrossHalf(
        uint hotbarId,
        uint firstSlot,
        Vector2 start,
        float slotSize,
        float slotGap,
        float clusterSize,
        float clusterGap)
    {
        AddCrossCluster(hotbarId, firstSlot, start, slotSize, slotGap);
        AddCrossCluster(hotbarId, firstSlot + 4u, start + new Vector2(clusterSize + clusterGap, 0f), slotSize, slotGap);
    }

    private void AddCrossCluster(uint hotbarId, uint firstSlot, Vector2 start, float slotSize, float gap)
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
            hitboxes.Add(new SlotHitbox(
                new HotbarSlotReference(hotbarId, firstSlot + (uint)index),
                start + offset,
                slotSize));
        }
    }

    private static bool Contains(HudBounds bounds, Vector2 point)
        => point.X >= bounds.Position.X &&
           point.X <= bounds.Position.X + bounds.Size.X &&
           point.Y >= bounds.Position.Y &&
           point.Y <= bounds.Position.Y + bounds.Size.Y;

    private void DrawStageLogo(ImDrawListPtr draw, Vector2 minimum, Vector2 maximum)
    {
        IDalamudTextureWrap? wrap = logoTexture.GetWrapOrDefault();
        if (wrap is null)
            return;

        var stageSize = maximum - minimum;
        var maxWidth = MathF.Max(220f, stageSize.X * 0.42f);
        var maxHeight = MathF.Max(140f, stageSize.Y * 0.46f);
        var textureSize = new Vector2(wrap.Width, wrap.Height);
        if (textureSize.X <= 0f || textureSize.Y <= 0f)
            return;

        var fit = MathF.Min(maxWidth / textureSize.X, maxHeight / textureSize.Y);
        var drawSize = textureSize * fit;
        var center = minimum + stageSize * 0.5f;
        var min = center - drawSize * 0.5f;
        var max = center + drawSize * 0.5f;

        var shadowColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.28f));
        draw.AddImage(wrap.Handle, min + new Vector2(2f, 4f), max + new Vector2(2f, 4f), Vector2.Zero, Vector2.One, shadowColor);
        var logoTint = ImGui.ColorConvertFloat4ToU32(WithAlpha(plugin.CurrentTheme.Text, 0.115f));
        draw.AddImage(wrap.Handle, min, max, Vector2.Zero, Vector2.One, logoTint);
    }

    private static Vector4 Blend(Vector4 from, Vector4 to, float amount, float alpha)
        => new(
            from.X + (to.X - from.X) * amount,
            from.Y + (to.Y - from.Y) * amount,
            from.Z + (to.Z - from.Z) * amount,
            alpha);

    private static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, alpha);

    public void OpenKeybindMode()
    {
        SetKeybindMode(true);
        keybindStatus = "Click any combat, pet, or utility slot. Native keyboard bars verify their safe disarm first; Pet and overflow slots listen immediately.";
    }

    public void ResetTransientState()
    {
        NativeHotbarKeybindService.CancelPreparedCapture();
        combatBarPage = 0;
        keybindMode = false;
        keybindBindingIndex = 0;
        keybindCaptureSlot = null;
        keybindCaptureReadyFrame = -1;
        pendingKeybindChange = null;
        keybindStatus = "Choose Keybinds, click a combat, pet, or utility slot, then press the key you want.";
        pressedSlot = null;
        dragging = false;
        movingPalette = false;
    }

    public void Dispose() => NativeHotbarKeybindService.CancelPreparedCapture();

    private readonly record struct EditorCombatBar(
        string Label,
        string ElementId,
        uint RuntimeHotbarId,
        ReframeAdditionalHotbar? Additional);

    private readonly record struct PendingKeybindChange(
        HotbarSlotReference Slot,
        int BindingIndex,
        NativeKeybindChord Chord,
        IReadOnlyList<NativeKeybindConflict> Conflicts);

    private readonly record struct RectF(Vector2 Min, Vector2 Max)
    {
        public bool Contains(Vector2 point)
            => point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y;

        public bool Intersects(RectF other)
            => Min.X < other.Max.X && Max.X > other.Min.X &&
               Min.Y < other.Max.Y && Max.Y > other.Min.Y;
    }

    private readonly record struct SlotHitbox(HotbarSlotReference Slot, Vector2 LocalPosition, float Size);
}
