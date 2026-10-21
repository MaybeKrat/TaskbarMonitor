using System;
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
    const long SearchInterval = 5000;   // мс: как часто кнопка ищется заново — она могла появиться или пропасть
    static readonly Condition Button = new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton");

    AutomationElement? _button;
    long _nextSearch;

    /// <summary>Где кнопка на экране; null — кнопки нет.</summary>
    public RECT? Bounds()
    {
        try
        {
            long now = Environment.TickCount64;
            if (now >= _nextSearch)
            {
                _nextSearch = now + SearchInterval;
                _button = Find();
            }
            if (_button?.Current.BoundingRectangle is { IsEmpty: false } r)
                return new RECT { Left = (int)Math.Round(r.Left), Top = (int)Math.Round(r.Top), Right = (int)Math.Round(r.Right), Bottom = (int)Math.Round(r.Bottom) };
        }
        catch (ElementNotAvailableException) { _button = null; }   // Проводник перезапускается
        catch (Exception e) { _button = null; Log.Error(e); }
        return null;
    }

    /// <summary>Поиск идёт в окне XAML панели задач, а не во всей панели: так он не заходит в окно нашей панели.</summary>
    static AutomationElement? Find()
    {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return null;
        IntPtr xaml = FindWindowEx(tray, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
        return xaml == IntPtr.Zero ? null : AutomationElement.FromHandle(xaml).FindFirst(TreeScope.Descendants, Button);
    }
}
