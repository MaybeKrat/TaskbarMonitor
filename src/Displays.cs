using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Впереди на этом мониторе приложение на весь экран — игра или видео. Что приложение на весь экран, говорит
    /// Windows (так же она решает, глушить ли уведомления): по размеру окна его не отличить от развёрнутого окна,
    /// когда панель задач скрывается автоматически.
    /// </summary>
    public static bool FullscreenApp(Display display)
    {
        if (SHQueryUserNotificationState(out int state) != 0 || state is not (QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE))
            return false;
        IntPtr window = GetForegroundWindow();
        RECT b = display.Bounds;
        return window != IntPtr.Zero && MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST) == display.Handle
            && GetWindowRect(window, out RECT r) && r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    /// <summary>Где панель задач, когда она видна: спрятанная автоскрытием уезжает за нижний край монитора.</summary>
    public static RECT Shown(IntPtr bar)
    {
        GetWindowRect(bar, out RECT r);
        int below = r.Bottom - Of(bar).Bounds.Bottom;
        if (below > 0)
        {
            r.Top -= below;
            r.Bottom -= below;
        }
        return r;
    }
}
