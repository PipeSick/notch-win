using NotchWin.Core.Music;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NotchWin.App.Modules.Music;

/// <summary>
/// Now-playing source backed by Windows' built-in System Media Transport Controls session
/// manager. This already covers Spotify, the Windows Media Player, browsers, and anything else
/// that registers a media session with the OS — no per-app integration (and no Spotify Web API
/// OAuth flow) needed for the common case.
/// </summary>
public sealed class GsmtcMediaProvider : INowPlayingSource, IMediaControls, IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    public event EventHandler<NowPlayingInfo?>? Changed;

    public async Task TogglePlayPauseAsync()
    {
        if (_currentSession is { } s) { await s.TryTogglePlayPauseAsync(); }
    }

    public async Task NextAsync()
    {
        if (_currentSession is { } s) { await s.TrySkipNextAsync(); }
    }

    public async Task PreviousAsync()
    {
        if (_currentSession is { } s) { await s.TrySkipPreviousAsync(); }
    }

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        AttachToCurrentSession();
    }

    public async Task<NowPlayingInfo?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null)
        {
            await InitializeAsync();
        }

        return await BuildNowPlayingAsync();
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        AttachToCurrentSession();

    private void AttachToCurrentSession()
    {
        DetachFromCurrentSession();
        _currentSession = _manager?.GetCurrentSession();

        if (_currentSession is null)
        {
            Changed?.Invoke(this, null);
            return;
        }

        _currentSession.MediaPropertiesChanged += OnSessionChanged;
        _currentSession.PlaybackInfoChanged += OnSessionChanged;
        _currentSession.TimelinePropertiesChanged += OnSessionChanged;

        _ = RaiseCurrentAsync();
    }

    private void DetachFromCurrentSession()
    {
        if (_currentSession is null)
        {
            return;
        }

        _currentSession.MediaPropertiesChanged -= OnSessionChanged;
        _currentSession.PlaybackInfoChanged -= OnSessionChanged;
        _currentSession.TimelinePropertiesChanged -= OnSessionChanged;
        _currentSession = null;
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RaiseCurrentAsync();

    private async Task RaiseCurrentAsync() => Changed?.Invoke(this, await BuildNowPlayingAsync());

    private async Task<NowPlayingInfo?> BuildNowPlayingAsync()
    {
        var session = _currentSession;
        if (session is null)
        {
            return null;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        var art = props.Thumbnail is null ? null : await ReadThumbnailAsync(props.Thumbnail);
        var duration = timeline.EndTime == TimeSpan.Zero ? null : (TimeSpan?)timeline.EndTime;

        return new NowPlayingInfo(
            Title: props.Title ?? string.Empty,
            Artist: props.Artist ?? string.Empty,
            IsPlaying: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Position: timeline.Position,
            Duration: duration,
            SourceAppName: session.SourceAppUserModelId ?? string.Empty,
            AlbumArt: art);
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference thumbnail)
    {
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0)
            {
                return null;
            }

            using var reader = new DataReader(stream);
            var size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception)
        {
            // The thumbnail stream is owned by whatever app registered the media session; a
            // misbehaving source app failing here must not take down the now-playing display.
            return null;
        }
    }

    public void Dispose()
    {
        DetachFromCurrentSession();
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }
}
