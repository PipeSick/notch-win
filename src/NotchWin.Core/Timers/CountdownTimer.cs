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

    /// <summary>The countdown reached zero and has not been started or reset since (V3: the «finished» state of the timer view).</summary>
    public bool IsFinished => _finished;

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

    /// <summary>V3: the countdown is «in play»: running, or paused with time left. Cancelled, reset or finished timers are not armed.</summary>
    public bool IsArmed => IsRunning || (_pausedRemaining > TimeSpan.Zero && !_finished);

    /// <summary>V3 «Reiniciar»: back to the full time and running again (unlike <see cref="Reset"/>, which discards the countdown).</summary>
    public void Restart()
    {
        _finished = false;
        _pausedRemaining = TimeSpan.Zero;
        _endsAt = _clock() + Total;
        IsRunning = true;
    }

    /// <summary>V3 «Cancelar»: stop and discard; the timer goes back to «ready» at the chosen time. Same effect as <see cref="Reset"/>, with the V3 name.</summary>
    public void Cancel() => Reset();

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
