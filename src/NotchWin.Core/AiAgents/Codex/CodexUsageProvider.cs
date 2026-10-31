using System.Text.Json;
using NotchWin.Core.Models;
using NotchWin.Core.Pricing;

namespace NotchWin.Core.AiAgents.Codex;

/// <summary>
/// Reads Codex CLI's local rollout transcripts (JSONL files under $CODEX_HOME/sessions/.../rollout-*.jsonl)
/// to surface cumulative token usage and, when present, the "rate_limits" snapshot the CLI itself
/// persists for its /status display. The exact rollout schema isn't officially documented and has
/// drifted across CLI versions (see docs/AI_AGENT_SOURCES.md), so every field here is read
/// defensively: a missing or renamed property degrades a snapshot instead of throwing.
/// One snapshot is returned per CODEX_HOME tracked, which is how multiple ChatGPT/Codex accounts
/// are supported (each account gets its own CODEX_HOME, exactly like the CLI itself).
/// </summary>
public sealed class CodexUsageProvider : IAgentUsageProvider
{
    private static readonly TimeSpan LiveWorkThreshold = TimeSpan.FromMinutes(2);

    private readonly IReadOnlyList<string> _codexHomes;
    private readonly PricingCatalog _pricing;
    private readonly TimeProvider _timeProvider;

    public CodexUsageProvider(IEnumerable<string>? additionalCodexHomes = null, PricingCatalog? pricing = null, TimeProvider? timeProvider = null)
    {
        _pricing = pricing ?? new PricingCatalog();
        _timeProvider = timeProvider ?? TimeProvider.System;

        var homes = new List<string> { ResolveDefaultCodexHome() };
        if (additionalCodexHomes is not null)
        {
            homes.AddRange(additionalCodexHomes.Where(h => !string.IsNullOrWhiteSpace(h)));
        }

        _codexHomes = homes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public AgentKind Kind => AgentKind.Codex;

    public Task<IReadOnlyList<AgentUsageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var results = _codexHomes.Select(BuildSnapshotForHome).ToList();
        return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>(results);
    }

    private static string ResolveDefaultCodexHome()
    {
        var envHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(envHome))
        {
            return envHome;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    private AgentUsageSnapshot BuildSnapshotForHome(string home)
    {
        var label = Path.GetFileName(home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(label))
        {
            label = home;
        }

        var sessionsDir = Path.Combine(home, "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            return new AgentUsageSnapshot { Kind = AgentKind.Codex, AccountLabel = label, Source = $"no sessions dir at {sessionsDir}" };
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(sessionsDir, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            files = [];
        }

        if (files.Length == 0)
        {
            return new AgentUsageSnapshot { Kind = AgentKind.Codex, AccountLabel = label, Source = $"no rollout files under {sessionsDir}" };
        }

        var now = _timeProvider.GetUtcNow();
        var todayLocal = now.ToLocalTime().Date;

        var todayUsage = default(TokenUsage);
        RolloutFileSummary? mostRecentSummary = null;
        var mostRecentWriteTime = DateTimeOffset.MinValue;

        foreach (var file in files)
        {
            var writeTime = JsonlFileHelpers.SafeGetLastWriteTimeUtc(file);
            var summary = ScanRolloutFile(file);

            if (summary.LatestUsage is { } usage && writeTime.ToLocalTime().Date == todayLocal)
            {
                todayUsage += usage;
            }

            if (writeTime > mostRecentWriteTime)
            {
                mostRecentWriteTime = writeTime;
                mostRecentSummary = summary;
            }
        }

        var isLive = mostRecentSummary is not null && now - mostRecentWriteTime <= LiveWorkThreshold;
        var activeUsage = mostRecentSummary?.LatestUsage ?? default;
        var model = mostRecentSummary?.Model;

        decimal? cost = model is null ? null : _pricing.EstimateCostUsd(todayUsage, model);

        return new AgentUsageSnapshot
        {
            Kind = AgentKind.Codex,
            AccountLabel = label,
            IsLiveWorking = isLive,
            LastActivityUtc = mostRecentSummary is null ? null : mostRecentWriteTime,
            Models = model is null ? [] : [model],
            TokensToday = todayUsage,
            TokensActiveSession = activeUsage,
            EstimatedCostUsdToday = cost,
            PlanLimits = mostRecentSummary?.PlanLimits ?? [],
            PlanName = mostRecentSummary?.PlanName,
            ActiveProject = mostRecentSummary is null ? null : Path.GetFileName(mostRecentSummary.Cwd?.TrimEnd('/', '\\')),
            IsEstimate = false,
            Source = $"local rollout JSONL under {sessionsDir}",
        };
    }

    /// <summary>
    /// Single forward pass over a rollout file. token_count payloads carry a *cumulative* counter
    /// for the whole session, so later events simply overwrite earlier ones rather than being summed.
    /// </summary>
    private static RolloutFileSummary ScanRolloutFile(string path)
    {
        var summary = new RolloutFileSummary();

        foreach (var line in JsonlFileHelpers.ReadLinesSafely(path))
        {
            if (!JsonlFileHelpers.TryParseJsonLine(line, out var doc))
            {
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;

                if (summary.Cwd is null && TryGetString(root, "cwd", out var cwd))
                {
                    summary.Cwd = cwd;
                }

                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (summary.Cwd is null && TryGetString(payload, "cwd", out var payloadCwd))
                {
                    summary.Cwd = payloadCwd;
                }

                if (TryGetString(payload, "model", out var model))
                {
                    summary.Model = model;
                }

                if (!TryGetString(payload, "type", out var payloadType) || !string.Equals(payloadType, "token_count", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryExtractTokenUsage(payload, out var usage))
                {
                    summary.LatestUsage = usage;
                }

                if (payload.TryGetProperty("rate_limits", out var rateLimits) && rateLimits.ValueKind == JsonValueKind.Object)
                {
                    summary.PlanLimits = ExtractPlanLimits(rateLimits);
                    if (TryGetString(rateLimits, "plan_type", out var planType) && planType.Length > 0)
                    {
                        summary.PlanName = char.ToUpperInvariant(planType[0]) + planType[1..].ToLowerInvariant();
                    }
                }
            }
        }

        return summary;
    }

    private static bool TryExtractTokenUsage(JsonElement payload, out TokenUsage usage)
    {
        // Known/observed shapes, tried in order: nested under "info.total_token_usage",
        // nested under "info", or flat on the payload itself.
        if (payload.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            if (info.TryGetProperty("total_token_usage", out var total) && total.ValueKind == JsonValueKind.Object &&
                TryReadTokenFields(total, out usage))
            {
                return true;
            }

            if (TryReadTokenFields(info, out usage))
            {
                return true;
            }
        }

        if (TryReadTokenFields(payload, out usage))
        {
            return true;
        }

        usage = default;
        return false;
    }

    private static bool TryReadTokenFields(JsonElement obj, out TokenUsage usage)
    {
        var input = ReadLong(obj, "input_tokens");
        var output = ReadLong(obj, "output_tokens");
        var cacheRead = ReadLong(obj, "cached_input_tokens") ?? ReadLong(obj, "cache_read_input_tokens");

        if (input is null && output is null)
        {
            usage = default;
            return false;
        }

        usage = new TokenUsage(input ?? 0, output ?? 0, CacheWrite: 0, CacheRead: cacheRead ?? 0);
        return true;
    }

    private static long? ReadLong(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var number))
        {
            return number;
        }

        return null;
    }

    private static IReadOnlyList<PlanLimitInfo> ExtractPlanLimits(JsonElement rateLimits)
    {
        var result = new List<PlanLimitInfo>();
        foreach (var (propertyName, label) in new[] { ("primary", "5h"), ("secondary", "weekly") })
        {
            if (!rateLimits.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var usedPercent = ReadDouble(window, "used_percent");
            if (usedPercent is null)
            {
                continue;
            }

            var windowMinutes = ReadDouble(window, "window_minutes");
            var resetsInSeconds = ReadDouble(window, "resets_in_seconds");
            DateTimeOffset? resetsAt = resetsInSeconds is null
                ? null
                : DateTimeOffset.UtcNow.AddSeconds(resetsInSeconds.Value);

            result.Add(new PlanLimitInfo($"Codex {label}", usedPercent.Value, windowMinutes, resetsAt));
        }

        return result;
    }

    private static double? ReadDouble(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number))
        {
            return number;
        }

        return null;
    }

    private static bool TryGetString(JsonElement obj, string propertyName, out string value)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out var raw) &&
            raw.ValueKind == JsonValueKind.String)
        {
            value = raw.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private sealed class RolloutFileSummary
    {
        public string? Cwd { get; set; }
        public string? Model { get; set; }
        public TokenUsage? LatestUsage { get; set; }
        public IReadOnlyList<PlanLimitInfo> PlanLimits { get; set; } = [];
        public string? PlanName { get; set; }
    }
}
