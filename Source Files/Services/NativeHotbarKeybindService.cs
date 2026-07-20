using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using REFrameXIV.Models;

namespace REFrameXIV.Services;

public readonly record struct NativeKeybindChord(SeVirtualKey Key, KeyModifierFlag Modifiers)
{


    public bool IsEmpty => (byte)Key is 0 or byte.MaxValue;
}

public enum NativeKeybindConflictSource
{
    Reframe,
    NativePerformance,
    NativeFfxiv,
}

public readonly record struct NativeKeybindConflict(
    InputId InputId,
    int BindingIndex,
    string DisplayName,
    NativeKeybindConflictSource Source)
{
    public bool CanReplace => Source == NativeKeybindConflictSource.Reframe;
    public bool IsPreservedPerformance => Source == NativeKeybindConflictSource.NativePerformance;
    public bool BlocksSafeBinding => Source == NativeKeybindConflictSource.NativeFfxiv;
}

public readonly record struct NativeKeybindSnapshot(InputId InputId, Keybind Value);

public enum NativeKeybindCaptureControl
{
    None,
    Cancel,
    Clear,
}


public static unsafe class NativeHotbarKeybindService
{
    private const int FirstHotbarInputId = (int)InputId.HOTBAR_1_1;
    private const int LastHotbarInputId = (int)InputId.HOTBAR_10_B;
    private const int KeyboardBindingCount = 2;
    private const short KeyDownMask = unchecked((short)0x8000);
    private const byte BackspaceKey = 0x08;
    private const byte ShiftKey = 0x10;
    private const byte ControlKey = 0x11;
    private const byte AltKey = 0x12;
    private const byte EscapeKey = 0x1B;
    private const byte DeleteKey = 0x2E;


    private static readonly (string ImGuiKeyNames, SeVirtualKey VirtualKey)[] CaptureKeyDefinitions =
    {
        ("Tab", (SeVirtualKey)0x09),
        ("LeftArrow", (SeVirtualKey)0x25),
        ("UpArrow", (SeVirtualKey)0x26),
        ("RightArrow", (SeVirtualKey)0x27),
        ("DownArrow", (SeVirtualKey)0x28),
        ("PageUp", (SeVirtualKey)0x21),
        ("PageDown", (SeVirtualKey)0x22),
        ("Home", (SeVirtualKey)0x24),
        ("End", (SeVirtualKey)0x23),
        ("Insert", (SeVirtualKey)0x2D),
        ("Space", (SeVirtualKey)0x20),
        ("Enter", (SeVirtualKey)0x0D),

        ("_0|Alpha0|D0", (SeVirtualKey)0x30),
        ("_1|Alpha1|D1", (SeVirtualKey)0x31),
        ("_2|Alpha2|D2", (SeVirtualKey)0x32),
        ("_3|Alpha3|D3", (SeVirtualKey)0x33),
        ("_4|Alpha4|D4", (SeVirtualKey)0x34),
        ("_5|Alpha5|D5", (SeVirtualKey)0x35),
        ("_6|Alpha6|D6", (SeVirtualKey)0x36),
        ("_7|Alpha7|D7", (SeVirtualKey)0x37),
        ("_8|Alpha8|D8", (SeVirtualKey)0x38),
        ("_9|Alpha9|D9", (SeVirtualKey)0x39),

        ("A", (SeVirtualKey)0x41),
        ("B", (SeVirtualKey)0x42),
        ("C", (SeVirtualKey)0x43),
        ("D", (SeVirtualKey)0x44),
        ("E", (SeVirtualKey)0x45),
        ("F", (SeVirtualKey)0x46),
        ("G", (SeVirtualKey)0x47),
        ("H", (SeVirtualKey)0x48),
        ("I", (SeVirtualKey)0x49),
        ("J", (SeVirtualKey)0x4A),
        ("K", (SeVirtualKey)0x4B),
        ("L", (SeVirtualKey)0x4C),
        ("M", (SeVirtualKey)0x4D),
        ("N", (SeVirtualKey)0x4E),
        ("O", (SeVirtualKey)0x4F),
        ("P", (SeVirtualKey)0x50),
        ("Q", (SeVirtualKey)0x51),
        ("R", (SeVirtualKey)0x52),
        ("S", (SeVirtualKey)0x53),
        ("T", (SeVirtualKey)0x54),
        ("U", (SeVirtualKey)0x55),
        ("V", (SeVirtualKey)0x56),
        ("W", (SeVirtualKey)0x57),
        ("X", (SeVirtualKey)0x58),
        ("Y", (SeVirtualKey)0x59),
        ("Z", (SeVirtualKey)0x5A),

        ("F1", (SeVirtualKey)0x70),
        ("F2", (SeVirtualKey)0x71),
        ("F3", (SeVirtualKey)0x72),
        ("F4", (SeVirtualKey)0x73),
        ("F5", (SeVirtualKey)0x74),
        ("F6", (SeVirtualKey)0x75),
        ("F7", (SeVirtualKey)0x76),
        ("F8", (SeVirtualKey)0x77),
        ("F9", (SeVirtualKey)0x78),
        ("F10", (SeVirtualKey)0x79),
        ("F11", (SeVirtualKey)0x7A),
        ("F12", (SeVirtualKey)0x7B),
        ("F13", (SeVirtualKey)0x7C),
        ("F14", (SeVirtualKey)0x7D),
        ("F15", (SeVirtualKey)0x7E),
        ("F16", (SeVirtualKey)0x7F),
        ("F17", (SeVirtualKey)0x80),
        ("F18", (SeVirtualKey)0x81),
        ("F19", (SeVirtualKey)0x82),
        ("F20", (SeVirtualKey)0x83),
        ("F21", (SeVirtualKey)0x84),
        ("F22", (SeVirtualKey)0x85),
        ("F23", (SeVirtualKey)0x86),
        ("F24", (SeVirtualKey)0x87),

        ("Apostrophe|Quote", (SeVirtualKey)0xDE),
        ("Comma", (SeVirtualKey)0xBC),
        ("Minus", (SeVirtualKey)0xBD),
        ("Period", (SeVirtualKey)0xBE),
        ("Slash", (SeVirtualKey)0xBF),
        ("Semicolon", (SeVirtualKey)0xBA),
        ("Equal|Equals", (SeVirtualKey)0xBB),
        ("LeftBracket", (SeVirtualKey)0xDB),
        ("Backslash", (SeVirtualKey)0xDC),
        ("RightBracket", (SeVirtualKey)0xDD),
        ("GraveAccent|Grave", (SeVirtualKey)0xC0),
        ("CapsLock", (SeVirtualKey)0x14),
        ("ScrollLock", (SeVirtualKey)0x91),
        ("NumLock", (SeVirtualKey)0x90),
        ("PrintScreen", (SeVirtualKey)0x2C),
        ("Pause", (SeVirtualKey)0x13),

        ("Keypad0|KeyPad0|Numpad0", (SeVirtualKey)0x60),
        ("Keypad1|KeyPad1|Numpad1", (SeVirtualKey)0x61),
        ("Keypad2|KeyPad2|Numpad2", (SeVirtualKey)0x62),
        ("Keypad3|KeyPad3|Numpad3", (SeVirtualKey)0x63),
        ("Keypad4|KeyPad4|Numpad4", (SeVirtualKey)0x64),
        ("Keypad5|KeyPad5|Numpad5", (SeVirtualKey)0x65),
        ("Keypad6|KeyPad6|Numpad6", (SeVirtualKey)0x66),
        ("Keypad7|KeyPad7|Numpad7", (SeVirtualKey)0x67),
        ("Keypad8|KeyPad8|Numpad8", (SeVirtualKey)0x68),
        ("Keypad9|KeyPad9|Numpad9", (SeVirtualKey)0x69),
        ("KeypadDecimal|KeyPadDecimal|NumpadDecimal", (SeVirtualKey)0x6E),
        ("KeypadDivide|KeyPadDivide|NumpadDivide", (SeVirtualKey)0x6F),
        ("KeypadMultiply|KeyPadMultiply|NumpadMultiply", (SeVirtualKey)0x6A),
        ("KeypadSubtract|KeyPadSubtract|NumpadSubtract", (SeVirtualKey)0x6D),
        ("KeypadAdd|KeyPadAdd|NumpadAdd", (SeVirtualKey)0x6B),
        ("KeypadEnter|KeyPadEnter|NumpadEnter", (SeVirtualKey)0x0D),
    };

    private static readonly byte[] CaptureKeyboardVirtualKeys = CaptureKeyDefinitions
        .Select(definition => (byte)definition.VirtualKey)
        .Distinct()
        .ToArray();

    private static readonly byte[] CaptureMouseVirtualKeys = { 0x02, 0x04, 0x05, 0x06 };

    private static readonly byte[] CaptureObservedVirtualKeys = CaptureKeyboardVirtualKeys
        .Concat(CaptureMouseVirtualKeys)
        .Concat(new[] { BackspaceKey, EscapeKey, DeleteKey })
        .Distinct()
        .ToArray();

    private static readonly HashSet<byte> CaptureHeldKeys = new();
    private static bool capturePhysicalStatePrimed;
    private static NativeKeybindChord? pendingCapturedChord;
    private static NativeKeybindCaptureControl pendingCaptureControl;
    private static bool captureResultDelivered;

    private sealed record KeybindUndoTransaction(
        ReframeHotbarKeybind[] RuntimeBindings,
        NativeKeybindSnapshot[] NativeSnapshots,
        string Description);

    private static KeybindUndoTransaction? lastUndoTransaction;
    public static bool CanUndo => lastUndoTransaction is not null;
    private static readonly Queue<PendingNativeMutation> PendingNativeMutations = new();
    private static bool processingNativeMutation;
    private static CaptureSession? captureSession;
    private static HotbarSlotReference? directCaptureSlot;
    private static int directCaptureBindingIndex = -1;
    private static NativeKeybindCapturePhase capturePhase;
    private static string captureMessage = string.Empty;
    private static PendingNativeVerification? pendingVerification;

    private enum PendingNativeMutationKind
    {
        DisarmCapture,
        Apply,
        Clear,
        Restore,
        CancelCapture,
    }

    private sealed record PendingNativeMutation(
        PendingNativeMutationKind Kind,
        InputId TargetInputId,
        int BindingIndex,
        NativeKeybindChord Chord,
        NativeKeybindConflict[] Conflicts,
        NativeKeybindSnapshot[] Snapshots);

    private sealed record CaptureSession(
        HotbarSlotReference Slot,
        InputId InputId,
        int BindingIndex,
        NativeKeybindSnapshot OriginalSnapshot);

    private sealed record PendingNativeVerification(
        InputId InputId,
        int BindingIndex,
        NativeKeybindChord Expected,
        string Label);

    public static string GetLabel(RaptureHotbarModule* module, uint hotbarId, uint slotId)
    {
        try
        {
            var reference = new HotbarSlotReference(hotbarId, slotId);


            if (TryGetBinding(reference, 0, out var primary, out _))
            {
                var primaryLabel = FormatChord(primary, true);
                if (!string.IsNullOrWhiteSpace(primaryLabel))
                    return primaryLabel;
            }
            if (TryGetBinding(reference, 1, out var secondary, out _))
            {
                var secondaryLabel = FormatChord(secondary, true);
                if (!string.IsNullOrWhiteSpace(secondaryLabel))
                    return secondaryLabel;
            }


            if (!reference.IsCrossHotbar)
                return string.Empty;


            if (module == null || !module->ModuleReady)
                return string.Empty;

            var slot = module->GetSlotById(hotbarId, slotId);
            if (slot == null)
                return string.Empty;

            var hint = slot->PopUpKeybindHintString;
            if (string.IsNullOrWhiteSpace(hint))
                hint = slot->KeybindHintString;

            return CompactNativeHint(hint);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "RE:Frame could not read native binding for hotbar {Hotbar}, slot {Slot}.", hotbarId + 1, slotId + 1);
            return string.Empty;
        }
    }

    public static bool TryMapSlot(HotbarSlotReference slot, out InputId inputId)
    {
        inputId = InputId.NotFound;
        if (slot.IsCrossHotbar || slot.HotbarId > 9u || slot.SlotId > 11u)
            return false;

        inputId = (InputId)(FirstHotbarInputId + (int)(slot.HotbarId * 12u + slot.SlotId));
        return (int)inputId is >= FirstHotbarInputId and <= LastHotbarInputId;
    }

    public static bool IsRuntimeOwnedSlot(HotbarSlotReference slot)
        => Plugin.Instance.AdditionalHotbars.IsVirtualReference(slot) ||
           (slot.HotbarId == ReframeHotbarIds.PetBar && slot.SlotId < ReframeHotbarIds.SlotCount);

    public static bool IsBindableSlot(HotbarSlotReference slot)
        => TryMapSlot(slot, out _) || IsRuntimeOwnedSlot(slot);

    public static bool TryGetBinding(
        HotbarSlotReference slot,
        int bindingIndex,
        out NativeKeybindChord chord,
        out string message)
    {
        chord = default;
        if (IsRuntimeOwnedSlot(slot))
        {
            if (!IsBindingIndexValid(bindingIndex))
            {
                message = "That keyboard binding position is not valid.";
                return false;
            }

            TryGetRuntimeBinding(slot, bindingIndex, out chord);
            message = string.Empty;
            return true;
        }

        if (!TryMapSlot(slot, out var inputId))
        {
            message = "Per-slot keyboard binding is available for normal, pet, and RE:Frame overflow hotbars. Cross hotbars use FFXIV's shared trigger and directional controls.";
            return false;
        }

        if (!IsBindingIndexValid(bindingIndex))
        {
            message = "That keyboard binding position is not valid.";
            return false;
        }

        if (TryGetRuntimeBinding(slot, bindingIndex, out chord))
        {
            message = string.Empty;
            return true;
        }

        try
        {
            var input = GetInputData();
            if (input == null || input->Keybinds == null)
            {
                message = "FFXIV's keybind data is not ready yet.";
                return false;
            }

            var keybind = input->GetKeybind(inputId);
            if (keybind == null)
            {
                message = "FFXIV did not return that hotbar binding.";
                return false;
            }

            chord = ReadKeyboardSetting(keybind, bindingIndex);
            message = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame could not read native keybind {InputId}.", inputId);
            message = "RE:Frame could not read that native keybind.";
            return false;
        }
    }

    public static string GetBindingLabel(HotbarSlotReference slot, int bindingIndex, bool compact = true)
    {
        if (!TryGetBinding(slot, bindingIndex, out var chord, out _))
            return string.Empty;

        var label = FormatChord(chord, compact);
        return string.IsNullOrWhiteSpace(label)
            ? (compact ? string.Empty : "Unbound")
            : label;
    }


    public static bool TryPrepareCapture(
        HotbarSlotReference slot,
        int bindingIndex,
        out string message)
    {
        if (IsRuntimeOwnedSlot(slot))
        {
            if (!IsBindingIndexValid(bindingIndex))
            {
                message = "That keyboard binding position is not valid.";
                return false;
            }
            if (captureSession is not null ||
                (directCaptureSlot is not null &&
                 (directCaptureSlot != slot || directCaptureBindingIndex != bindingIndex)))
            {
                message = "Finish or cancel the current slot capture first.";
                return false;
            }

            directCaptureSlot = slot;
            directCaptureBindingIndex = bindingIndex;
            capturePhase = NativeKeybindCapturePhase.Ready;
            captureMessage = $"Listening for a key for {slot.Label}. Press Esc to cancel or Delete to clear.";
            ResetCaptureInputState();
            message = captureMessage;
            return true;
        }

        if (!TryMapSlot(slot, out var inputId))
        {
            message = "Per-slot keyboard binding is available for normal, pet, and RE:Frame overflow hotbars.";
            return false;
        }

        if (!IsBindingIndexValid(bindingIndex))
        {
            message = "That keyboard binding position is not valid.";
            return false;
        }

        try
        {
            if (captureSession is not null)
            {
                if (captureSession.Slot == slot &&
                    captureSession.BindingIndex == bindingIndex &&
                    capturePhase is NativeKeybindCapturePhase.Preparing or NativeKeybindCapturePhase.Ready)
                {
                    ResetCaptureInputState();
                    message = captureMessage;
                    return true;
                }

                message = "Finish or cancel the current slot capture first.";
                return false;
            }

            var input = GetInputData();
            if (input == null || input->Keybinds == null)
            {
                message = "FFXIV's keybind data is not ready yet.";
                return false;
            }

            var existing = input->GetKeybind(inputId);
            if (existing == null)
            {
                message = "FFXIV did not return that hotbar binding.";
                return false;
            }

            var original = new NativeKeybindSnapshot(inputId, *existing);
            ResetCaptureInputState();
            captureSession = new CaptureSession(slot, inputId, bindingIndex, original);
            capturePhase = NativeKeybindCapturePhase.Preparing;
            captureMessage = $"Preparing {slot.Label}. Waiting for the native duplicate-disarm transaction to complete.";

            EnqueueNativeMutation(new PendingNativeMutation(
                PendingNativeMutationKind.DisarmCapture,
                inputId,
                bindingIndex,
                default,
                Array.Empty<NativeKeybindConflict>(),
                new[] { original }));

            message = captureMessage;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame could not prepare native capture for {Slot}.", slot.Label);
            captureSession = null;
            capturePhase = NativeKeybindCapturePhase.Failed;
            captureMessage = "RE:Frame could not prepare that native slot for key capture.";
            message = captureMessage;
            return false;
        }
    }

    public static NativeKeybindCapturePhase GetCapturePhase(
        HotbarSlotReference slot,
        int bindingIndex,
        out string message)
    {
        if (directCaptureSlot == slot && directCaptureBindingIndex == bindingIndex)
        {
            message = captureMessage;
            return capturePhase;
        }

        if (captureSession is null ||
            captureSession.Slot != slot ||
            captureSession.BindingIndex != bindingIndex)
        {
            message = string.Empty;
            return NativeKeybindCapturePhase.None;
        }

        message = captureMessage;
        return capturePhase;
    }

    public static void CancelPreparedCapture()
    {
        if (directCaptureSlot is not null)
        {
            EndDirectCapture();
            return;
        }

        if (captureSession is null)
            return;

        if (capturePhase is NativeKeybindCapturePhase.Cancelling or NativeKeybindCapturePhase.Committing)
            return;

        capturePhase = NativeKeybindCapturePhase.Cancelling;
        captureMessage = "Restoring the original native binding.";
        EnqueueNativeMutation(new PendingNativeMutation(
            PendingNativeMutationKind.CancelCapture,
            captureSession.InputId,
            captureSession.BindingIndex,
            default,
            Array.Empty<NativeKeybindConflict>(),
            new[] { captureSession.OriginalSnapshot }));
    }


    public static void ProcessCaptureInput()
    {
        try
        {
            var captureActive =
                (captureSession is not null &&
                 capturePhase is NativeKeybindCapturePhase.Preparing or NativeKeybindCapturePhase.Ready) ||
                (directCaptureSlot is not null && capturePhase == NativeKeybindCapturePhase.Ready);

            if (!captureActive)
            {
                ResetCaptureInputState();
                return;
            }

            var currentDown = CaptureObservedVirtualKeys
                .Where(IsPhysicalKeyDown)
                .ToArray();

            if (!capturePhysicalStatePrimed)
            {
                CaptureHeldKeys.UnionWith(currentDown);
                capturePhysicalStatePrimed = true;
                return;
            }

            CaptureHeldKeys.RemoveWhere(key => !currentDown.Contains(key));

            var mayCapture =
                capturePhase == NativeKeybindCapturePhase.Ready &&
                IsGameProcessForeground() &&
                pendingCapturedChord is null &&
                pendingCaptureControl == NativeKeybindCaptureControl.None &&
                !captureResultDelivered;

            if (!mayCapture)
            {
                CaptureHeldKeys.UnionWith(currentDown);
                return;
            }

            var newlyPressed = currentDown
                .Where(key => !CaptureHeldKeys.Contains(key))
                .ToArray();

            CaptureHeldKeys.UnionWith(currentDown);
            if (newlyPressed.Length == 0)
                return;

            if (newlyPressed.Contains(EscapeKey))
            {
                pendingCaptureControl = NativeKeybindCaptureControl.Cancel;
                SuppressCapturedKeyboardKey(EscapeKey);
                return;
            }

            if (newlyPressed.Contains(DeleteKey) || newlyPressed.Contains(BackspaceKey))
            {
                pendingCaptureControl = NativeKeybindCaptureControl.Clear;
                SuppressCapturedKeyboardKey(
                    newlyPressed.Contains(DeleteKey) ? DeleteKey : BackspaceKey);
                return;
            }

            var keyCode = CaptureMouseVirtualKeys
                .Concat(CaptureKeyboardVirtualKeys)
                .FirstOrDefault(key => newlyPressed.Contains(key));
            if (keyCode == 0)
                return;

            var chord = new NativeKeybindChord(
                (SeVirtualKey)keyCode,
                ReadPhysicalModifiers());

            pendingCapturedChord = chord;
            SuppressCapturedPhysicalInput(keyCode);
            Plugin.Instance.BarInputDiagnostics.RecordKeybindStage(
                "CAPTURED",
                FormatChord(chord, false));
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "RE:Frame keybind capture could not poll physical input.");
        }
    }

    public static bool TryConsumeCaptureControl(out NativeKeybindCaptureControl control)
    {
        control = pendingCaptureControl;
        if (control == NativeKeybindCaptureControl.None)
            return false;

        pendingCaptureControl = NativeKeybindCaptureControl.None;
        captureResultDelivered = true;
        return true;
    }


    public static bool TryCaptureChord(out NativeKeybindChord chord)
    {
        if (pendingCapturedChord is not { } captured)
        {
            chord = default;
            return false;
        }

        pendingCapturedChord = null;
        captureResultDelivered = true;
        chord = captured;
        return true;
    }

    private static KeyModifierFlag ReadPhysicalModifiers()
    {
        var modifiers = default(KeyModifierFlag);
        if (IsPhysicalKeyDown(ControlKey))
            modifiers |= KeyModifierFlag.Ctrl;
        if (IsPhysicalKeyDown(ShiftKey))
            modifiers |= KeyModifierFlag.Shift;
        if (IsPhysicalKeyDown(AltKey))
            modifiers |= KeyModifierFlag.Alt;
        return modifiers;
    }

    public static IReadOnlyList<NativeKeybindConflict> FindConflicts(
        HotbarSlotReference targetSlot,
        int targetBindingIndex,
        NativeKeybindChord chord)
    {
        if (chord.IsEmpty || !TryMapSlot(targetSlot, out var targetInputId))
            return Array.Empty<NativeKeybindConflict>();

        try
        {
            var conflicts = new Dictionary<(InputId InputId, int BindingIndex), NativeKeybindConflict>();

            foreach (var binding in GetRuntimeBindings())
            {
                var slot = new HotbarSlotReference(binding.HotbarId, binding.SlotId);
                if (!TryMapSlot(slot, out var inputId))
                    continue;
                if (inputId == targetInputId && binding.BindingIndex == targetBindingIndex)
                    continue;
                if (ToChord(binding) != chord)
                    continue;

                conflicts[(inputId, binding.BindingIndex)] = new NativeKeybindConflict(
                    inputId,
                    binding.BindingIndex,
                    DescribeInput(inputId),
                    NativeKeybindConflictSource.Reframe);
            }

            var input = GetInputData();
            if (input != null && input->Keybinds != null && input->NumKeybinds > 0)
            {
                for (var inputIndex = 0; inputIndex < input->NumKeybinds; inputIndex++)
                {
                    var inputId = (InputId)inputIndex;
                    var keybind = input->Keybinds + inputIndex;
                    for (var bindingIndex = 0; bindingIndex < KeyboardBindingCount; bindingIndex++)
                    {
                        if (inputId == targetInputId && bindingIndex == targetBindingIndex)
                            continue;
                        if (ReadKeyboardSetting(keybind, bindingIndex) != chord)
                            continue;

                        var key = (inputId, bindingIndex);
                        if (conflicts.ContainsKey(key))
                            continue;

                        conflicts[key] = new NativeKeybindConflict(
                            inputId,
                            bindingIndex,
                            DescribeInput(inputId),
                            IsPerformanceInput(inputId)
                                ? NativeKeybindConflictSource.NativePerformance
                                : NativeKeybindConflictSource.NativeFfxiv);
                    }
                }
            }

            return conflicts.Values.ToArray();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame could not scan keybind conflicts.");
            return Array.Empty<NativeKeybindConflict>();
        }
    }

    public static bool TryApplyBinding(
        HotbarSlotReference targetSlot,
        int targetBindingIndex,
        NativeKeybindChord chord,
        IReadOnlyList<NativeKeybindConflict> conflictsToReplace,
        out List<NativeKeybindSnapshot> undoSnapshots,
        out string message)
    {
        undoSnapshots = new List<NativeKeybindSnapshot>();
        if (IsRuntimeOwnedSlot(targetSlot))
            return TryApplyDirectRuntimeBinding(targetSlot, targetBindingIndex, chord, out message);

        if (!TryMapSlot(targetSlot, out var targetInputId))
        {
            message = "Per-slot keyboard binding is available for normal, pet, and RE:Frame overflow hotbars.";
            return false;
        }

        if (!IsBindingIndexValid(targetBindingIndex) || chord.IsEmpty)
        {
            message = "That keybind is not valid.";
            return false;
        }

        if (conflictsToReplace.Any(conflict => !conflict.CanReplace))
        {
            message = "RE:Frame refused to remove an unrelated native FFXIV binding. Choose another key or preserve the supported contextual binding.";
            return false;
        }

        try
        {
            var input = GetInputData();
            if (input == null || input->Keybinds == null)
            {
                message = "FFXIV's keybind data is not ready yet.";
                return false;
            }

            var affectedIds = conflictsToReplace
                .Select(conflict => conflict.InputId)
                .Append(targetInputId)
                .Distinct()
                .ToArray();
            var runtimeBefore = CaptureRuntimeBindings();

            foreach (var inputId in affectedIds)
            {
                if (inputId == targetInputId &&
                    captureSession is not null &&
                    captureSession.InputId == targetInputId &&
                    captureSession.BindingIndex == targetBindingIndex)
                {
                    undoSnapshots.Add(captureSession.OriginalSnapshot);
                    continue;
                }

                var existing = input->GetKeybind(inputId);
                if (existing == null)
                {
                    undoSnapshots.Clear();
                    message = $"FFXIV did not return {DescribeInput(inputId)}, so no bindings were changed.";
                    return false;
                }

                undoSnapshots.Add(new NativeKeybindSnapshot(inputId, *existing));
            }

            var replacedRuntimeBindings = GetRuntimeBindings().Count(binding =>
            {
                var boundSlot = new HotbarSlotReference(binding.HotbarId, binding.SlotId);
                return IsRuntimeOwnedSlot(boundSlot) &&
                       ToChord(binding) == chord;
            });
            GetRuntimeBindings().RemoveAll(binding =>
            {
                var boundSlot = new HotbarSlotReference(binding.HotbarId, binding.SlotId);
                return IsRuntimeOwnedSlot(boundSlot) &&
                       ToChord(binding) == chord;
            });
            foreach (var conflict in conflictsToReplace)
                RemoveRuntimeBinding(conflict.InputId, conflict.BindingIndex);
            SetRuntimeBinding(targetInputId, targetBindingIndex, chord);
            lastUndoTransaction = new KeybindUndoTransaction(
                runtimeBefore,
                undoSnapshots.ToArray(),
                $"binding {FormatChord(chord, false)} on {targetSlot.Label}");
            SaveRuntimeBindings();
            Plugin.Instance.BarInputDiagnostics.RecordKeybindStage(
                "BINDING STORED",
                $"{targetSlot.Label} = {FormatChord(chord, false)}");

            EnqueueNativeMutation(new PendingNativeMutation(
                PendingNativeMutationKind.Apply,
                targetInputId,
                targetBindingIndex,
                chord,
                conflictsToReplace.ToArray(),
                undoSnapshots.ToArray()));

            if (captureSession is not null && captureSession.InputId == targetInputId)
            {
                capturePhase = NativeKeybindCapturePhase.Committing;
                captureMessage = $"Saving {FormatChord(chord, false)} as an RE:Frame slot binding while preserving unrelated native controls.";
            }

            var replacedCount = conflictsToReplace.Count + replacedRuntimeBindings;
            message = replacedCount > 0
                ? $"Bound {FormatChord(chord, false)} to the RE:Frame slot and replaced {replacedCount} other RE:Frame binding{(replacedCount == 1 ? string.Empty : "s")}. Native FFXIV controls were preserved."
                : $"Bound {FormatChord(chord, false)} directly to the RE:Frame slot. Native FFXIV controls were preserved.";
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame could not prepare keybind for {Slot}.", targetSlot.Label);
            message = "RE:Frame could not prepare that binding. No confirmed change was kept.";
            return false;
        }
    }

    public static bool TryClearBinding(
        HotbarSlotReference targetSlot,
        int bindingIndex,
        out List<NativeKeybindSnapshot> undoSnapshots,
        out string message)
    {
        undoSnapshots = new List<NativeKeybindSnapshot>();
        if (IsRuntimeOwnedSlot(targetSlot))
            return TryClearDirectRuntimeBinding(targetSlot, bindingIndex, out message);

        if (!TryMapSlot(targetSlot, out var inputId))
        {
            message = "Per-slot keyboard binding is available for normal, pet, and RE:Frame overflow hotbars.";
            return false;
        }

        if (!IsBindingIndexValid(bindingIndex))
        {
            message = "That keyboard binding position is not valid.";
            return false;
        }

        try
        {
            var input = GetInputData();
            if (input == null || input->Keybinds == null)
            {
                message = "FFXIV's keybind data is not ready yet.";
                return false;
            }

            NativeKeybindSnapshot snapshot;
            NativeKeybindChord nativeCurrent;
            if (captureSession is not null &&
                captureSession.InputId == inputId &&
                captureSession.BindingIndex == bindingIndex)
            {
                snapshot = captureSession.OriginalSnapshot;
                var value = snapshot.Value;
                nativeCurrent = ReadKeyboardSetting(&value, bindingIndex);
            }
            else
            {
                var existing = input->GetKeybind(inputId);
                if (existing == null)
                {
                    message = "FFXIV did not return that hotbar binding.";
                    return false;
                }

                snapshot = new NativeKeybindSnapshot(inputId, *existing);
                nativeCurrent = ReadKeyboardSetting(existing, bindingIndex);
            }

            var current = TryGetRuntimeBinding(targetSlot, bindingIndex, out var runtimeCurrent)
                ? runtimeCurrent
                : nativeCurrent;

            if (current.IsEmpty)
            {
                message = "That binding is already empty.";
                return true;
            }

            undoSnapshots.Add(snapshot);
            var runtimeBefore = CaptureRuntimeBindings();
            RemoveRuntimeBinding(inputId, bindingIndex);
            lastUndoTransaction = new KeybindUndoTransaction(
                runtimeBefore,
                undoSnapshots.ToArray(),
                $"clear of {FormatChord(current, false)} from {targetSlot.Label}");
            SaveRuntimeBindings();
            Plugin.Instance.BarInputDiagnostics.RecordKeybindStage(
                "BINDING CLEARED",
                $"{targetSlot.Label} ({(bindingIndex == 0 ? "Primary" : "Secondary")})");

            EnqueueNativeMutation(new PendingNativeMutation(
                PendingNativeMutationKind.Clear,
                inputId,
                bindingIndex,
                default,
                Array.Empty<NativeKeybindConflict>(),
                undoSnapshots.ToArray()));

            if (captureSession is not null && captureSession.InputId == inputId)
            {
                capturePhase = NativeKeybindCapturePhase.Committing;
                captureMessage = "Clearing the RE:Frame slot binding and its native duplicate.";
            }

            message = $"Cleared {FormatChord(current, false)} from the RE:Frame slot.";
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame could not prepare keybind clear for {Slot}.", targetSlot.Label);
            message = "RE:Frame could not clear that binding. No confirmed change was kept.";
            return false;
        }
    }

    private static bool TryApplyDirectRuntimeBinding(
        HotbarSlotReference targetSlot,
        int bindingIndex,
        NativeKeybindChord chord,
        out string message)
    {
        if (!IsBindingIndexValid(bindingIndex) || chord.IsEmpty)
        {
            message = "That keybind is not valid.";
            return false;
        }

        try
        {
            var runtimeBefore = CaptureRuntimeBindings();
            var input = GetInputData();
            if (input != null && input->Keybinds != null && input->NumKeybinds > 0)
            {
                for (var inputIndex = 0; inputIndex < input->NumKeybinds; inputIndex++)
                {
                    var inputId = (InputId)inputIndex;
                    var keybind = input->Keybinds + inputIndex;
                    for (var index = 0; index < KeyboardBindingCount; index++)
                    {
                        if (ReadKeyboardSetting(keybind, index) != chord)
                            continue;
                        message = $"{FormatChord(chord, false)} is already used by {DescribeInput(inputId)}. RE:Frame preserved that native FFXIV binding.";
                        EndDirectCapture();
                        return false;
                    }
                }
            }

            var replaced = GetRuntimeBindings().Count(binding =>
                !(binding.HotbarId == targetSlot.HotbarId &&
                  binding.SlotId == targetSlot.SlotId &&
                  binding.BindingIndex == bindingIndex) &&
                ToChord(binding) == chord);
            GetRuntimeBindings().RemoveAll(binding =>
                (binding.HotbarId == targetSlot.HotbarId &&
                 binding.SlotId == targetSlot.SlotId &&
                 binding.BindingIndex == bindingIndex) ||
                ToChord(binding) == chord);
            SetRuntimeBinding(targetSlot, bindingIndex, chord);
            lastUndoTransaction = new KeybindUndoTransaction(
                runtimeBefore,
                Array.Empty<NativeKeybindSnapshot>(),
                $"binding {FormatChord(chord, false)} on {targetSlot.Label}");
            SaveRuntimeBindings();
            EndDirectCapture();
            Plugin.Instance.BarInputDiagnostics.RecordKeybindStage(
                "BINDING STORED",
                $"{targetSlot.Label} = {FormatChord(chord, false)}");
            message = replaced > 0
                ? $"Bound {FormatChord(chord, false)} to {targetSlot.Label} and moved that RE:Frame key away from {replaced} other slot{(replaced == 1 ? string.Empty : "s")}."
                : $"Bound {FormatChord(chord, false)} to {targetSlot.Label}.";
            return true;
        }
        catch (Exception ex)
        {
            EndDirectCapture();
            Plugin.Log.Error(ex, "RE:Frame could not store direct keybind for {Slot}.", targetSlot.Label);
            message = "RE:Frame could not save that direct slot binding.";
            return false;
        }
    }

    private static bool TryClearDirectRuntimeBinding(
        HotbarSlotReference targetSlot,
        int bindingIndex,
        out string message)
    {
        if (!IsBindingIndexValid(bindingIndex))
        {
            message = "That keyboard binding position is not valid.";
            return false;
        }

        var existed = TryGetRuntimeBinding(targetSlot, bindingIndex, out var chord);
        var runtimeBefore = CaptureRuntimeBindings();
        RemoveRuntimeBinding(targetSlot, bindingIndex);
        if (existed)
        {
            lastUndoTransaction = new KeybindUndoTransaction(
                runtimeBefore,
                Array.Empty<NativeKeybindSnapshot>(),
                $"clear of {FormatChord(chord, false)} from {targetSlot.Label}");
        }
        SaveRuntimeBindings();
        EndDirectCapture();
        message = existed
            ? $"Cleared {FormatChord(chord, false)} from {targetSlot.Label}."
            : "That binding is already empty.";
        return true;
    }

    public static bool TryUndoLastChange(out string message)
    {
        if (lastUndoTransaction is not { } undo)
        {
            message = "There is no keybind change to undo.";
            return false;
        }

        try
        {
            RestoreRuntimeBindings(undo.RuntimeBindings);
            SaveRuntimeBindings();

            if (undo.NativeSnapshots.Length > 0)
            {
                EnqueueNativeMutation(new PendingNativeMutation(
                    PendingNativeMutationKind.Restore,
                    InputId.NotFound,
                    -1,
                    default,
                    Array.Empty<NativeKeybindConflict>(),
                    undo.NativeSnapshots));
            }
            else
            {
                EndDirectCapture();
            }

            lastUndoTransaction = null;
            Plugin.Instance.BarInputDiagnostics.RecordKeybindStage(
                "UNDO RESTORED",
                undo.Description);
            message = $"Restored the previous RE:Frame keybind state before the {undo.Description}.";
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame could not restore the last keybind transaction.");
            message = "RE:Frame could not restore the previous bindings.";
            return false;
        }
    }

    public static string FormatChord(NativeKeybindChord chord, bool compact)
    {
        if (chord.IsEmpty)
            return compact ? string.Empty : "Unbound";

        var keyName = GetKeyName(chord.Key, compact);
        if (string.IsNullOrWhiteSpace(keyName))
            return compact ? string.Empty : "Unbound";

        var parts = new List<string>(4);
        if ((chord.Modifiers & KeyModifierFlag.Ctrl) != 0)
            parts.Add(compact ? "C" : "Ctrl");
        if ((chord.Modifiers & KeyModifierFlag.Shift) != 0)
            parts.Add(compact ? "S" : "Shift");
        if ((chord.Modifiers & KeyModifierFlag.Alt) != 0)
            parts.Add(compact ? "A" : "Alt");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public static bool IsPerformanceInput(InputId inputId)
    {
        var raw = inputId.ToString();
        return raw.Contains("PERFORM", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("MUSIC", StringComparison.OrdinalIgnoreCase);
    }

    public static string DescribeInput(InputId inputId)
    {
        var numeric = (int)inputId;
        if (numeric is >= FirstHotbarInputId and <= LastHotbarInputId)
        {
            var offset = numeric - FirstHotbarInputId;
            return $"Hotbar {offset / 12 + 1}, Slot {offset % 12 + 1}";
        }

        var raw = inputId.ToString();
        if (int.TryParse(raw, out _))
            return $"Game input {numeric}";

        return string.Join(" ", raw
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length <= 3
                ? word
                : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static UIInputData* GetInputData()
        => UIInputData.Instance();

    private static NativeKeybindChord ReadKeyboardSetting(Keybind* keybind, int bindingIndex)
    {
        var setting = ((KeySetting*)keybind)[bindingIndex];
        return new NativeKeybindChord(setting.Key, setting.KeyModifier);
    }

    private static void WriteKeyboardSetting(Keybind* keybind, int bindingIndex, NativeKeybindChord chord)
    {
        ((KeySetting*)keybind)[bindingIndex] = new KeySetting
        {
            Key = chord.Key,
            KeyModifier = chord.Modifiers,
        };
    }

    private static bool TryWriteAndReadBack(
        UIInputData* input,
        InputId inputId,
        int bindingIndex,
        NativeKeybindChord chord,
        out string detail)
    {
        detail = string.Empty;
        var live = input->GetKeybind(inputId);
        if (live == null)
        {
            detail = $"{DescribeInput(inputId)} returned a null native keybind pointer";
            return false;
        }

        var replacement = *live;
        WriteKeyboardSetting(&replacement, bindingIndex, chord);
        input->SetKeybind(inputId, &replacement);

        var readback = input->GetKeybind(inputId);
        if (readback == null)
        {
            detail = $"{DescribeInput(inputId)} returned a null readback pointer";
            return false;
        }

        var actual = ReadKeyboardSetting(readback, bindingIndex);
        var matched = actual == chord;
        detail = $"{(matched ? "MATCH" : "MISMATCH")} — {DescribeInput(inputId)} {(bindingIndex == 0 ? "primary" : "secondary")}: expected {FormatChord(chord, false)}, read {FormatChord(actual, false)}";
        return matched;
    }

    private static bool TryRestoreFullRecord(
        UIInputData* input,
        NativeKeybindSnapshot snapshot,
        out string detail)
    {
        var replacement = snapshot.Value;
        input->SetKeybind(snapshot.InputId, &replacement);
        var readback = input->GetKeybind(snapshot.InputId);
        if (readback == null)
        {
            detail = $"{DescribeInput(snapshot.InputId)} returned a null restore readback pointer";
            return false;
        }

        var expected = snapshot.Value;
        var matched = ReadKeyboardSetting(readback, 0) == ReadKeyboardSetting(&expected, 0) &&
                      ReadKeyboardSetting(readback, 1) == ReadKeyboardSetting(&expected, 1);
        detail = $"{(matched ? "MATCH" : "MISMATCH")} — restored {DescribeInput(snapshot.InputId)}";
        return matched;
    }

    private static void PersistNativeKeybindFile(UIInputData* input)
    {
        input->HasChanges = true;
        input->SaveFile(false);
    }

    private static void EnqueueNativeMutation(PendingNativeMutation mutation)
    {
        lock (PendingNativeMutations)
            PendingNativeMutations.Enqueue(mutation);
    }


    public static void ProcessPendingNativeMutations()
    {
        if (processingNativeMutation || !Plugin.ClientState.IsLoggedIn)
            return;

        ProcessPendingVerification();

        PendingNativeMutation? mutation;
        lock (PendingNativeMutations)
            mutation = PendingNativeMutations.Count > 0 ? PendingNativeMutations.Dequeue() : null;
        if (mutation is null)
            return;

        processingNativeMutation = true;
        try
        {
            ExecutePendingNativeMutation(mutation);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RE:Frame native keybind transaction {Kind} failed.", mutation.Kind);
            Plugin.Instance.BarInputDiagnostics.RecordNativeWrite(
                $"KEYBIND EXCEPTION — {mutation.Kind}: {ex.GetType().Name}: {ex.Message}");

            if (mutation.Snapshots.Length > 0)
                TryRestoreNativeSnapshots(mutation.Snapshots);

            if (mutation.Kind == PendingNativeMutationKind.DisarmCapture)
            {


                capturePhase = NativeKeybindCapturePhase.Ready;
                captureMessage = "Ready. Native duplicate could not be cleared, but RE:Frame capture remains active.";
            }
            else if (captureSession is not null)
            {
                captureSession = null;
                capturePhase = NativeKeybindCapturePhase.None;
                captureMessage = string.Empty;
            }
        }
        finally
        {
            processingNativeMutation = false;
        }
    }

    private static void ExecutePendingNativeMutation(PendingNativeMutation mutation)
    {
        var input = GetInputData();
        if (input == null || input->Keybinds == null)
            throw new InvalidOperationException("FFXIV keybind data is not ready.");

        switch (mutation.Kind)
        {
            case PendingNativeMutationKind.DisarmCapture:
            {
                if (TryWriteAndReadBack(input, mutation.TargetInputId, mutation.BindingIndex, default, out var detail))
                {
                    Plugin.Instance.BarInputDiagnostics.RecordNativeWrite($"KEYBIND CAPTURE NATIVE DISARMED — {detail}");
                    pendingVerification = new PendingNativeVerification(
                        mutation.TargetInputId,
                        mutation.BindingIndex,
                        default,
                        "capture disarm");
                }
                else
                {
                    Plugin.Instance.BarInputDiagnostics.RecordNativeWrite($"KEYBIND CAPTURE NATIVE DISARM SKIPPED — {detail}");
                }

                capturePhase = NativeKeybindCapturePhase.Ready;
                captureMessage = "Ready. Press the new key or Mouse 2–5.";
                break;
            }

            case PendingNativeMutationKind.Apply:
            {


                foreach (var conflict in mutation.Conflicts)
                {
                    Plugin.Instance.BarInputDiagnostics.RecordNativeWrite(
                        $"KEYBIND RE:FRAME CONFLICT REPLACED — {DescribeInput(conflict.InputId)} {(conflict.BindingIndex == 0 ? "primary" : "secondary")}; native record preserved");
                }


                if (!TryWriteAndReadBack(input, mutation.TargetInputId, mutation.BindingIndex, default, out var targetDetail))
                    throw new InvalidOperationException(targetDetail);

                PersistNativeKeybindFile(input);
                Plugin.Instance.BarInputDiagnostics.RecordNativeWrite($"KEYBIND NATIVE SLOT DISARMED — {targetDetail}");
                pendingVerification = new PendingNativeVerification(
                    mutation.TargetInputId,
                    mutation.BindingIndex,
                    default,
                    "RE:Frame-owned binding disarm");
                captureSession = null;
                capturePhase = NativeKeybindCapturePhase.None;
                captureMessage = string.Empty;
                break;
            }

            case PendingNativeMutationKind.Clear:
            {
                if (!TryWriteAndReadBack(input, mutation.TargetInputId, mutation.BindingIndex, default, out var clearDetail))
                    throw new InvalidOperationException(clearDetail);

                PersistNativeKeybindFile(input);
                Plugin.Instance.BarInputDiagnostics.RecordNativeWrite($"KEYBIND CLEAR VERIFIED — {clearDetail}");
                pendingVerification = new PendingNativeVerification(
                    mutation.TargetInputId,
                    mutation.BindingIndex,
                    default,
                    "cleared binding");
                captureSession = null;
                capturePhase = NativeKeybindCapturePhase.None;
                captureMessage = string.Empty;
                break;
            }

            case PendingNativeMutationKind.Restore:
            case PendingNativeMutationKind.CancelCapture:
            {
                foreach (var snapshot in mutation.Snapshots)
                {
                    if (!TryRestoreFullRecord(input, snapshot, out var restoreDetail))
                        throw new InvalidOperationException(restoreDetail);
                    Plugin.Instance.BarInputDiagnostics.RecordNativeWrite($"KEYBIND RESTORE VERIFIED — {restoreDetail}");
                }

                PersistNativeKeybindFile(input);
                captureSession = null;
                capturePhase = NativeKeybindCapturePhase.None;
                captureMessage = string.Empty;
                break;
            }
        }

        Plugin.Log.Information("RE:Frame completed native keybind transaction {Kind}.", mutation.Kind);
    }

    private static void ProcessPendingVerification()
    {
        if (pendingVerification is not { } verification)
            return;

        pendingVerification = null;
        try
        {
            var input = GetInputData();
            Keybind* keybind = null;
            if (input != null)
                keybind = input->GetKeybind(verification.InputId);
            if (keybind == null)
            {
                Plugin.Instance.BarInputDiagnostics.RecordNextFrameReadback(
                    $"KEYBIND FAILED — {verification.Label}: native pointer unavailable");
                return;
            }

            var actual = ReadKeyboardSetting(keybind, verification.BindingIndex);
            var matched = actual == verification.Expected;
            Plugin.Instance.BarInputDiagnostics.RecordNextFrameReadback(
                $"KEYBIND {(matched ? "MATCH" : "MISMATCH")} — {verification.Label}: expected {FormatChord(verification.Expected, false)}, read {FormatChord(actual, false)}");
        }
        catch (Exception ex)
        {
            Plugin.Instance.BarInputDiagnostics.RecordNextFrameReadback(
                $"KEYBIND EXCEPTION — {verification.Label}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryRestoreNativeSnapshots(IReadOnlyList<NativeKeybindSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return;

        try
        {
            var input = GetInputData();
            if (input == null || input->Keybinds == null)
                return;
            foreach (var snapshot in snapshots)
                TryRestoreFullRecord(input, snapshot, out _);
            PersistNativeKeybindFile(input);
        }
        catch (Exception restoreException)
        {
            Plugin.Log.Error(restoreException, "RE:Frame could not restore native keybind snapshots after failure.");
        }
    }

    private static List<ReframeHotbarKeybind> GetRuntimeBindings()
    {
        Plugin.Instance.Configuration.ReframeHotbarKeybinds ??= new List<ReframeHotbarKeybind>();
        return Plugin.Instance.Configuration.ReframeHotbarKeybinds;
    }

    private static NativeKeybindChord ToChord(ReframeHotbarKeybind binding)
        => new((SeVirtualKey)binding.Key, (KeyModifierFlag)binding.Modifiers);

    private static bool TryGetRuntimeBinding(HotbarSlotReference slot, int bindingIndex, out NativeKeybindChord chord)
    {
        var binding = GetRuntimeBindings().LastOrDefault(item =>
            item.HotbarId == slot.HotbarId &&
            item.SlotId == slot.SlotId &&
            item.BindingIndex == bindingIndex);
        if (binding is null)
        {
            chord = default;
            return false;
        }

        chord = ToChord(binding);
        return !chord.IsEmpty;
    }

    private static void SetRuntimeBinding(HotbarSlotReference slot, int bindingIndex, NativeKeybindChord chord)
    {
        RemoveRuntimeBinding(slot, bindingIndex);
        if (chord.IsEmpty)
            return;
        GetRuntimeBindings().Add(new ReframeHotbarKeybind
        {
            HotbarId = slot.HotbarId,
            SlotId = slot.SlotId,
            BindingIndex = bindingIndex,
            Key = (byte)chord.Key,
            Modifiers = (byte)chord.Modifiers,
        });
    }

    private static void RemoveRuntimeBinding(HotbarSlotReference slot, int bindingIndex)
        => GetRuntimeBindings().RemoveAll(item =>
            item.HotbarId == slot.HotbarId &&
            item.SlotId == slot.SlotId &&
            item.BindingIndex == bindingIndex);

    private static void SetRuntimeBinding(InputId inputId, int bindingIndex, NativeKeybindChord chord)
    {
        RemoveRuntimeBinding(inputId, bindingIndex);
        if (chord.IsEmpty || !TryMapInputId(inputId, out var slot))
            return;

        GetRuntimeBindings().Add(new ReframeHotbarKeybind
        {
            HotbarId = slot.HotbarId,
            SlotId = slot.SlotId,
            BindingIndex = bindingIndex,
            Key = (byte)chord.Key,
            Modifiers = (byte)chord.Modifiers,
        });
    }

    private static void RemoveRuntimeBinding(InputId inputId, int bindingIndex)
    {
        if (!TryMapInputId(inputId, out var slot))
            return;

        GetRuntimeBindings().RemoveAll(item =>
            item.HotbarId == slot.HotbarId &&
            item.SlotId == slot.SlotId &&
            item.BindingIndex == bindingIndex);
    }

    private static ReframeHotbarKeybind[] CaptureRuntimeBindings()
        => GetRuntimeBindings()
            .Select(CloneRuntimeBinding)
            .ToArray();

    private static void RestoreRuntimeBindings(IEnumerable<ReframeHotbarKeybind> bindings)
    {
        var target = GetRuntimeBindings();
        target.Clear();
        target.AddRange(bindings.Select(CloneRuntimeBinding));
    }

    private static ReframeHotbarKeybind CloneRuntimeBinding(ReframeHotbarKeybind binding)
        => new()
        {
            HotbarId = binding.HotbarId,
            SlotId = binding.SlotId,
            BindingIndex = binding.BindingIndex,
            Key = binding.Key,
            Modifiers = binding.Modifiers,
        };

    private static void EndDirectCapture()
    {
        directCaptureSlot = null;
        directCaptureBindingIndex = -1;
        capturePhase = NativeKeybindCapturePhase.None;
        captureMessage = string.Empty;
        ResetCaptureInputState();
    }

    private static void SaveRuntimeBindings()
        => Plugin.Instance.SaveConfiguration();

    private static bool TryMapInputId(InputId inputId, out HotbarSlotReference slot)
    {
        var numeric = (int)inputId;
        if (numeric is < FirstHotbarInputId or > LastHotbarInputId)
        {
            slot = default;
            return false;
        }

        var offset = numeric - FirstHotbarInputId;
        slot = new HotbarSlotReference((uint)(offset / 12), (uint)(offset % 12));
        return true;
    }

    private static void SuppressCapturedPhysicalInput(byte keyCode)
    {
        SuppressCapturedKeyboardKey(keyCode);

        var flag = keyCode switch
        {
            0x02 => MouseButtonFlags.RBUTTON,
            0x04 => MouseButtonFlags.MBUTTON,
            0x05 => MouseButtonFlags.XBUTTON1,
            0x06 => MouseButtonFlags.XBUTTON2,
            _ => default,
        };

        if (flag == default)
            return;

        try
        {
            var input = UIInputData.Instance();
            if (input == null)
                return;

            input->CursorInputs.Clear(false, flag);
            input->UIFilteredCursorInputs.Clear(false, flag);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "RE:Frame could not consume captured mouse virtual key {KeyCode}.", keyCode);
        }
    }

    private static void SuppressCapturedKeyboardKey(int keyCode)
    {
        try
        {
            Plugin.KeyState.SetRawValue(keyCode, 0);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "RE:Frame could not consume captured virtual key {KeyCode}.", keyCode);
        }
    }

    private static void ResetCaptureInputState()
    {
        CaptureHeldKeys.Clear();
        capturePhysicalStatePrimed = false;
        pendingCapturedChord = null;
        pendingCaptureControl = NativeKeybindCaptureControl.None;
        captureResultDelivered = false;
    }

    private static bool IsPhysicalKeyDown(byte virtualKey)
        => (GetAsyncKeyState(virtualKey) & KeyDownMask) != 0;

    private static bool IsGameProcessForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);


    public static void Shutdown()
    {
        try
        {
            if (captureSession is { } session && Plugin.ClientState.IsLoggedIn)
            {
                var input = GetInputData();
                if (input != null && input->Keybinds != null &&
                    TryRestoreFullRecord(input, session.OriginalSnapshot, out var detail))
                {
                    PersistNativeKeybindFile(input);
                    Plugin.Instance.BarInputDiagnostics.RecordNativeWrite(
                        $"KEYBIND DISPOSAL RESTORE VERIFIED — {detail}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not restore an in-progress keybind capture during disposal.");
        }
        finally
        {
            lock (PendingNativeMutations)
                PendingNativeMutations.Clear();
            pendingVerification = null;
            captureSession = null;
            directCaptureSlot = null;
            directCaptureBindingIndex = -1;
            capturePhase = NativeKeybindCapturePhase.None;
            captureMessage = string.Empty;
            processingNativeMutation = false;
            lastUndoTransaction = null;
            ResetCaptureInputState();
        }
    }

    private static bool IsBindingIndexValid(int bindingIndex)
        => bindingIndex is >= 0 and < KeyboardBindingCount;

    private static string GetKeyName(SeVirtualKey key, bool compact)
    {
        var code = (byte)key;
        if (code is >= 48 and <= 57)
            return ((char)code).ToString();
        if (code is >= 65 and <= 90)
            return ((char)code).ToString();
        if (code is >= 96 and <= 105)
            return compact ? $"N{code - 96}" : $"Numpad {code - 96}";
        if (code is >= 112 and <= 135)
            return $"F{code - 111}";

        return code switch
        {
            1 => "Mouse 1",
            2 => "Mouse 2",
            4 => "Mouse 3",
            5 => "Mouse 4",
            6 => "Mouse 5",
            9 => "Tab",
            12 => "Clear",
            13 => "Enter",
            19 => "Pause",
            20 => compact ? "Caps" : "Caps Lock",
            32 => "Space",
            33 => compact ? "PgUp" : "Page Up",
            34 => compact ? "PgDn" : "Page Down",
            35 => "End",
            36 => "Home",
            37 => "Left",
            38 => "Up",
            39 => "Right",
            40 => "Down",
            44 => compact ? "PrtSc" : "Print Screen",
            45 => "Insert",
            91 => compact ? "LWin" : "Left Windows",
            92 => compact ? "RWin" : "Right Windows",
            93 => "Menu",
            95 => "Sleep",
            106 => compact ? "N*" : "Numpad *",
            107 => compact ? "N+" : "Numpad +",
            108 => compact ? "N," : "Numpad Separator",
            109 => compact ? "N-" : "Numpad -",
            110 => compact ? "N." : "Numpad Decimal",
            111 => compact ? "N/" : "Numpad /",
            144 => compact ? "Num" : "Num Lock",
            145 => compact ? "Scroll" : "Scroll Lock",
            186 => ";",
            187 => "=",
            188 => ",",
            189 => "-",
            190 => ".",
            191 => "/",
            192 => "`",
            219 => "[",
            220 => "\\",
            221 => "]",
            222 => "'",
            226 => compact ? "OEM102" : "OEM 102",


            _ => string.Empty,
        };
    }

    private static string CompactNativeHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return string.Empty;

        var label = hint.Trim();
        if (label.Length >= 2 && label[0] == '[' && label[^1] == ']')
            label = label[1..^1].Trim();

        label = label
            .Replace("Control-", "C+", StringComparison.OrdinalIgnoreCase)
            .Replace("Ctrl-", "C+", StringComparison.OrdinalIgnoreCase)
            .Replace("Shift-", "S+", StringComparison.OrdinalIgnoreCase)
            .Replace("Alt-", "A+", StringComparison.OrdinalIgnoreCase)
            .Replace("Control+", "C+", StringComparison.OrdinalIgnoreCase)
            .Replace("Ctrl+", "C+", StringComparison.OrdinalIgnoreCase)
            .Replace("Shift+", "S+", StringComparison.OrdinalIgnoreCase)
            .Replace("Alt+", "A+", StringComparison.OrdinalIgnoreCase)
            .Replace("NumPad", "N", StringComparison.OrdinalIgnoreCase)
            .Replace("Numpad", "N", StringComparison.OrdinalIgnoreCase);


        if (label.Length > 24 || label.Any(char.IsControl))
            return string.Empty;

        var condensed = new string(label.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (ulong.TryParse(condensed, out _) || IsInternalNumericKeyLabel(condensed))
            return string.Empty;

        return label;
    }

    private static bool IsInternalNumericKeyLabel(string label)
    {
        foreach (var prefix in new[] { "key", "vk", "virtualkey", "input" })
        {
            if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(label[prefix.Length..], out _))
                return true;
        }

        return false;
    }
}

public enum NativeKeybindCapturePhase
{
    None,
    Preparing,
    Ready,
    Committing,
    Cancelling,
    Failed,
}
