namespace NotchWin.Core.Models;

/// <summary>
/// A single rate/plan-limit window (e.g. Codex's rolling 5h + weekly windows, or an Anthropic
/// Admin API rate-limit bucket). Not every provider exposes this locally, so callers always get
/// it wrapped in a nullable <see cref="AgentUsageSnapshot"/> field rather than a required one.
/// </summary>
public sealed record PlanLimitInfo(
    string Label,
    double UsedPercent,
    double? WindowMinutes,
    DateTimeOffset? ResetsAt);
