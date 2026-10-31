namespace NotchWin.Core.Lyrics;

/// <summary>
/// Finds synced lyrics from lrclib.net first and falls back to NetEase when lrclib has nothing or is down.
/// "Unavailable" is only reported when no source could give a definite answer.
/// </summary>
public sealed class LyricsProvider(LrclibClient primary, NeteaseLyricsClient? fallback = null)
{
    public async Task<(IReadOnlyList<LyricLine>? Lines, LyricsStatus Status)> LookupAsync(
        string title, string artist, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        var first = await primary.LookupAsync(title, artist, duration, cancellationToken);
        if (first.Status == LyricsStatus.Found || fallback is null)
        {
            return first;
        }

        var second = await fallback.LookupAsync(title, artist, duration, cancellationToken);
        if (second.Status == LyricsStatus.Found)
        {
            return second;
        }

        // If either source couldn't be reached we can't claim the track has no lyrics.
        var status = first.Status == LyricsStatus.Unavailable || second.Status == LyricsStatus.Unavailable
            ? LyricsStatus.Unavailable
            : LyricsStatus.NotFound;
        return (null, status);
    }
}
