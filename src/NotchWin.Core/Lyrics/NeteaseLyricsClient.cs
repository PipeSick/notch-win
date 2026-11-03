using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotchWin.Core.Lyrics;

/// <summary>How a lyrics lookup ended: lyrics found, the track isn't in the database, or the service couldn't be reached.</summary>
public enum LyricsStatus
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>
/// Synced lyrics from NetEase Cloud Music's public web endpoints (no account or key). They aren't an
/// official API, so this is best-effort. This is the only NotchWin feature that sends data off the
/// machine (the track's title and artist), which is why it only runs when the user turns lyrics on.
/// Matches by title and artist, then by the closest duration; results, including "not found", are
/// cached per track for the life of the process.
/// </summary>
public sealed partial class NeteaseLyricsClient(HttpClient http, LrclibLyricsClient? fallback = null)
{
    /// <summary>
    /// WM-008: how the last lookup was decided, WITHOUT the title or artist (safe for the log): candidates, which filter rejected them, the source used.
    /// </summary>
    public string LastDiagnosis { get; private set; } = "";

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

        var (lines, status, why) = await NeteaseAsync(title, artist, duration, cancellationToken);
        // WM-008: Spotify and other players often add « - Remastered 2007», «(feat. …)» or «[Live]»: the bare title is tried before giving up.
        var bare = BareTitle(title);
        if (lines is null && status != LyricsStatus.Unavailable && !SameText(bare, title))
        {
            var (l2, s2, w2) = await NeteaseAsync(bare, artist, duration, cancellationToken);
            why += " · título sin sufijos: " + w2; (lines, status) = (l2, s2);
        }

        // Second source only when NetEase has nothing or refused: the engine, its cache and its view stay the same.
        if (lines is null && fallback is not null)
        {
            var (l3, s3) = await fallback.LookupAsync(bare, artist, duration, cancellationToken);
            why += " · LRCLIB: " + (s3 == LyricsStatus.Found ? "letra sincronizada" : s3 == LyricsStatus.Unavailable ? "no disponible" : "sin letra sincronizada");
            if (l3 is not null) { lines = Clean(l3.ToList()); status = lines is null ? LyricsStatus.NotFound : LyricsStatus.Found; }
            else if (status != LyricsStatus.Unavailable) status = s3;
        }

        LastDiagnosis = why;
        if (status != LyricsStatus.Unavailable)
        {
            _cache[key] = lines;   // a refusal or a network error is not «not found»: it is tried again later
        }

        return (lines, status);
    }

    private async Task<(IReadOnlyList<LyricLine>? Lines, LyricsStatus Status, string Why)> NeteaseAsync(string title, string artist, TimeSpan? duration, CancellationToken cancellationToken)
    {
        try
        {
            var (id, why) = await FindSongIdAsync(title, artist, duration, cancellationToken);
            if (id is null)
            {
                return (null, why.StartsWith("NetEase rechazó", StringComparison.Ordinal) ? LyricsStatus.Unavailable : LyricsStatus.NotFound, why);
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
            return (lines, lines is null ? LyricsStatus.NotFound : LyricsStatus.Found, why + (lines is null ? " · la canción no tiene letra sincronizada" : $" · {lines.Count} líneas"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, LyricsStatus.Unavailable, "NetEase no disponible: " + ex.GetType().Name);
        }
    }

    /// <summary>The title without the decorations players add: « - Remastered 2007», « - Live», «(feat. X)», «[Explicit]».</summary>
    public static string BareTitle(string title)
    {
        var t = DecorationSuffix().Replace(title, "");
        t = Decoration().Replace(t, "");
        return t.Trim().Length == 0 ? title.Trim() : t.Trim();
    }

    [GeneratedRegex(@"\s+-\s+(?:.*\b(?:remaster(?:ed)?|live|version|versión|mono|stereo|edit|mix|acoustic|demo|bonus)\b.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DecorationSuffix();

    [GeneratedRegex(@"\s*[\(\[](?:feat\.?|ft\.?|with|con|explicit|clean|live|en vivo|remaster(?:ed)?[^\)\]]*|\d{4}\s+remaster[^\)\]]*)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Decoration();

    private async Task<(long? Id, string Why)> FindSongIdAsync(string title, string artist, TimeSpan? duration, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"{artist} {title}".Trim());
        using var request = Get($"https://music.163.com/api/search/get?s={query}&type=1&limit=8");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var code) && code != 200)
        {
            return (null, $"NetEase rechazó la búsqueda (código {code})");   // e.g. -460 «Cheating» from some networks: not the same as «no such song»
        }

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("songs", out var songs)
            || songs.ValueKind != JsonValueKind.Array)
        {
            return (null, "NetEase: 0 candidatos");
        }

        int total = 0, byTitle = 0, byArtist = 0, byDuration = 0;
        long? best = null;
        var bestScore = double.MaxValue;
        foreach (var song in songs.EnumerateArray())
        {
            if (!song.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
            {
                continue;
            }

            total++;

            var name = song.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
            if (!SameText(name, title) && !Contains(title, name) && !Contains(name, title))
            {
                continue;
            }

            byTitle++;
            if (!string.IsNullOrWhiteSpace(artist) && !ArtistMatches(song, artist))
            {
                continue;
            }

            byArtist++;

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

            byDuration++;

            if (score < bestScore)
            {
                bestScore = score;
                best = id;
            }
        }

        return (best, $"NetEase: {total} candidatos, {byTitle} por título, {byArtist} por artista, {byDuration} por duración{(duration is null ? " (sin duración del reproductor)" : "")}");
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

    internal static IReadOnlyList<LyricLine>? Clean(List<LyricLine> lines)
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
