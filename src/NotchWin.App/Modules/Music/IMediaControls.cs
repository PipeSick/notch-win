namespace NotchWin.App.Modules.Music;

/// <summary>Transport commands the notch's mini player sends to whatever app owns the current media session.</summary>
public interface IMediaControls
{
    Task TogglePlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    /// <summary>Jump to a position in the current track (click on the progress bar). The player may refuse it; the views re-read the real position afterwards.</summary>
    Task SeekAsync(TimeSpan position);

    /// <summary>WM-031: there is a media session to send commands to (a player is open, playing or paused).</summary>
    bool HasSession { get; }
}
