using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NotchWin.Core.Settings;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// Inicio (propuesta A+B): tablero de tarjetas. Cada tarjeta resume un módulo y, al tocarla, abre ese módulo con las mismas pestañas de siempre.
/// Solo aparecen las tarjetas de módulos que el usuario tiene activados (Personalizar); sin datos, la tarjeta lo dice en vez de esconderse.
/// </summary>
public sealed partial class NotchWindow
{
    private string _homeSignature = "";
    private DateTime _lastPeriphAsk = DateTime.MinValue;

    /// <summary>Al abrir Inicio se piden lecturas frescas de periféricos (como mucho una vez por minuto; la Barra respeta además su propio mínimo).</summary>
    private void AskPeripheralsIfDue()
    {
        if (_viewModel.RequestPeripheralsRefresh is not { } ask || DateTime.UtcNow - _lastPeriphAsk < TimeSpan.FromSeconds(60)) return;
        _lastPeriphAsk = DateTime.UtcNow; ask();
    }

    private void OpenSection(string id)
    {
        _active = id; _customizing = false;
        RebuildTabs(); ShowActivePane(); RelayoutIfExpanded();
    }

    private bool SectionOn(string id) => EnabledSections().Any(s => s.Id == id);

    /// <summary>Se reconstruye solo si cambió algo que se ve (la posición de la canción cuenta cada 3 s), para no parpadear ni perder un clic a medias.</summary>
    private void RebuildHome(bool force = false)
    {
        var timer = _viewModel.Timer;
        var notifs = _viewModel.Notifications.Items;
        var dl = _viewModel.Downloads;
        var ev = NextEvent();
        var clip = _viewModel.Clipboard.All.FirstOrDefault(e => e.Kind == NotchWin.Core.Clipboard.ClipboardItemKind.Text && !string.IsNullOrWhiteSpace(e.Text));
        var pos = _info is { } i0 ? i0.Position + (i0.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero) : TimeSpan.Zero;
        var sig = string.Join("|", _info?.Title, _info?.Artist, _info?.IsPlaying, (int)(pos.TotalSeconds / 3), CurrentLyric(), FormatCountdown(timer.Remaining), timer.IsRunning,
            notifs.Count, notifs.FirstOrDefault()?.Title, string.Join(",", _viewModel.Peripherals.Select(d => d.Name + d.Percent + d.Charging)), _homeSnap is { } hs ? $"{hs.Cpu:0}/{hs.Gpu:0}/{hs.Memory:0}/{hs.FreeDiskGb:0}" : "", dl.Count, dl.FirstOrDefault()?.FileName, dl.FirstOrDefault()?.Bytes / 1048576, ev?.Title, clip?.Text, string.Join(",", EnabledSections().Select(s => s.Id)));
        if (!force && sig == _homeSignature) return;
        _homeSignature = sig;

        HomeGrid.Children.Clear(); HomeGrid.RowDefinitions.Clear(); HomeGrid.ColumnDefinitions.Clear();
        HomeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        HomeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var small = new List<(string Id, Border Card)>();
        if (SectionOn(NotchSections.Timer)) small.Add((NotchSections.Timer, HomeCard("TEMPORIZADOR", TimerCardBody(), NotchSections.Timer)));
        // Periféricos: solo si la Barra mandó lecturas (módulo activo y con batería actual); su tarjeta lleva a Recursos, donde está «Dispositivos conectados»
        if (_viewModel.Peripherals.Count > 0 && SectionOn(NotchSections.Resources)) small.Add((NotchSections.Resources, HomeCard("PERIFÉRICOS", PeripheralsCardBody(), NotchSections.Resources)));
        if (SectionOn(NotchSections.Notifications)) small.Add((NotchSections.Notifications, HomeCard("NOTIFICACIONES" + (notifs.Count > 0 ? "  " + notifs.Count : ""), NotificationsCardBody(), NotchSections.Notifications)));
        if (SectionOn(NotchSections.Downloads)) small.Add((NotchSections.Downloads, HomeCard("DESCARGAS", DownloadsCardBody(), NotchSections.Downloads)));
        if (SectionOn(NotchSections.Calendar)) small.Add((NotchSections.Calendar, HomeCard("PRÓXIMO", CalendarCardBody(ev), NotchSections.Calendar)));
        if (SectionOn(NotchSections.Clipboard)) small.Add((NotchSections.Clipboard, HomeCard("PORTAPAPELES", ClipboardCardBody(clip?.Text), NotchSections.Clipboard)));

        var wide = SectionOn(NotchSections.Resources) ? HomeCard("RECURSOS", ResourcesCardBody(), NotchSections.Resources) : null;
        int row = 0, k = 0;
        if (SectionOn(NotchSections.Music))
        {
            var music = HomeCard("REPRODUCIENDO" + (_info is { SourceAppName: { Length: > 0 } src } ? " · " + src.ToUpperInvariant() : ""), MusicCardBody(), NotchSections.Music);
            Grid.SetRow(music, 0); Grid.SetColumn(music, 0); Grid.SetRowSpan(music, 2); HomeGrid.Children.Add(music);
            for (var r = 0; r < 2; r++) HomeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (; k < Math.Min(2, small.Count); k++) { var c = small[k].Card; Grid.SetRow(c, k); Grid.SetColumn(c, 1); HomeGrid.Children.Add(c); }
            row = 2;
        }

        if (wide is not null)
        {
            HomeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(wide, row); Grid.SetColumn(wide, 0); Grid.SetColumnSpan(wide, 2); HomeGrid.Children.Add(wide); row++;
        }

        while (k < small.Count)
        {
            HomeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var c1 = small[k++].Card; Grid.SetRow(c1, row); Grid.SetColumn(c1, 0); HomeGrid.Children.Add(c1);
            if (k < small.Count) { var c2 = small[k++].Card; Grid.SetRow(c2, row); Grid.SetColumn(c2, 1); HomeGrid.Children.Add(c2); }
            else Grid.SetColumnSpan(c1, 2);
            row++;
        }

        HomeEmpty.Visibility = HomeGrid.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border HomeCard(string title, UIElement body, string target)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 80, Foreground = new SolidColorBrush(CardUi.Faint), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        stack.Children.Add(body);
        var card = CardUi.Card(stack, 11);
        card.Tapped += (_, e) => { if (e.OriginalSource is FrameworkElement { Tag: "ctl" }) return; OpenSection(target); };
        ToolTipService.SetToolTip(card, SectionInfo[target].Name);
        return card;
    }

    private static TextBlock Line(string text, double size, Color color, bool bold = false) => CardUi.Text(text, size, color, bold, trim: true);

    // ---------- tarjetas ----------

    private UIElement MusicCardBody()
    {
        var s = new StackPanel { Spacing = 8 };
        if (_info is not { } info) { s.Children.Add(Line("Nada en reproducción", 13, CardUi.Dim)); return s; }
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(12), Background = MiniArt.Background };
        head.Children.Add(art);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(Line(info.Title, 14, Pal.Fg, bold: true));
        texts.Children.Add(Line(info.Artist, 12, CardUi.Dim));
        if (CurrentLyric() is { Length: > 0 } lyric) texts.Children.Add(new TextBlock { Text = lyric, FontSize = 11.5, FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = new SolidColorBrush(Pal.C(255, 0xE5, 0xE5, 0xEA)), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        Grid.SetColumn(texts, 1); head.Children.Add(texts);
        s.Children.Add(head);

        if (info.Duration is { TotalSeconds: > 0 } d)
        {
            var pos = info.Position + (info.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero);
            var frac = Math.Clamp(pos.TotalSeconds / d.TotalSeconds, 0, 1);
            var bar = new Grid { Height = 4 };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, frac), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - frac), GridUnitType.Star) });
            var track = new Border { Background = new SolidColorBrush(CardUi.Track), CornerRadius = new CornerRadius(2) };
            Grid.SetColumnSpan(track, 2); bar.Children.Add(track);
            var fill = new Border { Background = new SolidColorBrush(Pal.Fg), CornerRadius = new CornerRadius(2) };
            bar.Children.Add(fill);
            s.Children.Add(bar);
        }

        if (_viewModel.MediaControls is not null)
        {
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            ctl.Children.Add(HomeButton("", "Anterior", async () => await RunControl(c => c.PreviousAsync())));
            ctl.Children.Add(HomeButton(info.IsPlaying ? "" : "", "Reproducir o pausar", async () => await RunControl(c => c.TogglePlayPauseAsync())));
            ctl.Children.Add(HomeButton("", "Siguiente", async () => await RunControl(c => c.NextAsync())));
            s.Children.Add(ctl);
        }

        return s;
    }

    private static Button HomeButton(string glyph, string tip, Func<Task> act)
    {
        var b = new Button { Tag = "ctl", Width = 34, Height = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Content = CardUi.Icon(glyph, 14, Pal.Fg) };
        ToolTipService.SetToolTip(b, tip);
        b.Tapped += (_, e) => e.Handled = true;   // un clic en un control no abre el módulo de la tarjeta
        b.Click += async (_, _) => await act();
        return b;
    }

    private static string DeviceGlyph(string type) => type switch { "mouse" => "\uE962", "keyboard" => "\uE765", "headset" => "\uE7F6", "gamepad" => "\uE7FC", _ => "\uE772" };

    private UIElement PeripheralsCardBody()
    {
        var s = new StackPanel { Spacing = 4 };
        foreach (var d in _viewModel.Peripherals.Take(4))
        {
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(CardUi.Icon(DeviceGlyph(d.Type), 12, CardUi.Dim));
            var name = Line(d.Name, 12, Pal.Fg); Grid.SetColumn(name, 1); row.Children.Add(name);
            var pct = Line(d.Percent + " %" + (d.Charging ? " ⚡" : ""), 12, d.Percent <= 15 && !d.Charging ? CardUi.Danger : Pal.Fg, bold: true); Grid.SetColumn(pct, 2); row.Children.Add(pct);
            s.Children.Add(row);
        }

        return s;
    }

    private UIElement ResourcesCardBody()
    {
        if (_homeSnap is not { } snap) return Line("Midiendo…", 12, CardUi.Dim);
        var grid = new Grid { ColumnSpacing = 14 };
        void Col(int i, string label, string value, double fraction)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new StackPanel { Spacing = 4 };
            var head = new Grid();
            head.Children.Add(Line(label, 11.5, CardUi.Dim));
            var v = Line(value, 11.5, Pal.Fg, bold: true); v.HorizontalAlignment = HorizontalAlignment.Right; head.Children.Add(v);
            cell.Children.Add(head);
            cell.Children.Add(CardUi.Bar(Math.Clamp(fraction, 0, 1), BarColor, height: 4));
            Grid.SetColumn(cell, i); grid.Children.Add(cell);
        }

        Col(0, "CPU", $"{snap.Cpu:0}%", snap.Cpu / 100);
        Col(1, "GPU", snap.Gpu is { } g ? $"{g:0}%" : "—", (snap.Gpu ?? 0) / 100);
        Col(2, "Memoria", $"{snap.Memory:0}%", snap.Memory / 100);
        Col(3, "Disco", $"{snap.FreeDiskGb:0} GB", DiskFreeFraction());
        return grid;
    }

    private UIElement TimerCardBody()
    {
        var t = _viewModel.Timer;
        var s = new StackPanel { Spacing = 2 };
        s.Children.Add(Line(FormatCountdown(t.Remaining), 22, Pal.C(255, 0xFF, 0xD6, 0x0A), bold: true));
        s.Children.Add(Line(t.IsRunning ? "En marcha" : "Detenido", 11, CardUi.Dim));
        return s;
    }

    private UIElement NotificationsCardBody()
    {
        var s = new StackPanel { Spacing = 4 };
        var items = _viewModel.Notifications.Items;
        if (items.Count == 0) { s.Children.Add(Line("Sin notificaciones", 12, CardUi.Dim)); return s; }
        foreach (var n in items.Take(2))
        {
            var one = new StackPanel();
            one.Children.Add(Line(string.IsNullOrWhiteSpace(n.Source) ? n.Title : n.Source + " · " + n.Title, 12, Pal.Fg, bold: true));
            if (!string.IsNullOrWhiteSpace(n.Message)) one.Children.Add(Line(n.Message, 11.5, CardUi.Dim));
            s.Children.Add(one);
        }

        return s;
    }

    private UIElement DownloadsCardBody()
    {
        var s = new StackPanel { Spacing = 2 };
        var dl = _viewModel.Downloads;
        if (dl.Count == 0) { s.Children.Add(Line("Sin descargas en curso", 12, CardUi.Dim)); return s; }
        s.Children.Add(Line(dl[0].FileName, 12, Pal.Fg, bold: true));
        s.Children.Add(Line(FormatBytes(dl[0].Bytes) + (dl.Count > 1 ? $" · +{dl.Count - 1} más" : ""), 11.5, CardUi.Dim));
        return s;
    }

    private WinMac.Calendar.CalendarEvent? NextEvent()
    {
        var now = DateTimeOffset.Now;
        return _viewModel.CalendarToday.Where(e => e.AllDay || e.End >= now).OrderBy(e => e.Start).FirstOrDefault();
    }

    private UIElement CalendarCardBody(WinMac.Calendar.CalendarEvent? e)
    {
        var s = new StackPanel { Spacing = 2 };
        if (!_viewModel.CalendarConfigured) { s.Children.Add(Line("Sin calendario", 12, CardUi.Dim)); return s; }
        if (e is null) { s.Children.Add(Line("Nada más hoy", 12, CardUi.Dim)); return s; }
        s.Children.Add(Line(e.Title, 12, Pal.Fg, bold: true));
        s.Children.Add(Line(e.AllDay ? "Todo el día" : e.Start.ToLocalTime().ToString("HH:mm") + " – " + e.End.ToLocalTime().ToString("HH:mm"), 11.5, CardUi.Dim));
        return s;
    }

    private static UIElement ClipboardCardBody(string? text)
    {
        var s = new StackPanel();
        s.Children.Add(Line(string.IsNullOrWhiteSpace(text) ? "Aún no copiaste nada" : text.Replace('\n', ' ').Trim(), 12, string.IsNullOrWhiteSpace(text) ? CardUi.Dim : Pal.Fg));
        return s;
    }

    /// <summary>Línea de letra actual (misma fuente y cuenta que el cabecero de música); vacía si no hay letra o no suena.</summary>
    private string CurrentLyric()
    {
        if (_info is { IsPlaying: true } info && _lyrics is { Count: > 0 } lines)
        {
            var pos = info.Position + (DateTime.UtcNow - _infoAtUtc);
            var i = NotchWin.Core.Lyrics.LrcParser.IndexAt(lines, pos);
            return i >= 0 && lines[i].Text.Length > 0 ? lines[i].Text : "♪";
        }

        return "";
    }
}
