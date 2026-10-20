using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static TaskbarMonitor.Native;

namespace TaskbarMonitor;

/// <summary>Один замер датчиков; NaN — датчик недоступен.</summary>
internal sealed record Sample
{
    public static readonly Sample Empty = new();

    public string CpuName { get; init; } = "";
    public double CpuLoad { get; init; } = double.NaN;
    public double CpuGhz { get; init; } = double.NaN;
    public double CpuTemp { get; init; } = double.NaN;
    public double CpuWatts { get; init; } = double.NaN;

    public double RamUsedGb { get; init; } = double.NaN;
    public double RamTotalGb { get; init; } = double.NaN;
    public double RamLoad => RamUsedGb / RamTotalGb * 100;

    public string GpuName { get; init; } = "";
    public double GpuLoad { get; init; } = double.NaN;
    public double GpuMhz { get; init; } = double.NaN;
    public double GpuTemp { get; init; } = double.NaN;
    public double GpuWatts { get; init; } = double.NaN;
    public double GpuWattsLimit { get; init; } = double.NaN;
    public double GpuFan { get; init; } = double.NaN;
    public double VramUsedGb { get; init; } = double.NaN;
    public double VramTotalGb { get; init; } = double.NaN;

    /// <summary>
    /// Сглаживание: к прежним значениям подмешиваются новые с весом weight, поэтому цифры меняются
    /// плавно и не дёргаются от случайных скачков.
    /// </summary>
    public static Sample Smooth(Sample previous, Sample next, double weight)
    {
        double Mix(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? b : a + (b - a) * weight;
        return next with
        {
            CpuLoad = Mix(previous.CpuLoad, next.CpuLoad),
            CpuGhz = Mix(previous.CpuGhz, next.CpuGhz),
            CpuTemp = Mix(previous.CpuTemp, next.CpuTemp),
            CpuWatts = Mix(previous.CpuWatts, next.CpuWatts),
            RamUsedGb = Mix(previous.RamUsedGb, next.RamUsedGb),
            GpuLoad = Mix(previous.GpuLoad, next.GpuLoad),
            GpuMhz = Mix(previous.GpuMhz, next.GpuMhz),
            GpuTemp = Mix(previous.GpuTemp, next.GpuTemp),
            GpuWatts = Mix(previous.GpuWatts, next.GpuWatts),
            GpuFan = Mix(previous.GpuFan, next.GpuFan),
            VramUsedGb = Mix(previous.VramUsedGb, next.VramUsedGb),
        };
    }
}

/// <summary>
/// Датчики. CPU — счётчики Windows (те же, что у Диспетчера задач), температура и потребление CPU —
/// MSI Afterburner, RAM — Windows, GPU — драйвер NVIDIA (NVML). Прав администратора не требуют.
/// </summary>
internal sealed class Sensors : IDisposable
{
    const double GiB = 1024d * 1024 * 1024;
    readonly IntPtr _query, _utility, _performance, _frequency, _gpu;
    readonly bool _nvml;
    readonly string _cpuName, _gpuName = "";

    public Sensors()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) == 0)
        {
            PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _utility);
            PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _performance);
            PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\Processor Frequency", IntPtr.Zero, out _frequency);
            PdhCollectQueryData(_query);   // проценты считаются по разнице двух замеров
        }

        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            _cpuName = Regex.Replace(key?.GetValue("ProcessorNameString") as string ?? "", @"\(R\)|\(TM\)|\s*\d+-Core Processor|\s*Processor", "").Trim();

        try
        {
            _nvml = NvmlInit() == 0 && NvmlDeviceGetHandleByIndex(0, out _gpu) == 0;
            var name = new byte[96];
            if (_nvml && NvmlDeviceGetName(_gpu, name, (uint)name.Length) == 0)
                _gpuName = Encoding.ASCII.GetString(name, 0, Math.Max(0, Array.IndexOf(name, (byte)0)));
        }
        catch (DllNotFoundException) { }        // нет драйвера NVIDIA
        catch (EntryPointNotFoundException) { }
    }

    public Sample Read()
    {
        double cpuLoad = double.NaN, cpuGhz = double.NaN;
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            cpuLoad = Value(_utility, 0);   // как в Диспетчере задач: не больше 100%
            // Частота как в Диспетчере задач: базовая частота × текущая доля производительности.
            cpuGhz = Value(_frequency, PDH_FMT_NOCAP100) * Value(_performance, PDH_FMT_NOCAP100) / 100 / 1000;
        }
        double[] afterburner = Afterburner.Read("CPU temperature", "CPU power");
        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        bool hasMemory = GlobalMemoryStatusEx(ref memory);

        var sample = new Sample
        {
            CpuName = _cpuName,
            CpuLoad = cpuLoad,
            CpuGhz = cpuGhz,
            CpuTemp = afterburner[0],
            CpuWatts = afterburner[1],
            RamUsedGb = hasMemory ? (memory.ullTotalPhys - memory.ullAvailPhys) / GiB : double.NaN,
            RamTotalGb = hasMemory ? memory.ullTotalPhys / GiB : double.NaN,
        };
        return _nvml ? WithGpu(sample) : sample;
    }

    Sample WithGpu(Sample s)
    {
        bool hasMemory = NvmlDeviceGetMemoryInfo(_gpu, out NvmlMemory memory) == 0;
        return s with
        {
            GpuName = _gpuName,
            GpuLoad = NvmlDeviceGetUtilizationRates(_gpu, out NvmlUtilization utilization) == 0 ? utilization.Gpu : double.NaN,
            GpuMhz = NvmlDeviceGetClockInfo(_gpu, NVML_CLOCK_GRAPHICS, out uint mhz) == 0 ? mhz : double.NaN,
            GpuTemp = NvmlDeviceGetTemperature(_gpu, NVML_TEMPERATURE_GPU, out uint celsius) == 0 ? celsius : double.NaN,
            GpuWatts = NvmlDeviceGetPowerUsage(_gpu, out uint milliwatts) == 0 ? milliwatts / 1000.0 : double.NaN,
            GpuWattsLimit = NvmlDeviceGetEnforcedPowerLimit(_gpu, out uint limit) == 0 ? limit / 1000.0 : double.NaN,
            GpuFan = NvmlDeviceGetFanSpeed(_gpu, out uint fan) == 0 ? fan : double.NaN,
            VramUsedGb = hasMemory ? memory.Used / GiB : double.NaN,
            VramTotalGb = hasMemory ? memory.Total / GiB : double.NaN,
        };
    }

    static double Value(IntPtr counter, uint flags) =>
        PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | flags, out _, out var value) == 0 && value.CStatus <= 1
            ? value.doubleValue
            : double.NaN;

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        if (_nvml) NvmlShutdown();
    }
}
