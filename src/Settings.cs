using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarMonitor;

internal enum Sensor { Cpu, Ram, Gpu }
internal enum Metric { Load, Clock, Temperature, Power, Used, Free, Vram, Fan }
internal enum Flow { Row, Column }
internal enum Placement { Tray, Taskbar, Screen }

// Настройки до версии 1.2: одно значение под подписью. Читаются только для переноса в Elements.
internal enum CpuValue { Clock, Temperature }
internal enum RamValue { Used, Free }
internal enum GpuValue { Power, Temperature }

/// <summary>Элемент панели: кольцо загрузки датчика, подпись и выбранные значения под ней.</summary>
internal sealed record Element(Sensor Sensor, bool Visible, Metric[] Metrics);

/// <summary>Что и как показывает панель и где она стоит. Хранится в %APPDATA%\TaskbarMonitor\settings.json.</summary>
internal sealed record Settings
{
    static readonly Element[] Defaults =
    [
        new(Sensor.Cpu, true, [Metric.Clock]),
        new(Sensor.Ram, true, [Metric.Used]),
        new(Sensor.Gpu, true, [Metric.Power]),
    ];

    /// <summary>Элементы в порядке показа, каждый датчик ровно один раз.</summary>
    public Element[] Elements { get; init; } = Defaults;
    /// <summary>Кольца загрузки у элементов.</summary>
    public bool Rings { get; init; } = true;
    /// <summary>Элементы в строку или столбиком. Столбиком — только на экране: на панели задач всегда в строку.</summary>
    public Flow ItemsFlow { get; init; } = Flow.Row;
    /// <summary>Значения под подписью в строку или столбиком.</summary>
    public Flow ValuesFlow { get; init; } = Flow.Row;

    /// <summary>У трея основной панели задач, в выбранном месте панели задач или отдельно на экране.</summary>
    public Placement Placement { get; init; } = Placement.Tray;
    /// <summary>Монитор, на панели задач которого стоит панель (\\.\DISPLAY2); null — основной.</summary>
    public string? Display { get; init; }
    /// <summary>На панели задач — центр панели в долях её ширины; на экране — левый верхний угол в пикселях.</summary>
    public double X { get; init; }
    public double Y { get; init; }
    /// <summary>Перетаскиваемая панель примагничивается к панели задач и встраивается в неё.</summary>
    public bool SnapToTaskbar { get; init; } = true;
    /// <summary>Панель задач спрятана автоскрытием — панель остаётся на месте отдельной карточкой.</summary>
    public bool StayWhenTaskbarHides { get; init; } = true;
    /// <summary>Панель на экране — поверх всех окон.</summary>
    public bool OnTop { get; init; } = true;
    /// <summary>Панель на экране прячется, пока впереди приложение на весь экран.</summary>
    public bool HideInFullscreen { get; init; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CpuValue? Cpu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RamValue? Ram { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GpuValue? Gpu { get; init; }

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarMonitor", "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>Какие значения есть у датчика, в порядке показа.</summary>
    public static Metric[] MetricsOf(Sensor sensor) => sensor switch
    {
        Sensor.Cpu => [Metric.Load, Metric.Clock, Metric.Temperature, Metric.Power],
        Sensor.Ram => [Metric.Load, Metric.Used, Metric.Free],
        _ => [Metric.Load, Metric.Clock, Metric.Temperature, Metric.Power, Metric.Vram, Metric.Fan],
    };

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json)?.Normalized() ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>
    /// Настройки из файла в порядке: каждый датчик ровно один раз и только со своими значениями.
    /// Выбор из версий до 1.2 (одно значение у элемента) переносится в Elements.
    /// </summary>
    Settings Normalized()
    {
        Element[] elements = Elements ?? Defaults;
        if (ReferenceEquals(elements, Defaults) && (Cpu != null || Ram != null || Gpu != null))
            elements =
            [
                new(Sensor.Cpu, true, [Cpu == CpuValue.Temperature ? Metric.Temperature : Metric.Clock]),
                new(Sensor.Ram, true, [Ram == RamValue.Free ? Metric.Free : Metric.Used]),
                new(Sensor.Gpu, true, [Gpu == GpuValue.Temperature ? Metric.Temperature : Metric.Power]),
            ];
        var known = elements
            .Where(e => e != null && Enum.IsDefined(e.Sensor))
            .DistinctBy(e => e.Sensor)
            .Select(e => e with { Metrics = MetricsOf(e.Sensor).Where((e.Metrics ?? []).Contains).ToArray() });
        Element[] all = [.. known, .. Defaults.Where(d => !elements.Any(e => e?.Sensor == d.Sensor))];
        if (!all.Any(e => e.Visible)) all[0] = all[0] with { Visible = true };   // хотя бы один элемент виден
        return this with { Elements = all, Cpu = null, Ram = null, Gpu = null };
    }
}
