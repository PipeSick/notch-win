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

    /// <summary>
    /// Lowercase text identifying the player the user prefers (see <see cref="MusicPlayerLauncher.SessionToken"/>).
    /// While that app is playing its session is shown, even if Windows considers another app (a browser tab) current.
    /// </summary>
    public string? PreferredAppToken
    {
        get => _preferredToken;
        set
        {
            _preferredToken = value;
            if (_manager is not null)
            {
                AttachToCurrentSession();
            }
        }
    }

    private string? _preferredToken;

    private GlobalSystemMediaTransportControlsSession? PickSession()
    {
        var current = _manager?.GetCurrentSession();
        if (_manager is null || string.IsNullOrEmpty(_preferredToken))
        {
            return current;
        }

        bool IsPreferred(GlobalSystemMediaTransportControlsSession s) =>
            s.SourceAppUserModelId?.Contains(_preferredToken, StringComparison.OrdinalIgnoreCase) == true;

        if (current is not null && IsPreferred(current))
        {
            return current;
        }

        var playing = _manager.GetSessions().FirstOrDefault(s =>
            IsPreferred(s) && s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        return playing ?? current;
    }

    public bool HasSession => _currentSession is not null;

    public Task TogglePlayPauseAsync() => TransportAsync(s => s.TryTogglePlayPauseAsync());

    public Task NextAsync() => TransportAsync(s => s.TrySkipNextAsync());

    public Task PreviousAsync() => TransportAsync(s => s.TrySkipPreviousAsync());

    /// <summary>
    /// It7: a transport command is a REQUEST, not a fact: the UI never assumes it worked. Whatever the answer (accepted, refused, or the player closed meanwhile),
    /// the real state is read again a moment later (players take a while to change, and some never raise the event), so every view ends up showing what the player does.
    /// </summary>
    private async Task TransportAsync(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        var s = _currentSession;
        if (s is null) { AttachToCurrentSession(); return; }   // the session may have appeared since the last event
        try { await command(s); }
        catch (Exception) { AttachToCurrentSession(); }   // the player vanished or rejected the call: resync with whatever Windows has now
        _ = ResyncAfterCommandAsync();
    }

    private async Task ResyncAfterCommandAsync()
    {
        foreach (var delay in PlaybackReconcile.AfterCommandDelaysMs)
        {
            await Task.Delay(delay);
            try { await ResyncAsync(); } catch (Exception) { /* the session went away: the next event/attach says so */ }
        }
    }

    /// <summary>Reads the current state and publishes it only if it differs from what the views were last told.</summary>
    private async Task ResyncAsync()
    {
        var ticket = _gate.Begin();
        var actual = await BuildNowPlayingAsync();
        if (!PlaybackReconcile.Differs(_lastPublished, actual) || !_gate.TryPublish(ticket)) return;
        _lastPublished = actual; Changed?.Invoke(this, actual);
    }

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        AttachToCurrentSession();
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        AttachToCurrentSession();

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
        _currentSession = PickSession();

        if (_currentSession is null)
        {
            var gone = _gate.Begin(); _gate.TryPublish(gone);   // a read still in flight for the previous session must not resurrect it
            _lastPublished = null; Changed?.Invoke(this, null);
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

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        if (!ReferenceEquals(sender, _currentSession)) return;   // a late event of a session we already left must not repaint the views
        _ = RaiseCurrentAsync();
    }

    private readonly PlaybackPublishGate _gate = new();
    private NowPlayingInfo? _lastPublished;

    /// <summary>
    /// It7: reads can finish out of order (the album art is read asynchronously); an older read («playing») finishing after a newer one («paused») used to overwrite the
    /// right state in every view. Each read takes a ticket and only the newest one is published.
    /// </summary>
    private async Task RaiseCurrentAsync()
    {
        var ticket = _gate.Begin();
        var info = await BuildNowPlayingAsync();
        if (!_gate.TryPublish(ticket)) return;
        _lastPublished = info; Changed?.Invoke(this, info);
    }

    private async Task<NowPlayingInfo?> BuildNowPlayingAsync()
    {
        var session = _currentSession;
        if (session is null)
        {
            return null;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var art = props.Thumbnail is null ? null : await ReadThumbnailAsync(props.Thumbnail);
        // It7: the playback state and position are read LAST, after the slow awaits above, so they are the freshest values rather than a snapshot from before the thumbnail download.
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var duration = timeline.EndTime == TimeSpan.Zero ? null : (TimeSpan?)timeline.EndTime;

        return new NowPlayingInfo(
            Title: props.Title ?? string.Empty,
            Artist: props.Artist ?? string.Empty,
            IsPlaying: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Position: timeline.Position,
            Duration: duration,
            SourceAppName: session.SourceAppUserModelId ?? string.Empty,
            AlbumArt: art,
            Album: string.IsNullOrWhiteSpace(props.AlbumTitle) ? null : props.AlbumTitle);
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
            _manager.SessionsChanged -= OnSessionsChanged;
        }
    }
}
