using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static TaskbarMonitor.Native;
using static TaskbarMonitor.Paint;

namespace TaskbarMonitor;

/// <summary>Всплывающая карточка с подробностями над панелью. Мышь проходит сквозь неё.</summary>
internal sealed class DetailsFlyout : IDisposable
{
    const string ClassName = "TaskbarMonitorDetails";
    const double PanelGap = 8, ScreenEdge = 12;   // px при 125%

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

    /// <summary>
    /// Показывает или обновляет карточку: по центру над панелью, а если сверху нет места — под ней.
    /// За края area карточка не выходит.
    /// </summary>
    public void Show(Sample s, Theme t, double k, RECT panel, RECT area)
    {
        var (w, h) = _renderer.Size(s, k);
        int shadow = (int)Math.Round(CardShadow * k), edge = (int)Math.Round(ScreenEdge * k), gap = (int)Math.Round(PanelGap * k);
        int x = Math.Clamp((panel.Left + panel.Right - w) / 2, area.Left + edge - shadow, area.Right - edge - w + shadow);
        int y = panel.Top - gap - h + shadow;
        if (y + shadow < area.Top + edge) y = panel.Bottom + gap - shadow;
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
