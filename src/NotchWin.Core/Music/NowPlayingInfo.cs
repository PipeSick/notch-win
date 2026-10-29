namespace NotchWin.Core.Music;

/// <summary>Platform-agnostic snapshot of whatever is currently playing, for the notch's mini player.</summary>
public sealed record NowPlayingInfo(
    string Title,
    string Artist,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan? Duration,
    string SourceAppName,
    byte[]? AlbumArt);

/// <summary>Implemented per-platform (WinUI app uses GSMTC); Core only knows about the shape of the data.</summary>
public interface INowPlayingSource
{
    event EventHandler<NowPlayingInfo?>? Changed;

    Task<NowPlayingInfo?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
