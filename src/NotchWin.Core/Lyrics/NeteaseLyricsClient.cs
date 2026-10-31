using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotchWin.Core.Lyrics;

/// <summary>
/// Fallback lyrics source: NetEase Cloud Music's public web endpoints (no account or key). They aren't an
/// official API, so this is best-effort and only runs when lrclib.net has nothing. Matches by title and
/// artist, then by the closest duration; results, including "not found", are cached per track.
/// </summary>
public sealed partial class NeteaseLyricsClient(HttpClient http)
{
    // NetEase's own duration for a track often differs from the player's by several seconds (different masters).
    private static readonly TimeSpan MaxDurationDifference = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, IReadOnlyList<LyricLine>?> _cache = [];

    /// <summary>Credit lines NetEase puts at the very start ("作词 : …", "Producer : …"), which aren't lyrics.</summary>
    [GeneratedRegex(@"^[^:：]{1,24}\s*[:：]\s*\S")]
    private static partial Regex CreditLine();

    public async Task<(IReadOnlyList<LyricLine>? Lines, LyricsStatus Status)> LookupAsync(
        string title, string artist, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        var key = $"{artist}\u0001{title}\u0001{(int)(duration?.TotalSeconds ?? 0)}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return (cached, cached is null ? LyricsStatus.NotFound : LyricsStatus.Found);
        }

        try
        {
            var id = await FindSongIdAsync(title, artist, duration, cancellationToken);
            if (id is null)
            {
                _cache[key] = null;
                return (null, LyricsStatus.NotFound);
            }

            using var request = Get($"https://music.163.com/api/song/lyric?id={id}&lv=1");
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var lrc = doc.RootElement.TryGetProperty("lrc", out var lrcEl)
                      && lrcEl.TryGetProperty("lyric", out var lyricEl)
                      && lyricEl.ValueKind == JsonValueKind.String
                ? lyricEl.GetString()
                : null;

            var lines = string.IsNullOrWhiteSpace(lrc) ? null : Clean(LrcParser.Parse(lrc));
            _cache[key] = lines;
            return (lines, lines is null ? LyricsStatus.NotFound : LyricsStatus.Found);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, LyricsStatus.Unavailable);
        }
    }

    private async Task<long?> FindSongIdAsync(string title, string artist, TimeSpan? duration, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"{artist} {title}".Trim());
        using var request = Get($"https://music.163.com/api/search/get?s={query}&type=1&limit=8");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("songs", out var songs)
            || songs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        long? best = null;
        var bestScore = double.MaxValue;
        foreach (var song in songs.EnumerateArray())
        {
            if (!song.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
            {
                continue;
            }

            var name = song.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
            if (!SameText(name, title) && !Contains(title, name) && !Contains(name, title))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(artist) && !ArtistMatches(song, artist))
            {
                continue;
            }

            // Lower is better: an exact title beats a partial one, then the closest duration wins.
            var score = SameText(name, title) ? 0.0 : 1000.0;
            if (duration is { } wanted && song.TryGetProperty("duration", out var durEl) && durEl.TryGetInt64(out var ms))
            {
                var diff = (TimeSpan.FromMilliseconds(ms) - wanted).Duration();
                if (diff > MaxDurationDifference)
                {
                    continue;
                }

                score += diff.TotalSeconds;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = id;
            }
        }

        return best;
    }

    private static bool ArtistMatches(JsonElement song, string artist)
    {
        if (!song.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
        {
            return true; // nothing to compare against
        }

        foreach (var a in artists.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var el) ? el.GetString() ?? string.Empty : string.Empty;
            if (name.Length > 0 && (Contains(artist, name) || Contains(name, artist)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameText(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string haystack, string needle) =>
        needle.Trim().Length > 0 && haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<LyricLine>? Clean(List<LyricLine> lines)
    {
        // Credits only ever sit at the head of the file, in the first few seconds.
        var start = 0;
        while (start < lines.Count && lines[start].Time < TimeSpan.FromSeconds(5) && CreditLine().IsMatch(lines[start].Text))
        {
            start++;
        }

        var cleaned = lines.Skip(start).Where(l => l.Text.Length > 0).ToList();
        return cleaned.Count > 0 ? cleaned : null;
    }

    private static HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Referer", "https://music.163.com");
        return request;
    }
}
