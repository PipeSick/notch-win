using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Modules.Camera;
using NotchWin.App.ViewModels;
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

    // ---------- shelf (Shelf+) ----------

    private bool ShelfEnabled => _viewModel.Sections.Any(s => s.Id == NotchSections.Shelf && s.Enabled);

    private static bool Accepts(DataPackageView v) =>
        v.Contains(StandardDataFormats.StorageItems) || v.Contains(StandardDataFormats.Bitmap) || v.Contains(StandardDataFormats.Text);

    private void OnRootDragEnter(object sender, DragEventArgs e)
    {
        if (!ShelfEnabled || !Accepts(e.DataView))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;

        // Dragging something toward the notch opens the shelf, so the drop target is already there.
        if (!_expanded)
        {
            ShowSection(NotchSections.Shelf);
        }
    }

    /// <summary>Opens the notch on a tab (used by the shelf drag and by Hatsu's Spotlight action).</summary>
    public void ShowSection(string id)
    {
        if (_viewModel.Sections.All(s => s.Id != id || !s.Enabled))
        {
            return;
        }

        _active = id;
        _customizing = false;
        RebuildTabs();
        SetExpanded(true);
    }

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (ShelfEnabled && Accepts(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Guardar en la estantería";
        }
    }

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        if (!ShelfEnabled || !Accepts(e.DataView))
        {
            return;
        }

        var deferral = e.GetDeferral();
        var shelf = _viewModel.Shelf;
        try
        {
            var view = e.DataView;
            if (view.Contains(StandardDataFormats.StorageItems))
            {
                // Files and folders are added by reference: nothing is copied, moved or deleted.
                foreach (var item in await view.GetStorageItemsAsync())
                {
                    shelf.DeleteOwnedCopies(Added(shelf.AddPath(item.Path, item.IsOfType(StorageItemTypes.Folder), out var gone), gone));
                }
            }
            else if (view.Contains(StandardDataFormats.Bitmap))
            {
                await AddDroppedBitmapAsync(view);
            }
            else if (view.Contains(StandardDataFormats.Text))
            {
                var text = await view.GetTextAsync();
                shelf.DeleteOwnedCopies(Added(shelf.AddText(text, out var gone), gone));
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            // The source app withdrew the data mid-drop; keep whatever was added before.
        }
        finally
        {
            deferral.Complete();
        }

        _viewModel.RaiseDataChanged();
    }

    private static IReadOnlyList<string> Added(NotchWin.Core.Shelf.AddResult result, IReadOnlyList<string> toDelete) => result == NotchWin.Core.Shelf.AddResult.Added ? toDelete : [];

    /// <summary>An image with no file (dragged from a browser, say) becomes the shelf's own PNG copy, inside the shelf folder, removed when the item is.</summary>
    private async Task AddDroppedBitmapAsync(DataPackageView view)
    {
        var reference = await view.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var software = await decoder.GetSoftwareBitmapAsync();
        using var png = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, png);
        encoder.SetSoftwareBitmap(software);
        await encoder.FlushAsync();
        Directory.CreateDirectory(NotchViewModel.ShelfDir);
        var path = Path.Combine(NotchViewModel.ShelfDir, Guid.NewGuid().ToString("N") + ".png");
        png.Seek(0);
        await using (var file = File.Create(path))
        {
            await png.AsStreamForRead().CopyToAsync(file);
        }

        var shelf = _viewModel.Shelf;
        var result = shelf.AddOwnedImage(path, out var gone);
        shelf.DeleteOwnedCopies(result == NotchWin.Core.Shelf.AddResult.Added ? gone : [path]);
    }

    private static string KindGlyph(NotchWin.Core.Shelf.ShelfKind kind) => kind switch
    {
        NotchWin.Core.Shelf.ShelfKind.Folder => "\uE8B7",
        NotchWin.Core.Shelf.ShelfKind.Text => "\uE8C8",
        NotchWin.Core.Shelf.ShelfKind.Image => "\uE91B",
        _ => "\uE7C3",
    };

    private void RebuildShelf()
    {
        var shelf = _viewModel.Shelf;
        ShelfList.Children.Clear();
        ShelfHint.Visibility = shelf.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfHint.Text = "Suelta archivos, carpetas, imágenes o texto aquí, o arrástralos hacia el notch desde cualquier app";
        if (shelf.Items.Count == 0)
        {
            return;
        }

        // header: how many of the limit, and clear all (removes the shelf's own copies only; referenced originals are untouched)
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Label($"{shelf.Items.Count} de {shelf.Max}", 11, Dim));
        var clear = new Button { Content = Label("Vaciar", 11, Pal.Fg), Padding = new Thickness(10, 2, 10, 2), Background = new SolidColorBrush(Colors.Transparent) };
        clear.Click += (_, _) => { shelf.DeleteOwnedCopies(shelf.Clear()); _viewModel.RaiseDataChanged(); };
        Grid.SetColumn(clear, 1);
        header.Children.Add(clear);
        ShelfList.Children.Add(header);

        foreach (var item in shelf.Items.Reverse().ToList())
        {
            var missing = shelf.StatusOf(item) == NotchWin.Core.Shelf.ShelfStatus.Missing;
            var row = new Grid
            {
                Background = new SolidColorBrush(Card),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 4, 4, 4),
                ColumnSpacing = 8,
                CanDrag = !missing,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new FontIcon { Glyph = KindGlyph(item.Kind), FontSize = 14, Foreground = new SolidColorBrush(Dim), VerticalAlignment = VerticalAlignment.Center });

            var name = Label(missing ? item.Title + " (no encontrado)" : item.Title, 12, missing ? Dim : Pal.Fg, trim: true);
            name.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(name, item.Path ?? item.Text ?? item.Title);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            if (!missing && item.Kind is NotchWin.Core.Shelf.ShelfKind.File or NotchWin.Core.Shelf.ShelfKind.Folder && item.Path is { } shown)
            {
                buttons.Children.Add(SmallButton("\uE838", () => ShowInExplorer(shown)));
            }

            if (!missing && item.Kind is NotchWin.Core.Shelf.ShelfKind.Text or NotchWin.Core.Shelf.ShelfKind.Image)
            {
                buttons.Children.Add(SmallButton("\uE8C8", () => CopyShelfItem(item)));
            }

            buttons.Children.Add(SmallButton("\uE711", () =>
            {
                shelf.DeleteOwnedCopies([shelf.Remove(item.Id)]);
                _viewModel.RaiseDataChanged();
            }));
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);

            row.DragStarting += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    switch (item.Kind)
                    {
                        case NotchWin.Core.Shelf.ShelfKind.Text:
                            args.Data.SetText(item.Text ?? string.Empty);
                            break;
                        case NotchWin.Core.Shelf.ShelfKind.Image:
                            var image = await StorageFile.GetFileFromPathAsync(item.OwnedPath!);
                            args.Data.SetStorageItems([image]);
                            args.Data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(image));
                            break;
                        default:
                            IStorageItem original = item.Kind == NotchWin.Core.Shelf.ShelfKind.File
                                ? await StorageFile.GetFileFromPathAsync(item.Path!)
                                : await StorageFolder.GetFolderFromPathAsync(item.Path!);
                            args.Data.SetStorageItems([original]);
                            break;
                    }

                    args.Data.RequestedOperation = DataPackageOperation.Copy;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
                {
                    args.Cancel = true; // the original was moved or deleted since it was shelved: the row stays, marked, until the user removes it
                    _viewModel.RaiseDataChanged();
                }
                finally
                {
                    deferral.Complete();
                }
            };

            ShelfList.Children.Add(row);
        }
    }

    private static void ShowInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Explorer unavailable: nothing to do.
        }
    }

    /// <summary>Copies a text or image item to the Windows clipboard (the Island's clipboard history picks it up like any other copy: no second history).</summary>
    private static async void CopyShelfItem(NotchWin.Core.Shelf.ShelfItem item)
    {
        try
        {
            var package = new DataPackage();
            if (item.Kind == NotchWin.Core.Shelf.ShelfKind.Text)
            {
                package.SetText(item.Text ?? string.Empty);
            }
            else if (item.OwnedPath is { } path)
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            }

            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // The clipboard is busy or the copy vanished: ignore.
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
            // On a small screen the pill may not take more than 40% of its width, so it never looks oversized.
            var cap = Math.Clamp((int)((_controller?.MonitorWidthDip ?? 1920) * 0.4), CollapsedTrackWidth, MaxLyricPillWidth);
            width = Math.Clamp((wanted + 19) / 20 * 20, CollapsedTrackWidth, cap);
        }

        MiniTitle.MaxWidth = lyricMode ? width - PillTextChrome : 190;
        if (lyricMode != _pillLyrics || width != _pillWidth)
        {
            _pillLyrics = lyricMode;
            _pillWidth = width;
            if (!_expanded && _pill?.Current is null)
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
        LogLyrics(status, client.LastDiagnosis, info);
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
    /// WM-008: one line per lookup in %LOCALAPPDATA%\NotchWin\lyrics.log (kept under 64 KB) with how it was decided — candidates, which filter rejected
    /// them, the source used, the player and whether it gave a duration — but never the title or the artist. It is what a tester can send when a song has no lyrics.
    /// </summary>
    private static void LogLyrics(LyricsStatus status, string diagnosis, NowPlayingInfo info)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchWin"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "lyrics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
            string app = info.SourceAppName.Length > 40 ? info.SourceAppName[..40] : info.SourceAppName;
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {status} · reproductor {app} · duración {(info.Duration is { } d ? ((int)d.TotalSeconds) + " s" : "desconocida")} · {diagnosis}{Environment.NewLine}");
            if (Environment.GetEnvironmentVariable("WINMAC_PILL_SELFTEST") is { Length: > 0 } st) File.AppendAllText(st, $"{DateTime.UtcNow:HH:mm:ss.fff} lyrics {status} {diagnosis}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // diagnostics never break the lyrics
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
