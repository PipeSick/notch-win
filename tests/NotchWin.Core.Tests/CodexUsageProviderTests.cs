using NotchWin.Core.AiAgents.Codex;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class CodexUsageProviderTests : IDisposable
{
    private readonly string _codexHome = Path.Combine(Path.GetTempPath(), "notchwin-tests-codex-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_codexHome))
        {
            Directory.Delete(_codexHome, recursive: true);
        }
    }

    [Fact]
    public async Task UsesLatestCumulativeTokenCount_NotSumOfEvents()
    {
        var sessionsDir = Path.Combine(_codexHome, "sessions", "2026", "01", "01");
        Directory.CreateDirectory(sessionsDir);
        var rolloutFile = Path.Combine(sessionsDir, "rollout-2026-01-01T00-00-00-abc.jsonl");

        var lines = new[]
        {
            """{"type":"event_msg","cwd":"/home/user/my-codex-project","payload":{"type":"token_count","model":"gpt-5-codex","info":{"total_token_usage":{"input_tokens":1000,"output_tokens":200,"cached_input_tokens":50}},"rate_limits":{"primary":{"used_percent":10.0,"window_minutes":300,"resets_in_seconds":1000},"secondary":{"used_percent":2.0,"window_minutes":10080,"resets_in_seconds":500000}}}}""",
            // Later event: cumulative counters grow, must OVERWRITE rather than add to the first event.
            """{"type":"event_msg","payload":{"type":"token_count","model":"gpt-5-codex","info":{"total_token_usage":{"input_tokens":1500,"output_tokens":400,"cached_input_tokens":90}},"rate_limits":{"primary":{"used_percent":42.5,"window_minutes":300,"resets_in_seconds":1200},"secondary":{"used_percent":9.0,"window_minutes":10080,"resets_in_seconds":480000}}}}""",
        };
        await File.WriteAllLinesAsync(rolloutFile, lines);

        var provider = new CodexUsageProvider(additionalCodexHomes: [_codexHome]);
        var snapshots = await provider.GetSnapshotsAsync();

        var ourAccountLabel = Path.GetFileName(_codexHome);
        var snapshot = Assert.Single(snapshots, s => s.AccountLabel == ourAccountLabel);

        Assert.Equal(1500, snapshot.TokensToday.Input);
        Assert.Equal(400, snapshot.TokensToday.Output);
        Assert.Equal(90, snapshot.TokensToday.CacheRead);

        Assert.Equal(["gpt-5-codex"], snapshot.Models);
        Assert.Equal("my-codex-project", snapshot.ActiveProject);
        Assert.True(snapshot.IsLiveWorking);

        Assert.Equal(2, snapshot.PlanLimits.Count);
        var primary = Assert.Single(snapshot.PlanLimits, p => p.Label == "Codex 5h");
        Assert.Equal(42.5, primary.UsedPercent);
        var secondary = Assert.Single(snapshot.PlanLimits, p => p.Label == "Codex weekly");
        Assert.Equal(9.0, secondary.UsedPercent);
    }

    [Fact]
    public async Task EachAdditionalCodexHome_GetsItsOwnSnapshot()
    {
        var homeA = Path.Combine(_codexHome, "accountA");
        var homeB = Path.Combine(_codexHome, "accountB");
        Directory.CreateDirectory(Path.Combine(homeA, "sessions"));
        Directory.CreateDirectory(Path.Combine(homeB, "sessions"));

        var provider = new CodexUsageProvider(additionalCodexHomes: [homeA, homeB]);
        var snapshots = await provider.GetSnapshotsAsync();

        Assert.Contains(snapshots, s => s.AccountLabel == "accountA");
        Assert.Contains(snapshots, s => s.AccountLabel == "accountB");
    }
}
