namespace NotchWin.Core.Timers;

/// <summary>
/// Wall-clock countdown for the notch's timer tab. Remaining time is derived from an end instant
/// rather than decremented per tick, so a late or skipped UI tick never makes it drift.
/// </summary>
public sealed class CountdownTimer(Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset _endsAt;
    private TimeSpan _pausedRemaining;

    public TimeSpan Total { get; private set; } = TimeSpan.FromMinutes(25);

    public bool IsRunning { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (!IsRunning)
            {
                return _pausedRemaining == TimeSpan.Zero && !_finished ? Total : _pausedRemaining;
            }

            var left = _endsAt - _clock();
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    /// <summary>Fraction of the countdown still left, 1 → 0, for the progress ring.</summary>
    public double FractionLeft => Total <= TimeSpan.Zero ? 0 : Math.Clamp(Remaining / Total, 0, 1);

    private bool _finished;

    public void SetDuration(TimeSpan total)
    {
        Total = total;
        _pausedRemaining = TimeSpan.Zero;
        _finished = false;
        IsRunning = false;
    }

    public void Start()
    {
        var left = Remaining;
        if (left <= TimeSpan.Zero)
        {
            left = Total;
        }

        _finished = false;
        _endsAt = _clock() + left;
        IsRunning = true;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _pausedRemaining = Remaining;
        IsRunning = false;
    }

    public void Reset()
    {
        _pausedRemaining = TimeSpan.Zero;
        _finished = false;
        IsRunning = false;
    }

    /// <summary>Call on every UI tick; returns true exactly once, when a running countdown reaches zero.</summary>
    public bool TryComplete()
    {
        if (!IsRunning || Remaining > TimeSpan.Zero)
        {
            return false;
        }

        IsRunning = false;
        _pausedRemaining = TimeSpan.Zero;
        _finished = true;
        return true;
    }
}
