namespace NotchWin.Core.Music;

/// <summary>Seek by clicking on the progress bar: where a click at <c>x</c> on a bar <c>width</c> wide lands inside a track of <c>duration</c>. Pure, so it is tested without a window.</summary>
public static class SeekMath
{
    /// <summary>Position for a click, clamped to the track. Null when there is nothing to seek (no duration or a bar that has no width yet).</summary>
    public static TimeSpan? PositionAt(double x, double width, TimeSpan? duration)
    {
        if (duration is not { } d || d <= TimeSpan.Zero || width <= 0 || double.IsNaN(x)) return null;
        return TimeSpan.FromTicks((long)(d.Ticks * Math.Clamp(x / width, 0.0, 1.0)));
    }
}
