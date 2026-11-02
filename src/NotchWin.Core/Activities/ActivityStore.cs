namespace NotchWin.Core.Activities;

public enum ActivityKind { Media, Download, Transfer, Capture, Recording, Notification, Extension }

public enum ActivityState { Active, Paused, Completed, Failed, Cancelled }

/// <summary>What the user can do with an activity. The UI only enables an action when the Island has a real implementation for that kind (<see cref="ActivityActions.Supported"/>).</summary>
[Flags]
public enum ActivityAction
{
    None = 0,
    Open = 1,
    ShowInFolder = 2,
    Copy = 4,
    Delete = 8,
    PlayPause = 16,
    Next = 32,
    Previous = 64,
    Pause = 128,
    Resume = 256,
    Cancel = 512,
    RemoveFromHistory = 1024,
    /// <summary>Open the capture in WinMac's editor (only inside WinMac: the editor lives in its bar process).</summary>
    Edit = 2048,
}

/// <summary>When it ends: keep it in the history (downloads, captures) or just drop it (a song that stopped).</summary>
public enum ActivityEnd { KeepInHistory, Discard }

/// <summary>
/// One thing the Island is showing or has shown. Not every field applies to every kind. <see cref="Progress"/> is 0–1, or null when unknown (indeterminate).
/// <see cref="Path"/> is the file it refers to (download, capture) when there is one. <see cref="IsTest"/> marks sample data (CI): the UI says so.
/// </summary>
public sealed record Activity(string Id, ActivityKind Kind, string Title)
{
    public string? Source { get; init; }
    public string? Subtitle { get; init; }
    public string? Path { get; init; }
    public string? ThumbnailPath { get; init; }
    public ActivityState State { get; init; } = ActivityState.Active;
    public double? Progress { get; init; }
    public ActivityAction Actions { get; init; }
    public int Priority { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public ActivityEnd End { get; init; } = ActivityEnd.KeepInHistory;
    public bool IsTest { get; init; }

    public bool IsFinished => State is ActivityState.Completed or ActivityState.Failed or ActivityState.Cancelled;

    /// <summary>Default order in «En curso»: media first, then transfers, captures and the rest.</summary>
    public static int DefaultPriority(ActivityKind k) => k switch
    {
        ActivityKind.Media => 50, ActivityKind.Recording => 45, ActivityKind.Download or ActivityKind.Transfer => 40,
        ActivityKind.Capture => 30, ActivityKind.Notification => 20, _ => 10,
    };
}

public static class ActivityActions
{
    /// <summary>
    /// Actions the Island really implements per kind (and per state). Anything else an activity asks for is NOT shown: no decorative buttons.
    /// Pausing/resuming/cancelling a browser download has no universal Windows API, so downloads only get open / show in folder once finished.
    /// </summary>
    public static ActivityAction Supported(Activity a, bool canEdit = false)
    {
        var s = a.Kind switch
        {
            ActivityKind.Capture when a.Path is not null => ActivityAction.Open | ActivityAction.ShowInFolder | ActivityAction.Copy | ActivityAction.Delete,
            ActivityKind.Download when a.Path is not null && a.State == ActivityState.Completed => ActivityAction.Open | ActivityAction.ShowInFolder,
            ActivityKind.Media when !a.IsTest => ActivityAction.PlayPause | ActivityAction.Next | ActivityAction.Previous,
            _ => ActivityAction.None,
        };
        if (canEdit && a.Kind == ActivityKind.Capture && a.Path is not null) s |= ActivityAction.Edit;
        if (a.IsFinished) s |= ActivityAction.RemoveFromHistory;
        return a.Actions == ActivityAction.None ? s : s & (a.Actions | ActivityAction.RemoveFromHistory | ActivityAction.Edit);
    }
}

/// <summary>
/// The Island's activities: the ones in progress («En curso») and the finished ones («Historial»), for this session only (nothing is written to disk).
/// Pure and single-threaded (the UI thread calls it). Defensive: invalid input is rejected, lists are capped, an activity that stops getting updates
/// for <see cref="StaleAfter"/> is closed as failed (so nothing stays «in progress» forever), and a faulty subscriber cannot break the store.
/// </summary>
public sealed class ActivityStore(Func<DateTime> clockUtc)
{
    public const int MaxCurrent = 24, MaxHistory = 100;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private readonly List<Activity> _current = [];
    private readonly List<Activity> _history = [];

    public event Action? Changed;

    /// <summary>In progress, most important first (priority, then most recently updated).</summary>
    public IReadOnlyList<Activity> Current => _current.OrderByDescending(a => a.Priority).ThenByDescending(a => a.UpdatedUtc).ToList();

    /// <summary>Finished, newest first.</summary>
    public IReadOnlyList<Activity> History => _history;

    public Activity? Find(string id) => _current.FirstOrDefault(a => a.Id == id) ?? _history.FirstOrDefault(a => a.Id == id);

    /// <summary>Registers a new activity or replaces the one with the same id. A finished one goes straight to the history. Returns false if invalid.</summary>
    public bool Upsert(Activity a)
    {
        if (string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Title) || a.Id.Length > 200) return false;
        var now = clockUtc();
        var old = Find(a.Id);
        a = a with
        {
            Title = Clip(a.Title, 120)!, Subtitle = Clip(a.Subtitle, 200), Source = Clip(a.Source, 60),
            Progress = a.Progress is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : null,
            Priority = a.Priority != 0 ? Math.Clamp(a.Priority, -100, 100) : Activity.DefaultPriority(a.Kind),
            CreatedUtc = old?.CreatedUtc ?? (a.CreatedUtc == default ? now : a.CreatedUtc), UpdatedUtc = now,
        };
        _current.RemoveAll(x => x.Id == a.Id);
        if (a.IsFinished) { _history.RemoveAll(x => x.Id == a.Id); AddHistory(a); }
        else
        {
            _current.Add(a);
            while (_current.Count > MaxCurrent) _current.Remove(_current.OrderBy(x => x.Priority).ThenBy(x => x.UpdatedUtc).First());
        }

        Raise();
        return true;
    }

    /// <summary>Changes an activity in progress (progress, subtitle…). Unknown id or a change that throws: nothing happens.</summary>
    public bool Update(string id, Func<Activity, Activity> change)
    {
        var a = _current.FirstOrDefault(x => x.Id == id);
        if (a is null) return false;
        Activity next;
        try { next = change(a) with { Id = a.Id, Kind = a.Kind, CreatedUtc = a.CreatedUtc }; }
        catch (Exception) { return false; }
        return Upsert(next);
    }

    /// <summary>Ends an activity: into the history (or dropped, for <see cref="ActivityEnd.Discard"/>).</summary>
    public bool Finish(string id, ActivityState state = ActivityState.Completed, string? subtitle = null)
    {
        var a = _current.FirstOrDefault(x => x.Id == id);
        if (a is null) return false;
        if (state == ActivityState.Active || state == ActivityState.Paused) state = ActivityState.Completed;
        _current.Remove(a);
        if (a.End == ActivityEnd.KeepInHistory) AddHistory(a with { State = state, Subtitle = subtitle ?? a.Subtitle, UpdatedUtc = clockUtc() });
        Raise();
        return true;
    }

    /// <summary>Removes an activity wherever it is («Eliminar del historial»).</summary>
    public bool Remove(string id)
    {
        int n = _current.RemoveAll(a => a.Id == id) + _history.RemoveAll(a => a.Id == id);
        if (n > 0) Raise();
        return n > 0;
    }

    public void ClearHistory() { if (_history.Count == 0) return; _history.Clear(); Raise(); }

    /// <summary>Closes activities that stopped getting news (their source died): they cannot stay «in progress» forever. Call it from a slow timer.</summary>
    public int ExpireStale()
    {
        var now = clockUtc();
        var stale = _current.Where(a => now - a.UpdatedUtc > StaleAfter && a.Kind != ActivityKind.Media).ToList();
        foreach (var a in stale) { _current.Remove(a); if (a.End == ActivityEnd.KeepInHistory) AddHistory(a with { State = ActivityState.Failed, Subtitle = "Sin noticias de su origen", UpdatedUtc = now }); }
        if (stale.Count > 0) Raise();
        return stale.Count;
    }

    private void AddHistory(Activity a)
    {
        _history.Insert(0, a);
        if (_history.Count > MaxHistory) _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
    }

    private void Raise()
    {
        if (Changed is null) return;
        foreach (var h in Changed.GetInvocationList())
        {
            try { ((Action)h)(); } catch (Exception) { /* a faulty view must not break the store or the other views */ }
        }
    }

    private static string? Clip(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length > max ? s[..(max - 1)] + "…" : s;
    }
}
