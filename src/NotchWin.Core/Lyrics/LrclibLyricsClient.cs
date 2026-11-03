using System.Text.Json;

namespace NotchWin.Core.Lyrics;

/// <summary>
/// WM-008: second source of synced lyrics, LRCLIB (https://lrclib.net: free, open, no account or key). Only asked when NetEase has nothing or refuses,
/// with the same data NetEase receives (title, artist, duration) and only while the user has lyrics turned on. Same matching rules as NetEase:
/// title and artist must match and the duration must be within 20 s; only SYNCED lyrics are accepted (plain text would not follow the song).
/// </summary>
/// The HttpClient carries NotchWin's User-Agent, as LRCLIB asks of its clients.
public sealed class LrclibLyricsClient(HttpClient http)
{
    private static readonly TimeSpan MaxDurationDifference = TimeSpan.FromSeconds(20);

    public async Task<(IReadOnlyList<LyricLine>? Lines, LyricsStatus Status)> LookupAsync(string title, string artist, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return (null, LyricsStatus.NotFound);
        try
        {
            var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title.Trim())}" + (string.IsNullOrWhiteSpace(artist) ? "" : $"&artist_name={Uri.EscapeDataString(artist.Trim())}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 404) return (null, LyricsStatus.NotFound);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, LyricsStatus.NotFound);

            string? best = null; double bestScore = double.MaxValue;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var synced = e.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String ? sl.GetString() : null;
                if (string.IsNullOrWhiteSpace(synced)) continue;
                var name = Clean(e, "trackName"); var who = Clean(e, "artistName");
                if (!Same(name, title) && !Has(name, title) && !Has(title, name)) continue;
                if (!string.IsNullOrWhiteSpace(artist) && !Has(who, artist) && !Has(artist, who)) continue;
                double score = Same(name, title) ? 0 : 1000;
                if (duration is { } wanted && e.TryGetProperty("duration", out var d) && d.TryGetDouble(out var secs) && secs > 0)
                {
                    var diff = Math.Abs(secs - wanted.TotalSeconds);
                    if (diff > MaxDurationDifference.TotalSeconds) continue;
                    score += diff;
                }
                else score += 30;   // entries without a duration are kept, behind any with a close one

                if (score < bestScore) { bestScore = score; best = synced; }
            }

            var lines = best is null ? null : LrcParser.Parse(best);
            return lines is { Count: > 0 } ? (lines, LyricsStatus.Found) : (null, LyricsStatus.NotFound);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, LyricsStatus.Unavailable);
        }
    }

    /// <summary>Some entries carry a byte-order mark or stray spaces in their names (seen in the CI diagnosis for «Fighting Myself»).</summary>
    private static string Clean(JsonElement e, string prop) => (e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "").Replace("﻿", "").Trim();
    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Has(string haystack, string needle) => needle.Trim().Length > 0 && haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
}
