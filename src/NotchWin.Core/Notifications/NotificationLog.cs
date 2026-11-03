namespace NotchWin.Core.Notifications;

/// <param name="Key">Stable identity: «win:&lt;id&gt;» for a notification Windows still holds (sent by WinMac's bar), null for NotchWin's own events.</param>
/// <param name="AppId">The sending app's AUMID when Windows gives it (to open the app).</param>
public sealed record NotchNotification(string Source, string Title, string Message, DateTimeOffset At, string? Key = null, string? AppId = null);

/// <summary>
/// History of the notch's Notifications tab. WM-025: it shows two things together, newest first:
/// <list type="bullet">
/// <item>what Windows' notification centre holds now — WinMac's bar reads it with the public UserNotificationListener API (this unpackaged app cannot) and sends a
/// snapshot whenever it changes (<see cref="SetWindows"/>);</item>
/// <item>what NotchWin itself observed (an agent finishing, the timer, a finished download).</item>
/// </list>
/// «Limpiar» hides them in the Island only: deleting from Windows is done (and confirmed) from WinMac's notification centre, never pretended here.
/// </summary>
public sealed class NotificationLog(int capacity = 50)
{
    private readonly List<NotchNotification> _own = [];
    private IReadOnlyList<NotchNotification> _windows = [];
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);

    public event Action<NotchNotification>? Added;

    public event Action? Changed;

    /// <summary>Newest first: Windows' current notifications (minus the ones cleared here) and NotchWin's own events.</summary>
    public IReadOnlyList<NotchNotification> Items =>
        _windows.Where(n => n.Key is null || !_hidden.Contains(n.Key)).Concat(_own).OrderByDescending(n => n.At).Take(capacity).ToList();

    public void Add(string source, string title, string message, DateTimeOffset? at = null)
    {
        var item = new NotchNotification(source, title, message, at ?? DateTimeOffset.Now);
        _own.Insert(0, item);
        if (_own.Count > capacity)
        {
            _own.RemoveRange(capacity, _own.Count - capacity);
        }

        Added?.Invoke(item);
        Changed?.Invoke();
    }

    /// <summary>Replaces the Windows part with a fresh snapshot. No «Added» (the bar already announced new ones as pills); «Changed» only if something changed.</summary>
    public void SetWindows(IReadOnlyList<NotchNotification> snapshot)
    {
        if (snapshot.Count == _windows.Count && snapshot.Zip(_windows).All(p => p.First == p.Second))
        {
            return;
        }

        _windows = snapshot.ToList();
        _hidden.IntersectWith(_windows.Where(n => n.Key is not null).Select(n => n.Key!));   // forget hidden keys Windows no longer has
        Changed?.Invoke();
    }

    public NotchNotification? Find(string key) => _windows.FirstOrDefault(n => n.Key == key);

    /// <summary>Clears the Island's list: NotchWin's own events go; Windows' are hidden here (they stay in Windows).</summary>
    public void Clear()
    {
        _own.Clear();
        foreach (var n in _windows)
        {
            if (n.Key is not null)
            {
                _hidden.Add(n.Key);
            }
        }

        Changed?.Invoke();
    }
}
