using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Views.V3;
using NotchWin.Core.Calendar;
using NotchWin.Core.Notch.V3;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// Calendario V3: mes interactivo (días con evento marcados) junto a la agenda del día elegido. Fuentes REALES: el .ics configurado (solo lectura, nunca se modifica) y el calendario LOCAL
/// de Hatsu, que es el único editable: los eventos que se crean aquí se guardan en un JSON en este equipo y no se sincronizan con nadie. Sin ninguna fuente hay un estado vacío que lo explica.
/// </summary>
public sealed partial class NotchWindow
{
    private DateOnly _calMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateOnly _calSelected = DateOnly.FromDateTime(DateTime.Now);
    private bool _calAdding;
    private string _calNewTitle = "", _calNewTime = "";
    private int _calNewDuration = 1, _calNewColor = 3;   // índice en {30 min, 1 h, 2 h, todo el día} y en CalendarColors.Palette

    private static Color Hex(string hex) => Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));

    private IReadOnlyList<CalendarEntry> CalendarEntries() => CalendarAgenda.Merge(_viewModel.CalendarAll, _viewModel.LocalCalendar?.All() ?? []);

    private void RebuildCalendarV3()
    {
        CalendarPane.Children.Clear();
        var entries = CalendarEntries(); var today = DateOnly.FromDateTime(DateTime.Now);
        var root = new Grid { ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CalendarMonthView(entries, today));
        var agenda = _calAdding ? CalendarAddForm() : CalendarAgendaView(entries, today); Grid.SetColumn(agenda, 1); root.Children.Add(agenda);
        CalendarPane.Children.Add(root);
    }

    // ───────────── mes
    private UIElement CalendarMonthView(IReadOnlyList<CalendarEntry> entries, DateOnly today)
    {
        var s = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnSpacing = 4 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(V3Ui.ActionButton("", "Mes anterior", () => { _calMonth = _calMonth.AddMonths(-1); RebuildCalendarV3(); RelayoutIfExpanded(); }, ActionStyle.Ghost, size: 28));
        var title = CardUi.Text(char.ToUpperInvariant(CalendarMonth.MonthName(_calMonth.Month)[0]) + CalendarMonth.MonthName(_calMonth.Month)[1..] + " " + _calMonth.Year, 13, Pal.Fg, bold: true); title.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(title, 1); head.Children.Add(title);
        var next = V3Ui.ActionButton("", "Mes siguiente", () => { _calMonth = _calMonth.AddMonths(1); RebuildCalendarV3(); RelayoutIfExpanded(); }, ActionStyle.Ghost, size: 28); Grid.SetColumn(next, 2); head.Children.Add(next);
        s.Children.Add(head);

        var grid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
        for (int c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var names = new[] { "L", "M", "M", "J", "V", "S", "D" };
        for (int c = 0; c < 7; c++) { var n = CardUi.Text(names[c], 10.5, CardUi.Dim, bold: true); n.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(n, c); grid.Children.Add(n); }
        var weeks = CalendarMonth.Grid(_calMonth.Year, _calMonth.Month, today, d => CalendarAgenda.CountOn(entries, d));
        for (int w = 0; w < weeks.Count; w++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int d = 0; d < 7; d++)
            {
                var cell = weeks[w][d]; var date = cell.Date; bool selected = date == _calSelected;
                var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 1 };
                col.Children.Add(new TextBlock { Text = date.Day.ToString(), FontSize = 12, FontWeight = cell.IsToday ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(selected ? Colors.White : cell.InMonth ? Pal.Fg : Color.FromArgb(0x66, Pal.Fg.R, Pal.Fg.G, Pal.Fg.B)) });
                col.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = cell.Events > 0 ? new SolidColorBrush(selected ? Colors.White : V3Ui.AccentText) : new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Center });
                var b = new Button { Content = col, Padding = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(cell.IsToday && !selected ? 1 : 0), BorderBrush = new SolidColorBrush(V3Ui.AccentText), UseSystemFocusVisuals = true };
                var rest = selected ? V3Ui.Accent : Colors.Transparent; b.Resources["ButtonBackground"] = new SolidColorBrush(rest); b.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(selected ? V3Ui.Accent : Pal.C(0x22, 0xFF, 0xFF, 0xFF)); b.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(selected ? V3Ui.Accent : Pal.C(0x33, 0xFF, 0xFF, 0xFF)); b.Background = new SolidColorBrush(rest);
                ToolTipService.SetToolTip(b, date.ToString("dddd d 'de' MMMM")); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, date.ToString("dddd d 'de' MMMM") + (cell.Events > 0 ? $", {cell.Events} evento(s)" : ""));
                b.Click += (_, _) => { _calSelected = date; _calAdding = false; if (date.Month != _calMonth.Month) _calMonth = new DateOnly(date.Year, date.Month, 1); RebuildCalendarV3(); RelayoutIfExpanded(); };
                Grid.SetColumn(b, d); Grid.SetRow(b, w + 1); grid.Children.Add(b);
            }
        }

        s.Children.Add(grid);
        return V3Ui.Surface(s, 10);
    }

    // ───────────── agenda
    private UIElement CalendarAgendaView(IReadOnlyList<CalendarEntry> entries, DateOnly today)
    {
        var s = new StackPanel { Spacing = 8 };
        var title = _calSelected == today ? "Hoy · " + _calSelected.ToString("ddd d 'de' MMM") : _calSelected.ToString("dddd d 'de' MMMM");
        var addBtn = V3Ui.ActionButton("", "Crear un evento local", () => { _calAdding = true; _calNewTitle = ""; _calNewTime = DateTime.Now.AddHours(1).ToString("HH:00"); RebuildCalendarV3(); RelayoutIfExpanded(); KeyboardOn(); }, ActionStyle.Primary, size: 30);
        s.Children.Add(V3Ui.ModuleHeader("", char.ToUpperInvariant(title[0]) + title[1..], null, addBtn));

        var day = CalendarAgenda.On(entries, _calSelected);
        if (day.Count == 0)
        {
            var hint = _viewModel.CalendarConfigured ? "Nada agendado este día." : "Crea eventos locales con «+». Para ver tu calendario del sistema, elige un archivo .ics en los ajustes de Hatsu Notch.";
            s.Children.Add(V3Ui.EmptyState("", "Sin eventos", hint, ("Crear un evento", () => { _calAdding = true; _calNewTitle = ""; _calNewTime = DateTime.Now.AddHours(1).ToString("HH:00"); RebuildCalendarV3(); RelayoutIfExpanded(); KeyboardOn(); })));
            return s;
        }

        var list = new StackPanel { Spacing = 6 };
        foreach (var e in day) list.Children.Add(CalendarEventRow(e));
        s.Children.Add(new ScrollViewer { MaxHeight = 214, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = list });
        return s;
    }

    private UIElement CalendarEventRow(CalendarEntry e)
    {
        var accent = Hex(e.ColorHex);
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Border { Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(2) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(CardUi.Text(e.AllDay ? "Todo el día" : $"{e.Start.LocalDateTime:HH:mm} – {e.End.LocalDateTime:HH:mm}", 11, CardUi.Dim));
        text.Children.Add(CardUi.Text(e.Title, 13, Pal.Fg, bold: true, trim: true));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        if (e.Editable)
        {
            var id = e.Id; var del = V3Ui.ActionButton("", "Eliminar este evento local", () => { _viewModel.LocalCalendar?.Remove(id); }, ActionStyle.Ghost, size: 28);
            Grid.SetColumn(del, 2); row.Children.Add(del);
        }
        else { var tag = V3Ui.StatusBadge("Solo lectura", BadgeTone.Neutral); tag.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(tag, 2); row.Children.Add(tag); }
        return V3Ui.Surface(row, 9);
    }

    // ───────────── crear evento local
    private UIElement CalendarAddForm()
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(V3Ui.ModuleHeader("", "Nuevo evento", _calSelected.ToString("dddd d 'de' MMMM") + " · solo en este equipo"));
        var title = new TextBox { PlaceholderText = "Título", Text = _calNewTitle, MaxLength = LocalCalendarStore.MaxTitle, CornerRadius = new CornerRadius(10) };
        title.TextChanged += (_, _) => _calNewTitle = title.Text; s.Children.Add(title);
        var time = new TextBox { PlaceholderText = "Hora (HH:mm)", Text = _calNewTime, MaxLength = 5, CornerRadius = new CornerRadius(10) };
        time.TextChanged += (_, _) => _calNewTime = time.Text;
        s.Children.Add(V3Ui.SegmentedControl(["30 min", "1 h", "2 h", "Todo el día"], _calNewDuration, i => { _calNewDuration = i; RebuildCalendarV3(); RelayoutIfExpanded(); KeyboardOn(); }));
        if (_calNewDuration != 3) s.Children.Add(time);
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        for (int i = 0; i < CalendarColors.Palette.Count; i++)
        {
            int idx = i; var dot = new Button { Width = 24, Height = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(idx == _calNewColor ? 2 : 0), BorderBrush = new SolidColorBrush(Pal.Fg), UseSystemFocusVisuals = true };
            var c = new SolidColorBrush(Hex(CalendarColors.Palette[i])); dot.Background = c; dot.Resources["ButtonBackground"] = c; dot.Resources["ButtonBackgroundPointerOver"] = c; dot.Resources["ButtonBackgroundPressed"] = c;
            ToolTipService.SetToolTip(dot, "Color " + (i + 1)); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dot, "Color " + (i + 1) + (idx == _calNewColor ? ", elegido" : ""));
            dot.Click += (_, _) => { _calNewColor = idx; RebuildCalendarV3(); KeyboardOn(); }; colors.Children.Add(dot);
        }

        s.Children.Add(colors);
        var msg = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(V3Ui.Danger), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap }; s.Children.Add(msg);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(V3Ui.TextButton("Guardar", () => SaveLocalEvent(msg), ActionStyle.Primary));
        buttons.Children.Add(V3Ui.TextButton("Cancelar", () => { _calAdding = false; RebuildCalendarV3(); RelayoutIfExpanded(); }));
        s.Children.Add(buttons);
        return s;
    }

    private void SaveLocalEvent(TextBlock msg)
    {
        void Fail(string text) { msg.Text = text; msg.Visibility = Visibility.Visible; }
        if (string.IsNullOrWhiteSpace(_calNewTitle)) { Fail("Escribe un título."); return; }
        bool allDay = _calNewDuration == 3; TimeOnly t = default;
        if (!allDay && !TimeOnly.TryParse(_calNewTime, out t)) { Fail("La hora debe ser como 14:30."); return; }
        var start = new DateTimeOffset(_calSelected.ToDateTime(allDay ? TimeOnly.MinValue : t), DateTimeOffset.Now.Offset);
        var end = allDay ? start.AddDays(1) : start.AddMinutes(_calNewDuration switch { 0 => 30, 1 => 60, _ => 120 });
        var saved = _viewModel.LocalCalendar?.Add(_calNewTitle, start, end, allDay, CalendarColors.Palette[Math.Clamp(_calNewColor, 0, CalendarColors.Palette.Count - 1)]);
        if (saved is null) { Fail("No se pudo guardar el evento."); return; }
        _calAdding = false; RebuildCalendarV3(); RelayoutIfExpanded();
    }
}
