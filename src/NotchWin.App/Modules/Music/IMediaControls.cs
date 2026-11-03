namespace NotchWin.App.Modules.Music;

/// <summary>Transport commands the notch's mini player sends to whatever app owns the current media session.</summary>
public interface IMediaControls
{
    Task TogglePlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    /// <summary>WM-031: there is a media session to send commands to (a player is open, playing or paused).</summary>
    bool HasSession { get; }
}
