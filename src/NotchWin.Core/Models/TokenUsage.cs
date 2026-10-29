namespace NotchWin.Core.Models;

/// <summary>
/// Raw token counters for one model's slice of usage. Cache tokens are kept separate from
/// <see cref="Input"/> because providers bill (and should be reported) them at different rates:
/// summing everything into one "input" number silently overstates cost once prompt caching is in play.
/// </summary>
public readonly record struct TokenUsage(
    long Input = 0,
    long Output = 0,
    long CacheWrite = 0,
    long CacheRead = 0)
{
    public long Total => Input + Output + CacheWrite + CacheRead;

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new(
        a.Input + b.Input,
        a.Output + b.Output,
        a.CacheWrite + b.CacheWrite,
        a.CacheRead + b.CacheRead);
}
