using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using static TaskbarMonitor.Paint;

namespace TaskbarMonitor;

/// <summary>
/// Рисует панель: элементы CPU, RAM и GPU — кольцо загрузки с числом, подпись и выбранные значения.
/// Элементы и значения под подписью идут в строку или столбиком, как задано в настройках.
/// Размеры в пикселях заданы для панели задач высотой 60 px (масштаб 125%) и умножаются на k, а затем на Scale.
/// </summary>
internal sealed class PanelRenderer
{
    const double Scale = 0.9;   // общий масштаб панели: чем меньше, тем больше отступы сверху и снизу
    const double Pad = 8, VPad = 11, RingSize = 44, Stroke = 4, TextGap = 7, ItemGap = 16, RowGap = 10, ValueGap = 10;
    const double TextSize = 14, LineHeight = 18, TextHeight = 14;   // TextHeight — от верха прописных до низа выносных
    const double BarHeight = 60 / Scale;   // высота панели задач в тех же единицах

    /// <summary>Кусок текста. Место под него считается по Widest — самому широкому варианту, чтобы панель не дёргалась.</summary>
    readonly record struct Part(string Text, Color Color, FontWeight Weight, string Widest);

    /// <summary>Элемент на панели, в пикселях: центр кольца, начало и строки текста, их базовые линии.</summary>
    sealed record Block(Sensor Sensor, Color Color, double Load, Point Ring, double TextX, Part[][] Lines, double[] Baselines, double Font, double Gap);

    sealed record Arrangement(double Width, double Height, Block[] Blocks);

    /// <summary>Что и как показывать.</summary>
    public Settings Settings { get; set; } = new();

    // Всё, кроме дуг, меняется только с новым замером: этот слой рисуется один раз,
    // а кадры анимации добавляют к нему только дуги.
    object? _layerKey;
    DrawingGroup _layer = new();
    Block[] _blocks = [];

    /// <summary>Загрузка для колец в порядке датчиков: CPU, RAM, GPU.</summary>
    public static double[] Loads(Sample s) => [s.CpuLoad, s.RamLoad, s.GpuLoad];

    /// <summary>
    /// Размер панели; не зависит от текущих чисел, чтобы панель не дёргалась.
    /// docked — панель на панели задач: высота по ней, элементы всегда в строку.
    /// </summary>
    public (int Width, int Height) Size(Sample s, double k, bool docked)
    {
        Arrangement a = Arrange(s, Theme.Light, k, docked, 60 * k);
        return ((int)Math.Ceiling(a.Width), (int)Math.Ceiling(a.Height));
    }

    /// <param name="arcs">Сколько закрашено на кольцах сейчас, по датчикам: во время анимации дуги догоняют числа.</param>
    public void Draw(DrawingContext dc, Sample s, double[] arcs, Theme t, double k, int w, int h, bool hover, bool docked)
    {
        var key = (s, t, k, w, h, hover, docked, Settings);
        if (!key.Equals(_layerKey))
        {
            _layer = Layer(s, t, k, w, h, hover, docked);
            _layerKey = key;
        }
        dc.DrawDrawing(_layer);
        if (!Settings.Rings) return;

        double u = k * Scale, stroke = Stroke * u, r = (RingSize * u - stroke) / 2;
        foreach (Block b in _blocks)
            if (arcs[(int)b.Sensor] >= 0.5)
                DrawArc(dc, new Pen(Brush(b.Color), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    b.Ring, r, Math.Min(arcs[(int)b.Sensor], 100));
    }

    DrawingGroup Layer(Sample s, Theme t, double k, int w, int h, bool hover, bool docked)
    {
        var layer = new DrawingGroup();
        using (var dc = layer.Open())
        {
            // Почти прозрачный фон: без него клики проходили бы сквозь пустые места панели.
            dc.DrawRectangle(Brush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(0, 0, w, h));
            if (hover)
            {
                double inset = Math.Round(5 * k), radius = 5 * k;   // по размеру кнопок панели задач
                dc.DrawRoundedRectangle(Brush(t.Hover), new Pen(Brush(t.HoverBorder), 1),
                    new Rect(0.5, inset + 0.5, w - 1, h - 2 * inset - 1), radius, radius);
            }
            _blocks = Arrange(s, t, k, docked, h).Blocks;
            foreach (Block b in _blocks) DrawBlock(dc, b, t, k * Scale);
        }
        layer.Freeze();
        return layer;
    }

    /// <param name="height">Высота панели на панели задач, px; без docked не нужна.</param>
    Arrangement Arrange(Sample s, Theme t, double k, bool docked, double height)
    {
        double u = k * Scale;
        Element[] shown = Settings.Elements.Where(e => e.Visible).ToArray();
        Part[][][] lines = shown.Select(e => Lines(e, s, t)).ToArray();
        // На панели задач высота задана: если строк больше трёх, текст уменьшается, чтобы влезть.
        int most = lines.Length == 0 ? 1 : lines.Max(l => l.Length);
        double fit = docked ? Math.Min(1, (BarHeight - 4) / TextBlock(most)) : 1;
        double ring = Settings.Rings ? RingSize * u : 0, textX = Settings.Rings ? (RingSize + TextGap) * u : 0;
        double font = TextSize * u * fit, gap = ValueGap * u * fit;
        var sizes = lines.Select(l => (W: textX + l.Max(line => LineWidth(line, font, gap)), H: Math.Max(ring, TextBlock(l.Length) * u * fit))).ToArray();

        bool column = !docked && Settings.ItemsFlow == Flow.Column;
        double tallest = sizes.Length == 0 ? 0 : sizes.Max(x => x.H);
        double width = 2 * Pad * u + (column
            ? (sizes.Length == 0 ? 0 : sizes.Max(x => x.W))
            : sizes.Sum(x => x.W) + ItemGap * u * Math.Max(0, sizes.Length - 1));
        double h = docked ? height
            : column ? 2 * VPad * u + sizes.Sum(x => x.H) + RowGap * u * Math.Max(0, sizes.Length - 1)
            : Math.Max(BarHeight * u, tallest + 2 * VPad * u);

        var blocks = new Block[shown.Length];
        double left = Pad * u, top = VPad * u;
        for (int i = 0; i < shown.Length; i++)
        {
            double cy = Math.Round(column ? top + sizes[i].H / 2 : h / 2);
            int n = lines[i].Length;
            double[] baselines = Enumerable.Range(0, n).Select(j => cy + (4 + LineHeight * (j - (n - 1) / 2.0)) * u * fit).ToArray();
            Sensor sensor = shown[i].Sensor;
            blocks[i] = new Block(sensor, ColorOf(sensor, t), Loads(s)[(int)sensor], new Point(left + ring / 2, cy), left + textX,
                lines[i], baselines, font, gap);
            if (column) top += sizes[i].H + RowGap * u;
            else left += sizes[i].W + ItemGap * u;
        }
        return new Arrangement(width, h, blocks);
    }

    /// <summary>Высота текста из n строк в единицах панели.</summary>
    static double TextBlock(int n) => (n - 1) * LineHeight + TextHeight;

    static double LineWidth(Part[] line, double font, double gap) =>
        line.Sum(p => Text(p.Widest, font, p.Color, p.Weight).Width) + gap * (line.Length - 1);

    static Color ColorOf(Sensor sensor, Theme t) => sensor switch { Sensor.Cpu => t.Cpu, Sensor.Ram => t.Ram, _ => t.Gpu };

    /// <summary>Строки элемента: подпись, под ней значения — одной строкой или каждое на своей.</summary>
    Part[][] Lines(Element e, Sample s, Theme t)
    {
        string label = e.Sensor.ToString().ToUpperInvariant();
        Part[] title = [new(label, ColorOf(e.Sensor, t), FontWeights.SemiBold, label)];
        Part[] values = Settings.MetricsOf(e.Sensor).Where(e.Metrics.Contains).Select(m => Value(e.Sensor, m, s, t)).ToArray();
        if (values.Length == 0) return [title];
        return Settings.ValuesFlow == Flow.Column ? [title, .. values.Select(v => new[] { v })] : [title, values];
    }

    /// <summary>Значение под подписью; температура окрашивается по нагреву, как в карточке.</summary>
    static Part Value(Sensor sensor, Metric metric, Sample s, Theme t)
    {
        Part Plain(string text, string widest) => new(text, t.Ink, FontWeights.Normal, widest);
        Part Heat(double celsius) => new(Degrees(celsius), t.Temperature(celsius), FontWeights.Normal, Degrees(99));
        return (sensor, metric) switch
        {
            (_, Metric.Load) => Plain(Percent(Loads(s)[(int)sensor]), Percent(100)),
            (Sensor.Cpu, Metric.Clock) => Plain(Format(s.CpuGhz, "0.0", "GHz"), Format(0, "0.0", "GHz")),
            (Sensor.Cpu, Metric.Temperature) => Heat(s.CpuTemp),
            (Sensor.Cpu, Metric.Power) => Plain(Format(s.CpuWatts, "0", "W"), Format(0, "000", "W")),
            (Sensor.Ram, Metric.Used) => Plain(Format(s.RamUsedGb, "0.0", "GB"), Format(s.RamTotalGb, "0.0", "GB")),
            (Sensor.Ram, Metric.Free) => Plain(Format(s.RamTotalGb - s.RamUsedGb, "0.0", "GB"), Format(s.RamTotalGb, "0.0", "GB")),
            (Sensor.Gpu, Metric.Clock) => Plain(Format(s.GpuMhz, "N0", "MHz"), Format(8888, "N0", "MHz")),
            (Sensor.Gpu, Metric.Temperature) => Heat(s.GpuTemp),
            (Sensor.Gpu, Metric.Power) => Plain(Format(s.GpuWatts, "0", "W"), Format(0, "000", "W")),
            (Sensor.Gpu, Metric.Vram) => Plain(Format(s.VramUsedGb, "0.0", "GB"), Format(s.VramTotalGb, "0.0", "GB")),
            _ => Plain(Percent(s.GpuFan), Percent(100)),
        };
    }

    /// <summary>Неподвижная часть элемента: дорожка кольца, число, подпись и значения.</summary>
    void DrawBlock(DrawingContext dc, Block b, Theme t, double u)
    {
        if (Settings.Rings)
        {
            double stroke = Stroke * u, r = (RingSize * u - stroke) / 2;
            dc.DrawEllipse(null, new Pen(Brush(WithAlpha(b.Color, t.TrackAlpha)), stroke), b.Ring, r, r);
            string number = Format(b.Load, "0");
            CenterInk(dc, Text(number, (number.Length > 2 ? 15 : 17) * u, t.Ink, FontWeights.SemiBold), b.Ring);
        }
        for (int i = 0; i < b.Lines.Length; i++)
        {
            double x = b.TextX;
            foreach (Part p in b.Lines[i])
            {
                AtBaseline(dc, Text(p.Text, b.Font, p.Color, p.Weight), x, b.Baselines[i]);
                x += Text(p.Widest, b.Font, p.Color, p.Weight).Width + b.Gap;
            }
        }
    }

    static void DrawArc(DrawingContext dc, Pen pen, Point c, double r, double load)
    {
        if (load >= 99.5) { dc.DrawEllipse(null, pen, c, r, r); return; }
        double a = load / 100 * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(c.X, c.Y - r) };
        figure.Segments.Add(new ArcSegment(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)),
            new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pen, new PathGeometry([figure]));
    }
}
