using NotchWin.Core.Models;

namespace NotchWin.Core.AiAgents;

/// <summary>
/// Reads local CLI/session data for one AI coding agent and turns it into normalized snapshots.
/// None of these hit the network: everything here reads files already on disk, matching how the
/// providers we're cloning this feature from describe their own "AI agents" module (local-first,
/// no account needed). See docs/AI_AGENT_SOURCES.md for what is/isn't officially documented.
/// </summary>
public interface IAgentUsageProvider
{
    AgentKind Kind { get; }

    /// <summary>
    /// Returns one snapshot per account/profile found (most providers return exactly one).
    /// Never throws for "nothing found" (returns an empty list); only throws for programmer errors.
    /// </summary>
    Task<IReadOnlyList<AgentUsageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);
}
