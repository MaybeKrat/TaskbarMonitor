using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskbarMonitor;

/// <summary>
/// Окно настроек: какие элементы и значения показывать, в каком порядке и как их раскладывать, где и как стоит панель.
/// Изменения применяются сразу.
/// </summary>
internal sealed class SettingsWindow : Window
{
    readonly Func<Settings> _get;
    readonly Action<Settings> _apply;
    readonly StackPanel _elements = new();
    Action? _sync;     // обновляет поля окна по текущим настройкам: их меняют и меню, и перетаскивание панели
    bool _syncing;

    public SettingsWindow(Func<Settings> get, Action<Settings> apply)
    {
        _get = get;
        _apply = apply;
        Title = Strings.SettingsTitle;
#pragma warning disable WPF0001   // ThemeMode — оформление Windows 11; в .NET 10 помечено как экспериментальное
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 24) };
        root.Children.Add(Header(Strings.ElementsHeader));
        root.Children.Add(_elements);

        root.Children.Add(Header(Strings.LookHeader));
        root.Children.Add(Toggle(Strings.Rings, () => _get().Rings, v => Apply(_get() with { Rings = v })));
        root.Children.Add(Choice(Strings.ItemsFlow, () => _get().ItemsFlow, v => Apply(_get() with { ItemsFlow = v })));
        root.Children.Add(Hint(Strings.ItemsFlowHint));
        root.Children.Add(Choice(Strings.ValuesFlow, () => _get().ValuesFlow, v => Apply(_get() with { ValuesFlow = v })));

        root.Children.Add(Header(Strings.PlaceHeader));
        root.Children.Add(PlaceRow());
        root.Children.Add(Hint(Strings.PlaceHint));
        root.Children.Add(Toggle(Strings.OnTop, () => _get().OnTop, v => Apply(_get() with { OnTop = v })));
        root.Children.Add(Toggle(Strings.HideInFullscreen, () => _get().HideInFullscreen, v => Apply(_get() with { HideInFullscreen = v })));
        root.Children.Add(Hint(Strings.ScreenOnlyHint));

        root.Children.Add(Header(Strings.GeneralHeader));
        root.Children.Add(Toggle(Strings.Autostart, () => Autostart.Enabled, v => Autostart.Enabled = v));

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Activated += (_, _) => Sync();
        Sync();
    }

    void Apply(Settings settings)
    {
        _apply(settings);
        Sync();
    }

    void Sync()
    {
        _syncing = true;
        _sync?.Invoke();
        BuildElements();
        _syncing = false;
    }

    /// <summary>Карточки элементов в порядке показа: видимость, порядок и значения под подписью.</summary>
    void BuildElements()
    {
        _elements.Children.Clear();
        Element[] elements = _get().Elements;
        for (int i = 0; i < elements.Length; i++) _elements.Children.Add(ElementCard(elements, i));
    }

    Border ElementCard(Element[] elements, int index)
    {
        Element element = elements[index];
        var shown = new CheckBox
        {
            Content = element.Sensor.ToString().ToUpperInvariant(),
            IsChecked = element.Visible,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        shown.Click += (_, _) =>
        {
            if (shown.IsChecked != true && elements.Count(e => e.Visible) == 1)
            {
                shown.IsChecked = true;   // хотя бы один элемент остаётся на панели
                return;
            }
            Change(index, element with { Visible = shown.IsChecked == true });
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(IconButton("", Strings.MoveUp, index > 0, () => Move(index, -1)));
        buttons.Children.Add(IconButton("", Strings.MoveDown, index < elements.Length - 1, () => Move(index, 1)));
        var top = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(shown);

        var metrics = new WrapPanel { Margin = new Thickness(28, 2, 0, 0), IsEnabled = element.Visible };
        foreach (Metric metric in Settings.MetricsOf(element.Sensor))
        {
            var box = new CheckBox { Content = Strings.Of(metric), IsChecked = element.Metrics.Contains(metric), Margin = new Thickness(0, 0, 20, 0) };
            box.Click += (_, _) => Change(index, element with
            {
                Metrics = box.IsChecked == true ? [.. element.Metrics, metric] : element.Metrics.Where(m => m != metric).ToArray(),
            });
            metrics.Children.Add(box);
        }

        var content = new StackPanel();
        content.Children.Add(top);
        content.Children.Add(metrics);
        var card = new Border
        {
            Child = content,
            Padding = new Thickness(14, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    void Change(int index, Element element)
    {
        Settings s = _get();
        Element[] elements = [.. s.Elements];
        elements[index] = element;
        Apply(s with { Elements = elements });
    }

    void Move(int index, int step)
    {
        Settings s = _get();
        Element[] elements = [.. s.Elements];
        (elements[index], elements[index + step]) = (elements[index + step], elements[index]);
        Apply(s with { Elements = elements });
    }

    /// <summary>Где сейчас панель и кнопка «Вернуть к трею».</summary>
    DockPanel PlaceRow()
    {
        var place = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var back = new Button { Content = Strings.BackToTray };
        back.Click += (_, _) => Apply(_get() with { Placement = Placement.Tray, Display = null });
        _sync += () =>
        {
            Placement p = _get().Placement;
            place.Text = p switch { Placement.Tray => Strings.PlaceTray, Placement.Taskbar => Strings.PlaceTaskbar, _ => Strings.PlaceScreen };
            back.IsEnabled = p != Placement.Tray;
        };
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        DockPanel.SetDock(back, Dock.Right);
        row.Children.Add(back);
        row.Children.Add(place);
        return row;
    }

    CheckBox Toggle(string text, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = text, Margin = new Thickness(0, 2, 0, 2) };
        box.Click += (_, _) => set(box.IsChecked == true);
        _sync += () => box.IsChecked = get();
        return box;
    }

    /// <summary>Подпись и выпадающий список «В строку / Столбиком».</summary>
    DockPanel Choice(string text, Func<Flow> get, Action<Flow> set)
    {
        var combo = new ComboBox { Width = 170, ItemsSource = new[] { Strings.InRow, Strings.InColumn } };
        combo.SelectionChanged += (_, _) => { if (!_syncing && combo.SelectedIndex >= 0) set((Flow)combo.SelectedIndex); };
        _sync += () => combo.SelectedIndex = (int)get();
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(combo, Dock.Right);
        row.Children.Add(combo);
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    static TextBlock Header(string text) =>
        new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 8) };

    static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return hint;
    }

    /// <summary>Кнопка со значком Windows 11 (стрелки «выше» и «ниже»).</summary>
    static Button IconButton(string glyph, string tip, bool enabled, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12 },
            ToolTip = tip,
            IsEnabled = enabled,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(4, 0, 0, 0),
        };
        AutomationProperties.SetName(button, tip);   // у значка нет текста — имя для экранного диктора
        button.Click += (_, _) => click();
        return button;
    }
}
