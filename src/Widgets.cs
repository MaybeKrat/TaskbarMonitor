using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using static TaskbarMonitor.Native;

namespace TaskbarMonitor;

/// <summary>
/// Кнопка «Виджеты» (погода, новости) на панели задач. Если значки панели задач выровнены по левому краю,
/// Windows ставит эту кнопку справа, у трея, — туда, где стоит наша панель. Своего окна у кнопки нет,
/// её место узнаётся через UI Automation. Вызывается из фонового потока: UI Automation ждёт ответа Проводника.
/// </summary>
internal sealed class Widgets
{
    const long SearchInterval = 5000;   // мс: как часто кнопка ищется заново — её могли включить, выключить или пересоздать
    const long Memory = 3000;           // мс: столько пропавшая кнопка считается на прежнем месте и ищется на каждом замере
    const string XamlClass = "Windows.UI.Composition.DesktopWindowContentBridge";
    static readonly Condition Button = new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton");

    AutomationElement? _button;
    RECT? _last;
    long _nextSearch, _lastSeen;

    /// <summary>Где кнопка на экране; null — кнопки нет.</summary>
    public RECT? Bounds()
    {
        long now = Environment.TickCount64;
        try
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return null;
            // Пока панель задач спрятана автоскрытием, Проводник может убрать кнопку, а при показе создать заново.
            // Поэтому до полного показа кнопка считается на прежнем месте.
            if (Hidden(tray))
            {
                _lastSeen = now;
                return _last;
            }
            if (now >= _nextSearch || _button == null && now - _lastSeen < Memory)
            {
                _nextSearch = now + SearchInterval;
                _button = Find(tray);
            }
            if (_button?.Current.BoundingRectangle is { IsEmpty: false } r)
            {
                _lastSeen = now;
                return _last = new RECT { Left = (int)Math.Round(r.Left), Top = (int)Math.Round(r.Top), Right = (int)Math.Round(r.Right), Bottom = (int)Math.Round(r.Bottom) };
            }
            _button = null;
        }
        catch (ElementNotAvailableException) { _button = null; }   // Проводник перезапускается или пересоздал кнопку
        catch (Exception e) { _button = null; Log.Error(e); }
        if (now - _lastSeen >= Memory) _last = null;
        return _last;
    }

    /// <summary>Поиск идёт в XAML-окнах панели задач, а не во всей панели: так он не заходит в окно нашей панели.</summary>
    static AutomationElement? Find(IntPtr tray)
    {
        for (IntPtr xaml = FindWindowEx(tray, IntPtr.Zero, XamlClass, null); xaml != IntPtr.Zero; xaml = FindWindowEx(tray, xaml, XamlClass, null))
            if (AutomationElement.FromHandle(xaml).FindFirst(TreeScope.Descendants, Button) is { } button)
                return button;
        return null;
    }

    /// <summary>Панель задач скрыта или уехала за нижний край экрана: спрятана автоскрытием, прячется или показывается.</summary>
    static bool Hidden(IntPtr tray)
    {
        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return !IsWindowVisible(tray) || GetWindowRect(tray, out RECT bar) && GetMonitorInfo(MonitorFromWindow(tray, MONITOR_DEFAULTTOPRIMARY), ref monitor)
            && bar.Bottom > monitor.rcMonitor.Bottom;
    }
}
