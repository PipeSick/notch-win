using NotchWin.Core.AiAgents.ClaudeCode;
using NotchWin.Core.Clipboard;
using NotchWin.Core.Pricing;
using NotchWin.Core.Usage;

namespace NotchWin.Core.Tests;

public sealed class ClipboardHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddText_NewestFirst_AndRepeatMovesToTopWithoutDuplicating()
    {
        var history = new ClipboardHistory();
        history.AddText("uno", T0);
        history.AddText("dos", T0.AddMinutes(1));
        history.AddText("uno", T0.AddMinutes(2));

        Assert.Equal(["uno", "dos"], history.View().Select(i => i.Text));
    }

    [Fact]
    public void BlankText_IsIgnored()
    {
        var history = new ClipboardHistory();
        history.AddText("   ", T0);

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void PinnedItems_StayOnTop_SurviveClear_AndAreNotTrimmed()
    {
        var history = new ClipboardHistory(capacity: 2);
        history.AddText("fijado", T0);
        history.TogglePin(history.View()[0].Id);
        for (var i = 0; i < 5; i++)
        {
            history.AddText($"x{i}", T0.AddMinutes(i + 1));
        }

        Assert.Equal(3, history.Count); // the pinned one plus the two newest
        Assert.Equal("fijado", history.View()[0].Text);

        history.Clear();

        Assert.Equal(["fijado"], history.View().Select(i => i.Text));
    }

    [Fact]
    public void Search_FiltersTextEntries_IgnoringCase()
    {
        var history = new ClipboardHistory();
        history.AddText("Hola Mundo", T0);
        history.AddText("adios", T0.AddMinutes(1));

        Assert.Equal(["Hola Mundo"], history.View("mundo").Select(i => i.Text));
    }

    [Fact]
    public void SameImageBytes_AreStoredOnce()
    {
        var history = new ClipboardHistory();
        history.AddImage([1, 2, 3], T0);
        history.AddImage([1, 2, 3], T0.AddMinutes(1));
        history.AddImage([9], T0.AddMinutes(2));

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void Edit_Remove_AndChangedEvent()
    {
        var history = new ClipboardHistory();
        var changes = 0;
        history.Changed += () => changes++;
        history.AddText("viejo", T0);
        var id = history.View()[0].Id;

        history.UpdateText(id, "nuevo");
        Assert.Equal("nuevo", history.View()[0].Text);

        history.Remove(id);
        Assert.Equal(0, history.Count);
        Assert.Equal(3, changes);
    }
}

public sealed class UsageHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "notchwin-usage-" + Guid.NewGuid() + ".json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Record_KeepsTheLargestValueOfTheDay_AndPersists()
    {
        var day = new DateOnly(2026, 10, 31);
        var history = new UsageHistory(_path);
        history.Record(day, 5m);
        history.Record(day, 3m); // costs only grow within a day, a smaller reading is stale
        history.Record(day, 9.5m);

        var reloaded = new UsageHistory(_path);

        Assert.Equal(9.5m, reloaded.Last(day, 1)[0].Cost);
    }

    [Fact]
    public void Last_ReturnsOldestFirst_WithZerosForMissingDays()
    {
        var today = new DateOnly(2026, 10, 31);
        var history = new UsageHistory(_path);
        history.Record(today.AddDays(-2), 4m);

        var week = history.Last(today, 4);

        Assert.Equal([today.AddDays(-3), today.AddDays(-2), today.AddDays(-1), today], week.Select(d => d.Day));
        Assert.Equal([0m, 4m, 0m, 0m], week.Select(d => d.Cost));
    }

    [Fact]
    public void CorruptFile_StartsEmpty()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.All(new UsageHistory(_path).Last(new DateOnly(2026, 10, 31), 3), d => Assert.Equal(0m, d.Cost));
    }
}

public sealed class ClaudeAccountInfoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "notchwin-claude-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ReadsEmailAndPlan_FromTheSiblingClaudeJson_AndIgnoresSecrets()
    {
        var home = Path.Combine(_root, ".claude");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(_root, ".claude.json"),
            "{\"oauthAccount\":{\"emailAddress\":\"a@b.c\",\"organizationType\":\"claude_pro\"},\"accessToken\":\"secret\"}");

        var info = ClaudeAccountInfo.TryRead(home);

        Assert.Equal(new ClaudeAccountInfo("a@b.c", "Pro"), info);
    }

    [Fact]
    public void MissingOrUnreadableFile_ReturnsNull()
    {
        var home = Path.Combine(_root, ".claude");
        Directory.CreateDirectory(home);
        Assert.Null(ClaudeAccountInfo.TryRead(home));

        File.WriteAllText(Path.Combine(_root, ".claude.json"), "{ broken");
        Assert.Null(ClaudeAccountInfo.TryRead(home));
    }

    [Theory]
    [InlineData("claude_max", "Max")]
    [InlineData("claude_team", "Team")]
    [InlineData("other", null)]
    public void PlanNames(string organizationType, string? expected) =>
        Assert.Equal(expected, ClaudeAccountInfo.PlanFromOrganizationType(organizationType));
}

public sealed class PricingForNewModelsTests
{
    [Theory]
    [InlineData("claude-sonnet-5-5", 2.0)]
    [InlineData("claude-sonnet-5-5-20261001", 2.0)]
    [InlineData("claude-opus-5-5", 4.0)]
    [InlineData("claude-opus-4-5", 5.0)] // the older, broader claude-opus-4 entry is $15
    [InlineData("claude-opus-4-1", 15.0)]
    public void InputPrice_UsesTheMostSpecificPrefix(string model, double expectedInputPerMillion) =>
        Assert.Equal(expectedInputPerMillion, new PricingCatalog().TryGetPrice(model)!.InputPerMillion);
}
