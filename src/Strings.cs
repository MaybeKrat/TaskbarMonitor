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
    public static readonly string Pin = Russian ? "Закрепить поверх окон" : "Pin on top";
    public static readonly string Unpin = Russian ? "Открепить" : "Unpin";
    public static readonly string SettingsMenu = Russian ? "Настройки…" : "Settings…";

    // Окно настроек
    public static readonly string SettingsTitle = Russian ? "Настройки TaskbarMonitor" : "TaskbarMonitor settings";
    public static readonly string ElementsHeader = Russian ? "Элементы" : "Items";
    public static readonly string MoveUp = Russian ? "Выше" : "Move up";
    public static readonly string MoveDown = Russian ? "Ниже" : "Move down";
    public static readonly string LookHeader = Russian ? "Вид" : "Layout";
    public static readonly string Rings = Russian ? "Кольца загрузки" : "Load rings";
    public static readonly string ItemsFlow = Russian ? "Элементы" : "Items";
    public static readonly string ValuesFlow = Russian ? "Значения под подписью" : "Values under the label";
    public static readonly string ItemsFlowHint = Russian
        ? "Столбиком — только для панели на экране: на панели задач элементы всегда идут в строку."
        : "In a column applies only on the screen: on the taskbar items always go in a row.";
    public static readonly string InRow = Russian ? "В строку" : "In a row";
    public static readonly string InColumn = Russian ? "Столбиком" : "In a column";
    public static readonly string PlaceHeader = Russian ? "Место" : "Position";
    public static readonly string PlaceTray = Russian ? "Сейчас у трея" : "Now next to the tray";
    public static readonly string PlaceTaskbar = Russian ? "Сейчас на панели задач" : "Now on the taskbar";
    public static readonly string PlaceScreen = Russian ? "Сейчас на экране" : "Now on the screen";
    public static readonly string PlaceHint = Russian
        ? "Чтобы переставить панель, перетащите её мышью: вдоль панели задач, на другой монитор или на экран."
        : "To move the panel, drag it with the mouse: along the taskbar, to another display or onto the screen.";
    public static readonly string SnapToTaskbar = Russian
        ? "Примагничиваться к панели задач при перетаскивании"
        : "Snap to the taskbar while dragging";
    public static readonly string StayWhenTaskbarHides = Russian
        ? "Оставаться на месте, когда панель задач скрывается"
        : "Stay in place when the taskbar auto-hides";
    public static readonly string OnTop = Russian ? "Поверх всех окон" : "On top of all windows";
    public static readonly string HideInFullscreen = Russian
        ? "Прятать, пока открыта игра или видео на весь экран"
        : "Hide while a full-screen game or video is in front";
    public static readonly string ScreenOnlyHint = Russian
        ? "Эти два пункта — для панели на экране: панель задач и так поверх окон и прячется сама."
        : "These two apply on the screen: the taskbar is on top anyway and hides by itself.";
    public static readonly string GeneralHeader = Russian ? "Общие" : "General";

    /// <summary>Название значения в настройках.</summary>
    public static string Of(Metric metric) => metric switch
    {
        Metric.Load => Load,
        Metric.Clock => Clock,
        Metric.Temperature => Temperature,
        Metric.Power => Power,
        Metric.Used => Used,
        Metric.Free => Free,
        Metric.Vram => Vram,
        _ => Fans,
    };
    public static readonly string Exit = Russian ? "Выход" : "Exit";
}
