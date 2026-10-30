namespace NotchWin.App.Modules.Music;

/// <summary>Transport commands the notch's mini player sends to whatever app owns the current media session.</summary>
public interface IMediaControls
{
    Task TogglePlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();
}
