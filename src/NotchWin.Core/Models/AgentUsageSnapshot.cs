namespace NotchWin.Core.Models;

/// <summary>
/// Normalized, point-in-time usage reading for one agent account (a CLI home directory / profile).
/// A single provider can return several of these (e.g. Codex with more than one CODEX_HOME).
/// </summary>
public sealed record AgentUsageSnapshot
{
    public required AgentKind Kind { get; init; }

    /// <summary>Account e-mail shown on the card, when the agent's local config names one (never a token).</summary>
    public string? AccountEmail { get; init; }

    /// <summary>Subscription plan badge such as "Pro", "Max" or "Plus", when known.</summary>
    public string? PlanName { get; init; }

    /// <summary>Human-readable account/profile label, e.g. "default" or a CODEX_HOME path leaf.</summary>
    public required string AccountLabel { get; init; }

    /// <summary>True when the agent appears to be mid-turn right now (recently touched session file).</summary>
    public bool IsLiveWorking { get; init; }

    public DateTimeOffset? LastActivityUtc { get; init; }

    /// <summary>Distinct model ids seen in the aggregation window, most recent first.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>Tokens spent today (local day) across all sessions for this account.</summary>
    public TokenUsage TokensToday { get; init; }

    /// <summary>Tokens spent in the currently active/most recent session, if one is live.</summary>
    public TokenUsage TokensActiveSession { get; init; }

    /// <summary>Best-effort USD estimate for <see cref="TokensToday"/>, using <see cref="Pricing.PricingCatalog"/>.</summary>
    public decimal? EstimatedCostUsdToday { get; init; }

    /// <summary>Plan/rate-limit windows, when the local data exposes them (e.g. Codex rate_limits).</summary>
    public IReadOnlyList<PlanLimitInfo> PlanLimits { get; init; } = [];

    /// <summary>Project/workspace the active session belongs to, if known (e.g. cwd basename).</summary>
    public string? ActiveProject { get; init; }

    /// <summary>
    /// True when the numbers come from a schema the provider doesn't have an official spec for
    /// (currently: Copilot CLI's undocumented events.jsonl). UI should hedge the display.
    /// </summary>
    public bool IsEstimate { get; init; }

    /// <summary>One-line description of where the data came from, shown in tooltips/diagnostics.</summary>
    public required string Source { get; init; }

    /// <summary>Set when reading local data failed; other fields are left at their defaults.</summary>
    public string? Error { get; init; }
}
