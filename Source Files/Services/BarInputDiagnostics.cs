using System;
using System.Collections.Generic;
using System.Numerics;
using REFrameXIV.UI;

namespace REFrameXIV.Services;


public sealed class BarInputDiagnostics
{
    public int InputFrame { get; private set; } = -1;
    public string InputMethod { get; private set; } = "No hotbar input method observed yet";
    public string CurrentOwner { get; private set; } = "None";
    public Vector2 MousePosition { get; private set; }
    public HudCanvasInfo Canvas { get; private set; }
    public HudBounds BarOneBounds { get; private set; }
    public HudBounds BarOneSlotOneBounds { get; private set; }
    public bool HasBarOneBounds { get; private set; }
    public bool HasBarOneSlotOneBounds { get; private set; }
    public bool MouseInsideBarOneSlotOne { get; private set; }
    public bool BarOneSlotOneItemRegistered { get; private set; }
    public bool BarOneSlotOneItemHovered { get; private set; }
    public bool BarOneSlotOneBlockedByPalette { get; private set; }
    public bool LeftMouseDown { get; private set; }
    public bool LeftMouseClicked { get; private set; }
    public bool LeftMouseReleased { get; private set; }
    public bool PaletteOwnsInput { get; private set; }
    public uint SelectedActionId { get; private set; }
    public HotbarSlotReference? SelectedSourceSlot { get; private set; }
    public string LastInputEvent { get; private set; } = "None";
    public string LastNativeSlotWriteResult { get; private set; } = "No native slot write observed";
    public string LastExecutionAttempt { get; private set; } = "No native execution attempt observed";

    private const int MaximumKeybindTraceEntries = 16;
    private readonly List<string> keybindTrace = new(MaximumKeybindTraceEntries);
    private readonly Dictionary<string, long> keybindTraceRateLimits = new(StringComparer.Ordinal);
    public IReadOnlyList<string> KeybindTrace => keybindTrace;

    public HotbarSlotReference? LastAssignmentDestination { get; private set; }
    public uint LastAssignmentActionId { get; private set; }
    public int LastAssignmentFrame { get; private set; } = -1;
    public string LastAssignmentRequest { get; private set; } = "No palette assignment requested";
    public string ImmediateNativeReadback { get; private set; } = "No immediate native readback observed";
    public string NextFrameNativeReadback { get; private set; } = "No next-frame native readback observed";
    public string RendererReadback { get; private set; } = "Renderer has not observed a tracked destination";

    public bool DrawRanThisFrame(int currentFrame) => InputFrame == currentFrame;

    public bool IsTrackingDestination(uint hotbarId, uint slotId)
        => LastAssignmentDestination is { } destination &&
           destination.HotbarId == hotbarId &&
           destination.SlotId == slotId;

    public void ResetForEditSession()
    {
        InputFrame = -1;
        InputMethod = "Waiting for HotbarSlotEditorWindow.Draw";
        CurrentOwner = "HotbarSlotEditorWindow (expected)";
        HasBarOneBounds = false;
        HasBarOneSlotOneBounds = false;
        MouseInsideBarOneSlotOne = false;
        BarOneSlotOneItemRegistered = false;
        BarOneSlotOneItemHovered = false;
        BarOneSlotOneBlockedByPalette = false;
        LeftMouseDown = false;
        LeftMouseClicked = false;
        LeftMouseReleased = false;
        PaletteOwnsInput = false;
        SelectedActionId = 0;
        SelectedSourceSlot = null;
        LastInputEvent = "Entered /ref bars; waiting for the first editor draw";
        LastAssignmentDestination = null;
        LastAssignmentActionId = 0;
        LastAssignmentFrame = -1;
        LastAssignmentRequest = "No palette assignment requested";
        ImmediateNativeReadback = "No immediate native readback observed";
        NextFrameNativeReadback = "No next-frame native readback observed";
        RendererReadback = "Renderer has not observed a tracked destination";
    }

    public void BeginEditorFrame(
        int frame,
        Vector2 mousePosition,
        HudCanvasInfo canvas,
        bool paletteOwnsInput,
        bool leftMouseDown,
        bool leftMouseClicked,
        bool leftMouseReleased)
    {
        InputFrame = frame;
        InputMethod = "HotbarSlotEditorWindow.Draw";
        CurrentOwner = "HotbarSlotEditorWindow (edit mode)";
        MousePosition = mousePosition;
        Canvas = canvas;
        PaletteOwnsInput = paletteOwnsInput;
        LeftMouseDown = leftMouseDown;
        LeftMouseClicked = leftMouseClicked;
        LeftMouseReleased = leftMouseReleased;
        HasBarOneBounds = false;
        HasBarOneSlotOneBounds = false;
        MouseInsideBarOneSlotOne = false;
        BarOneSlotOneItemRegistered = false;
        BarOneSlotOneItemHovered = false;
        BarOneSlotOneBlockedByPalette = false;
    }

    public void RecordNormalInputFrame(int frame, string owner, Vector2 mousePosition)
    {
        InputFrame = frame;
        InputMethod = $"{owner}.Draw";
        CurrentOwner = owner;
        MousePosition = mousePosition;
    }

    public void RecordBarOneGeometry(HudBounds barBounds, HudBounds slotOneBounds, bool mouseInsideSlotOne)
    {
        BarOneBounds = barBounds;
        BarOneSlotOneBounds = slotOneBounds;
        HasBarOneBounds = true;
        HasBarOneSlotOneBounds = true;
        MouseInsideBarOneSlotOne = mouseInsideSlotOne;
    }

    public void RecordBarOneSlotOneItem(bool registered, bool hovered, bool blockedByPalette)
    {
        BarOneSlotOneItemRegistered = registered;
        BarOneSlotOneItemHovered = hovered;
        BarOneSlotOneBlockedByPalette = blockedByPalette;
    }

    public void RecordSelection(uint selectedActionId, HotbarSlotReference? selectedSourceSlot)
    {
        SelectedActionId = selectedActionId;
        SelectedSourceSlot = selectedSourceSlot;
    }

    public void BeginAssignmentAudit(
        HotbarSlotReference destination,
        string commandType,
        uint actionId,
        string actionName,
        int frame,
        byte activeClassJobId,
        bool sharedHotbar)
    {
        LastAssignmentDestination = destination;
        LastAssignmentActionId = actionId;
        LastAssignmentFrame = frame;
        LastAssignmentRequest = $"{commandType} {actionId} ({actionName}) → {destination.Label}; active job {activeClassJobId}; shared={YesNo(sharedHotbar)}";
        ImmediateNativeReadback = "Pending native HotbarSlot.Set + WriteSavedSlot call";
        NextFrameNativeReadback = "Waiting for the next editor frame";
        RendererReadback = $"Waiting for HudRenderer to draw {destination.Label}";
    }

    public void RecordImmediateReadback(string result)
    {
        if (!string.IsNullOrWhiteSpace(result))
            ImmediateNativeReadback = result;
    }

    public void RecordNextFrameReadback(string result)
    {
        if (!string.IsNullOrWhiteSpace(result))
            NextFrameNativeReadback = result;
    }

    public void RecordRendererReadback(uint hotbarId, uint slotId, string result)
    {
        if (!IsTrackingDestination(hotbarId, slotId) || string.IsNullOrWhiteSpace(result))
            return;

        RendererReadback = result;
    }

    public void RecordInputEvent(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            LastInputEvent = message;
    }

    public void RecordNativeWrite(string result)
    {
        if (!string.IsNullOrWhiteSpace(result))
            LastNativeSlotWriteResult = result;
    }

    public void RecordExecution(string result)
    {
        if (!string.IsNullOrWhiteSpace(result))
            LastExecutionAttempt = result;
    }

    public void RecordKeybindStage(string stage, string detail = "", int minimumRepeatIntervalMs = 350)
    {
        if (string.IsNullOrWhiteSpace(stage))
            return;

        var message = string.IsNullOrWhiteSpace(detail)
            ? stage.Trim()
            : $"{stage.Trim()} — {detail.Trim()}";
        var now = Environment.TickCount64;
        if (keybindTraceRateLimits.TryGetValue(message, out var last) &&
            now - last < Math.Max(0, minimumRepeatIntervalMs))
        {
            return;
        }

        keybindTraceRateLimits[message] = now;
        keybindTrace.Add($"{DateTime.Now:HH:mm:ss.fff}  {message}");
        while (keybindTrace.Count > MaximumKeybindTraceEntries)
            keybindTrace.RemoveAt(0);

        Plugin.Log.Information("RE:Frame keybind diagnostic: {Message}", message);
    }

    public void ClearKeybindTrace()
    {
        keybindTrace.Clear();
        keybindTraceRateLimits.Clear();
    }

    private static string YesNo(bool value) => value ? "YES" : "NO";
}
