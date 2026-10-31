using System.Net;
using NotchWin.Core.AiAgents.OpenCode;
using NotchWin.Core.Lyrics;
using NotchWin.Core.Models;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class LrcParserTests
{
    [Fact]
    public void Parse_ReadsTimestampsAndText_IgnoringMetadata()
    {
        var lines = LrcParser.Parse("[ar:Alguien]\n[ti:Cancion]\n[00:12.50] Primera linea\n[01:05.5]Segunda\n[00:03.05]Intro");

        Assert.Equal(["Intro", "Primera linea", "Segunda"], lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromMilliseconds(3050), lines[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(12500), lines[1].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(65500), lines[2].Time);
    }

    [Fact]
    public void Parse_RepeatedTimestampsOnOneLine_ProduceOneEntryEach()
    {
        var lines = LrcParser.Parse("[00:10.00][00:40.00]Estribillo");

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)], lines.Select(l => l.Time));
        Assert.All(lines, l => Assert.Equal("Estribillo", l.Text));
    }

    [Fact]
    public void Parse_AppliesOffset_AndClampsAtZero()
    {
        var lines = LrcParser.Parse("[offset:+500]\n[00:01.00]Uno\n[00:00.20]Cero");

        Assert.Equal(TimeSpan.Zero, lines[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(500), lines[1].Time);
    }

    [Fact]
    public void IndexAt_FindsCurrentLine()
    {
        var lines = LrcParser.Parse("[00:05.00]A\n[00:10.00]B\n[00:20.00]C");

        Assert.Equal(-1, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(19)));
        Assert.Equal(2, LrcParser.IndexAt(lines, TimeSpan.FromMinutes(5)));
    }
}

public sealed class OpenCodeUsageProviderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "notchwin-opencode-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private void WriteMessage(string session, string id, string json)
    {
        var dir = Path.Combine(_home, "storage", "message", session);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"), json);
    }

    private static string Assistant(long created, string model, int input, int output, int reasoning, int cacheRead, int cacheWrite, decimal cost, string cwd) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            id = "msg",
            role = "assistant",
            modelID = model,
            cost,
            tokens = new { input, output, reasoning, cache = new { read = cacheRead, write = cacheWrite } },
            time = new { created },
            path = new { cwd },
        });

    [Fact]
    public async Task SumsTodaysAssistantMessages_AndReportsModelProjectCost()
    {
        var now = DateTimeOffset.UtcNow;
        WriteMessage("s1", "m1", Assistant(now.AddSeconds(-30).ToUnixTimeMilliseconds(), "claude-sonnet", 100, 50, 10, 1000, 200, 0.25m, "/work/notch-win"));
        WriteMessage("s1", "m2", Assistant(now.ToUnixTimeMilliseconds(), "claude-sonnet", 20, 5, 0, 0, 0, 0.05m, "/work/notch-win"));
        WriteMessage("s1", "user", "{\"role\":\"user\"}");

        var snapshot = Assert.Single(await new OpenCodeUsageProvider(_home).GetSnapshotsAsync());

        Assert.Equal(AgentKind.OpenCode, snapshot.Kind);
        Assert.Equal(120, snapshot.TokensToday.Input);
        Assert.Equal(65, snapshot.TokensToday.Output); // 50 + 10 reasoning + 5
        Assert.Equal(200, snapshot.TokensToday.CacheWrite);
        Assert.Equal(1000, snapshot.TokensToday.CacheRead);
        Assert.Equal(0.30m, snapshot.EstimatedCostUsdToday);
        Assert.Equal(["claude-sonnet"], snapshot.Models);
        Assert.Equal("notch-win", snapshot.ActiveProject);
        Assert.True(snapshot.IsLiveWorking);
        Assert.True(snapshot.IsEstimate);
    }

    [Fact]
    public async Task MissingDirectory_ReturnsNoSnapshot()
    {
        Assert.Empty(await new OpenCodeUsageProvider(Path.Combine(_home, "nope")).GetSnapshotsAsync());
    }

    [Fact]
    public async Task OldOrCorruptFiles_AreIgnored()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-3);
        WriteMessage("s1", "old", Assistant(old.ToUnixTimeMilliseconds(), "m", 999, 999, 0, 0, 0, 1m, "/x"));
        File.SetLastWriteTimeUtc(Path.Combine(_home, "storage", "message", "s1", "old.json"), old.UtcDateTime);
        WriteMessage("s1", "bad", "{ not json");

        Assert.Empty(await new OpenCodeUsageProvider(_home).GetSnapshotsAsync());
    }
}
