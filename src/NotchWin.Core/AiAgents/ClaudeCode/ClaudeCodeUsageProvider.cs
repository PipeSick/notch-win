using System.Text.Json;
using System.Text.Json.Serialization;
using NotchWin.Core.Models;
using NotchWin.Core.Pricing;

namespace NotchWin.Core.AiAgents.ClaudeCode;

/// <summary>
/// Reads Claude Code's local session transcripts (JSONL files under ~/.claude/projects/&lt;slug&gt;/&lt;sessionId&gt;.jsonl)
/// to compute today's token usage and detect a live session. There is no documented API for a
/// personal Claude.ai Pro/Max plan's usage percentage, so <see cref="AgentUsageSnapshot.PlanLimits"/>
/// stays empty here; see docs/AI_AGENT_SOURCES.md for the Admin-API-only alternative.
/// </summary>
public sealed class ClaudeCodeUsageProvider : IAgentUsageProvider
{
    private static readonly TimeSpan LiveWorkThreshold = TimeSpan.FromMinutes(2);

    private readonly string _claudeHome;
    private readonly PricingCatalog _pricing;
    private readonly TimeProvider _timeProvider;

    public ClaudeCodeUsageProvider(string? claudeHomeOverride = null, PricingCatalog? pricing = null, TimeProvider? timeProvider = null)
    {
        _claudeHome = claudeHomeOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        _pricing = pricing ?? new PricingCatalog();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AgentKind Kind => AgentKind.ClaudeCode;

    public Task<IReadOnlyList<AgentUsageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var projectsDir = Path.Combine(_claudeHome, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([]);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(projectsDir, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([]);
        }

        var now = _timeProvider.GetUtcNow();
        var todayLocal = now.ToLocalTime().Date;
        var seenMessageIds = new HashSet<string>();

        var todayUsage = default(TokenUsage);
        var modelsByRecency = new List<(DateTimeOffset Timestamp, string Model)>();

        string? mostRecentFile = null;
        var mostRecentFileTime = DateTimeOffset.MinValue;

        foreach (var file in files)
        {
            DateTimeOffset fileWriteTime;
            try
            {
                fileWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            }
            catch (IOException)
            {
                continue;
            }

            // Only files touched today (or still warm enough to matter for "live" detection) are worth
            // opening; everything older can't contribute to today's totals or the active session.
            if (fileWriteTime.ToLocalTime().Date != todayLocal && now - fileWriteTime > LiveWorkThreshold)
            {
                if (fileWriteTime > mostRecentFileTime)
                {
                    mostRecentFileTime = fileWriteTime;
                    mostRecentFile = file;
                }

                continue;
            }

            if (fileWriteTime > mostRecentFileTime)
            {
                mostRecentFileTime = fileWriteTime;
                mostRecentFile = file;
            }

            foreach (var line in JsonlFileHelpers.ReadLinesSafely(file))
            {
                var entry = TryParseLine(line);
                if (entry?.Message?.Usage is null || entry.Timestamp is null)
                {
                    continue;
                }

                if (entry.Message.Id is not null && !seenMessageIds.Add(entry.Message.Id))
                {
                    continue; // duplicate line for a message we already counted (e.g. resumed/forked session)
                }

                if (entry.Timestamp.Value.ToLocalTime().Date != todayLocal)
                {
                    continue;
                }

                var usage = ToTokenUsage(entry.Message.Usage);
                todayUsage += usage;

                if (entry.Message.Model is not null)
                {
                    modelsByRecency.Add((entry.Timestamp.Value, entry.Message.Model));
                }
            }
        }

        var activeSessionUsage = default(TokenUsage);
        string? activeProject = null;
        var isLive = false;

        if (mostRecentFile is not null)
        {
            isLive = now - mostRecentFileTime <= LiveWorkThreshold;
            var seenInActiveSession = new HashSet<string>(); // own dedup set: this file may already
                                                               // have contributed to todayUsage above,
                                                               // and reusing that set would hide it here.
            foreach (var line in JsonlFileHelpers.ReadLinesSafely(mostRecentFile))
            {
                var entry = TryParseLine(line);
                if (entry is null)
                {
                    continue;
                }

                activeProject ??= entry.Cwd is null ? null : Path.GetFileName(entry.Cwd.TrimEnd('/', '\\'));

                if (entry.Message?.Usage is null)
                {
                    continue;
                }

                if (entry.Message.Id is not null && !seenInActiveSession.Add(entry.Message.Id))
                {
                    continue;
                }

                activeSessionUsage += ToTokenUsage(entry.Message.Usage);
            }
        }

        var models = modelsByRecency
            .OrderByDescending(m => m.Timestamp)
            .Select(m => m.Model)
            .Distinct()
            .ToList();

        decimal? estimatedCost = null;
        if (models.Count > 0)
        {
            var total = decimal.Zero;
            var anyKnown = false;
            foreach (var model in models)
            {
                var perModelCost = _pricing.EstimateCostUsd(todayUsage, model);
                if (perModelCost is not null)
                {
                    // Models aren't split out per-token here, so this approximates by pricing the
                    // whole day's usage under each model seen and keeping the cheapest-to-skip "unknown"
                    // path honest: when exactly one model was used (the common case) this is exact.
                    total = perModelCost.Value;
                    anyKnown = true;
                    break;
                }
            }

            estimatedCost = anyKnown ? total : null;
        }

        var account = ClaudeAccountInfo.TryRead(_claudeHome);
        var snapshot = new AgentUsageSnapshot
        {
            Kind = AgentKind.ClaudeCode,
            AccountLabel = "default",
            AccountEmail = account?.Email,
            PlanName = account?.Plan,
            IsLiveWorking = isLive,
            LastActivityUtc = mostRecentFile is null ? null : mostRecentFileTime,
            Models = models,
            TokensToday = todayUsage,
            TokensActiveSession = activeSessionUsage,
            EstimatedCostUsdToday = estimatedCost,
            PlanLimits = [],
            ActiveProject = activeProject,
            IsEstimate = false,
            Source = $"local JSONL transcripts under {projectsDir}",
        };

        return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([snapshot]);
    }

    private static TokenUsage ToTokenUsage(UsagePayload usage) => new(
        usage.InputTokens,
        usage.OutputTokens,
        usage.CacheCreationInputTokens,
        usage.CacheReadInputTokens);

    private static TranscriptLine? TryParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TranscriptLine>(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class TranscriptLine
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonPropertyName("cwd")]
        public string? Cwd { get; set; }

        [JsonPropertyName("message")]
        public MessagePayload? Message { get; set; }
    }

    private sealed class MessagePayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public UsagePayload? Usage { get; set; }
    }

    private sealed class UsagePayload
    {
        [JsonPropertyName("input_tokens")]
        public long InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public long OutputTokens { get; set; }

        [JsonPropertyName("cache_creation_input_tokens")]
        public long CacheCreationInputTokens { get; set; }

        [JsonPropertyName("cache_read_input_tokens")]
        public long CacheReadInputTokens { get; set; }
    }
}
