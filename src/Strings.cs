using System.Globalization;

namespace TaskbarMonitor;

/// <summary>Тексты интерфейса на языке Windows: русский, для остальных языков — английский.</summary>
internal static class Strings
{
    static readonly bool Russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    public static readonly string Load = Russian ? "Загрузка" : "Load";
    public static readonly string Clock = Russian ? "Частота" : "Clock";
    public static readonly string Temperature = Russian ? "Температура" : "Temperature";
    public static readonly string Power = Russian ? "Потребление" : "Power";
    public static readonly string Used = Russian ? "Занято" : "Used";
    public static readonly string Free = Russian ? "Свободно" : "Free";
    public static readonly string Vram = Russian ? "Видеопамять" : "VRAM";
    public static readonly string Fans = Russian ? "Вентиляторы" : "Fans";
    public static readonly string Autostart = Russian ? "Автозапуск" : "Start with Windows";
    public static readonly string BackToTray = Russian ? "Вернуть к трею" : "Move back to the tray";
    public static readonly string Exit = Russian ? "Выход" : "Exit";
}
