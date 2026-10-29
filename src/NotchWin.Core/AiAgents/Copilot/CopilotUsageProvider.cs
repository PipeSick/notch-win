using System.Text.Json;
using NotchWin.Core.Models;

namespace NotchWin.Core.AiAgents.Copilot;

/// <summary>
/// Reads GitHub Copilot CLI's local session data (events.jsonl files under
/// ~/.copilot/session-state/&lt;session-id&gt;/, with a legacy ~/.copilot/history-session-state/ fallback)
/// to detect live activity and the active workspace. GitHub does not document a usage/token schema
/// for this file (nor any per-user quota API for an individual Copilot plan — see
/// docs/AI_AGENT_SOURCES.md), so <see cref="AgentUsageSnapshot.TokensToday"/> here comes from a
/// heuristic scan for numeric fields whose name contains "token", not a documented counter.
/// Every snapshot this provider returns has <see cref="AgentUsageSnapshot.IsEstimate"/> set to true
/// for that reason; treat the token/cost numbers as a rough signal, not a bill.
/// </summary>
public sealed class CopilotUsageProvider : IAgentUsageProvider
{
    private static readonly TimeSpan LiveWorkThreshold = TimeSpan.FromMinutes(2);

    private readonly string _copilotHome;
    private readonly TimeProvider _timeProvider;

    public CopilotUsageProvider(string? copilotHomeOverride = null, TimeProvider? timeProvider = null)
    {
        _copilotHome = copilotHomeOverride ?? ResolveDefaultHome();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AgentKind Kind => AgentKind.Copilot;

    private static string ResolveDefaultHome()
    {
        var envHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(envHome))
        {
            return envHome;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
    }

    public Task<IReadOnlyList<AgentUsageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var sessionStateDirs = new[] { "session-state", "history-session-state" }
            .Select(name => Path.Combine(_copilotHome, name))
            .Where(Directory.Exists)
            .ToList();

        if (sessionStateDirs.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([
                new AgentUsageSnapshot
                {
                    Kind = AgentKind.Copilot,
                    AccountLabel = "default",
                    IsEstimate = true,
                    Source = $"no session-state directory found under {_copilotHome}",
                },
            ]);
        }

        string[] eventFiles;
        try
        {
            eventFiles = sessionStateDirs
                .SelectMany(dir => Directory.GetFiles(dir, "events.jsonl", SearchOption.AllDirectories))
                .ToArray();
        }
        catch (IOException)
        {
            eventFiles = [];
        }

        if (eventFiles.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([
                new AgentUsageSnapshot
                {
                    Kind = AgentKind.Copilot,
                    AccountLabel = "default",
                    IsEstimate = true,
                    Source = $"no events.jsonl files under {string.Join(", ", sessionStateDirs)}",
                },
            ]);
        }

        var now = _timeProvider.GetUtcNow();
        var todayLocal = now.ToLocalTime().Date;

        long heuristicTokensToday = 0;
        string? mostRecentFile = null;
        var mostRecentWriteTime = DateTimeOffset.MinValue;

        foreach (var file in eventFiles)
        {
            var writeTime = JsonlFileHelpers.SafeGetLastWriteTimeUtc(file);
            if (writeTime > mostRecentWriteTime)
            {
                mostRecentWriteTime = writeTime;
                mostRecentFile = file;
            }

            if (writeTime.ToLocalTime().Date == todayLocal)
            {
                heuristicTokensToday += SumHeuristicTokenFields(file);
            }
        }

        var isLive = mostRecentFile is not null && now - mostRecentWriteTime <= LiveWorkThreshold;
        var activeProject = mostRecentFile is null ? null : Path.GetFileName(Path.GetDirectoryName(mostRecentFile));

        var snapshot = new AgentUsageSnapshot
        {
            Kind = AgentKind.Copilot,
            AccountLabel = "default",
            IsLiveWorking = isLive,
            LastActivityUtc = mostRecentFile is null ? null : mostRecentWriteTime,
            Models = [],
            TokensToday = new TokenUsage(Output: heuristicTokensToday),
            ActiveProject = activeProject,
            IsEstimate = true,
            Source = $"heuristic scan of undocumented Copilot CLI logs under {string.Join(", ", sessionStateDirs)}",
        };

        return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([snapshot]);
    }

    private static long SumHeuristicTokenFields(string path)
    {
        long sum = 0;
        foreach (var line in JsonlFileHelpers.ReadLinesSafely(path))
        {
            if (!JsonlFileHelpers.TryParseJsonLine(line, out var doc))
            {
                continue;
            }

            using (doc)
            {
                sum += SumTokenLikeFields(doc.RootElement);
            }
        }

        return sum;
    }

    private static long SumTokenLikeFields(JsonElement element)
    {
        long sum = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number &&
                        property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.TryGetInt64(out var number))
                    {
                        sum += number;
                    }
                    else
                    {
                        sum += SumTokenLikeFields(property.Value);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    sum += SumTokenLikeFields(item);
                }

                break;
        }

        return sum;
    }
}
