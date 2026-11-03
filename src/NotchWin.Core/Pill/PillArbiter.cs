namespace NotchWin.Core.Pill;

/// <summary>What the collapsed notch can show for a moment on top of its usual content (the track or the idle pill).</summary>
public enum PillKind
{
    /// <summary>System volume changed (level 0–100, or muted).</summary>
    Volume,
    /// <summary>Screen brightness changed (level 0–100 of the display that changed).</summary>
    Brightness,
    /// <summary>A Windows notification relayed by WinMac's top bar.</summary>
    Notification,
    /// <summary>NotchWin's own events: an agent finished, the timer ran out, a download completed.</summary>
    Activity,
    /// <summary>A screenshot was saved: its thumbnail (<see cref="PillItem.ImagePath"/>) and where it went.</summary>
    Capture,
    /// <summary>A new track started.</summary>
    Track,
    /// <summary>WM-038: an iPhone call (WinMac Connect). While it rings it stays (it does not expire like a notification); missed or ended, a few seconds.</summary>
    Call,
}

/// <summary>
/// One transient item. <see cref="Level"/> is only for the volume/brightness indicators. <see cref="Icon"/> is raw 32-bit BGRA premultiplied pixels
/// (<see cref="IconWidth"/> × <see cref="IconHeight"/>), as WinMac reads them from Windows; null when the app gave none (the UI draws the initial).
/// </summary>
public sealed record PillItem(PillKind Kind, string Title, string? Subtitle = null, int Level = 0, bool Muted = false, byte[]? Icon = null, int IconWidth = 0, int IconHeight = 0)
{
    /// <summary>The app a notification came from (its initial is drawn when there is no icon).</summary>
    public string? App { get; init; }

    /// <summary>WM-025: identity of a Windows notification («win:&lt;id&gt;»), so a click on the pill opens that same notification in full.</summary>
    public string? NotificationKey { get; init; }

    /// <summary>How long this item stays, when it is not the kind's usual time (WM-038: a ringing call stays until it ends).</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>WM-038: not something to show but «take the current item of this kind away» (the call ended).</summary>
    public bool Withdraw { get; init; }

    /// <summary>Image file shown as a small thumbnail (a saved screenshot). Decoded at thumbnail size, never in full.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Glyph for the indicator in Segoe Fluent Icons: speaker by level, crossed speaker when muted, sun for brightness.</summary>
    public string Glyph => Kind switch
    {
        PillKind.Volume when Muted || Level == 0 => "",
        PillKind.Volume when Level < 34 => "",
        PillKind.Volume when Level < 67 => "",
        PillKind.Volume => "",
        PillKind.Brightness => "",
        _ => "",
    };

    /// <summary>"65 %" for the indicators; muted reads "Silencio" so it is not mistaken for 0 %.</summary>
    public string LevelText => Kind == PillKind.Volume && Muted ? "Silencio" : Level + " %";

    /// <summary>Two items with the same key are the same thing (used to drop repeats).</summary>
    public string Key => Kind is PillKind.Volume or PillKind.Brightness ? Kind.ToString() : Kind + "\u001f" + Title + "\u001f" + Subtitle;
}

/// <summary>How one kind behaves. <paramref name="Coalesce"/>: a new item of this kind replaces the one on screen (or waiting) instead of queueing behind it.
/// <paramref name="Resumable"/>: if something more important interrupts it, it comes back afterwards with the time it had left.</summary>
public sealed record PillRule(int Priority, TimeSpan Duration, bool Coalesce, bool Resumable, bool Enabled = true);

/// <summary>
/// All the timings and priorities of the collapsed notch in one place. Built from the user's settings (<see cref="FromSettings"/>), so no other code holds
/// its own durations.
/// </summary>
public sealed class PillPolicy
{
    private readonly Dictionary<PillKind, PillRule> _rules;

    public PillPolicy(IReadOnlyDictionary<PillKind, PillRule> rules) { _rules = new(rules); }

    /// <summary>An interrupted item never comes back for less than this (a flash of a few ms would read as a glitch).</summary>
    public TimeSpan MinResume { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>The same notification (app + title + text) arriving again within this window is shown once.</summary>
    public TimeSpan RepeatWindow { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>At most this many items wait; beyond it the oldest of the least important kind is dropped (and counted).</summary>
    public int QueueLimit { get; init; } = 6;

    public PillRule Rule(PillKind kind) => _rules.TryGetValue(kind, out var r) ? r : new PillRule(0, TimeSpan.FromSeconds(3), false, false, false);

    /// <summary>
    /// Volume and brightness come first (the user is acting right now and needs the feedback), then Windows notifications, NotchWin's own events and a new track.
    /// Volume and brightness interrupt a notification without losing it: it resumes with the time it had left.
    /// </summary>
    public static PillPolicy FromSettings(bool volume = true, bool brightness = true, bool notifications = true, int hudSeconds = 2, int notifySeconds = 5) => new(new Dictionary<PillKind, PillRule>
    {
        [PillKind.Volume] = new(40, TimeSpan.FromSeconds(Math.Clamp(hudSeconds, 1, 10)), Coalesce: true, Resumable: false, volume),
        [PillKind.Brightness] = new(40, TimeSpan.FromSeconds(Math.Clamp(hudSeconds, 1, 10)), Coalesce: true, Resumable: false, brightness),
        [PillKind.Notification] = new(30, TimeSpan.FromSeconds(Math.Clamp(notifySeconds, 2, 30)), Coalesce: false, Resumable: true, notifications),
        [PillKind.Capture] = new(25, TimeSpan.FromSeconds(3), Coalesce: false, Resumable: true),
        [PillKind.Activity] = new(20, TimeSpan.FromMilliseconds(3200), Coalesce: false, Resumable: true),
        [PillKind.Track] = new(10, TimeSpan.FromMilliseconds(3200), Coalesce: true, Resumable: false),
        // WM-038: above everything (a ringing phone matters more than a volume change); a new state of the call replaces the one on screen
        [PillKind.Call] = new(50, TimeSpan.FromSeconds(95), Coalesce: true, Resumable: true, notifications),
    });
}

/// <summary>
/// Decides what the collapsed notch shows. One item at a time; the rest wait by priority (first come, first served within a priority). When nothing is pending
/// <see cref="Current"/> is null and the notch shows its usual content again (the playing track, or the idle pill). Pure and single-threaded: the UI thread calls
/// <see cref="Post"/> and <see cref="Tick"/> and schedules its timer at <see cref="NextDeadline"/>.
/// </summary>
public sealed class PillArbiter
{
    private sealed record Entry(PillItem Item, TimeSpan Duration, long Seq);

    private readonly Func<DateTime> _clock;
    private readonly List<Entry> _queue = [];
    private readonly Dictionary<string, DateTime> _recent = [];
    private Entry? _current;
    private DateTime _until;
    private TimeSpan _pausedLeft;
    private bool _paused;
    private long _seq;

    public PillArbiter(PillPolicy policy, Func<DateTime> clockUtc) { Policy = policy; _clock = clockUtc; }

    /// <summary>Settings changed: kinds that were switched off are removed at once.</summary>
    public PillPolicy Policy { get; private set; }

    /// <summary>The item on screen, or null for the usual content.</summary>
    public PillItem? Current => _paused ? null : _current?.Item;

    /// <summary>When <see cref="Current"/> ends (UTC), or null when nothing is timed.</summary>
    public DateTime? NextDeadline => _current is null || _paused ? null : _until;

    public int Pending => _queue.Count;

    /// <summary>Items dropped because the queue was full (diagnostics).</summary>
    public int Dropped { get; private set; }

    /// <summary>Raised whenever <see cref="Current"/> changes or the item on screen is updated in place.</summary>
    public event Action? Changed;

    public void SetPolicy(PillPolicy policy)
    {
        Policy = policy;
        _queue.RemoveAll(e => !policy.Rule(e.Item.Kind).Enabled);
        if (_current is not null && !policy.Rule(_current.Item.Kind).Enabled) { _current = null; Advance(); Changed?.Invoke(); }
    }

    /// <summary>Offers an item. Returns false when it is ignored (its kind is off, or it repeats one just shown).</summary>
    public bool Post(PillItem item)
    {
        var rule = Policy.Rule(item.Kind);
        if (!rule.Enabled) return false;
        var now = _clock();
        Prune(now);
        if (!rule.Coalesce)
        {
            if (_current?.Item.Key == item.Key || _queue.Any(e => e.Item.Key == item.Key) || _recent.ContainsKey(item.Key)) return false;
            _recent[item.Key] = now;
        }

        var entry = new Entry(item, item.Duration ?? rule.Duration, ++_seq);
        if (_paused)
        {
            if (rule.Resumable) Enqueue(entry);
            return true;   // volume/brightness while the notch is open: the open notch has its own controls, the indicator would be stale later
        }

        if (_current is null) { Show(entry, now); return true; }
        if (rule.Coalesce && _current.Item.Kind == item.Kind) { Show(entry, now); return true; }   // e.g. holding the volume key: same indicator, new level, timer restarts

        var currentRule = Policy.Rule(_current.Item.Kind);
        if (rule.Priority > currentRule.Priority)
        {
            if (currentRule.Resumable) Requeue(_current, _until - now);
            Show(entry, now);
            return true;
        }

        if (rule.Coalesce) _queue.RemoveAll(e => e.Item.Kind == item.Kind);
        Enqueue(entry);
        return true;
    }

    /// <summary>
    /// Call when the timer fires (or at any time): moves on if the item on screen is over. A timer that fires a little EARLY changes nothing here, so the caller
    /// must always schedule again with <see cref="NextWait"/> afterwards (otherwise the pill would stay on the same item forever).
    /// </summary>
    public void Tick()
    {
        if (_paused || _current is null || _clock() < _until) return;
        _current = null;
        Advance();
        Changed?.Invoke();
    }

    /// <summary>Shortest timer interval: a deadline that is (almost) due is checked again after this, never with a zero or negative interval.</summary>
    public static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(15);

    /// <summary>How long until the timer must fire again (null = nothing timed). Always positive, so an early or late timer is simply rescheduled.</summary>
    public TimeSpan? NextWait()
    {
        if (NextDeadline is not { } at) return null;
        var wait = at - _clock();
        return wait > MinWait ? wait : MinWait;
    }

    /// <summary>The item on screen is past its deadline (the UI's watchdog uses this to recover if a timer was lost).</summary>
    public bool Overdue => NextDeadline is { } at && _clock() >= at;

    /// <summary>The notch opened (true) or closed (false). While open nothing is shown in the pill and time stands still for notifications; indicators are dropped.</summary>
    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        var now = _clock();
        if (paused)
        {
            _paused = true;
            if (_current is not null)
            {
                if (Policy.Rule(_current.Item.Kind).Resumable) _pausedLeft = Max(_until - now, Policy.MinResume); else _current = null;
            }

            _queue.RemoveAll(e => !Policy.Rule(e.Item.Kind).Resumable);
            return;
        }

        _paused = false;
        if (_current is not null) _until = now + _pausedLeft; else Advance();
        Changed?.Invoke();
    }

    /// <summary>WM-038: the item of this kind on screen or waiting goes away now (the call ended without a «missed» to show).</summary>
    public void Withdraw(PillKind kind)
    {
        _queue.RemoveAll(e => e.Item.Kind == kind);
        if (_current?.Item.Kind != kind) return;
        _current = null; if (!_paused) Advance();
        Changed?.Invoke();
    }

    /// <summary>Forget everything (the notch is being rebuilt or the module turned off).</summary>
    public void Clear()
    {
        bool had = _current is not null;
        _current = null; _queue.Clear(); _recent.Clear(); _paused = false;
        if (had) Changed?.Invoke();
    }

    private void Show(Entry e, DateTime now)
    {
        _current = e; _until = now + e.Duration;
        Changed?.Invoke();
    }

    private void Advance()
    {
        if (_queue.Count == 0) return;
        var next = _queue.OrderByDescending(e => Policy.Rule(e.Item.Kind).Priority).ThenBy(e => e.Seq).First();
        _queue.Remove(next);
        _current = next; _until = _clock() + next.Duration;
    }

    private void Requeue(Entry e, TimeSpan left) => Enqueue(e with { Duration = Max(left, Policy.MinResume) });

    private void Enqueue(Entry e)
    {
        _queue.Add(e);
        while (_queue.Count > Math.Max(1, Policy.QueueLimit))
        {
            var drop = _queue.OrderBy(x => Policy.Rule(x.Item.Kind).Priority).ThenBy(x => x.Seq).First();
            _queue.Remove(drop); Dropped++;
        }
    }

    private void Prune(DateTime now)
    {
        if (_recent.Count == 0) return;
        foreach (var k in _recent.Where(p => now - p.Value >= Policy.RepeatWindow).Select(p => p.Key).ToList()) _recent.Remove(k);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
