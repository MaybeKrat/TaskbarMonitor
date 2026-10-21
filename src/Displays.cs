using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static TaskbarMonitor.Native;

namespace TaskbarMonitor;

/// <summary>Монитор: имя (\\.\DISPLAY2), границы, рабочая область без панели задач и масштаб k (1 при 125%).</summary>
internal readonly record struct Display(IntPtr Handle, string Name, RECT Bounds, RECT Work, double K);

/// <summary>Мониторы и их панели задач.</summary>
internal static class Displays
{
    public static Display At(POINT point) => Info(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST));

    public static Display Of(IntPtr hwnd) => Info(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    static Display Info(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        uint dpi = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint x, out _) == 0 ? x : 96;
        return new Display(monitor, info.szDevice ?? "", info.rcMonitor, info.rcWork, dpi / 120.0);
    }

    /// <summary>Видимые панели задач всех мониторов, первой — основная.</summary>
    public static IEnumerable<IntPtr> Taskbars()
    {
        IntPtr main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        for (IntPtr bar = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); bar != IntPtr.Zero;
             bar = FindWindowEx(IntPtr.Zero, bar, "Shell_SecondaryTrayWnd", null))
            if (IsWindowVisible(bar)) yield return bar;
    }

    /// <summary>Панель задач монитора с этим именем; null или такого монитора нет — основная.</summary>
    public static IntPtr Taskbar(string? display)
    {
        foreach (IntPtr bar in Taskbars())
            if (display != null && Of(bar).Name == display) return bar;
        return FindWindow("Shell_TrayWnd", null);
    }

    /// <summary>Впереди на этом мониторе приложение на весь экран — игра или видео.</summary>
    public static bool FullscreenApp(Display display)
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero || MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST) != display.Handle) return false;
        var name = new StringBuilder(16);
        GetClassName(window, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW") return false;   // рабочий стол тоже на весь экран
        RECT b = display.Bounds;
        return GetWindowRect(window, out RECT r) && r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }
}
