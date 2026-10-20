using System;
using System.Runtime.InteropServices;
using static TaskbarMonitor.Native;

namespace TaskbarMonitor;

/// <summary>
/// Показания MSI Afterburner из его общей памяти «MAHMSharedMemory». Нужны для температуры
/// и потребления CPU: без прав администратора Windows их не отдаёт.
/// </summary>
internal static class Afterburner
{
    const int Signature = 0x4D41484D;        // 'MAHM' — данные готовы
    const int DataOffset = 5 * 260;          // значение float идёт после пяти строк описания по MAX_PATH

    /// <summary>Значения датчиков по английским именам из Afterburner; NaN — нет датчика или Afterburner не запущен.</summary>
    public static double[] Read(params string[] names)
    {
        var values = new double[names.Length];
        Array.Fill(values, double.NaN);
        IntPtr map = OpenFileMapping(FILE_MAP_READ, false, "MAHMSharedMemory");
        if (map == IntPtr.Zero) return values;
        IntPtr view = MapViewOfFile(map, FILE_MAP_READ, 0, 0, UIntPtr.Zero);
        try
        {
            if (view == IntPtr.Zero || Marshal.ReadInt32(view, 0) != Signature) return values;
            int headerSize = Marshal.ReadInt32(view, 8), count = Marshal.ReadInt32(view, 12), entrySize = Marshal.ReadInt32(view, 16);
            for (int i = 0; i < count; i++)
            {
                IntPtr entry = view + headerSize + i * entrySize;
                int index = Array.IndexOf(names, Marshal.PtrToStringAnsi(entry));
                if (index < 0) continue;
                float value = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(entry, DataOffset));
                if (value < float.MaxValue) values[index] = value;   // FLT_MAX — значения нет
            }
        }
        finally
        {
            if (view != IntPtr.Zero) UnmapViewOfFile(view);
            CloseHandle(map);
        }
        return values;
    }
}
