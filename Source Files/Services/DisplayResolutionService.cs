using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace REFrameXIV.Services;

public readonly record struct DisplayResolutionInfo(
    int ClientWidth,
    int ClientHeight,
    int MonitorWidth,
    int MonitorHeight,
    bool UsedNativeClientRect)
{
    public Vector2 ClientSize => new(Math.Max(1, ClientWidth), Math.Max(1, ClientHeight));
}


public static class DisplayResolutionService
{
    private const uint MonitorDefaultToNearest = 2;

    public static DisplayResolutionInfo Detect(Vector2 fallbackViewport)
    {
        var fallbackWidth = Math.Max(1, (int)MathF.Round(fallbackViewport.X));
        var fallbackHeight = Math.Max(1, (int)MathF.Round(fallbackViewport.Y));
        var clientWidth = fallbackWidth;
        var clientHeight = fallbackHeight;
        var monitorWidth = fallbackWidth;
        var monitorHeight = fallbackHeight;
        var usedNativeClientRect = false;

        try
        {
            using var process = Process.GetCurrentProcess();
            var window = process.MainWindowHandle;
            if (window == IntPtr.Zero)
                return new DisplayResolutionInfo(clientWidth, clientHeight, monitorWidth, monitorHeight, false);

            if (GetClientRect(window, out var clientRect))
            {
                var width = clientRect.Right - clientRect.Left;
                var height = clientRect.Bottom - clientRect.Top;
                if (width > 0 && height > 0)
                {
                    clientWidth = width;
                    clientHeight = height;
                    usedNativeClientRect = true;
                }
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var width = info.Monitor.Right - info.Monitor.Left;
                    var height = info.Monitor.Bottom - info.Monitor.Top;
                    if (width > 0 && height > 0)
                    {
                        monitorWidth = width;
                        monitorHeight = height;
                    }
                }
            }
        }
        catch
        {


        }

        return new DisplayResolutionInfo(
            clientWidth,
            clientHeight,
            monitorWidth,
            monitorHeight,
            usedNativeClientRect);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
}
