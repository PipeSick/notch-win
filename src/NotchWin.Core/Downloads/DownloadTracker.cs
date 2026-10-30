namespace NotchWin.Core.Downloads;

public sealed record ActiveDownload(string FileName, long Bytes);

/// <summary>
/// Watches a folder for in-progress browser downloads. Browsers write to a temporary name while
/// downloading (Chrome/Edge .crdownload, Firefox .part, others .download/.opdownload) and rename it
/// when done, so the tracker infers "finished" from a partial file that vanished while its final
/// name appeared. The total size isn't known locally, so progress is shown as bytes so far.
/// </summary>
public sealed class DownloadTracker(string folder)
{
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
                active.Add(new ActiveDownload(finalName, SafeLength(path)));
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

        _knownPartials.Clear();
        foreach (var (path, finalName) in current)
        {
            _knownPartials[path] = finalName;
        }

        return (active, completed);
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
