using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using static TaskbarMonitor.Native;
using static TaskbarMonitor.Paint;

namespace TaskbarMonitor;

/// <summary>
/// Панель. Обычно это дочернее окно панели задач: у трея основного монитора или в месте, куда её перетащили,
/// на панели задач любого монитора. Перетащенная на экран — отдельное окно-карточка поверх остальных окон.
/// При наведении показывает карточку с подробностями, по клику открывает Диспетчер задач, по правому клику — меню.
/// </summary>
internal sealed class TaskbarPanel : IDisposable
{
    const string ClassName = "TaskbarMonitorPanel";
    const double TrayGap = 40;      // от правого края панели до трея, px при 125%
    const double WidgetsGap = 56;   // до кнопки «Виджеты»: на глаз столько же, сколько от погоды до трея
    const double SnapDistance = 40; // px при 125%: отпущенная так близко к месту у трея панель встаёт к трею
    const double Magnet = 24;       // px при 125%: на столько выше панели задач перетаскиваемая панель уже примагничивается к ней
    const uint HoverDelay = 300;    // мс до показа подробностей
    const double ArcStep = 0.3;     // сдвиг дуги меньше 0,3% (доли пикселя) показывается без анимации
    const int MenuExit = 1, MenuAutostart = 2, MenuTray = 3, MenuSettings = 4, MenuOnTop = 5;

    /// <summary>
    /// Где панель. Parent — панель задач, в которую встроено окно; IntPtr.Zero — окно само по себе, поверх остальных.
    /// Bar — панель задач, на которой панель стоит или к которой примагничена при перетаскивании; Content — где на экране
    /// содержимое; Margin — поле под тень карточки вокруг содержимого, когда панель отдельно на экране.
    /// </summary>
    readonly record struct Layout(IntPtr Parent, IntPtr Bar, RECT Content, double K, int Margin, Display Display, double DetailsK);

    readonly PanelRenderer _renderer = new();
    readonly LayeredCanvas _canvas = new();
    readonly DetailsFlyout _details;
    readonly WndProc _wndProc;      // делегат хранится в поле, иначе его соберёт GC
    readonly IntPtr _instance = GetModuleHandle(null);
    IntPtr _hwnd, _parent;
    RECT _placed, _content;
    RECT? _widgets;
    bool _hover, _tracking, _showDetails;
    Sample _sample = Sample.Empty;
    Settings _settings;
    SettingsWindow? _settingsWindow;

    // Перетаскивание: пока кнопка мыши зажата, панель едет за курсором (кнопку опрашивает таймер, чтобы не зависеть от окна под курсором).
    readonly DispatcherTimer _drag = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    // Автоскрытие панели задач: как только она начала прятаться, панель становится карточкой на том же месте,
    // а когда показалась — встраивается обратно. Проверка дешёвая, поэтому частая.
    readonly DispatcherTimer _watch = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    IntPtr _bar;   // панель задач, на которой стоит панель; IntPtr.Zero — панель на экране
    bool _pressed, _dragging;
    POINT _press, _cursor;
    double _grabX, _grabY;   // за какое место взяли панель — в долях её ширины и высоты

    // Анимация колец и раскладка, по которой рисуются её кадры.
    readonly DispatcherTimer _frames = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly double[] _arcs = [double.NaN, double.NaN, double.NaN];   // сколько закрашено на кольцах сейчас
    readonly double[] _from = new double[3];                         // откуда дуги едут к свежим значениям
    readonly TimeSpan _tween;   // дуги едут ровно до следующего замера, поэтому движутся без остановок
    TimeSpan _tweenStart;
    Theme _theme = Theme.Light;
    double _k = 1;
    int _width, _height, _margin;
    bool _docked;   // панель на панели задач или примагничена к ней: высота по панели задач, элементы в строку

    public event Action? ExitRequested;

    /// <param name="interval">Как часто приходят новые замеры.</param>
    public TaskbarPanel(TimeSpan interval, Settings settings)
    {
        _tween = interval;
        _settings = _renderer.Settings = settings;
        _wndProc = WindowProc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = _instance,
            hCursor = LoadCursor(IntPtr.Zero, (IntPtr)IDC_ARROW),
            lpszClassName = ClassName,
        };
        if (RegisterClassEx(ref wc) == 0) throw new Win32Exception();
        _details = new DetailsFlyout(_instance);
        _frames.Tick += (_, _) => Animate();
        _drag.Tick += (_, _) => DragStep();
        _watch.Tick += (_, _) => WatchTaskbar();
        _watch.Start();
    }

    /// <param name="widgets">Где на экране кнопка «Виджеты»; null — её нет.</param>
    public void Update(Sample sample, RECT? widgets)
    {
        _sample = sample;
        _widgets = widgets;
        double[] targets = PanelRenderer.Loads(sample);
        bool animate = false;
        for (int i = 0; i < _arcs.Length; i++)
        {
            if (double.IsNaN(_arcs[i]) || double.IsNaN(targets[i]) || Math.Abs(targets[i] - _arcs[i]) < ArcStep)
                _arcs[i] = targets[i];   // первый замер, нет данных или сдвиг на доли пикселя
            else
                animate = true;
            _from[i] = _arcs[i];
        }
        _tweenStart = _clock.Elapsed;
        Refresh();
        if (animate && !_frames.IsEnabled) _frames.Start();
    }

    void Refresh()
    {
        if (CurrentLayout() is not { } layout) return;   // Проводник перезапускается — панель вернётся вместе с ним
        if (_hwnd == IntPtr.Zero || layout.Parent != _parent) Create(layout.Parent);
        _theme = Theme.Current();
        _content = layout.Content;
        _width = _content.Right - _content.Left;
        _height = _content.Bottom - _content.Top;
        _k = layout.K;
        _margin = layout.Margin;
        _docked = layout.Bar != IntPtr.Zero;
        _bar = layout.Bar;

        // Отдельная панель прячется, пока впереди игра или видео на весь экран, — как и панель задач.
        // Оставшаяся на месте спрятанной панели задач прячется всегда: сама панель задач в играх тоже не видна.
        if (!_dragging && layout.Parent == IntPtr.Zero && (_docked || _settings.HideInFullscreen) && Displays.FullscreenApp(layout.Display))
        {
            _showDetails = false;
            _details.Hide();
            ShowWindow(_hwnd, SW_HIDE);
            return;
        }
        Render();
        Place();
        if (_showDetails)
            _details.Show(_sample, _theme, layout.DetailsK, _content, layout.Bar != IntPtr.Zero ? layout.Display.Bounds : layout.Display.Work);
    }

    Layout? CurrentLayout()
    {
        if (_dragging) return DragLayout();
        if (_settings.Placement == Placement.Screen)
        {
            var at = new POINT { X = (int)_settings.X, Y = (int)_settings.Y };
            return Floating(Displays.At(at), at, clamp: true);
        }
        IntPtr bar = _settings.Placement == Placement.Taskbar ? Displays.Taskbar(_settings.Display) : FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero) return null;
        double? x = _settings.Placement == Placement.Taskbar ? _settings.X : null;
        if (!_settings.StayWhenTaskbarHides || !Displays.Hidden(bar)) return Docked(bar, x, embedded: true);
        // Панель задач спрятана автоскрытием: панель остаётся на её месте карточкой поверх окон.
        Layout stay = Docked(bar, x, embedded: false);
        return stay with { Margin = (int)Math.Round(CardShadow * stay.K) };
    }

    /// <summary>Панель задач спряталась или показалась — панель переходит в карточку на её месте или обратно.</summary>
    void WatchTaskbar()
    {
        if (_dragging || _bar == IntPtr.Zero || _settings.Placement == Placement.Screen || !_settings.StayWhenTaskbarHides) return;
        if (Displays.Hidden(_bar) != (_parent == IntPtr.Zero)) Refresh();
    }

    /// <summary>
    /// Панель на панели задач: по её высоте, центром в доле x её ширины, а без x — у трея.
    /// embedded — окно встроено в панель задач; иначе это перетаскиваемая панель, примагниченная к ней.
    /// </summary>
    Layout Docked(IntPtr bar, double? x, bool embedded)
    {
        // Встроенное окно едет вместе с панелью задач, а примагниченное при перетаскивании стоит там,
        // где спрятанная автоскрытием панель задач появится, — а не уезжает с ней за край экрана.
        RECT r;
        if (embedded) GetWindowRect(bar, out r);
        else r = Displays.Shown(bar);
        // Панель нарисована под панель задач высотой 60 px (как при масштабе 125%) и масштабируется по её фактической высоте.
        double k = (r.Bottom - r.Top) / 60.0;
        int width = _renderer.Size(_sample, k, docked: true).Width;
        int left = x is double f
            ? Math.Clamp((int)Math.Round(r.Left + f * (r.Right - r.Left) - width / 2.0), r.Left, Math.Max(r.Left, r.Right - width))
            : TrayLeft(bar, r, width, k);
        var content = new RECT { Left = left, Top = r.Top, Right = left + width, Bottom = r.Bottom };
        // Карточка подробностей нарисована для масштаба 125%.
        return new Layout(embedded ? bar : IntPtr.Zero, bar, content, k, 0, Displays.Of(bar), GetDpiForWindow(bar) / 120.0);
    }

    /// <summary>Левый край панели у трея, а если кнопка «Виджеты» стоит у трея — левее неё.</summary>
    int TrayLeft(IntPtr tray, RECT bar, int width, double k)
    {
        IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        int trayLeft = notify != IntPtr.Zero && GetWindowRect(notify, out RECT n) ? n.Left : bar.Right;
        int right = trayLeft - (int)Math.Round(TrayGap * k);
        // Кнопка «Виджеты» в правой половине панели задач стоит у трея — тогда панель встаёт левее неё.
        if (_widgets is RECT w && w.Left > (bar.Left + bar.Right) / 2 && w.Left < trayLeft)
            right = w.Left - (int)Math.Round(WidgetsGap * k);
        return right - width;
    }

    /// <summary>Панель отдельной карточкой на экране, в масштабе монитора; clamp — не заходить за его рабочую область.</summary>
    Layout Floating(Display display, POINT at, bool clamp)
    {
        var (width, height) = _renderer.Size(_sample, display.K, docked: false);
        if (clamp)
        {
            RECT work = display.Work;
            at.X = Math.Clamp(at.X, work.Left, Math.Max(work.Left, work.Right - width));
            at.Y = Math.Clamp(at.Y, work.Top, Math.Max(work.Top, work.Bottom - height));
        }
        var content = new RECT { Left = at.X, Top = at.Y, Right = at.X + width, Bottom = at.Y + height };
        return new Layout(IntPtr.Zero, IntPtr.Zero, content, display.K, (int)Math.Round(CardShadow * display.K), display, display.K);
    }

    /// <summary>
    /// Перетаскиваемая панель едет за курсором. Над панелью задач любого монитора или чуть выше неё
    /// панель примагничивается (если это не выключено): встаёт в неё по высоте и выглядит как встроенная.
    /// </summary>
    Layout DragLayout()
    {
        foreach (IntPtr bar in _settings.SnapToTaskbar ? Displays.Taskbars() : [])
        {
            RECT r = Displays.Shown(bar);
            double k = (r.Bottom - r.Top) / 60.0;
            if (_cursor.X < r.Left || _cursor.X >= r.Right || _cursor.Y < r.Top - Magnet * k || _cursor.Y >= r.Bottom) continue;
            double width = _renderer.Size(_sample, k, docked: true).Width;
            return Docked(bar, (_cursor.X + (0.5 - _grabX) * width - r.Left) / (r.Right - r.Left), embedded: false);
        }
        Display display = Displays.At(_cursor);
        var (w, h) = _renderer.Size(_sample, display.K, docked: false);
        return Floating(display, new POINT { X = _cursor.X - (int)Math.Round(_grabX * w), Y = _cursor.Y - (int)Math.Round(_grabY * h) }, clamp: false);
    }

    void Render()
    {
        if (_hwnd == IntPtr.Zero || _width == 0) return;
        int m = _margin;
        _canvas.Present(_hwnd, _width + 2 * m, _height + 2 * m, dc =>
        {
            if (m > 0) Card(dc, new Rect(m, m, _width, _height), _theme, _k);   // отдельно на экране — на карточке
            dc.PushTransform(new TranslateTransform(m, m));
            _renderer.Draw(dc, _sample, _arcs, _theme, _k, _width, _height, _hover, _docked);
            dc.Pop();
        });
    }

    /// <summary>Кадр анимации: дуги равномерно едут к свежим значениям и приходят к ним к следующему замеру.</summary>
    void Animate()
    {
        double progress = Math.Min(1, (_clock.Elapsed - _tweenStart) / _tween);
        double[] targets = PanelRenderer.Loads(_sample);
        for (int i = 0; i < _arcs.Length; i++)
            _arcs[i] = _from[i] + (targets[i] - _from[i]) * progress;
        Render();
        if (progress >= 1) _frames.Stop();
    }

    void Place()
    {
        var origin = new POINT { X = _content.Left - _margin, Y = _content.Top - _margin };
        if (_parent != IntPtr.Zero) ScreenToClient(_parent, ref origin);
        int w = _width + 2 * _margin, h = _height + 2 * _margin;
        bool moved = origin.X != _placed.Left || origin.Y != _placed.Top || w != _placed.Right - _placed.Left || h != _placed.Bottom - _placed.Top;
        bool covered = _parent != IntPtr.Zero && GetWindow(_hwnd, GW_HWNDPREV) != IntPtr.Zero;   // Проводник поднял над панелью своё окно
        if (!moved && !covered && IsWindowVisible(_hwnd)) return;
        IntPtr z = _parent != IntPtr.Zero ? HWND_TOP : _dragging || _docked || _settings.OnTop ? HWND_TOPMOST : HWND_NOTOPMOST;
        SetWindowPos(_hwnd, z, origin.X, origin.Y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _placed = new RECT { Left = origin.X, Top = origin.Y, Right = origin.X + w, Bottom = origin.Y + h };
    }

    /// <summary>Окно панели: дочернее окно панели задач parent, а без parent — отдельное окно поверх остальных.</summary>
    void Create(IntPtr parent)
    {
        DestroyPanel();
        _hwnd = parent != IntPtr.Zero
            ? CreateWindowEx(WS_EX_LAYERED | WS_EX_NOPARENTNOTIFY, ClassName, "TaskbarMonitor", WS_CHILD | WS_CLIPSIBLINGS,
                0, 0, 0, 0, parent, IntPtr.Zero, _instance, IntPtr.Zero)
            : CreateWindowEx(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "TaskbarMonitor", WS_POPUP,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception();
        _parent = parent;
        _placed = default;
    }

    /// <summary>Курсор с зажатой кнопкой ушёл от места нажатия дальше, чем Windows считает началом перетаскивания.</summary>
    bool DragStarted()
    {
        GetCursorPos(out POINT p);
        return Math.Abs(p.X - _press.X) > GetSystemMetrics(SM_CXDRAG) || Math.Abs(p.Y - _press.Y) > GetSystemMetrics(SM_CYDRAG);
    }

    void BeginDrag()
    {
        _grabX = (_press.X - _content.Left) / (double)_width;
        _grabY = (_press.Y - _content.Top) / (double)_height;
        _pressed = false;
        _dragging = true;
        ReleaseCapture();
        _hover = _showDetails = false;
        _details.Hide();
        _drag.Start();
        DragStep();
    }

    /// <summary>Шаг перетаскивания: панель едет за курсором, а когда кнопку отпустили — остаётся там.</summary>
    void DragStep()
    {
        GetCursorPos(out _cursor);
        if (GetAsyncKeyState(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON) < 0)
        {
            Refresh();
            return;
        }
        _drag.Stop();
        Settings dropped = Drop(DragLayout());
        _dragging = false;
        Apply(dropped);
    }

    /// <summary>Куда встаёт отпущенная панель: туда, где её показывало перетаскивание.</summary>
    Settings Drop(Layout layout)
    {
        RECT c = layout.Content;
        if (layout.Bar == IntPtr.Zero)
        {
            RECT placed = Floating(layout.Display, new POINT { X = c.Left, Y = c.Top }, clamp: true).Content;
            return _settings with { Placement = Placement.Screen, Display = null, X = placed.Left, Y = placed.Top };
        }
        GetWindowRect(layout.Bar, out RECT r);
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        // Отпустили у прежнего места рядом с треем — панель снова стоит у трея и обходит кнопку «Виджеты».
        if (layout.Bar == tray && Math.Abs(c.Left - TrayLeft(tray, r, c.Right - c.Left, layout.K)) < SnapDistance * layout.K)
            return _settings with { Placement = Placement.Tray, Display = null };
        return _settings with
        {
            Placement = Placement.Taskbar,
            Display = layout.Bar == tray ? null : layout.Display.Name,
            X = ((c.Left + c.Right) / 2.0 - r.Left) / (r.Right - r.Left),
        };
    }

    IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_MOUSEMOVE:
                    if (_dragging) break;
                    if (_pressed)
                    {
                        if (DragStarted()) BeginDrag();
                        break;
                    }
                    if (!_tracking)
                    {
                        var tme = new TRACKMOUSEEVENT
                        {
                            cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(),
                            dwFlags = TME_HOVER | TME_LEAVE,
                            hwndTrack = hwnd,
                            dwHoverTime = HoverDelay,
                        };
                        _tracking = TrackMouseEvent(ref tme);
                    }
                    SetHover(true, _showDetails);
                    break;
                case WM_MOUSEHOVER:
                    if (!_dragging && !_pressed) SetHover(true, true);
                    break;
                case WM_MOUSELEAVE:
                    _tracking = false;
                    if (!_dragging) SetHover(false, false);
                    break;
                case WM_LBUTTONDOWN:
                    GetCursorPos(out _press);
                    _pressed = true;
                    SetCapture(hwnd);   // начало перетаскивания заметно, даже если курсор сразу ушёл с панели
                    return IntPtr.Zero;
                case WM_LBUTTONUP:
                    if (!_pressed) break;   // кнопку нажали не на панели или панель уже перетащили
                    _pressed = false;
                    ReleaseCapture();
                    SetHover(_hover, false);
                    OpenTaskManager();      // клик без перетаскивания
                    return IntPtr.Zero;
                case WM_CAPTURECHANGED:
                    _pressed = false;
                    break;
                case WM_RBUTTONUP:
                    if (_dragging) break;
                    SetHover(_hover, false);
                    ShowMenu();
                    return IntPtr.Zero;
                case WM_DESTROY:
                    if (hwnd == _hwnd)   // панель задач закрылась вместе с Проводником
                    {
                        _hwnd = IntPtr.Zero;
                        _hover = _tracking = _showDetails = false;
                        _details.Hide();
                    }
                    break;
            }
        }
        catch (Exception e) { Log.Error(e); }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    void SetHover(bool hover, bool details)
    {
        if (hover == _hover && details == _showDetails) return;
        _hover = hover;
        _showDetails = details;
        if (!details) _details.Hide();
        Refresh();
    }

    void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, (IntPtr)MenuSettings, Strings.SettingsMenu);
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            // Поверх окон бывает только панель на экране: панель задач и так всегда поверх.
            if (_settings.Placement == Placement.Screen)
                AppendMenu(menu, MF_STRING, (IntPtr)MenuOnTop, _settings.OnTop ? Strings.Unpin : Strings.Pin);
            if (_settings.Placement != Placement.Tray) AppendMenu(menu, MF_STRING, (IntPtr)MenuTray, Strings.BackToTray);
            AppendMenu(menu, MF_STRING | (Autostart.Enabled ? MF_CHECKED : 0), (IntPtr)MenuAutostart, Strings.Autostart);
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (IntPtr)MenuExit, Strings.Exit);

            GetCursorPos(out POINT p);
            SetForegroundWindow(_parent != IntPtr.Zero ? _parent : _hwnd);   // иначе меню не закроется кликом мимо него
            Execute(TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.X, p.Y, _hwnd, IntPtr.Zero));
        }
        finally { DestroyMenu(menu); }
    }

    void Execute(int command)
    {
        switch (command)
        {
            case MenuExit: ExitRequested?.Invoke(); break;
            case MenuAutostart: Autostart.Enabled = !Autostart.Enabled; break;
            case MenuTray: Apply(_settings with { Placement = Placement.Tray, Display = null }); break;
            case MenuSettings: ShowSettings(); break;
            case MenuOnTop: Apply(_settings with { OnTop = !_settings.OnTop }); break;
        }
    }

    /// <summary>Окно настроек; если оно уже открыто — выходит на передний план.</summary>
    void ShowSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(() => _settings, Apply);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    void Apply(Settings settings)
    {
        _settings = _renderer.Settings = settings;
        settings.Save();
        _placed = default;   // заново выставить окно: могло поменяться «поверх всех окон»
        Refresh();
    }

    /// <summary>
    /// Диспетчер задач запускается с повышением прав, и ShellExecute ждёт его. Запуск идёт в фоне:
    /// если поток окна, встроенного в панель задач, занят, курсор над панелью мигает «загрузкой».
    /// </summary>
    static void OpenTaskManager() => Task.Run(() =>
    {
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) { Log.Error(e); }
    });

    void DestroyPanel()
    {
        _details.Hide();
        _hover = _tracking = _showDetails = false;
        if (_hwnd == IntPtr.Zero) return;
        IntPtr hwnd = _hwnd;
        _hwnd = IntPtr.Zero;
        DestroyWindow(hwnd);
    }

    public void Dispose()
    {
        _frames.Stop();
        _drag.Stop();
        _watch.Stop();
        _settingsWindow?.Close();
        DestroyPanel();
        _details.Dispose();
        _canvas.Dispose();
    }
}
