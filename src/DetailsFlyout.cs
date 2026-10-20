using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static TaskbarMonitor.Native;

namespace TaskbarMonitor;

/// <summary>Всплывающая карточка с подробностями над панелью. Мышь проходит сквозь неё.</summary>
internal sealed class DetailsFlyout : IDisposable
{
    const string ClassName = "TaskbarMonitorDetails";
    const double TaskbarGap = 8, ScreenEdge = 12;   // px при 125%

    readonly DetailsRenderer _renderer = new();
    readonly LayeredCanvas _canvas = new();
    readonly WndProc _wndProc = (hwnd, msg, wParam, lParam) => DefWindowProc(hwnd, msg, wParam, lParam);
    readonly IntPtr _hwnd;
    bool _visible;

    public DetailsFlyout(IntPtr instance)
    {
        var wc = new WNDCLASSEX { cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _wndProc, hInstance = instance, lpszClassName = ClassName };
        if (RegisterClassEx(ref wc) == 0) throw new Win32Exception();
        _hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            ClassName, "TaskbarMonitor", WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception();
    }

    /// <summary>Показывает или обновляет карточку: по центру над панелью, но не за краем экрана.</summary>
    public void Show(Sample s, Theme t, double k, RECT panel, RECT bar)
    {
        var (w, h) = _renderer.Size(s, k);
        int shadow = (int)Math.Round(DetailsRenderer.Shadow * k), edge = (int)Math.Round(ScreenEdge * k);
        int x = Math.Clamp((panel.Left + panel.Right - w) / 2, bar.Left + edge - shadow, bar.Right - edge - w + shadow);
        int y = bar.Top - (int)Math.Round(TaskbarGap * k) - h + shadow;
        _canvas.Present(_hwnd, w, h, dc => _renderer.Draw(dc, s, t, k, w, h));
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _visible = true;
    }

    public void Hide()
    {
        if (!_visible) return;
        ShowWindow(_hwnd, SW_HIDE);
        _visible = false;
    }

    public void Dispose()
    {
        DestroyWindow(_hwnd);
        _canvas.Dispose();
    }
}
