using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarMonitor;

internal enum CpuValue { Clock, Temperature }
internal enum RamValue { Used, Free }
internal enum GpuValue { Power, Temperature }

/// <summary>Что показывать под подписями колец. Хранится в %APPDATA%\TaskbarMonitor\settings.json.</summary>
internal sealed record Settings
{
    public CpuValue Cpu { get; init; } = CpuValue.Clock;
    public RamValue Ram { get; init; } = RamValue.Used;
    public GpuValue Gpu { get; init; } = GpuValue.Power;

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarMonitor", "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
