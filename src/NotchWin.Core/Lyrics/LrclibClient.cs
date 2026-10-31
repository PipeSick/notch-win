using System.Text.Json;

namespace NotchWin.Core.Lyrics;

/// <summary>How a lyrics lookup ended: lyrics found, the track isn't in the database, or the service couldn't be reached.</summary>
public enum LyricsStatus
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>
/// Looks up synced lyrics on lrclib.net, a free community lyrics database, by title, artist and
/// duration. This is the only NotchWin feature that sends data off the machine (the track's title and
/// artist), which is why the app only uses it when the user turns lyrics on in Settings.
/// Results, including "not found", are cached per track for the life of the process.
/// </summary>
public sealed class LrclibClient(HttpClient http)
{
    private readonly Dictionary<string, IReadOnlyList<LyricLine>?> _cache = [];

    public async Task<IReadOnlyList<LyricLine>?> GetSyncedAsync(
        string title, string artist, TimeSpan? duration, CancellationToken cancellationToken = default) =>
        (await LookupAsync(title, artist, duration, cancellationToken)).Lines;

    /// <summary>Like <see cref="GetSyncedAsync"/>, but also says why there are no lines, so the UI can tell "no lyrics" from "service down".</summary>
    public async Task<(IReadOnlyList<LyricLine>? Lines, LyricsStatus Status)> LookupAsync(
        string title, string artist, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        var key = $"{artist}\u0001{title}\u0001{(int)(duration?.TotalSeconds ?? 0)}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return (cached, cached is null ? LyricsStatus.NotFound : LyricsStatus.Found);
        }

        var url = "https://lrclib.net/api/get"
                  + $"?track_name={Uri.EscapeDataString(title)}"
                  + $"&artist_name={Uri.EscapeDataString(artist)}"
                  + (duration is { } d ? $"&duration={(int)Math.Round(d.TotalSeconds)}" : string.Empty);

        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _cache[key] = null;
                return (null, LyricsStatus.NotFound);
            }

            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var synced = doc.RootElement.TryGetProperty("syncedLyrics", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

            var lines = string.IsNullOrWhiteSpace(synced) ? null : LrcParser.Parse(synced);
            var result = lines is { Count: > 0 } ? lines : null;
            _cache[key] = result;
            return (result, result is null ? LyricsStatus.NotFound : LyricsStatus.Found);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline or a transient server error: don't cache, so a later attempt retries.
            return (null, LyricsStatus.Unavailable);
        }
    }
}
