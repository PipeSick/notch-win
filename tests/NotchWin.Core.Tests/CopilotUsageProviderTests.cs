using NotchWin.Core.AiAgents.Copilot;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class CopilotUsageProviderTests : IDisposable
{
    private readonly string _copilotHome = Path.Combine(Path.GetTempPath(), "notchwin-tests-copilot-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_copilotHome))
        {
            Directory.Delete(_copilotHome, recursive: true);
        }
    }

    [Fact]
    public async Task AlwaysMarksResultAsEstimate_RegardlessOfOutcome()
    {
        var provider = new CopilotUsageProvider(_copilotHome);
        var snapshot = Assert.Single(await provider.GetSnapshotsAsync());

        Assert.True(snapshot.IsEstimate);
    }

    [Fact]
    public async Task SumsTokenLikeFields_AndDetectsLiveSession()
    {
        var sessionDir = Path.Combine(_copilotHome, "session-state", "session-123");
        Directory.CreateDirectory(sessionDir);
        var eventsFile = Path.Combine(sessionDir, "events.jsonl");

        var lines = new[]
        {
            """{"type":"turn","usage":{"input_tokens":30,"output_tokens":12}}""",
            """{"type":"shutdown","metrics":{"prompt_tokens":5,"other_metric":999}}""",
        };
        await File.WriteAllLinesAsync(eventsFile, lines);

        var provider = new CopilotUsageProvider(_copilotHome);
        var snapshot = Assert.Single(await provider.GetSnapshotsAsync());

        // 30 (input_tokens) + 12 (output_tokens) + 5 (prompt_tokens); other_metric is excluded
        // because its name doesn't contain "token" — this is deliberately a loose heuristic, not a
        // real schema, so the test only pins down that the name-matching rule is applied consistently.
        Assert.Equal(47, snapshot.TokensToday.Output);
        Assert.True(snapshot.IsLiveWorking);
        Assert.Equal("session-123", snapshot.ActiveProject);
        Assert.True(snapshot.IsEstimate);
    }
}
