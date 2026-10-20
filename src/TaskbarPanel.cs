using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using static TaskbarMonitor.Native;

namespace TaskbarMonitor;

/// <summary>
/// Панель — дочернее окно панели задач основного монитора, слева от трея. При наведении
/// показывает карточку с подробностями, по клику открывает Диспетчер задач, по правому клику — меню.
/// </summary>
internal sealed class TaskbarPanel : IDisposable
{
    const string ClassName = "TaskbarMonitorPanel";
    const double TrayGap = 40;      // от правого края панели до трея, px при 125%
    const uint HoverDelay = 300;    // мс до показа подробностей
    const double ArcStep = 0.3;     // сдвиг дуги меньше 0,3% (доли пикселя) показывается без анимации
    const int MenuExit = 1, MenuAutostart = 2, MenuCpu = 10, MenuRam = 20, MenuGpu = 30;   // у выбора значений — номер первого пункта

    readonly PanelRenderer _renderer = new();
    readonly LayeredCanvas _canvas = new();
    readonly DetailsFlyout _details;
    readonly WndProc _wndProc;      // делегат хранится в поле, иначе его соберёт GC
    readonly IntPtr _instance = GetModuleHandle(null);
    IntPtr _hwnd, _tray;
    RECT _placed;
    bool _hover, _tracking, _showDetails;
    Sample _sample = Sample.Empty;
    Settings _settings;

    // Анимация колец и раскладка, по которой рисуются её кадры.
    readonly DispatcherTimer _frames = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly double[] _arcs = [double.NaN, double.NaN, double.NaN];   // сколько закрашено на кольцах сейчас
    readonly double[] _from = new double[3];                         // откуда дуги едут к свежим значениям
    readonly TimeSpan _tween;   // дуги едут ровно до следующего замера, поэтому движутся без остановок
    TimeSpan _tweenStart;
    Theme _theme = Theme.Light;
    double _k = 1;
    int _width, _height;

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
    }

    public void Update(Sample sample)
    {
        _sample = sample;
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
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;   // Проводник перезапускается — панель вернётся вместе с ним
        if (_hwnd == IntPtr.Zero || tray != _tray) Create(tray);

        double k = GetDpiForWindow(tray) / 120.0;   // карточка нарисована для масштаба 125%
        GetWindowRect(tray, out RECT bar);
        IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        int trayLeft = notify != IntPtr.Zero && GetWindowRect(notify, out RECT n) ? n.Left : bar.Right;

        // Панель нарисована под панель задач высотой 60 px (как при масштабе 125%)
        // и масштабируется по её фактической высоте.
        _height = bar.Bottom - bar.Top;
        _k = _height / 60.0;
        _width = _renderer.Width(_sample, _k);
        _theme = Theme.Current();
        int left = trayLeft - (int)Math.Round(TrayGap * _k) - _width;
        var screen = new RECT { Left = left, Top = bar.Top, Right = left + _width, Bottom = bar.Bottom };

        Render();
        Place(tray, screen);
        if (_showDetails) _details.Show(_sample, _theme, k, screen, bar);
    }

    void Render()
    {
        if (_hwnd == IntPtr.Zero || _width == 0) return;
        _canvas.Present(_hwnd, _width, _height, dc => _renderer.Draw(dc, _sample, _arcs, _theme, _k, _width, _height, _hover));
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

    void Place(IntPtr tray, RECT screen)
    {
        var origin = new POINT { X = screen.Left, Y = screen.Top };
        ScreenToClient(tray, ref origin);
        int w = screen.Right - screen.Left, h = screen.Bottom - screen.Top;
        bool moved = origin.X != _placed.Left || origin.Y != _placed.Top || w != _placed.Right - _placed.Left || h != _placed.Bottom - _placed.Top;
        bool covered = GetWindow(_hwnd, GW_HWNDPREV) != IntPtr.Zero;   // Проводник поднял над панелью своё окно
        if (!moved && !covered && IsWindowVisible(_hwnd)) return;
        SetWindowPos(_hwnd, HWND_TOP, origin.X, origin.Y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _placed = new RECT { Left = origin.X, Top = origin.Y, Right = origin.X + w, Bottom = origin.Y + h };
    }

    void Create(IntPtr tray)
    {
        DestroyPanel();
        _hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_NOPARENTNOTIFY, ClassName, "TaskbarMonitor", WS_CHILD | WS_CLIPSIBLINGS,
            0, 0, 0, 0, tray, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception();
        _tray = tray;
        _placed = default;
    }

    IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_MOUSEMOVE:
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
                    SetHover(true, true);
                    break;
                case WM_MOUSELEAVE:
                    _tracking = false;
                    SetHover(false, false);
                    break;
                case WM_LBUTTONUP:
                    SetHover(_hover, false);
                    OpenTaskManager();
                    return IntPtr.Zero;
                case WM_RBUTTONUP:
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
            AppendMenu(menu, MF_STRING | (Autostart.Enabled ? MF_CHECKED : 0), (IntPtr)MenuAutostart, Strings.Autostart);
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendChoice(menu, "CPU", MenuCpu, (int)_settings.Cpu, Strings.Clock, Strings.Temperature);
            AppendChoice(menu, "RAM", MenuRam, (int)_settings.Ram, Strings.Used, Strings.Free);
            AppendChoice(menu, "GPU", MenuGpu, (int)_settings.Gpu, Strings.Power, Strings.Temperature);
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (IntPtr)MenuExit, Strings.Exit);

            GetCursorPos(out POINT p);
            SetForegroundWindow(_tray);   // иначе меню не закроется кликом мимо него
            Execute(TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.X, p.Y, _hwnd, IntPtr.Zero));
        }
        finally { DestroyMenu(menu); }   // подменю удаляются вместе с меню
    }

    /// <summary>Подменю с выбором одного значения: пункты first, first + 1, …; выбранный отмечен точкой.</summary>
    static void AppendChoice(IntPtr menu, string title, int first, int selected, params string[] options)
    {
        IntPtr submenu = CreatePopupMenu();
        for (int i = 0; i < options.Length; i++)
            AppendMenu(submenu, MF_STRING, (IntPtr)(first + i), options[i]);
        CheckMenuRadioItem(submenu, (uint)first, (uint)(first + options.Length - 1), (uint)(first + selected), MF_BYCOMMAND);
        AppendMenu(menu, MF_POPUP, submenu, title);
    }

    void Execute(int command)
    {
        switch (command)
        {
            case MenuExit: ExitRequested?.Invoke(); break;
            case MenuAutostart: Autostart.Enabled = !Autostart.Enabled; break;
            case >= MenuCpu and < MenuCpu + 10: Apply(_settings with { Cpu = (CpuValue)(command - MenuCpu) }); break;
            case >= MenuRam and < MenuRam + 10: Apply(_settings with { Ram = (RamValue)(command - MenuRam) }); break;
            case >= MenuGpu and < MenuGpu + 10: Apply(_settings with { Gpu = (GpuValue)(command - MenuGpu) }); break;
        }
    }

    void Apply(Settings settings)
    {
        _settings = _renderer.Settings = settings;
        settings.Save();
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
        DestroyPanel();
        _details.Dispose();
        _canvas.Dispose();
    }
}
