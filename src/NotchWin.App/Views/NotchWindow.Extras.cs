using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Modules.Camera;
using NotchWin.Core.Lyrics;
using NotchWin.Core.Music;
using NotchWin.Core.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>Shelf (drag files in and out), camera mirror and synced lyrics.</summary>
public sealed partial class NotchWindow
{
    private CameraPreviewService? _camera;
    private bool _cameraWanted;
    private int _cameraGeneration;
    private IReadOnlyList<LyricLine>? _lyrics;

    // ---------- shelf ----------

    private bool ShelfEnabled => _viewModel.Sections.Any(s => s.Id == NotchSections.Shelf && s.Enabled);

    private void OnRootDragEnter(object sender, DragEventArgs e)
    {
        if (!ShelfEnabled || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;

        // Dragging something toward the notch opens the shelf, so the drop target is already there.
        if (!_expanded)
        {
            _active = NotchSections.Shelf;
            _customizing = false;
            RebuildTabs();
            SetExpanded(true);
        }
    }

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (ShelfEnabled && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Guardar en la estantería";
        }
    }

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        if (!ShelfEnabled || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            foreach (var item in await e.DataView.GetStorageItemsAsync())
            {
                if (!string.IsNullOrEmpty(item.Path) && !_viewModel.ShelfFiles.Contains(item.Path))
                {
                    _viewModel.ShelfFiles.Add(item.Path);
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // The source app withdrew the data mid-drop; keep whatever was added before.
        }
        finally
        {
            deferral.Complete();
        }

        _viewModel.RaiseDataChanged();
    }

    private void RebuildShelf()
    {
        ShelfList.Children.Clear();
        ShelfHint.Visibility = _viewModel.ShelfFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var path in _viewModel.ShelfFiles.ToList())
        {
            var row = new Grid
            {
                Background = new SolidColorBrush(Card),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 4, 4, 4),
                ColumnSpacing = 8,
                CanDrag = true,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new FontIcon { Glyph = "", FontSize = 14, Foreground = new SolidColorBrush(Dim), VerticalAlignment = VerticalAlignment.Center });

            var name = Label(Path.GetFileName(path), 12, Microsoft.UI.Colors.White, trim: true);
            name.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(name, path);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var remove = SmallButton("", () =>
            {
                _viewModel.ShelfFiles.Remove(path);
                _viewModel.RaiseDataChanged();
            });
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);

            row.DragStarting += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    IStorageItem item = File.Exists(path)
                        ? await StorageFile.GetFileFromPathAsync(path)
                        : await StorageFolder.GetFolderFromPathAsync(path);
                    args.Data.SetStorageItems([item]);
                    args.Data.RequestedOperation = DataPackageOperation.Copy;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
                {
                    args.Cancel = true; // file moved or deleted since it was shelved
                }
                finally
                {
                    deferral.Complete();
                }
            };

            ShelfList.Children.Add(row);
        }
    }

    // ---------- camera ----------

    /// <summary>Opens the webcam only while the camera tab is visible in an expanded notch, and closes it right after.</summary>
    private void UpdateCamera()
    {
        var wanted = _expanded && !_customizing && _active == NotchSections.Camera &&
                     _viewModel.Sections.Any(s => s.Id == NotchSections.Camera && s.Enabled);
        if (wanted == _cameraWanted)
        {
            return;
        }

        _cameraWanted = wanted;
        var generation = ++_cameraGeneration;
        if (wanted)
        {
            _ = StartCameraAsync(generation);
        }
        else
        {
            StopCamera();
        }
    }

    private async Task StartCameraAsync(int generation)
    {
        CameraStatus.Text = "Abriendo la cámara…";
        CameraStatus.Visibility = Visibility.Visible;
        CameraView.Visibility = Visibility.Collapsed;

        var camera = new CameraPreviewService();
        var (source, error) = await camera.StartAsync();

        if (generation != _cameraGeneration)
        {
            camera.Dispose(); // the user left the tab while Windows was still opening the device
            return;
        }

        if (source is null)
        {
            camera.Dispose();
            CameraStatus.Text = error ?? "No se pudo abrir la cámara.";
            return;
        }

        _camera = camera;
        CameraView.SetMediaPlayer(new MediaPlayer { RealTimePlayback = true, AutoPlay = true, Source = source });
        CameraView.Visibility = Visibility.Visible;
        CameraStatus.Visibility = Visibility.Collapsed;
    }

    private void StopCamera()
    {
        CameraView.MediaPlayer?.Dispose();
        CameraView.SetMediaPlayer(null!);
        CameraView.Visibility = Visibility.Collapsed;
        _camera?.Dispose();
        _camera = null;
    }

    private void OnMirrorClicked(object sender, RoutedEventArgs e) =>
        CameraMirror.ScaleX = MirrorButton.IsChecked == true ? -1 : 1;

    // ---------- lyrics ----------

    /// <summary>
    /// Sets the collapsed notch's text: the song title, or (opt-in) the current lyric line once lyrics are loaded
    /// and the song is playing. The wider lyric layout is kept for the whole song so the notch doesn't resize per line.
    /// </summary>
    private void UpdatePillText()
    {
        var wantLyrics = _viewModel.LyricsInPill && _viewModel.Lyrics is not null && _info is { IsPlaying: true };
        if (wantLyrics)
        {
            RetryLyricsIfNeeded(); // lyrics may have been switched on mid-song, or the lookup failed earlier
        }

        var lyricMode = wantLyrics && _lyrics is { Count: > 0 };
        var text = _hasTrack ? _info!.Title : string.Empty;
        if (lyricMode)
        {
            var position = _info!.Position + (_info.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero);
            var i = LrcParser.IndexAt(_lyrics!, position);
            text = i >= 0 && _lyrics![i].Text.Length > 0 ? _lyrics[i].Text : "♪";
        }

        if (MiniTitle.Text != text)
        {
            MiniTitle.Text = text;
        }

        // Default size when the line fits; otherwise just wide enough for it (rounded up so tiny differences
        // between lines don't make the notch twitch), up to a cap where it finally truncates with "…".
        var width = CollapsedTrackWidth;
        if (lyricMode)
        {
            _pillMeasurer.Text = text;
            _pillMeasurer.Measure(new Size(double.PositiveInfinity, CollapsedHeight));
            var wanted = (int)Math.Ceiling(_pillMeasurer.DesiredSize.Width) + PillTextChrome;
            width = Math.Clamp((wanted + 19) / 20 * 20, CollapsedTrackWidth, MaxLyricPillWidth);
        }

        MiniTitle.MaxWidth = lyricMode ? width - PillTextChrome : 190;
        if (lyricMode != _pillLyrics || width != _pillWidth)
        {
            _pillLyrics = lyricMode;
            _pillWidth = width;
            if (!_expanded && !_toasting)
            {
                ApplyLayout(animate: true);
            }
        }
    }

    /// <summary>Space the pill needs around the text: padding plus the cover on the left and the wave/timer on the right.</summary>
    private const int PillTextChrome = 116;

    private const int MaxLyricPillWidth = 720;

    private int _pillWidth = CollapsedTrackWidth;

    private readonly TextBlock _pillMeasurer = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    private bool _pillLyrics;
    private bool _lyricsLoading;
    private DateTime _lyricsTriedUtc;
    private LyricsStatus? _lyricsStatus;

    private TimeSpan? _lyricsLookupDuration;

    private bool LyricsDurationChanged(NowPlayingInfo info) =>
        _viewModel.Lyrics is not null && !_lyricsLoading && _lyrics is null
        && _lyricsStatus is LyricsStatus.NotFound or LyricsStatus.Unavailable
        && info.Duration is { } now
        && (_lyricsLookupDuration is not { } before || (now - before).Duration() > TimeSpan.FromSeconds(3));

    private async Task LoadLyricsAsync(NowPlayingInfo info)
    {
        _lyrics = null;
        _lyricsStatus = null;
        _lyricsLookupDuration = info.Duration;
        LyricsBox.Visibility = Visibility.Collapsed;
        if (_viewModel.Lyrics is not { } client)
        {
            return;
        }

        _lyricsLoading = true;
        UpdateLyrics();
        var (lines, status) = await client.LookupAsync(info.Title, info.Artist, info.Duration);
        _lyricsLoading = false;
        _lyricsTriedUtc = DateTime.UtcNow;
        if (_info?.Title != info.Title || _info?.Artist != info.Artist)
        {
            return; // the track changed while the lookup was in flight
        }

        _lyrics = lines;
        _lyricsStatus = status;
        UpdateLyrics();
        UpdatePillText();
        if (_expanded)
        {
            ApplyLayout(animate: true);
        }
    }

    /// <summary>
    /// Lyrics are looked up when a track starts, which misses the case where they were switched on mid-track
    /// or the service was down. Opening the notch tries again, at most every 15 s and never for "not found".
    /// </summary>
    private void RetryLyricsIfNeeded()
    {
        if (_viewModel.Lyrics is null || _info is null || !_hasTrack || _lyricsLoading || _lyrics is not null
            || _lyricsStatus == LyricsStatus.NotFound
            || DateTime.UtcNow - _lyricsTriedUtc < TimeSpan.FromSeconds(15))
        {
            return;
        }

        _ = LoadLyricsAsync(_info);
    }

    private void UpdateLyrics()
    {
        if (_lyrics is not { Count: > 0 } lines || _info is null)
        {
            var message = _viewModel.Lyrics is null || !_hasTrack
                ? null
                : _lyricsLoading ? "Buscando letra…"
                : _lyricsStatus switch
                {
                    LyricsStatus.NotFound => "Sin letra sincronizada para esta canción",
                    LyricsStatus.Unavailable => "Letras no disponibles ahora (el servicio no responde)",
                    _ => null,
                };

            LyricPrev.Text = " ";
            LyricCur.Text = message ?? " ";
            LyricNext.Text = " ";
            LyricsBox.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        var position = _info.Position + (_info.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero);
        var i = LrcParser.IndexAt(lines, position);

        // A non-breaking space keeps an empty line's height so the box doesn't jump between lines.
        static string Or(string? text) => string.IsNullOrEmpty(text) ? " " : text;
        LyricPrev.Text = Or(i > 0 ? lines[i - 1].Text : null);
        LyricCur.Text = Or(i >= 0 ? lines[i].Text : "♪");
        LyricNext.Text = Or(i + 1 < lines.Count ? lines[i + 1].Text : null);
        LyricsBox.Visibility = Visibility.Visible;
    }
}
