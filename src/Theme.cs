using System.Windows.Media;
using Microsoft.Win32;
using static TaskbarMonitor.Paint;

namespace TaskbarMonitor;

/// <summary>Цвета для светлой и тёмной панели задач.</summary>
internal sealed record Theme(
    Color Ink, Color Dim, Color Cpu, Color Ram, Color Gpu, byte TrackAlpha,
    Color Hover, Color HoverBorder,
    Color Surface, Color SurfaceBorder, Color Divider, byte ShadowAlpha,
    Color Warm, Color Hot, Color Critical)
{
    public static readonly Theme Light = new(
        Ink: Rgb(0x1A1A1A), Dim: Rgb(0x5C5C5C), Cpu: Rgb(0x0F6CBD), Ram: Rgb(0x107C10), Gpu: Rgb(0x7A3EC8), TrackAlpha: 0x30,
        Hover: Argb(0x99FFFFFF), HoverBorder: Argb(0x14000000),
        Surface: Rgb(0xF9F9F9), SurfaceBorder: Argb(0x1F000000), Divider: Argb(0x14000000), ShadowAlpha: 0x30,
        Warm: Rgb(0x9A6700), Hot: Rgb(0xD0570A), Critical: Rgb(0xC50F1F));

    public static readonly Theme Dark = new(
        Ink: Rgb(0xFFFFFF), Dim: Rgb(0xC5C5C5), Cpu: Rgb(0x60B0F0), Ram: Rgb(0x6CCB5F), Gpu: Rgb(0xB18CF2), TrackAlpha: 0x40,
        Hover: Argb(0x15FFFFFF), HoverBorder: Argb(0x12FFFFFF),
        Surface: Rgb(0x2B2B2B), SurfaceBorder: Argb(0x24FFFFFF), Divider: Argb(0x19FFFFFF), ShadowAlpha: 0x60,
        Warm: Rgb(0xF2C94C), Hot: Rgb(0xFF9A4D), Critical: Rgb(0xFF6B6B));

    /// <summary>Тема панели задач из настроек Windows.</summary>
    public static Theme Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is 0 ? Dark : Light;
    }

    /// <summary>Цвет температуры: обычный до 50°, к 62° янтарный, к 74° оранжевый, с 85° красный.</summary>
    public Color Temperature(double t)
    {
        (double At, Color Color)[] stops = [(50, Ink), (62, Warm), (74, Hot), (85, Critical)];
        if (double.IsNaN(t) || t <= stops[0].At) return Ink;
        for (int i = 1; i < stops.Length; i++)
            if (t <= stops[i].At)
                return Lerp(stops[i - 1].Color, stops[i].Color, (t - stops[i - 1].At) / (stops[i].At - stops[i - 1].At));
        return Critical;
    }

    static Color Lerp(Color a, Color b, double k) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
}
