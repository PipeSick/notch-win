namespace NotchWin.Core.Downloads;

/// <summary>
/// A download in progress. <see cref="BytesPerSecond"/> is MEASURED from how the partial file grows (null until there are enough samples, 0 when it stalls).
/// <see cref="Source"/> is inferred from the partial file's extension (each browser family uses its own). The total size is not known locally, so there is no
/// percentage and no ETA: the Island never invents them.
/// </summary>
public sealed record ActiveDownload(string FileName, long Bytes, double? BytesPerSecond = null, string? Source = null);

/// <summary>
/// Watches a folder for in-progress browser downloads. Browsers write to a temporary name while
/// downloading (Chrome/Edge .crdownload, Firefox .part, others .download/.opdownload) and rename it
/// when done, so the tracker infers "finished" from a partial file that vanished while its final
/// name appeared. The total size isn't known locally, so progress is shown as bytes so far.
/// </summary>
public sealed class DownloadTracker(string folder, Func<DateTime>? clock = null)
{
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly Dictionary<string, Queue<(DateTime At, long Bytes)>> _samples = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Speed window: long enough to smooth bursts, short enough to follow changes.</summary>
    public static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(4);

    /// <summary>Browser family by partial extension (Chromium browsers: .crdownload; Firefox: .part; Opera old: .opdownload; Safari-like: .download).</summary>
    public static string? SourceOf(string ext) => ext.ToLowerInvariant() switch
    {
        ".crdownload" => "Chrome/Edge", ".part" => "Firefox", ".opdownload" => "Opera", ".download" => "Navegador",
        _ => null,
    };

    /// <summary>Bytes per second over the window, from real samples; null with fewer than two samples spanning at least 0.5 s.</summary>
    public static double? Rate(IReadOnlyCollection<(DateTime At, long Bytes)> samples)
    {
        if (samples.Count < 2) return null;
        var first = samples.First(); var last = samples.Last();
        double secs = (last.At - first.At).TotalSeconds;
        if (secs < 0.5) return null;
        return Math.Max(0, (last.Bytes - first.Bytes) / secs);
    }
    private static readonly string[] PartialExtensions = [".crdownload", ".part", ".download", ".opdownload"];

    private readonly Dictionary<string, string> _knownPartials = new(StringComparer.OrdinalIgnoreCase);

    public string Folder { get; } = folder;

    /// <summary>Rescans the folder; returns what is downloading now and which downloads finished since the last call.</summary>
    public (List<ActiveDownload> Active, List<string> Completed) Poll()
    {
        var active = new List<ActiveDownload>();
        var completed = new List<string>();

        if (!Directory.Exists(Folder))
        {
            return (active, completed);
        }

        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(Folder))
            {
                var ext = Path.GetExtension(path);
                if (!PartialExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var finalName = Path.GetFileNameWithoutExtension(path);
                current[path] = finalName;
                long len = SafeLength(path);
                active.Add(new ActiveDownload(finalName, len, Sample(path, len), SourceOf(ext)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (active, completed);
        }

        foreach (var (path, finalName) in _knownPartials)
        {
            if (!current.ContainsKey(path) && File.Exists(Path.Combine(Folder, finalName)))
            {
                completed.Add(finalName);
            }
        }

        foreach (var gone in _samples.Keys.Where(k => !current.ContainsKey(k)).ToList()) _samples.Remove(gone);
        _knownPartials.Clear();
        foreach (var (path, finalName) in current)
        {
            _knownPartials[path] = finalName;
        }

        return (active, completed);
    }

    private double? Sample(string path, long bytes)
    {
        var now = _clock();
        if (!_samples.TryGetValue(path, out var q)) _samples[path] = q = new Queue<(DateTime, long)>();
        if (q.Count > 0 && bytes < q.Last().Bytes) q.Clear();   // the browser restarted the file
        q.Enqueue((now, bytes));
        while (q.Count > 2 && now - q.Peek().At > RateWindow) q.Dequeue();
        return Rate(q);
    }

    /// <summary>«2,4 MB/s».</summary>
    public static string FormatRate(double bytesPerSecond)
    {
        var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        return bytesPerSecond switch
        {
            < 1024 => bytesPerSecond.ToString("0", es) + " B/s",
            < 1024 * 1024 => (bytesPerSecond / 1024).ToString("0", es) + " KB/s",
            _ => (bytesPerSecond / 1048576).ToString("0.0", es) + " MB/s",
        };
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
