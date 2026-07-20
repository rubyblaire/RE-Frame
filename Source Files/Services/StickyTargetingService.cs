using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

namespace REFrameXIV.Services;


public sealed class StickyTargetingService
{
    private const int EscapeKey = 0x1B;
    private const short KeyDownMask = unchecked((short)0x8000);

    private readonly Configuration configuration;
    private readonly ITargetManager targetManager;
    private bool escapeWasDown;

    public StickyTargetingService(Configuration configuration, ITargetManager targetManager)
    {
        this.configuration = configuration;
        this.targetManager = targetManager;
    }

    public void Process(bool blocked)
    {
        var escapeDown = IsGameProcessForeground() && (GetAsyncKeyState(EscapeKey) & KeyDownMask) != 0;
        var pressed = escapeDown && !escapeWasDown;
        escapeWasDown = escapeDown;

        if (!pressed || configuration.StickyTargeting || blocked || IsPluginTextInputActive())
            return;

        try
        {
            if (targetManager.Target is not null)
                targetManager.Target = null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not clear the current target with Escape.");
        }
    }

    public void ResetLatch() => escapeWasDown = false;

    private static bool IsPluginTextInputActive()
    {
        try
        {
            var io = ImGui.GetIO();
            return io.WantTextInput || io.WantCaptureKeyboard;
        }
        catch
        {
            return true;
        }
    }

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
}
