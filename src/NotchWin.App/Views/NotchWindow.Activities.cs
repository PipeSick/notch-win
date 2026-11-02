using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using Activity = NotchWin.Core.Activities.Activity;
using ActivityKind = NotchWin.Core.Activities.ActivityKind;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NotchWin.Core.Activities;
using NotchWin.Core.Lyrics;
using NotchWin.Core.Settings;
using Windows.Foundation;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// WinMac 2.0 — Centro de actividades (docs/25): media header, «En curso», «Capturas», «Historial» and the compact activity when there is no track.
/// It only READS the synced-lyrics state (<c>_lyrics</c>, <c>_info</c>) to show the current line in the header; the lyrics code itself is untouched.
/// Every action button calls a real implementation; activities without one show no buttons.
/// </summary>
public sealed partial class NotchWindow
{
    private string? _selectedCapture;

    private void InitActivitiesUi()
    {
        _viewModel.Activities.Changed += OnActivitiesChanged;
        _viewModel.CapturesChanged += OnCapturesChanged;
    }

    private void DetachActivitiesUi()
    {
        _viewModel.Activities.Changed -= OnActivitiesChanged;
        _viewModel.CapturesChanged -= OnCapturesChanged;
    }

    private void OnActivitiesChanged()
    {
        if (_closed) return;
        UpdateMiniActivity();
        if (!_expanded) return;
        if (_active == NotchSections.Activity) RebuildActivities();
        if (_active == NotchSections.History) RebuildHistory();
        UpdateNowHeader();
        RelayoutIfExpanded();
    }

    private void OnCapturesChanged()
    {
        if (_closed || !_expanded || _active != NotchSections.Captures) return;
        RebuildCaptures();
        RelayoutIfExpanded();
    }

    // ───────────────────────────── compact
    /// <summary>With no track (the track and its lyrics always win), the collapsed Island shows a download in progress, or test media in CI.</summary>
    private void UpdateMiniActivity()
    {
        var current = _viewModel.Activities.Current;
        var dl = current.FirstOrDefault(a => a.Kind == ActivityKind.Download);
        var media = current.FirstOrDefault(a => a.Kind == ActivityKind.Media && a.IsTest);
        var show = !_hasTrack && (dl is not null || media is not null);
        var a = dl ?? media;
        if (show && a is not null)
        {
            MiniActivityIcon.Glyph = a.Kind == ActivityKind.Download ? "" : "";
            MiniActivityText.Text = a.Title;
            MiniActivitySub.Text = a.Kind == ActivityKind.Download ? (a.Progress is { } p ? $"{p:P0}" : "…") : "prueba";
        }

        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        if (MiniActivity.Visibility != vis)
        {
            MiniActivity.Visibility = vis;
            if (!_expanded && _pill.Current is null) ApplyLayout(animate: true);
        }
    }

    // ───────────────────────────── media header
    private void UpdateNowHeader()
    {
        if (!_expanded) { NowHeader.Visibility = Visibility.Collapsed; return; }
        var test = _info is null ? _viewModel.Activities.Current.FirstOrDefault(a => a.Kind == ActivityKind.Media && a.IsTest) : null;
        if (_info is null && test is null) { NowHeader.Visibility = Visibility.Collapsed; return; }

        NowHeader.Visibility = Visibility.Visible;
        NowTestTag.Visibility = test is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_info is { } info)
        {
            NowTitle.Text = info.Title; NowArtist.Text = info.Artist;
            NowArt.Background = MiniArt.Background; NowArtGlyph.Visibility = MiniArt.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            var pos = info.Position + (info.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero);
            SetProgress(info.Duration is { TotalSeconds: > 0 } d ? Math.Clamp(pos.TotalSeconds / d.TotalSeconds, 0, 1) : null, pos, info.Duration);
            // Current synced-lyric line, read from the existing lyrics state (no change to how lyrics are fetched or timed).
            if (_lyrics is { Count: > 0 } lines && info.IsPlaying)
            {
                var i = LrcParser.IndexAt(lines, pos);
                NowLyric.Text = i >= 0 && lines[i].Text.Length > 0 ? lines[i].Text : "♪";
                NowLyric.Visibility = Visibility.Visible;
            }
            else NowLyric.Visibility = Visibility.Collapsed;

            NowPlayGlyph.Glyph = info.IsPlaying ? "" : "";
            NowControls.Visibility = _viewModel.MediaControls is not null ? Visibility.Visible : Visibility.Collapsed;   // only real controls
        }
        else if (test is not null)
        {
            NowTitle.Text = test.Title; NowArtist.Text = test.Subtitle ?? "";
            NowArt.Background = new SolidColorBrush(Color.FromArgb(255, 0x3A, 0x2D, 0x5C)); NowArtGlyph.Visibility = Visibility.Visible;
            SetProgress(test.Progress, null, null);
            NowLyric.Visibility = Visibility.Collapsed;
            NowControls.Visibility = Visibility.Collapsed;   // test data: no controls that would look functional
        }
    }

    private void SetProgress(double? p, TimeSpan? pos, TimeSpan? total)
    {
        NowProgressRow.Visibility = p is null ? Visibility.Collapsed : Visibility.Visible;
        if (p is not { } v) return;
        NowElapsed.Text = pos is { } a ? $"{(int)a.TotalMinutes}:{a.Seconds:00}" : "";
        NowTotal.Text = total is { } t ? $"{(int)t.TotalMinutes}:{t.Seconds:00}" : "";
        var w = NowTrack.ActualWidth > 0 ? NowTrack.ActualWidth : 200;
        NowFill.Width = Math.Round(w * v);
    }

    private async void OnNowPlayPause(object sender, RoutedEventArgs e) { if (_viewModel.MediaControls is { } m) await m.TogglePlayPauseAsync(); }
    private async void OnNowNext(object sender, RoutedEventArgs e) { if (_viewModel.MediaControls is { } m) await m.NextAsync(); }
    private async void OnNowPrev(object sender, RoutedEventArgs e) { if (_viewModel.MediaControls is { } m) await m.PreviousAsync(); }

    // ───────────────────────────── «En curso» and «Historial»
    private void RebuildActivities()
    {
        var items = _viewModel.Activities.Current;
        ActivityList.Children.Clear();
        ActivityEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var a in items) ActivityList.Children.Add(ActivityRow(a));
    }

    private void RebuildHistory()
    {
        var items = _viewModel.Activities.History;
        HistoryList.Children.Clear();
        HistoryEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryClear.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var a in items.Take(30)) HistoryList.Children.Add(ActivityRow(a));
    }

    private void OnHistoryClear(object sender, RoutedEventArgs e) => _viewModel.Activities.ClearHistory();

    private FrameworkElement ActivityRow(Activity a)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 10, 10, 10), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement icon;
        if (a.ThumbnailPath is { } thumb && File.Exists(thumb))
            icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Background = Thumbnail(thumb, 80) };
        else
            icon = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0A, 0x84, 0xFF)),
                Child = new FontIcon { Glyph = KindGlyph(a.Kind), FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x84, 0xFF)) },
            };
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(new TextBlock { Text = a.Title, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250 });
        if (a.IsTest) titleRow.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x9F, 0x0A)), CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 5, 1), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "prueba", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xB3, 0x40)) } });
        text.Children.Add(titleRow);
        var sub = StateText(a);
        if (sub.Length > 0) text.Children.Add(new TextBlock { Text = sub, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x98, 0x98, 0x9D)), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!a.IsFinished && a.Kind is ActivityKind.Download or ActivityKind.Transfer)
            text.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = a.Progress ?? 0, IsIndeterminate = a.Progress is null, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(text, 1); row.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var supported = ActivityActions.Supported(a);
        foreach (var (flag, glyph, tip) in new (ActivityAction, string, string)[]
        {
            (ActivityAction.PlayPause, "", "Reproducir o pausar"), (ActivityAction.Next, "", "Siguiente"),
            (ActivityAction.Open, "", "Abrir"), (ActivityAction.ShowInFolder, "", "Mostrar en la carpeta"), (ActivityAction.Copy, "", "Copiar"),
            (ActivityAction.Delete, "", "Eliminar (a la Papelera)"), (ActivityAction.RemoveFromHistory, "", "Quitar del historial"),
        })
        {
            if (!supported.HasFlag(flag)) continue;   // no decorative buttons: only what the Island really does
            var b = new Button { Width = 30, Height = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Content = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xD1, 0xD1, 0xD6)) } };
            ToolTipService.SetToolTip(b, tip);
            b.Click += async (_, _) => await RunAction(a, flag);
            actions.Children.Add(b);
        }

        Grid.SetColumn(actions, 2); row.Children.Add(actions);
        return row;
    }

    private static string StateText(Activity a) => a.State switch
    {
        ActivityState.Completed => a.Subtitle ?? "Terminado",
        ActivityState.Failed => "Falló · " + (a.Subtitle ?? ""),
        ActivityState.Cancelled => a.Subtitle ?? "Cancelado",
        ActivityState.Paused => "En pausa · " + (a.Subtitle ?? ""),
        _ => a.Subtitle ?? "",
    };

    private static string KindGlyph(ActivityKind k) => k switch
    {
        ActivityKind.Media => "", ActivityKind.Download => "", ActivityKind.Transfer => "", ActivityKind.Capture => "",
        ActivityKind.Recording => "", ActivityKind.Notification => "", _ => "",
    };

    // ───────────────────────────── «Capturas»
    private void RebuildCaptures()
    {
        var files = CaptureLibrary.List(_viewModel.CaptureFolder, 6);
        CapturesGrid.Children.Clear();
        CapturesEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_selectedCapture is not null && !files.Any(f => f.Path == _selectedCapture)) _selectedCapture = null;
        foreach (var f in files)
        {
            bool sel = f.Path == _selectedCapture;
            var tile = new StackPanel { Spacing = 4, Margin = new Thickness(4) };
            var img = new Border
            {
                Width = 146, Height = 86, CornerRadius = new CornerRadius(10), Background = Thumbnail(f.Path, 292),
                BorderThickness = new Thickness(sel ? 2 : 1), BorderBrush = new SolidColorBrush(sel ? Color.FromArgb(255, 0x0A, 0x84, 0xFF) : Color.FromArgb(0x26, 255, 255, 255)),
            };
            tile.Children.Add(img);
            tile.Children.Add(new TextBlock { Text = CaptureLibrary.When(f.ModifiedUtc.ToLocalTime(), DateTime.Now), FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC7, 0xC7, 0xCC)), HorizontalAlignment = HorizontalAlignment.Center });
            var path = f.Path;
            tile.Tapped += (_, _) => { _selectedCapture = _selectedCapture == path ? null : path; RebuildCaptures(); RelayoutIfExpanded(); };
            ToolTipService.SetToolTip(tile, f.Name);
            CapturesGrid.Children.Add(tile);
        }

        CaptureActions.Children.Clear();
        CaptureActions.Visibility = _selectedCapture is null ? Visibility.Collapsed : Visibility.Visible;
        if (_selectedCapture is not { } chosen) return;
        var act = new Activity("gallery:" + chosen, ActivityKind.Capture, Path.GetFileName(chosen)) { Path = chosen, State = ActivityState.Completed };
        foreach (var (flag, text) in new[] { (ActivityAction.Open, "Abrir"), (ActivityAction.Copy, "Copiar"), (ActivityAction.ShowInFolder, "Mostrar en carpeta"), (ActivityAction.Delete, "Eliminar") })
        {
            var b = new Button { Content = text, FontSize = 12, Padding = new Thickness(12, 4, 12, 5), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), BorderThickness = new Thickness(0) };
            b.Click += async (_, _) => { await RunAction(act, flag); if (flag == ActivityAction.Delete) { _selectedCapture = null; RebuildCaptures(); RelayoutIfExpanded(); } };
            CaptureActions.Children.Add(b);
        }
    }

    /// <summary>Thumbnail decoded at the size it is drawn (never the full image in memory).</summary>
    private static Brush Thumbnail(string path, int decodeWidth)
    {
        try { return new ImageBrush { ImageSource = new BitmapImage(new Uri(path)) { DecodePixelWidth = decodeWidth }, Stretch = Stretch.UniformToFill }; }
        catch (Exception) { return new SolidColorBrush(Color.FromArgb(255, 0x2C, 0x2C, 0x2E)); }
    }

    // ───────────────────────────── actions (real implementations only)
    private async Task RunAction(Activity a, ActivityAction what)
    {
        try
        {
            switch (what)
            {
                case ActivityAction.Open when a.Path is { } p && File.Exists(p): Process.Start(new ProcessStartInfo(p) { UseShellExecute = true })?.Dispose(); break;
                case ActivityAction.ShowInFolder when a.Path is { } p: Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = false })?.Dispose(); break;
                case ActivityAction.Copy when a.Path is { } p && File.Exists(p):
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(p);
                    var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                    break;
                case ActivityAction.Delete when a.Path is { } p && File.Exists(p):
                    var f = await Windows.Storage.StorageFile.GetFileFromPathAsync(p);
                    await f.DeleteAsync(Windows.Storage.StorageDeleteOption.Default);   // to the Recycle Bin, never permanently
                    _viewModel.Activities.Remove(a.Id);
                    break;
                case ActivityAction.RemoveFromHistory: _viewModel.Activities.Remove(a.Id); break;
                case ActivityAction.PlayPause when _viewModel.MediaControls is { } m: await m.TogglePlayPauseAsync(); break;
                case ActivityAction.Next when _viewModel.MediaControls is { } m: await m.NextAsync(); break;
                case ActivityAction.Previous when _viewModel.MediaControls is { } m: await m.PreviousAsync(); break;
            }
        }
        catch (Exception)
        {
            // a file that vanished or a locked clipboard: the action simply does nothing
        }
    }

    // ───────────────────────────── CI diagnostics (only with WINMAC_PILL_SELFTEST)
    /// <summary>Screen position (px) of each tab, so the visual audit can click them like a user.</summary>
    private void SelfTestLogTabs()
    {
        if (string.IsNullOrEmpty(SelfTestPath) || _controller is null || TabBar.ActualWidth <= 0) return;
        try
        {
            var ids = EnabledSections().Select(s => s.Id).ToList();
            var origin = TabBar.TransformToVisual(null).TransformPoint(new Point(0, 0));
            double scale = Content.XamlRoot?.RasterizationScale ?? 1, colW = TabBar.ActualWidth / Math.Max(1, ids.Count);
            var win = _controller.ActualRect;
            var parts = ids.Select((id, i) => $"{id}@{win.X + (int)((origin.X + colW * (i + 0.5)) * scale)},{win.Y + (int)((origin.Y + TabBar.ActualHeight / 2) * scale)}");
            File.AppendAllText(SelfTestPath!, $"{DateTime.UtcNow:HH:mm:ss.fff} tabs {string.Join(" ", parts)} active={_active}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
