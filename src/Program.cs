using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TaskbarMonitor;

internal static class Program
{
    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);
    const double SmoothingSeconds = 1.5;   // чем больше, тем спокойнее и медленнее меняются цифры

    [STAThread]
    static void Main()
    {
        using var single = new Mutex(true, @"Local\TaskbarMonitor", out bool first);
        if (!first) return;

        var dispatcher = Dispatcher.CurrentDispatcher;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error((Exception)e.ExceptionObject);
        dispatcher.UnhandledException += (_, e) => { Log.Error(e.Exception); e.Handled = true; };

        using var sensors = new Sensors();
        using var panel = new TaskbarPanel(Interval, Settings.Load());
        panel.ExitRequested += () => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);

        // Датчики опрашиваются в фоне: окно живёт внутри панели задач, и его поток не должен подвисать.
        using var stop = new CancellationTokenSource();
        var polling = Task.Run(async () =>
        {
            double weight = 1 - Math.Exp(-Interval.TotalSeconds / SmoothingSeconds);
            Sample smoothed = Sample.Empty;
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                try
                {
                    Sample sample = smoothed = Sample.Smooth(smoothed, sensors.Read(), weight);
                    _ = dispatcher.InvokeAsync(() => panel.Update(sample));
                }
                catch (Exception e) { Log.Error(e); }
            }
        });

        Dispatcher.Run();
        stop.Cancel();
        try { polling.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }
}
