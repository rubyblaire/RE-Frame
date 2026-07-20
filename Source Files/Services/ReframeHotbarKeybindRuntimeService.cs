using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using REFrameXIV.Models;

namespace REFrameXIV.Services;


public sealed unsafe class ReframeHotbarKeybindRuntimeService : IDisposable
{
    private const short KeyDownMask = unchecked((short)0x8000);
    private const byte ShiftKey = 0x10;
    private const byte ControlKey = 0x11;
    private const byte AltKey = 0x12;

    private static readonly string[] ChatAddonNames =
    {
        "_ChatLog",
        "ChatLog",
        "_ChatLogPanel_0",
        "ChatLogPanel_0",
        "_ChatLogPanel_1",
        "ChatLogPanel_1",
        "_ChatLogPanel_2",
        "ChatLogPanel_2",
        "_ChatLogPanel_3",
        "ChatLogPanel_3",
    };

    private readonly Configuration configuration;
    private readonly HotbarInputService executor;
    private readonly AdditionalHotbarService additionalHotbars;
    private readonly BarInputDiagnostics diagnostics;
    private readonly HashSet<byte> heldKeys = new();
    private long lastRuntimeFailureLog;
    private static long lastChatFocusFailureLog;
    private bool disposed;

    public ReframeHotbarKeybindRuntimeService(
        Configuration configuration,
        HotbarInputService executor,
        AdditionalHotbarService additionalHotbars,
        BarInputDiagnostics diagnostics)
    {
        this.configuration = configuration;
        this.executor = executor;
        this.additionalHotbars = additionalHotbars;
        this.diagnostics = diagnostics;
    }


    public void Process(bool blocked)
    {
        if (disposed)
            return;

        try
        {
            var bindings = (configuration.ReframeHotbarKeybinds ?? new List<ReframeHotbarKeybind>())
                .Where(IsUsableBinding)
                .ToArray();

            ReleaseKeysThatAreNoLongerDown();
            if (bindings.Length == 0)
                return;

            var inputBlocked = blocked ||
                               !Plugin.ClientState.IsLoggedIn ||
                               Plugin.Condition[ConditionFlag.Performing] ||
                               Plugin.GameGui.GameUiHidden ||
                               !IsGameProcessForeground() ||
                               IsPluginTextInputActive() ||
                               IsNativeChatInputFocused();

            foreach (var keyGroup in bindings.GroupBy(binding => binding.Key))
            {
                var keyCode = keyGroup.Key;
                if (!IsPhysicalKeyDown(keyCode))
                {
                    heldKeys.Remove(keyCode);
                    continue;
                }


                if (!heldKeys.Add(keyCode))
                    continue;

                if (inputBlocked)
                    continue;

                var modifiers = ReadPhysicalModifiers();
                var binding = keyGroup.LastOrDefault(candidate =>
                    NormalizeModifiers((KeyModifierFlag)candidate.Modifiers) == modifiers);
                if (binding is null)
                    continue;

                var slot = new HotbarSlotReference(binding.HotbarId, binding.SlotId);
                if (!NativeHotbarKeybindService.TryMapSlot(slot, out _) &&
                    !NativeHotbarKeybindService.IsRuntimeOwnedSlot(slot))
                    continue;

                var chord = new NativeKeybindChord((SeVirtualKey)binding.Key, modifiers);
                diagnostics.RecordKeybindStage(
                    "PRESS DETECTED",
                    NativeHotbarKeybindService.FormatChord(chord, false));
                diagnostics.RecordKeybindStage(
                    "SLOT EXECUTION REQUESTED",
                    slot.Label);

                var executed = additionalHotbars.IsVirtualReference(slot)
                    ? additionalHotbars.ExecuteVirtual(slot)
                    : executor.Execute(slot.HotbarId, slot.SlotId);
                diagnostics.RecordKeybindStage(
                    "SLOT EXECUTION RESULT",
                    $"{(executed ? "success" : "failure")} — {slot.Label}");
            }
        }
        catch (Exception ex)
        {
            var now = Environment.TickCount64;
            if (now - lastRuntimeFailureLog >= 5000)
            {
                lastRuntimeFailureLog = now;
                Plugin.Log.Warning(ex, "RE:Frame direct-slot keybind runtime could not process this input frame.");
            }

            diagnostics.RecordKeybindStage(
                "SLOT EXECUTION RESULT",
                $"failure — {ex.GetType().Name}: {ex.Message}",
                5000);
        }
    }

    public void ResetLatch()
        => heldKeys.Clear();

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        heldKeys.Clear();
    }

    private void ReleaseKeysThatAreNoLongerDown()
    {
        foreach (var keyCode in heldKeys.ToArray())
        {
            if (!IsPhysicalKeyDown(keyCode))
                heldKeys.Remove(keyCode);
        }
    }

    private bool IsUsableBinding(ReframeHotbarKeybind binding)
    {
        if (binding.Key == 0 || binding.BindingIndex is < 0 or > 1)
            return false;

        var slot = new HotbarSlotReference(binding.HotbarId, binding.SlotId);
        return NativeHotbarKeybindService.TryMapSlot(slot, out _) ||
               NativeHotbarKeybindService.IsRuntimeOwnedSlot(slot);
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

    private static KeyModifierFlag NormalizeModifiers(KeyModifierFlag modifiers)
        => modifiers & (KeyModifierFlag.Shift | KeyModifierFlag.Ctrl | KeyModifierFlag.Alt);

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

    private static bool IsPluginTextInputActive()
    {
        try
        {
            return ImGui.GetIO().WantTextInput;
        }
        catch
        {

            return true;
        }
    }

    private static bool IsNativeChatInputFocused()
    {
        try
        {
            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null)
                return false;

            var focused = manager->AtkUnitManager.FocusedAddon;
            if (focused == null)
                return false;

            foreach (var addonName in ChatAddonNames)
            {
                var chatAddon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
                if (chatAddon != null && chatAddon == focused)
                    return true;
            }
        }
        catch (Exception ex)
        {
            var now = Environment.TickCount64;
            if (now - lastChatFocusFailureLog >= 5000)
            {
                lastChatFocusFailureLog = now;
                Plugin.Log.Debug(ex, "RE:Frame could not inspect native chat focus; direct keybind execution remains blocked for this frame.");
            }
            return true;
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
