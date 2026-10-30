using System.Text.Json;
using NotchWin.Core.Models;

namespace NotchWin.Core.AiAgents.OpenCode;

/// <summary>
/// Reads OpenCode's local session data: one JSON file per message under
/// <c>&lt;data&gt;/opencode/storage/message/&lt;session&gt;/</c>, where <c>&lt;data&gt;</c> is
/// <c>$XDG_DATA_HOME</c> or <c>~/.local/share</c>. Assistant messages carry <c>tokens</c>
/// (input/output/reasoning/cache.read/cache.write), <c>modelID</c>, <c>cost</c>, <c>time.created</c>
/// (Unix ms) and <c>path.cwd</c>.
/// This layout comes from community tooling, not an OpenCode spec, and OpenCode has changed storage
/// between releases (newer builds may keep a SQLite database instead, which this provider does not
/// read). It could not be checked against a real install when written, so snapshots are flagged
/// <see cref="AgentUsageSnapshot.IsEstimate"/>, and a missing directory yields no snapshot at all
/// rather than an empty OpenCode row.
/// </summary>
public sealed class OpenCodeUsageProvider : IAgentUsageProvider
{
    private static readonly TimeSpan LiveWorkThreshold = TimeSpan.FromMinutes(2);

    private readonly string _dataHome;
    private readonly TimeProvider _timeProvider;

    public OpenCodeUsageProvider(string? dataHomeOverride = null, TimeProvider? timeProvider = null)
    {
        _dataHome = dataHomeOverride ?? ResolveDefaultHome();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AgentKind Kind => AgentKind.OpenCode;

    private static string ResolveDefaultHome()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : xdg;
        return Path.Combine(root, "opencode");
    }

    public Task<IReadOnlyList<AgentUsageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var messageRoot = Path.Combine(_dataHome, "storage", "message");
        if (!Directory.Exists(messageRoot))
        {
            return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([]);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(messageRoot, "*.json", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([]);
        }

        var now = _timeProvider.GetUtcNow();
        var todayLocal = now.ToLocalTime().Date;

        var usage = default(TokenUsage);
        decimal cost = 0;
        var models = new List<(DateTimeOffset At, string Model)>();
        var newest = DateTimeOffset.MinValue;
        string? project = null;
        var newestWrite = DateTimeOffset.MinValue;

        foreach (var file in files)
        {
            var written = JsonlFileHelpers.SafeGetLastWriteTimeUtc(file);
            if (written.ToLocalTime().Date != todayLocal)
            {
                continue;
            }

            if (!TryReadMessage(file, written, out var message) || message.At.ToLocalTime().Date != todayLocal)
            {
                continue;
            }

            if (written > newestWrite)
            {
                newestWrite = written;
            }

            usage += message.Usage;
            cost += message.Cost;
            if (message.Model is not null)
            {
                models.Add((message.At, message.Model));
            }

            if (message.At > newest)
            {
                newest = message.At;
                project = message.Project ?? project;
            }
        }

        if (newestWrite == DateTimeOffset.MinValue)
        {
            return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([]);
        }

        var snapshot = new AgentUsageSnapshot
        {
            Kind = AgentKind.OpenCode,
            AccountLabel = "default",
            IsLiveWorking = now - newestWrite <= LiveWorkThreshold,
            LastActivityUtc = newestWrite,
            Models = models.OrderByDescending(m => m.At).Select(m => m.Model).Distinct().ToList(),
            TokensToday = usage,
            EstimatedCostUsdToday = cost > 0 ? cost : null,
            ActiveProject = project,
            IsEstimate = true,
            Source = $"OpenCode message files under {messageRoot} (community-documented layout, unverified)",
        };

        return Task.FromResult<IReadOnlyList<AgentUsageSnapshot>>([snapshot]);
    }

    private readonly record struct Message(DateTimeOffset At, TokenUsage Usage, decimal Cost, string? Model, string? Project);

    private static bool TryReadMessage(string file, DateTimeOffset fallbackTime, out Message message)
    {
        message = default;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("role", out var role) || role.GetString() != "assistant" ||
                !root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var cache = tokens.TryGetProperty("cache", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
            var usage = new TokenUsage(
                Input: Long(tokens, "input"),
                // Reasoning tokens are billed as output, so they count toward it.
                Output: Long(tokens, "output") + Long(tokens, "reasoning"),
                CacheWrite: cache.ValueKind == JsonValueKind.Object ? Long(cache, "write") : 0,
                CacheRead: cache.ValueKind == JsonValueKind.Object ? Long(cache, "read") : 0);

            var at = fallbackTime;
            if (root.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Object &&
                time.TryGetProperty("created", out var created) && created.TryGetInt64(out var ms))
            {
                at = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            var cost = root.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number
                ? costEl.GetDecimal()
                : 0m;
            var model = root.TryGetProperty("modelID", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

            string? project = null;
            if (root.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.Object &&
                path.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
            {
                project = Path.GetFileName(cwd.GetString()!.TrimEnd('/', '\\'));
            }

            message = new Message(at, usage, cost, model, project);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            // A half-written or foreign file shouldn't break the poll; skip it.
            return false;
        }
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
