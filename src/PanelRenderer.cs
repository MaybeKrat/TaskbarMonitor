using System;
using System.Windows;
using System.Windows.Media;
using static TaskbarMonitor.Paint;

namespace TaskbarMonitor;

/// <summary>
/// Рисует панель: для CPU, RAM и GPU — кольцо загрузки с числом, подпись и одно значение.
/// Размеры в пикселях заданы для панели задач высотой 60 px (масштаб 125%) и умножаются на k, а затем на Scale.
/// </summary>
internal sealed class PanelRenderer
{
    const double Scale = 0.9;   // общий масштаб панели: чем меньше, тем больше отступы сверху и снизу
    const double Pad = 8, RingSize = 44, Stroke = 4, TextGap = 7, ItemGap = 16, TextSize = 14;

    readonly record struct Item(string Label, Color Color, double Load, string Value, Color ValueColor, string Widest);

    /// <summary>Что показывать под подписями колец.</summary>
    public Settings Settings { get; set; } = new();

    // Всё, кроме дуг, меняется только с новым замером: этот слой рисуется один раз,
    // а кадры анимации добавляют к нему только дуги.
    object? _layerKey;
    DrawingGroup _layer = new();
    readonly Point[] _centers = new Point[3];

    /// <summary>Загрузка для колец в порядке CPU, RAM, GPU.</summary>
    public static double[] Loads(Sample s) => [s.CpuLoad, s.RamLoad, s.GpuLoad];

    /// <summary>Ширина не зависит от текущих чисел, чтобы панель не дёргалась.</summary>
    public int Width(Sample s, double k)
    {
        k *= Scale;
        double w = 2 * Pad * k + 2 * ItemGap * k;
        foreach (var item in Items(s, Theme.Light)) w += ItemWidth(item, k);
        return (int)Math.Ceiling(w);
    }

    /// <param name="arcs">Сколько закрашено на кольцах сейчас: во время анимации дуги догоняют числа.</param>
    public void Draw(DrawingContext dc, Sample s, double[] arcs, Theme t, double k, int w, int h, bool hover)
    {
        var key = (s, t, k, w, h, hover, Settings);
        if (!key.Equals(_layerKey))
        {
            _layer = Layer(s, t, k, w, h, hover);
            _layerKey = key;
        }
        dc.DrawDrawing(_layer);

        k *= Scale;
        double stroke = Stroke * k, r = (RingSize * k - stroke) / 2;
        Color[] colors = [t.Cpu, t.Ram, t.Gpu];
        for (int i = 0; i < arcs.Length; i++)
            if (arcs[i] >= 0.5)
                DrawArc(dc, new Pen(Brush(colors[i]), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    _centers[i], r, Math.Min(arcs[i], 100));
    }

    DrawingGroup Layer(Sample s, Theme t, double k, int w, int h, bool hover)
    {
        var layer = new DrawingGroup();
        using (var dc = layer.Open())
        {
            // Почти прозрачный фон: без него клики проходили бы сквозь пустые места панели.
            dc.DrawRectangle(Brush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(0, 0, w, h));
            if (hover)
            {
                double inset = Math.Round(5 * k), radius = 5 * k;
                dc.DrawRoundedRectangle(Brush(t.Hover), new Pen(Brush(t.HoverBorder), 1),
                    new Rect(0.5, inset + 0.5, w - 1, h - 2 * inset - 1), radius, radius);
            }

            k *= Scale;   // подсветка выше — по размеру кнопок панели задач, содержимое — в общем масштабе
            double x = Pad * k, cy = Math.Round(h / 2.0);
            Item[] items = Items(s, t);
            for (int i = 0; i < items.Length; i++)
            {
                _centers[i] = new Point(x + RingSize * k / 2, cy);
                DrawItem(dc, items[i], t, k, _centers[i]);
                x += ItemWidth(items[i], k) + ItemGap * k;
            }
        }
        layer.Freeze();
        return layer;
    }

    /// <summary>Элементы панели; температура под подписью окрашивается по нагреву, как в карточке.</summary>
    Item[] Items(Sample s, Theme t) =>
    [
        Settings.Cpu == CpuValue.Temperature
            ? new("CPU", t.Cpu, s.CpuLoad, Degrees(s.CpuTemp), t.Temperature(s.CpuTemp), Degrees(99))
            : new("CPU", t.Cpu, s.CpuLoad, Format(s.CpuGhz, "0.0", "GHz"), t.Ink, Format(0, "0.0", "GHz")),
        new("RAM", t.Ram, s.RamLoad, Format(Settings.Ram == RamValue.Free ? s.RamTotalGb - s.RamUsedGb : s.RamUsedGb, "0.0", "GB"),
            t.Ink, Format(s.RamTotalGb, "0.0", "GB")),
        Settings.Gpu == GpuValue.Temperature
            ? new("GPU", t.Gpu, s.GpuLoad, Degrees(s.GpuTemp), t.Temperature(s.GpuTemp), Degrees(99))
            : new("GPU", t.Gpu, s.GpuLoad, Format(s.GpuWatts, "0", "W"), t.Ink, Format(0, "000", "W")),
    ];

    static double ItemWidth(Item item, double k) =>
        (RingSize + TextGap) * k + Math.Max(Text(item.Label, TextSize * k, item.Color, FontWeights.SemiBold).Width,
                                            Text(item.Widest, TextSize * k, item.Color, FontWeights.Normal).Width);

    /// <summary>Неподвижная часть элемента: дорожка кольца, число, подпись и значение.</summary>
    static void DrawItem(DrawingContext dc, Item item, Theme t, double k, Point center)
    {
        double stroke = Stroke * k, r = (RingSize * k - stroke) / 2;
        dc.DrawEllipse(null, new Pen(Brush(WithAlpha(item.Color, t.TrackAlpha)), stroke), center, r, r);

        string number = Format(item.Load, "0");
        CenterInk(dc, Text(number, (number.Length > 2 ? 15 : 17) * k, t.Ink, FontWeights.SemiBold), center);

        double tx = center.X + RingSize * k / 2 + TextGap * k;
        AtBaseline(dc, Text(item.Label, TextSize * k, item.Color, FontWeights.SemiBold), tx, center.Y - 5 * k);
        AtBaseline(dc, Text(item.Value, TextSize * k, item.ValueColor, FontWeights.Normal), tx, center.Y + 13 * k);
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
