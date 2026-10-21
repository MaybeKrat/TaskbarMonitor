using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TaskbarMonitor;

/// <summary>Общие приёмы рисования: шрифт, текст по базовой линии, кисти, форматы чисел.</summary>
internal static class Paint
{
    /// <summary>Числа — по региональным настройкам Windows: «5,0» или «5.0».</summary>
    public static readonly CultureInfo Culture = CultureInfo.CurrentCulture;
    static readonly FontFamily Font = new("Segoe UI Variable Text");

    public static FormattedText Text(string s, double size, Color color, FontWeight weight) =>
        new(s, Culture, FlowDirection.LeftToRight, new Typeface(Font, FontStyles.Normal, weight, FontStretches.Normal),
            size, Brush(color), null, TextFormattingMode.Display, 1.0);

    public static void AtBaseline(DrawingContext dc, FormattedText text, double x, double baseline) =>
        dc.DrawText(text, new Point(Math.Round(x), Math.Round(baseline - text.Baseline)));

    /// <summary>Центрирует по видимым контурам цифр, а не по строке с запасом под выносные элементы.</summary>
    public static void CenterInk(DrawingContext dc, FormattedText text, Point c)
    {
        double inkMiddle = text.Height + text.OverhangAfter - text.Extent / 2;
        dc.DrawText(text, new Point(Math.Round(c.X - text.Width / 2), Math.Round(c.Y - inkMiddle)));
    }

    /// <summary>Число с единицей измерения; «—», если датчик недоступен.</summary>
    public static string Format(double value, string format, string unit = "") =>
        double.IsNaN(value) ? "—" : unit.Length == 0 ? value.ToString(format, Culture) : $"{value.ToString(format, Culture)} {unit}";

    public static string Percent(double value) => double.IsNaN(value) ? "—" : $"{value.ToString("0", Culture)}%";

    public static string Degrees(double value) => double.IsNaN(value) ? "—" : $"{value.ToString("0", Culture)}°";

    /// <summary>Поле вокруг карточки под тень, px при 125%.</summary>
    public const double CardShadow = 12;
    const double CardRadius = 8;

    /// <summary>Карточка: фон с рамкой и мягкой тенью. Тень выходит за card на CardShadow × k.</summary>
    public static void Card(DrawingContext dc, Rect card, Theme t, double k)
    {
        // Тень — полупрозрачные слои, каждый чуть больше предыдущего, сдвинуты вниз.
        double offset = Math.Round(2 * k), radius = CardRadius * k;
        int layers = (int)(Math.Round(CardShadow * k) - offset);
        var shadow = Brush(Color.FromArgb((byte)Math.Max(1, t.ShadowAlpha / layers), 0, 0, 0));
        for (int i = 1; i <= layers; i++)
            dc.DrawRoundedRectangle(shadow, null, new Rect(card.X - i, card.Y - i + offset, card.Width + 2 * i, card.Height + 2 * i),
                radius + i, radius + i);
        dc.DrawRoundedRectangle(Brush(t.Surface), new Pen(Brush(t.SurfaceBorder), 1),
            new Rect(card.X + 0.5, card.Y + 0.5, card.Width - 1, card.Height - 1), radius, radius);
    }

    public static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    public static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    public static Color Argb(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
