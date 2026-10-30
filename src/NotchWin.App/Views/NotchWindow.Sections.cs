using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.Core.Notifications;
using NotchWin.Core.Settings;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>The expanded notch's tabs: timer, calendar, downloads, notifications and the section customizer.</summary>
public sealed partial class NotchWindow
{
    private static readonly Color Dim = Color.FromArgb(255, 0x8E, 0x8E, 0x93);
    private static readonly Color Faint = Color.FromArgb(255, 0x63, 0x63, 0x66);
    private static readonly Color Card = Color.FromArgb(255, 0x12, 0x12, 0x14);
    private static readonly Color TabOn = Color.FromArgb(255, 0x2A, 0x2A, 0x2D);

    private string _active = NotchSections.Music;
    private bool _customizing;
    private string? _sourceName;
    private bool _timerWasRunning;

    private static readonly Dictionary<string, (string Name, string Glyph)> SectionInfo = new()
    {
        [NotchSections.Music] = ("Música", ""),
        [NotchSections.Agents] = ("Agentes de IA", ""),
        [NotchSections.Timer] = ("Temporizador", ""),
        [NotchSections.Calendar] = ("Calendario", ""),
        [NotchSections.Downloads] = ("Descargas", ""),
        [NotchSections.Shelf] = ("Estantería", ""),
        [NotchSections.Camera] = ("Cámara", ""),
        [NotchSections.Notifications] = ("Notificaciones", ""),
    };

    private void InitSections()
    {
        _viewModel.Ticked += OnTicked;
        _viewModel.DataChanged += OnDataChanged;
        _viewModel.Notifications.Changed += () => DispatcherQueue.TryEnqueue(() =>
        {
            RebuildTabs();
            RefreshPanes();
            RelayoutIfExpanded();
        });
        _viewModel.Notifications.Added += n => DispatcherQueue.TryEnqueue(() => ShowNotificationToast(n));

        RebuildTabs();
        RefreshPanes();
        UpdateTimerUi();
    }

    private List<NotchSectionConfig> EnabledSections() =>
        _viewModel.Sections.Where(s => s.Enabled).ToList();

    // ---------- tabs ----------

    private void RebuildTabs()
    {
        var enabled = EnabledSections();
        if (enabled.Count > 0 && !enabled.Any(s => s.Id == _active))
        {
            _active = enabled[0].Id;
        }

        TabBar.Children.Clear();
        TabBar.ColumnDefinitions.Clear();

        for (var i = 0; i < enabled.Count; i++)
        {
            var id = enabled[i].Id;
            TabBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var content = new Grid();
            content.Children.Add(new FontIcon
            {
                Glyph = SectionInfo[id].Glyph,
                FontSize = 15,
                Foreground = new SolidColorBrush(id == _active && !_customizing ? Colors.White : Dim),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (id == NotchSections.Notifications && _viewModel.Notifications.Items.Count > 0)
            {
                content.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x45, 0x3A)),
                    CornerRadius = new CornerRadius(7),
                    MinWidth = 14,
                    Height = 14,
                    Padding = new Thickness(3, 0, 3, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = Math.Min(99, _viewModel.Notifications.Items.Count).ToString(),
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }

            var tab = new Button
            {
                Content = content,
                Height = 30,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(id == _active && !_customizing ? TabOn : Colors.Transparent),
            };
            ToolTipService.SetToolTip(tab, SectionInfo[id].Name);
            tab.Click += (_, _) =>
            {
                _active = id;
                _customizing = false;
                RebuildTabs();
                ShowActivePane();
                RelayoutIfExpanded();
            };

            Grid.SetColumn(tab, i);
            TabBar.Children.Add(tab);
        }

        TabBar.Visibility = _customizing || enabled.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowActivePane()
    {
        var empty = EnabledSections().Count == 0;
        MusicPane.Visibility = Show(!_customizing && _active == NotchSections.Music && !empty);
        AgentsPane.Visibility = Show(!_customizing && _active == NotchSections.Agents && !empty);
        TimerPane.Visibility = Show(!_customizing && _active == NotchSections.Timer && !empty);
        CalendarPane.Visibility = Show(!_customizing && _active == NotchSections.Calendar && !empty);
        DownloadsPane.Visibility = Show(!_customizing && _active == NotchSections.Downloads && !empty);
        NotificationsPane.Visibility = Show(!_customizing && _active == NotchSections.Notifications && !empty);
        ShelfPane.Visibility = Show(!_customizing && _active == NotchSections.Shelf && !empty);
        CameraPane.Visibility = Show(!_customizing && _active == NotchSections.Camera && !empty);
        CustomizePane.Visibility = Show(_customizing || empty);
        if (CustomizePane.Visibility == Visibility.Visible)
        {
            RebuildCustomize();
        }

        UpdateHeader();
        UpdateCamera();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateHeader()
    {
        SourceText.Text = _customizing
            ? "Personalizar"
            : _active == NotchSections.Music && _sourceName is not null
                ? _sourceName
                : SectionInfo.TryGetValue(_active, out var info) ? info.Name : "NotchWin";
    }

    private void RelayoutIfExpanded()
    {
        if (_expanded)
        {
            ApplyLayout(animate: true);
        }
    }

    private void OnGearClicked(object sender, RoutedEventArgs e)
    {
        _customizing = !_customizing;
        RebuildTabs();
        ShowActivePane();
        RelayoutIfExpanded();
    }

    private void OnCollapseClicked(object sender, RoutedEventArgs e) => SetExpanded(false);

    // ---------- clock ----------

    private void OnTicked()
    {
        if (_viewModel.Timer.TryComplete())
        {
            var total = FormatCountdown(_viewModel.Timer.Total);
            _viewModel.Notifications.Add("Temporizador", "Tiempo cumplido", $"{total} completados");
        }

        UpdateTimerUi();
    }

    private void OnDataChanged()
    {
        RefreshPanes();
        RelayoutIfExpanded();
    }

    // ---------- timer ----------

    private void UpdateTimerUi()
    {
        var timer = _viewModel.Timer;
        var text = FormatCountdown(timer.Remaining);

        TimerBig.Text = text;
        TimerText.Text = text;
        TimerRing.Value = timer.FractionLeft * 100;
        TimerStartButton.Content = timer.IsRunning ? "Pausar" : timer.Remaining <= TimeSpan.Zero ? "Otra vez" : "Iniciar";
        TimerText.Visibility = timer.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        if (_timerWasRunning != timer.IsRunning)
        {
            _timerWasRunning = timer.IsRunning;
            if (!_expanded)
            {
                ApplyLayout(animate: true);
            }
        }
    }

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string minutes } && int.TryParse(minutes, out var m))
        {
            _viewModel.Timer.SetDuration(TimeSpan.FromMinutes(m));
            UpdateTimerUi();
        }
    }

    private void OnTimerStartClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Timer.IsRunning)
        {
            _viewModel.Timer.Pause();
        }
        else
        {
            _viewModel.Timer.Start();
        }

        UpdateTimerUi();
    }

    private void OnTimerResetClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.Timer.Reset();
        UpdateTimerUi();
    }

    // ---------- lists ----------

    private void RefreshPanes()
    {
        RebuildCalendar();
        RebuildDownloads();
        RebuildNotifications();
        RebuildShelf();
    }

    private void RebuildCalendar()
    {
        CalendarList.Children.Clear();
        CalendarHeader.Text = $"HOY · {DateTime.Now:dddd d}".ToUpperInvariant();

        if (!_viewModel.CalendarConfigured)
        {
            CalendarEmpty.Text = "Elige un archivo .ics en Ajustes para ver tus eventos aquí.";
            CalendarEmpty.Visibility = Visibility.Visible;
            return;
        }

        CalendarEmpty.Text = "Nada más por hoy";
        CalendarEmpty.Visibility = _viewModel.CalendarToday.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var palette = new[] { 0xFF9F0A, 0x0A84FF, 0x30D158, 0xBF5AF2, 0xFF375F };
        var i = 0;
        foreach (var ev in _viewModel.CalendarToday)
        {
            var color = palette[i++ % palette.Length];
            var accent = Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color);

            var row = new Grid { Background = new SolidColorBrush(Card), CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 9, 12, 9), ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new Border { Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(2) });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label(ev.Title, 13, Colors.White, bold: true, trim: true));
            text.Children.Add(Label(ev.AllDay ? "Todo el día" : $"{ev.Start:HH:mm} – {ev.End:HH:mm}", 11, Dim));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var until = ev.Start - DateTimeOffset.Now;
            var when = ev.AllDay ? string.Empty : until <= TimeSpan.Zero ? "ahora" : $"en {FormatSpan(until)}";
            var whenText = Label(when, 11, Color.FromArgb(255, 0xFF, 0xD6, 0x0A));
            whenText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(whenText, 2);
            row.Children.Add(whenText);

            CalendarList.Children.Add(row);
        }
    }

    private void RebuildDownloads()
    {
        DownloadsList.Children.Clear();
        DownloadsEmpty.Visibility = _viewModel.Downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var d in _viewModel.Downloads)
        {
            var card = new StackPanel { Background = new SolidColorBrush(Card), Padding = new Thickness(12, 10, 12, 10), Spacing = 6 };
            card.CornerRadius = new CornerRadius(12);

            var head = new Grid { ColumnSpacing = 8 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Label(d.FileName, 12, Colors.White, bold: true, trim: true));
            var size = Label(FormatBytes(d.Bytes), 12, Dim);
            Grid.SetColumn(size, 1);
            head.Children.Add(size);

            card.Children.Add(head);
            card.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x84, 0xFF)) });
            DownloadsList.Children.Add(card);
        }
    }

    private void RebuildNotifications()
    {
        NotificationsList.Children.Clear();
        var items = _viewModel.Notifications.Items;
        NotificationsEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var n in items)
        {
            var accent = ColorForSource(n.Source);
            var row = new Grid { Background = new SolidColorBrush(Card), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9), ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(accent) };
            badge.Child = new TextBlock
            {
                Text = n.Source.Length > 0 ? n.Source[..1].ToUpperInvariant() : "?",
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(badge);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label(n.Title, 12.5, Colors.White, bold: true, trim: true));
            text.Children.Add(Label(n.Message, 11, Dim, trim: true));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var ago = Label(FormatAgo(DateTimeOffset.Now - n.At), 10, Faint);
            ago.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ago, 2);
            row.Children.Add(ago);

            NotificationsList.Children.Add(row);
        }
    }

    private void OnClearNotificationsClicked(object sender, RoutedEventArgs e) => _viewModel.Notifications.Clear();

    private void ShowNotificationToast(NotchNotification n)
    {
        if (_expanded)
        {
            return;
        }

        ShowToast(n.Title, n.Source, new SolidColorBrush(ColorForSource(n.Source)));
    }

    // ---------- customize ----------

    private void RebuildCustomize()
    {
        CustomizeList.Children.Clear();
        var sections = _viewModel.Sections;

        for (var i = 0; i < sections.Count; i++)
        {
            var index = i;
            var section = sections[i];

            var row = new Grid { Background = new SolidColorBrush(Card), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 4, 6, 4), ColumnSpacing = 2 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = Label(SectionInfo[section.Id].Name, 13, section.Enabled ? Colors.White : Faint);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);

            var up = SmallButton("", () => MoveSection(index, -1));
            up.IsEnabled = index > 0;
            Grid.SetColumn(up, 1);
            row.Children.Add(up);

            var down = SmallButton("", () => MoveSection(index, 1));
            down.IsEnabled = index < sections.Count - 1;
            Grid.SetColumn(down, 2);
            row.Children.Add(down);

            var check = new CheckBox { IsChecked = section.Enabled, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0) };
            AutomationProperties_SetName(check, SectionInfo[section.Id].Name);
            check.Click += (_, _) =>
            {
                section.Enabled = check.IsChecked == true;
                CommitSections();
            };
            Grid.SetColumn(check, 3);
            row.Children.Add(check);

            CustomizeList.Children.Add(row);
        }
    }

    private static void AutomationProperties_SetName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);

    private void MoveSection(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _viewModel.Sections.Count)
        {
            return;
        }

        (_viewModel.Sections[index], _viewModel.Sections[target]) = (_viewModel.Sections[target], _viewModel.Sections[index]);
        CommitSections();
    }

    private void CommitSections()
    {
        _viewModel.PersistSections?.Invoke();
        RebuildTabs();
        ShowActivePane();
        RelayoutIfExpanded();
    }

    private static Button SmallButton(string glyph, Action onClick)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 10 },
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---------- helpers ----------

    private static TextBlock Label(string text, double size, Color color, bool bold = false, bool trim = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(color) };
        if (bold)
        {
            block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        if (trim)
        {
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.MaxLines = 1;
        }

        return block;
    }

    private static Color ColorForSource(string source) => source switch
    {
        "Claude Code" => Color.FromArgb(255, 0xD9, 0x77, 0x57),
        "Codex" => Color.FromArgb(255, 0x10, 0xA3, 0x7F),
        "Copilot" => Color.FromArgb(255, 0x8B, 0x5C, 0xF6),
        "Temporizador" => Color.FromArgb(255, 0xFF, 0xD6, 0x0A),
        "Descargas" => Color.FromArgb(255, 0x0A, 0x84, 0xFF),
        "OpenCode" => Color.FromArgb(255, 0x3B, 0x9C, 0xFF),
        _ => Color.FromArgb(255, 0xE5, 0xE5, 0xEA),
    };

    private static string FormatCountdown(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} m" : $"{Math.Max(1, (int)t.TotalMinutes)} min";

    private static string FormatAgo(TimeSpan t) =>
        t.TotalMinutes < 1 ? "ahora" : t.TotalHours < 1 ? $"hace {(int)t.TotalMinutes} min" : t.TotalDays < 1 ? $"hace {(int)t.TotalHours} h" : $"hace {(int)t.TotalDays} d";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}
