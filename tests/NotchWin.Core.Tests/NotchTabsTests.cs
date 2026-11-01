using NotchWin.Core.AiAgents;
using WinMac.Calendar;
using NotchWin.Core.Downloads;
using NotchWin.Core.Models;
using NotchWin.Core.Notifications;
using NotchWin.Core.Settings;
using NotchWin.Core.Timers;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class NotchSectionsTests
{
    [Fact]
    public void Normalize_KeepsSavedOrderAndState_AppendsMissing_DropsUnknown()
    {
        var saved = new List<NotchSectionConfig>
        {
            new() { Id = NotchSections.Timer, Enabled = false },
            new() { Id = "removed-in-a-later-version", Enabled = true },
            new() { Id = NotchSections.Music, Enabled = true },
            new() { Id = NotchSections.Timer, Enabled = true }, // duplicate ignored
        };

        var result = NotchSections.Normalize(saved);

        Assert.Equal(NotchSections.AllIds.Count, result.Count);
        Assert.Equal(NotchSections.Timer, result[0].Id);
        Assert.False(result[0].Enabled);
        Assert.Equal(NotchSections.Music, result[1].Id);
        Assert.DoesNotContain(result, r => r.Id == "removed-in-a-later-version");
        // Sections the saved layout lacked come in at their default: everything on except the camera.
        Assert.All(result.Skip(2), r => Assert.Equal(r.Id != NotchSections.Camera, r.Enabled));
    }

    [Fact]
    public void Normalize_Null_ReturnsDefaults()
    {
        Assert.Equal(NotchSections.AllIds, NotchSections.Normalize(null).Select(s => s.Id));
    }
}

public sealed class IcsParserTests
{
    private const string Sample = """
        BEGIN:VCALENDAR
        BEGIN:VEVENT
        SUMMARY:Daily del equipo
        DTSTART:20261030T100000
        DTEND:20261030T101500
        END:VEVENT
        BEGIN:VEVENT
        SUMMARY:Almuerzo\, con Pipe
        DTSTART:20261030T140000
        DTEND:20261030T150000
        END:VEVENT
        BEGIN:VEVENT
        SUMMARY:Feriado
        DTSTART;VALUE=DATE:20261031
        END:VEVENT
        BEGIN:VEVENT
        SUMMARY:Linea larga que se
          dobla
        DTSTART:20261029T090000
        DTEND:20261029T093000
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Parse_ReadsTitlesTimesAndAllDay()
    {
        var events = IcsParser.Parse(Sample);

        Assert.Equal(4, events.Count);
        var daily = events.Single(e => e.Title == "Daily del equipo");
        Assert.Equal(new DateTime(2026, 10, 30, 10, 0, 0), daily.Start.DateTime);
        Assert.Equal(new DateTime(2026, 10, 30, 10, 15, 0), daily.End.DateTime);
        Assert.False(daily.AllDay);

        Assert.True(events.Single(e => e.Title == "Feriado").AllDay);
    }

    [Fact]
    public void Parse_UnescapesCommasAndUnfoldsLines()
    {
        var events = IcsParser.Parse(Sample);

        Assert.Contains(events, e => e.Title == "Almuerzo, con Pipe");
        Assert.Contains(events, e => e.Title == "Linea larga que se dobla");
    }

    [Fact]
    public void UpcomingToday_ExcludesFinishedAndOtherDays_Sorted()
    {
        var events = IcsParser.Parse(Sample);
        var now = new DateTimeOffset(2026, 10, 30, 10, 5, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 30)));

        var today = IcsParser.UpcomingToday(events, now);

        Assert.Equal(["Daily del equipo", "Almuerzo, con Pipe"], today.Select(e => e.Title));
    }

    [Fact]
    public void Parse_GarbageInput_ReturnsEmpty()
    {
        Assert.Empty(IcsParser.Parse("not a calendar"));
    }
}

public sealed class CountdownTimerTests
{
    private DateTimeOffset _now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private CountdownTimer Create() => new(() => _now);

    [Fact]
    public void RunningTimer_CountsDownByWallClock_AndCompletesOnce()
    {
        var timer = Create();
        timer.SetDuration(TimeSpan.FromMinutes(5));
        timer.Start();

        _now += TimeSpan.FromMinutes(2);
        Assert.Equal(TimeSpan.FromMinutes(3), timer.Remaining);
        Assert.False(timer.TryComplete());

        _now += TimeSpan.FromMinutes(4);
        Assert.True(timer.TryComplete());
        Assert.False(timer.TryComplete());
        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }

    [Fact]
    public void Pause_FreezesRemaining_ResumeContinues()
    {
        var timer = Create();
        timer.SetDuration(TimeSpan.FromMinutes(10));
        timer.Start();
        _now += TimeSpan.FromMinutes(3);
        timer.Pause();

        _now += TimeSpan.FromMinutes(30);
        Assert.Equal(TimeSpan.FromMinutes(7), timer.Remaining);

        timer.Start();
        _now += TimeSpan.FromMinutes(1);
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Remaining);
    }

    [Fact]
    public void StartAfterCompletion_RestartsFromTotal_AndResetRestoresTotal()
    {
        var timer = Create();
        timer.SetDuration(TimeSpan.FromMinutes(1));
        timer.Start();
        _now += TimeSpan.FromMinutes(2);
        timer.TryComplete();

        timer.Start();
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining);

        _now += TimeSpan.FromSeconds(20);
        timer.Reset();
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining);
        Assert.Equal(1.0, timer.FractionLeft);
    }
}

public sealed class DownloadTrackerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "notchwin-dl-" + Guid.NewGuid());

    public DownloadTrackerTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Poll_ReportsPartialFiles_ThenCompletionWhenRenamed()
    {
        var tracker = new DownloadTracker(_folder);
        var partial = Path.Combine(_folder, "setup.exe.crdownload");
        File.WriteAllBytes(partial, new byte[1024]);

        var (active, completed) = tracker.Poll();
        Assert.Single(active);
        Assert.Equal("setup.exe", active[0].FileName);
        Assert.Equal(1024, active[0].Bytes);
        Assert.Empty(completed);

        File.Move(partial, Path.Combine(_folder, "setup.exe"));
        (active, completed) = tracker.Poll();
        Assert.Empty(active);
        Assert.Equal(["setup.exe"], completed);

        (_, completed) = tracker.Poll();
        Assert.Empty(completed);
    }

    [Fact]
    public void Poll_CancelledDownload_IsNotReportedAsCompleted()
    {
        var tracker = new DownloadTracker(_folder);
        var partial = Path.Combine(_folder, "movie.mkv.part");
        File.WriteAllBytes(partial, new byte[10]);
        tracker.Poll();

        File.Delete(partial);
        var (active, completed) = tracker.Poll();

        Assert.Empty(active);
        Assert.Empty(completed);
    }

    [Fact]
    public void Poll_MissingFolder_ReturnsEmpty()
    {
        var (active, completed) = new DownloadTracker(Path.Combine(_folder, "nope")).Poll();
        Assert.Empty(active);
        Assert.Empty(completed);
    }
}

public sealed class NotificationLogTests
{
    [Fact]
    public void Add_PutsNewestFirst_RaisesEvents_AndCapsCapacity()
    {
        var log = new NotificationLog(capacity: 2);
        var added = 0;
        log.Added += _ => added++;

        log.Add("Claude Code", "uno", "m");
        log.Add("Claude Code", "dos", "m");
        log.Add("Claude Code", "tres", "m");

        Assert.Equal(3, added);
        Assert.Equal(["tres", "dos"], log.Items.Select(i => i.Title));

        log.Clear();
        Assert.Empty(log.Items);
    }
}

public sealed class AgentCompletionDetectorTests
{
    private DateTimeOffset _now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static AgentUsageSnapshot Snapshot(bool working, string account = "default", string? error = null) => new()
    {
        Kind = AgentKind.ClaudeCode,
        AccountLabel = account,
        IsLiveWorking = working,
        Source = "test",
        Error = error,
    };

    [Fact]
    public void LongTaskFinishing_ProducesOneCompletion()
    {
        var detector = new AgentCompletionDetector(TimeSpan.FromSeconds(60), () => _now);

        Assert.Empty(detector.Observe([Snapshot(true)]));
        _now += TimeSpan.FromMinutes(5);
        Assert.Empty(detector.Observe([Snapshot(true)]));
        _now += TimeSpan.FromMinutes(2);

        var done = Assert.Single(detector.Observe([Snapshot(false)]));
        Assert.Equal(AgentKind.ClaudeCode, done.Kind);
        Assert.Equal(TimeSpan.FromMinutes(7), done.WorkedFor);
        Assert.Empty(detector.Observe([Snapshot(false)]));
    }

    [Fact]
    public void ShortBurst_IsIgnored()
    {
        var detector = new AgentCompletionDetector(TimeSpan.FromSeconds(60), () => _now);

        detector.Observe([Snapshot(true)]);
        _now += TimeSpan.FromSeconds(20);

        Assert.Empty(detector.Observe([Snapshot(false)]));
    }

    [Fact]
    public void AccountsAreTrackedIndependently_AndErrorsSkipped()
    {
        var detector = new AgentCompletionDetector(TimeSpan.FromSeconds(10), () => _now);

        detector.Observe([Snapshot(true, "a"), Snapshot(true, "b")]);
        _now += TimeSpan.FromMinutes(1);

        var done = Assert.Single(detector.Observe([Snapshot(false, "a"), Snapshot(true, "b"), Snapshot(false, "c", error: "boom")]));
        Assert.Equal("a", done.AccountLabel);
    }
}
