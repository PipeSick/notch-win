using NotchWin.Core.Activities;
using NotchWin.Core.Music;

namespace NotchWin.App;

/// <summary>
/// WinMac 2.0 — feeds the Island's activity store from REAL sources (docs/25): the playing track (GSMTC), browser downloads in the Downloads folder,
/// new screenshots in Windows' capture folder, and WinMac's internal API (notch.activity over the host's pipe). Everything runs on the UI thread.
/// </summary>
public partial class App
{
    private const string MediaId = "media:gsmtc";
    private FileSystemWatcher? _captureWatcher;

    private void InitActivities()
    {
        if (_viewModel is null) return;
        var folder = CaptureLibrary.DefaultFolder();
        _viewModel.CaptureFolder = folder;
        try
        {
            Directory.CreateDirectory(folder);   // the folder Windows itself uses for Win+PrtScn; creating it (empty) changes nothing else
            _captureWatcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
            _captureWatcher.Created += (_, e) => _uiQueue?.TryEnqueue(() => OnCaptureSaved(e.FullPath));
            _captureWatcher.Renamed += (_, e) => _uiQueue?.TryEnqueue(() => OnCaptureSaved(e.FullPath));
        }
        catch (Exception) { /* no capture folder: the gallery shows its empty state */ }
    }

    private readonly HashSet<string> _announcedCaptures = new(StringComparer.OrdinalIgnoreCase);

    private void OnCaptureSaved(string path)
    {
        if (_viewModel is null || !CaptureLibrary.IsImage(path) || !_announcedCaptures.Add(path)) return;
        var name = Path.GetFileName(path);
        _viewModel.Activities.Upsert(new Activity("capture:" + path, ActivityKind.Capture, "Captura guardada")
        {
            Subtitle = name, Path = path, ThumbnailPath = path, Source = "Capturas de pantalla", State = ActivityState.Completed,
            IsTest = name.Contains("prueba", StringComparison.OrdinalIgnoreCase),
        });
        _viewModel.PostPill(new Core.Pill.PillItem(Core.Pill.PillKind.Capture, "Captura guardada", Path.GetFileName(Path.GetDirectoryName(path)) ?? "") { ImagePath = path });
        _viewModel.RaiseCapturesChanged();
    }

    /// <summary>The track as an activity (shown in «En curso»; dropped, not kept in the history, when it stops).</summary>
    private void MirrorMedia(NowPlayingInfo? info)
    {
        if (_viewModel is null) return;
        if (info is null || string.IsNullOrWhiteSpace(info.Title)) { _viewModel.Activities.Finish(MediaId); return; }
        _viewModel.Activities.Upsert(new Activity(MediaId, ActivityKind.Media, info.Title)
        {
            Subtitle = info.Artist, Source = info.SourceAppName, End = ActivityEnd.Discard, State = info.IsPlaying ? ActivityState.Active : ActivityState.Paused,
            Progress = info.Duration is { TotalSeconds: > 0 } d ? info.Position.TotalSeconds / d.TotalSeconds : null,
        });
    }

    /// <summary>Browser downloads (the existing tracker): one activity per file; finished ones go to the history with their final path.</summary>
    private void MirrorDownloads(IReadOnlyList<Core.Downloads.ActiveDownload> active, IReadOnlyList<string> completed, string folder)
    {
        if (_viewModel is null) return;
        var store = _viewModel.Activities;
        foreach (var d in active)
            store.Upsert(new Activity("download:" + d.FileName, ActivityKind.Download, d.FileName)
            {
                Subtitle = FormatBytes(d.Bytes) + " descargados", Source = "Descargas", Path = Path.Combine(folder, d.FileName),
                IsTest = d.FileName.Contains("prueba", StringComparison.OrdinalIgnoreCase),
            });
        foreach (var name in completed)
        {
            var id = "download:" + name;
            if (store.Find(id) is null) store.Upsert(new Activity(id, ActivityKind.Download, name) { Path = Path.Combine(folder, name), Source = "Descargas" });
            store.Finish(id, ActivityState.Completed, "Descarga terminada");
        }

        foreach (var gone in store.Current.Where(a => a.Kind == ActivityKind.Download && a.Id.StartsWith("download:", StringComparison.Ordinal)
                     && !active.Any(d => "download:" + d.FileName == a.Id)).ToList())
            store.Finish(gone.Id, ActivityState.Cancelled, "Interrumpida o cancelada");   // the partial file vanished without a final file
        store.ExpireStale();
    }

    private static string FormatBytes(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.0} MB" : $"{Math.Max(0, b) / 1024.0:0} KB";
}
