using System.Net;
using System.Net.Http.Headers;

namespace NotchWin.Core.Downloads;

public enum ManagedState { Queued, Downloading, Paused, Completed, Failed, Cancelled }

/// <summary>
/// Una descarga ADMINISTRADA por Hatsu (el usuario pidió descargar una dirección con Hatsu). A diferencia de las descargas que solo se OBSERVAN en la carpeta de descargas del navegador
/// (<see cref="DownloadTracker"/>: bytes y velocidad, sin total), aquí el tamaño total sale de la cabecera del servidor, y pausar, reanudar y cancelar son reales.
/// </summary>
public sealed class ManagedDownload
{
    internal ManagedDownload(string id, Uri url, string fileName, string targetPath) { Id = id; Url = url; FileName = fileName; TargetPath = targetPath; PartPath = targetPath + ".hatsu-part"; }

    public string Id { get; }
    public Uri Url { get; }
    public string FileName { get; internal set; }
    public string TargetPath { get; internal set; }
    public string PartPath { get; internal set; }
    public ManagedState State { get; internal set; } = ManagedState.Queued;
    public long ReceivedBytes { get; internal set; }
    /// <summary>Tamaño total que informa el servidor; null si no lo dice (entonces la barra es indeterminada).</summary>
    public long? TotalBytes { get; internal set; }
    public double? BytesPerSecond { get; internal set; }
    public string? Error { get; internal set; }
    public DateTime StartedUtc { get; internal set; }
    public DateTime? FinishedUtc { get; internal set; }

    /// <summary>0–1, o null si no se conoce el total.</summary>
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)ReceivedBytes / TotalBytes.Value, 0, 1) : null;

    /// <summary>Tiempo restante estimado con la velocidad medida; null si falta el total o la velocidad.</summary>
    public TimeSpan? Remaining => TotalBytes is { } t && BytesPerSecond is > 1 ? TimeSpan.FromSeconds(Math.Max(0, t - ReceivedBytes) / BytesPerSecond.Value) : null;

    internal CancellationTokenSource? Cts;
    internal bool PauseRequested, CancelRequested;
    internal readonly List<(DateTime At, long Bytes)> Samples = [];
}

public sealed class DownloadManager
{
    public const int MaxConcurrent = 3;
    private readonly HttpClient _http; private readonly string _folder; private readonly Func<DateTime> _now;
    private readonly List<ManagedDownload> _items = []; private readonly object _gate = new();
    private int _seq;

    public DownloadManager(HttpClient http, string folder, Func<DateTime>? clock = null) { _http = http; _folder = folder; _now = clock ?? (() => DateTime.UtcNow); }

    public event Action? Changed;

    public IReadOnlyList<ManagedDownload> Items { get { lock (_gate) return [.. _items]; } }

    /// <summary>Solo http y https, y solo cuando el usuario lo pide. Devuelve null si la dirección no vale.</summary>
    public ManagedDownload? Add(string url, string? suggestedName = null)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        ManagedDownload d;
        lock (_gate)
        {
            var name = SanitizeFileName(suggestedName) ?? NameFromUrl(uri);
            d = new ManagedDownload("dl" + (++_seq), uri, name, UniquePath(name)) { StartedUtc = _now() };
            _items.Add(d);
        }

        Changed?.Invoke(); Pump();
        return d;
    }

    public void Pause(string id) => With(id, d => { if (d.State == ManagedState.Downloading) { d.PauseRequested = true; d.Cts?.Cancel(); } else if (d.State == ManagedState.Queued) { d.State = ManagedState.Paused; } });

    public void Resume(string id) { With(id, d => { if (d.State == ManagedState.Paused || d.State == ManagedState.Failed) { d.State = ManagedState.Queued; d.Error = null; d.PauseRequested = false; } }); Pump(); }

    public void Cancel(string id) => With(id, d =>
    {
        if (d.State is ManagedState.Completed or ManagedState.Cancelled) return;
        d.CancelRequested = true;
        if (d.State == ManagedState.Downloading) d.Cts?.Cancel(); else { d.State = ManagedState.Cancelled; d.FinishedUtc = _now(); TryDelete(d.PartPath); }
    });

    /// <summary>Quita de la lista una descarga terminada, fallida o cancelada (no borra el archivo descargado).</summary>
    public void Remove(string id) { lock (_gate) _items.RemoveAll(d => d.Id == id && d.State is ManagedState.Completed or ManagedState.Failed or ManagedState.Cancelled); Changed?.Invoke(); }

    private void With(string id, Action<ManagedDownload> act) { ManagedDownload? d; lock (_gate) d = _items.FirstOrDefault(x => x.Id == id); if (d is null) return; lock (_gate) act(d); Changed?.Invoke(); }

    private void Pump()
    {
        List<ManagedDownload> start = [];
        lock (_gate)
        {
            int running = _items.Count(d => d.State == ManagedState.Downloading);
            foreach (var d in _items.Where(d => d.State == ManagedState.Queued).OrderBy(d => d.StartedUtc)) { if (running >= MaxConcurrent) break; d.State = ManagedState.Downloading; d.Cts = new CancellationTokenSource(); running++; start.Add(d); }
        }

        foreach (var d in start) { var item = d; _ = Task.Run(() => RunAsync(item)); }
        if (start.Count > 0) Changed?.Invoke();
    }

    private async Task RunAsync(ManagedDownload d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(d.TargetPath)!);
            long existing = File.Exists(d.PartPath) ? new FileInfo(d.PartPath).Length : 0;
            using var req = new HttpRequestMessage(HttpMethod.Get, d.Url);
            if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, d.Cts!.Token).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0) { TryDelete(d.PartPath); existing = 0; throw new RestartException(); }
            resp.EnsureSuccessStatusCode();
            bool partial = resp.StatusCode == HttpStatusCode.PartialContent;
            if (!partial && existing > 0) { TryDelete(d.PartPath); existing = 0; }   // el servidor no admite reanudar: se empieza de cero
            lock (_gate)
            {
                d.ReceivedBytes = existing;
                d.TotalBytes = partial ? resp.Content.Headers.ContentRange?.Length : resp.Content.Headers.ContentLength;
                if (resp.Content.Headers.ContentDisposition is { } cd && SanitizeFileName(cd.FileNameStar ?? cd.FileName) is { } cdName && existing == 0 && !File.Exists(Path.Combine(_folder, cdName)))
                { d.FileName = cdName; d.TargetPath = Path.Combine(_folder, cdName); d.PartPath = d.TargetPath + ".hatsu-part"; }
            }

            await using var src = await resp.Content.ReadAsStreamAsync(d.Cts.Token).ConfigureAwait(false);
            await using (var dst = new FileStream(d.PartPath, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                var buf = new byte[64 * 1024]; var lastTick = _now(); int n;
                while ((n = await src.ReadAsync(buf, d.Cts.Token).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), d.Cts.Token).ConfigureAwait(false);
                    var now = _now();
                    lock (_gate) { d.ReceivedBytes += n; d.Samples.Add((now, d.ReceivedBytes)); d.Samples.RemoveAll(s => now - s.At > DownloadTracker.RateWindow); d.BytesPerSecond = DownloadTracker.Rate(d.Samples); }
                    if ((now - lastTick).TotalMilliseconds >= 100) { lastTick = now; Changed?.Invoke(); }
                }
            }

            lock (_gate)
            {
                if (d.TotalBytes is { } tb && d.ReceivedBytes < tb) throw new IOException("La conexión se cortó antes de terminar.");
                var final = UniquePathIfTaken(d.TargetPath); File.Move(d.PartPath, final, overwrite: false);
                d.TargetPath = final; d.FileName = Path.GetFileName(final); d.State = ManagedState.Completed; d.FinishedUtc = _now(); d.BytesPerSecond = null; d.TotalBytes ??= d.ReceivedBytes;
            }
        }
        catch (RestartException) { lock (_gate) { d.State = ManagedState.Queued; } }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (d.CancelRequested) { d.State = ManagedState.Cancelled; d.FinishedUtc = _now(); TryDelete(d.PartPath); }
                else d.State = ManagedState.Paused;   // pausa: el archivo parcial se conserva para reanudar
                d.BytesPerSecond = null;
            }
        }
        catch (Exception e)
        {
            lock (_gate) { d.State = ManagedState.Failed; d.FinishedUtc = _now(); d.BytesPerSecond = null; d.Error = e is HttpRequestException hre && hre.StatusCode is { } sc ? $"El servidor respondió {(int)sc}." : e.Message; }
        }
        finally { d.Cts?.Dispose(); d.Cts = null; d.Samples.Clear(); }

        Changed?.Invoke(); Pump();
    }

    private sealed class RestartException : Exception;

    // ───────────── nombres seguros
    public static string? SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Path.GetFileName(name.Trim().Trim('"').Replace('\\', '/').Split('/').Last());   // nunca una ruta
        n = new string(n.Select(c => c < ' ' || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());   // el juego de Windows, también al probar en otro sistema
        n = n.Trim().TrimEnd('.', ' ');
        if (n.Length == 0 || n is "." or "..") return null;
        return n.Length > 150 ? n[..150] : n;
    }

    public static string NameFromUrl(Uri u) => SanitizeFileName(Uri.UnescapeDataString(Path.GetFileName(u.AbsolutePath))) ?? ("descarga-" + u.Host);

    private string UniquePath(string name)
    {
        var path = Path.Combine(_folder, name);
        var taken = _items.Select(i => i.TargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return UniqueIn(path, p => File.Exists(p) || File.Exists(p + ".hatsu-part") || taken.Contains(p));
    }

    private string UniquePathIfTaken(string path) => UniqueIn(path, File.Exists);

    private static string UniqueIn(string path, Func<string, bool> exists)
    {
        if (!exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!; var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++) { var p = Path.Combine(dir, $"{stem} ({i}){ext}"); if (!exists(p)) return p; }
        return Path.Combine(dir, stem + "-" + Guid.NewGuid().ToString("N")[..6] + ext);
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { } }
}
