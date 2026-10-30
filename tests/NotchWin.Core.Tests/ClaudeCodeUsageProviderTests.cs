using System.Text.Json;
using NotchWin.Core.AiAgents.ClaudeCode;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class ClaudeCodeUsageProviderTests : IDisposable
{
    private readonly string _claudeHome = Path.Combine(Path.GetTempPath(), "notchwin-tests-claude-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_claudeHome))
        {
            Directory.Delete(_claudeHome, recursive: true);
        }
    }

    private static string Line(string id, string model, long input, long output, DateTimeOffset timestamp, string cwd) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp,
            cwd,
            message = new
            {
                id,
                model,
                usage = new
                {
                    input_tokens = input,
                    output_tokens = output,
                    cache_creation_input_tokens = 0,
                    cache_read_input_tokens = 0,
                },
            },
        });

    [Fact]
    public async Task AggregatesToday_DedupesRepeatedMessageIds_AndExcludesOtherDays()
    {
        var projectDir = Path.Combine(_claudeHome, "projects", "my-proj");
        Directory.CreateDirectory(projectDir);
        var sessionFile = Path.Combine(projectDir, "session1.jsonl");

        var now = DateTimeOffset.UtcNow;
        var yesterday = now.AddDays(-1);
        const string model = "claude-sonnet-4-20250514";

        var lines = new[]
        {
            Line("msg_1", model, 100, 50, now, "/home/user/myproj"),
            Line("msg_1", model, 100, 50, now, "/home/user/myproj"), // duplicate: must not be double-counted
            Line("msg_2", model, 10, 5, yesterday, "/home/user/myproj"), // different day: excluded from "today"
            Line("msg_3", model, 20, 8, now, "/home/user/myproj"),
        };
        await File.WriteAllLinesAsync(sessionFile, lines);

        var provider = new ClaudeCodeUsageProvider(_claudeHome);
        var snapshots = await provider.GetSnapshotsAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(120, snapshot.TokensToday.Input);
        Assert.Equal(58, snapshot.TokensToday.Output);

        // Active-session total covers the whole file (both days) but still dedupes msg_1.
        Assert.Equal(130, snapshot.TokensActiveSession.Input);
        Assert.Equal(63, snapshot.TokensActiveSession.Output);

        Assert.True(snapshot.IsLiveWorking);
        Assert.Equal("myproj", snapshot.ActiveProject);
        Assert.Equal([model], snapshot.Models);
        Assert.NotNull(snapshot.EstimatedCostUsdToday);
        Assert.False(snapshot.IsEstimate);
    }

    [Fact]
    public async Task ReturnsEmptySnapshot_WhenNoProjectsDirExists()
    {
        var provider = new ClaudeCodeUsageProvider(_claudeHome);
        var snapshots = await provider.GetSnapshotsAsync();

        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task NotLive_WhenMostRecentFileIsOld()
    {
        var projectDir = Path.Combine(_claudeHome, "projects", "my-proj");
        Directory.CreateDirectory(projectDir);
        var sessionFile = Path.Combine(projectDir, "session1.jsonl");

        var now = DateTimeOffset.UtcNow;
        await File.WriteAllLinesAsync(sessionFile, [Line("msg_1", "claude-sonnet-4-20250514", 5, 5, now, "/x")]);

        // Backdate the file itself well past the live-work threshold, independently of the
        // in-content timestamp (which stays "today" so it's still counted toward TokensToday).
        // 3 min is past the 2 min threshold but doesn't cross local midnight (an hour did, which
        // made this test fail whenever it ran between 00:00 and 01:00).
        var oldTime = now.AddMinutes(-3).UtcDateTime;
        File.SetLastWriteTimeUtc(sessionFile, oldTime);

        var provider = new ClaudeCodeUsageProvider(_claudeHome);
        var snapshot = Assert.Single(await provider.GetSnapshotsAsync());

        Assert.False(snapshot.IsLiveWorking);
        Assert.Equal(5, snapshot.TokensToday.Input);
    }
}
