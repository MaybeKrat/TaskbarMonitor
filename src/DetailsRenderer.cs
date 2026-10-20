using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using static TaskbarMonitor.Paint;

namespace TaskbarMonitor;

/// <summary>
/// Рисует карточку с подробностями, которая всплывает над панелью при наведении.
/// Размеры в пикселях заданы для масштаба 125% и умножаются на k.
/// </summary>
internal sealed class DetailsRenderer
{
    /// <summary>Поле вокруг карточки под тень.</summary>
    public const double Shadow = 12;
    const double Pad = 14, MinWidth = 300, TitleHeight = 26, RowHeight = 22, SectionGap = 14, ValueGap = 24, Radius = 8, TextSize = 14;

    readonly record struct Row(string Label, string Value, Color? Tint = null);
    readonly record struct Section(string Title, Color Color, string Subtitle, Row[] Rows);

    public (int Width, int Height) Size(Sample s, double k)
    {
        Section[] sections = Sections(s, Theme.Light);
        double content = 0;
        foreach (var section in sections)
        {
            content = Math.Max(content, Bold(section.Title, section.Color, k).Width + 8 * k + Regular(section.Subtitle, section.Color, k).Width);
            foreach (var row in section.Rows)
                content = Math.Max(content, Regular(row.Label, section.Color, k).Width + ValueGap * k + Regular(row.Value, section.Color, k).Width);
        }
        double width = Math.Max(MinWidth * k, content + 2 * Pad * k);
        double height = (2 * Pad + sections.Sum(x => TitleHeight + x.Rows.Length * RowHeight) + (sections.Length - 1) * SectionGap) * k;
        return ((int)Math.Ceiling(width + 2 * Shadow * k), (int)Math.Ceiling(height + 2 * Shadow * k));
    }

    public void Draw(DrawingContext dc, Sample s, Theme t, double k, int w, int h)
    {
        double margin = Math.Round(Shadow * k);
        var card = new Rect(margin, margin, w - 2 * margin, h - 2 * margin);
        DrawShadow(dc, card, t, k);
        dc.DrawRoundedRectangle(Brush(t.Surface), new Pen(Brush(t.SurfaceBorder), 1),
            new Rect(card.X + 0.5, card.Y + 0.5, card.Width - 1, card.Height - 1), Radius * k, Radius * k);

        double left = card.Left + Pad * k, right = card.Right - Pad * k, y = card.Top + Pad * k;
        Section[] sections = Sections(s, t);
        for (int i = 0; i < sections.Length; i++)
        {
            if (i > 0)
            {
                double line = Math.Round(y + SectionGap * k / 2) + 0.5;
                dc.DrawLine(new Pen(Brush(t.Divider), 1), new Point(left, line), new Point(right, line));
                y += SectionGap * k;
            }

            Section section = sections[i];
            var title = Bold(section.Title, section.Color, k);
            AtBaseline(dc, title, left, y + 18 * k);
            AtBaseline(dc, Regular(section.Subtitle, t.Dim, k), left + title.Width + 8 * k, y + 18 * k);
            y += TitleHeight * k;

            foreach (var row in section.Rows)
            {
                AtBaseline(dc, Regular(row.Label, t.Dim, k), left, y + 16 * k);
                var value = Regular(row.Value, row.Tint ?? t.Ink, k);
                AtBaseline(dc, value, right - value.Width, y + 16 * k);
                y += RowHeight * k;
            }
        }
    }

    static Section[] Sections(Sample s, Theme t) =>
    [
        new("CPU", t.Cpu, s.CpuName,
        [
            new(Strings.Load, Percent(s.CpuLoad)),
            new(Strings.Clock, Format(s.CpuGhz, "0.00", "GHz")),
            new(Strings.Temperature, Degrees(s.CpuTemp), t.Temperature(s.CpuTemp)),
            new(Strings.Power, Format(s.CpuWatts, "0", "W")),
        ]),
        new("RAM", t.Ram, Format(s.RamTotalGb, "0.0", "GB"),
        [
            new(Strings.Load, Percent(s.RamLoad)),
            new(Strings.Used, Format(s.RamUsedGb, "0.0", "GB")),
            new(Strings.Free, Format(s.RamTotalGb - s.RamUsedGb, "0.0", "GB")),
        ]),
        new("GPU", t.Gpu, s.GpuName,
        [
            new(Strings.Load, Percent(s.GpuLoad)),
            new(Strings.Clock, Format(s.GpuMhz, "N0", "MHz")),
            new(Strings.Temperature, Degrees(s.GpuTemp), t.Temperature(s.GpuTemp)),
            new(Strings.Power, OutOf(s.GpuWatts, s.GpuWattsLimit, "0", "W")),
            new(Strings.Vram, OutOf(s.VramUsedGb, s.VramTotalGb, "0.0", "GB")),
            new(Strings.Fans, Percent(s.GpuFan)),
        ]),
    ];

    /// <summary>«59 / 400 W»; без общего значения — просто «59 W».</summary>
    static string OutOf(double value, double total, string format, string unit) =>
        double.IsNaN(value) || double.IsNaN(total) ? Format(value, format, unit) : $"{Format(value, format)} / {Format(total, format, unit)}";

    static void DrawShadow(DrawingContext dc, Rect card, Theme t, double k)
    {
        // Мягкая тень: полупрозрачные слои, каждый чуть больше предыдущего, сдвинуты вниз.
        double offset = Math.Round(2 * k);
        int layers = (int)(Math.Round(Shadow * k) - offset);
        var brush = Brush(Color.FromArgb((byte)Math.Max(1, t.ShadowAlpha / layers), 0, 0, 0));
        for (int i = 1; i <= layers; i++)
            dc.DrawRoundedRectangle(brush, null, new Rect(card.X - i, card.Y - i + offset, card.Width + 2 * i, card.Height + 2 * i),
                Radius * k + i, Radius * k + i);
    }

    static FormattedText Regular(string s, Color c, double k) => Text(s, TextSize * k, c, FontWeights.Normal);
    static FormattedText Bold(string s, Color c, double k) => Text(s, TextSize * k, c, FontWeights.SemiBold);
}
