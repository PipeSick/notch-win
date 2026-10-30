namespace NotchWin.Core.Notifications;

public sealed record NotchNotification(string Source, string Title, string Message, DateTimeOffset At);

/// <summary>
/// In-app notification history for the notch's notifications tab. It records what NotchWin itself
/// observed (an agent finishing a task, the timer, a finished download). Reading other apps'
/// Windows toast notifications needs an app package identity this unpackaged app doesn't have.
/// </summary>
public sealed class NotificationLog(int capacity = 50)
{
    private readonly List<NotchNotification> _items = [];

    public event Action<NotchNotification>? Added;

    public event Action? Changed;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<NotchNotification> Items => _items;

    public void Add(string source, string title, string message, DateTimeOffset? at = null)
    {
        var item = new NotchNotification(source, title, message, at ?? DateTimeOffset.Now);
        _items.Insert(0, item);
        if (_items.Count > capacity)
        {
            _items.RemoveRange(capacity, _items.Count - capacity);
        }

        Added?.Invoke(item);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _items.Clear();
        Changed?.Invoke();
    }
}
