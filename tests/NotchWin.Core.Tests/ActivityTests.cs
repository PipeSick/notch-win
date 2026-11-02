using System.Text.Json;
using NotchWin.Core.Activities;
using NotchWin.Core.Pill;
using Xunit;

namespace NotchWin.Core.Tests;

public class ActivityStoreTests
{
    private DateTime _now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private ActivityStore Make() => new(() => _now);

    [Fact]
    public void RegisterUpdateFinishAndHistory()
    {
        var s = Make(); int changes = 0; s.Changed += () => changes++;
        Assert.True(s.Upsert(new Activity("d1", ActivityKind.Download, "Windows 11.iso") { Progress = 0.2 }));
        _now = _now.AddSeconds(5);
        Assert.True(s.Update("d1", a => a with { Progress = 1.7 }));      // clamped
        Assert.Equal(1.0, s.Current[0].Progress);
        Assert.Equal(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), s.Current[0].CreatedUtc);   // created stays, updated moves
        Assert.True(s.Finish("d1"));
        Assert.Empty(s.Current); Assert.Equal(ActivityState.Completed, s.History[0].State);
        Assert.True(s.Remove("d1")); Assert.Empty(s.History);
        Assert.Equal(4, changes);
        Assert.False(s.Update("nope", a => a)); Assert.False(s.Finish("nope"));
    }

    [Fact]
    public void SimultaneousActivitiesAreOrderedByPriorityThenRecency()
    {
        var s = Make();
        s.Upsert(new Activity("c", ActivityKind.Capture, "Captura"));
        _now = _now.AddSeconds(1); s.Upsert(new Activity("d", ActivityKind.Download, "Descarga"));
        _now = _now.AddSeconds(1); s.Upsert(new Activity("m", ActivityKind.Media, "Canción"));
        _now = _now.AddSeconds(1); s.Upsert(new Activity("d2", ActivityKind.Download, "Otra descarga"));
        Assert.Equal(["m", "d2", "d", "c"], s.Current.Select(a => a.Id));
    }

    [Fact]
    public void InvalidInputIsRejectedAndListsAreCapped()
    {
        var s = Make();
        Assert.False(s.Upsert(new Activity("", ActivityKind.Download, "x")));
        Assert.False(s.Upsert(new Activity("x", ActivityKind.Download, "  ")));
        for (int i = 0; i < 40; i++) { _now = _now.AddSeconds(1); s.Upsert(new Activity("n" + i, ActivityKind.Notification, "n" + i)); }
        Assert.Equal(ActivityStore.MaxCurrent, s.Current.Count);
        Assert.DoesNotContain(s.Current, a => a.Id == "n0");   // the oldest of the least important goes
        for (int i = 0; i < 130; i++) s.Upsert(new Activity("h" + i, ActivityKind.Capture, "c") { State = ActivityState.Completed });
        Assert.Equal(ActivityStore.MaxHistory, s.History.Count);
        Assert.False(s.Update("n39", _ => throw new InvalidOperationException()));   // a faulty change does nothing
    }

    [Fact]
    public void FinishedMediaIsDiscardedAndStaleActivitiesNeverStayInProgress()
    {
        var s = Make();
        s.Upsert(new Activity("m", ActivityKind.Media, "Canción") { End = ActivityEnd.Discard });
        s.Upsert(new Activity("d", ActivityKind.Download, "Descarga"));
        s.Finish("m"); Assert.Empty(s.History);
        _now = _now.AddMinutes(11);
        Assert.Equal(1, s.ExpireStale());
        Assert.Empty(s.Current); Assert.Equal(ActivityState.Failed, s.History[0].State);
    }

    [Fact]
    public void AFaultySubscriberDoesNotBreakTheOthers()
    {
        var s = Make(); int ok = 0;
        s.Changed += () => throw new InvalidOperationException("vista rota");
        s.Changed += () => ok++;
        s.Upsert(new Activity("a", ActivityKind.Capture, "c"));
        Assert.Equal(1, ok); Assert.Single(s.Current);
    }

    [Fact]
    public void OnlyRealActionsAreOffered()
    {
        Assert.Equal(ActivityAction.Open | ActivityAction.ShowInFolder | ActivityAction.Copy | ActivityAction.Delete,
            ActivityActions.Supported(new Activity("c", ActivityKind.Capture, "c") { Path = @"C:\x.png" }));
        Assert.Equal(ActivityAction.None, ActivityActions.Supported(new Activity("d", ActivityKind.Download, "d") { Path = @"C:\x.iso" }));   // in progress: no fake pause/cancel
        Assert.Equal(ActivityAction.Open | ActivityAction.ShowInFolder | ActivityAction.RemoveFromHistory,
            ActivityActions.Supported(new Activity("d", ActivityKind.Download, "d") { Path = @"C:\x.iso", State = ActivityState.Completed }));
        Assert.Equal(ActivityAction.None, ActivityActions.Supported(new Activity("m", ActivityKind.Media, "m") { IsTest = true }));   // test media: no controls
        Assert.Equal(ActivityAction.None, ActivityActions.Supported(new Activity("r", ActivityKind.Recording, "r")));                 // no recorder yet
    }
}

public class ActivityFeedTests
{
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void ParsesAndAppliesInternalCommandsSafely()
    {
        var s = new ActivityStore(() => DateTime.UtcNow);
        var up = ActivityFeed.Parse(J("""{"op":"upsert","activity":{"id":"m1","kind":"media","title":"Canción de prueba","subtitle":"Artista","progress":0.3,"test":true}}"""))!;
        Assert.True(ActivityFeed.Apply(s, up));
        var a = s.Current.Single(); Assert.Equal("ipc:m1", a.Id); Assert.True(a.IsTest); Assert.Equal(0.3, a.Progress);
        Assert.True(ActivityFeed.Apply(s, ActivityFeed.Parse(J("""{"op":"finish","activity":{"id":"m1"}}"""))!));
        Assert.Empty(s.Current);
        Assert.Null(ActivityFeed.Parse(J("""{"op":"upsert","activity":{"id":"x","kind":"virus","title":"t"}}""")));
        Assert.Null(ActivityFeed.Parse(J("""{"op":"rm -rf","activity":{"id":"x"}}""")));
        var bad = ActivityFeed.Parse(J("""{"op":"upsert","activity":{"id":"x","kind":"capture","title":"t","path":"\\\\server\\share\\a.png","thumbnail":"https://x/y.png"}}"""))!;
        Assert.Null(bad.Activity!.Path); Assert.Null(bad.Activity.ThumbnailPath);   // only local absolute paths
    }
}

/// <summary>Synced lyrics: the compact Island keeps its rule, and transient activities give the lyrics back when they end.</summary>
public class LyricsRegressionTests
{
    [Fact]
    public void LyricsRuleIsUnchanged()
    {
        Assert.Equal(CompactContent.Lyrics, CompactPolicy.Choose(true, true, true, true, 40, downloadActive: true, timerRunning: true));   // nothing displaces lyrics
        Assert.Equal(CompactContent.Track, CompactPolicy.Choose(true, false, true, true, 40, false, false));    // paused: the track, like before
        Assert.Equal(CompactContent.Lyrics, CompactPolicy.Choose(true, true, true, true, 40, false, false));    // resumed: lyrics again
        Assert.Equal(CompactContent.Track, CompactPolicy.Choose(true, true, true, true, 0, false, false));      // new song, lyrics still loading
        Assert.Equal(CompactContent.Track, CompactPolicy.Choose(true, true, true, false, 40, false, false));    // «letra en el notch» off
        Assert.Equal(CompactContent.Download, CompactPolicy.Choose(false, false, true, true, 0, true, true));
        Assert.Equal(CompactContent.Timer, CompactPolicy.Choose(false, false, false, false, 0, false, true));
        Assert.Equal(CompactContent.Idle, CompactPolicy.Choose(false, false, false, false, 0, false, false));
    }

    [Fact]
    public void InterruptionsByOtherActivitiesGiveTheLyricsBack()
    {
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var a = new PillArbiter(PillPolicy.FromSettings(), () => now);
        a.Post(new PillItem(PillKind.Capture, "Captura guardada", "Escritorio"));
        a.Post(new PillItem(PillKind.Notification, "Correo", "Hola"));
        a.Post(new PillItem(PillKind.Volume, "Altavoces", null, 40));
        for (int i = 0; i < 50 && a.NextWait() is { } w; i++) { now += w; a.Tick(); }
        Assert.Null(a.Current);   // nothing on top → the compact Island shows its usual content again: the lyric line (rule above)
        Assert.Equal(CompactContent.Lyrics, CompactPolicy.Choose(true, true, true, true, 40, false, false));
    }
}

public class CaptureLibraryTests
{
    [Fact]
    public void ListsOnlyImagesNewestFirstAndCaps()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 15; i++) { var f = Path.Combine(dir, $"c{i}.png"); File.WriteAllBytes(f, [1, 2, 3]); File.SetLastWriteTimeUtc(f, new DateTime(2026, 1, 1).AddMinutes(i)); }
            File.WriteAllText(Path.Combine(dir, "nota.txt"), "x");
            var l = CaptureLibrary.List(dir);
            Assert.Equal(12, l.Count); Assert.Equal("c14.png", l[0].Name); Assert.DoesNotContain(l, c => c.Name.EndsWith(".txt"));
            Assert.Empty(CaptureLibrary.List(Path.Combine(dir, "no-existe")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WhenReadsLikeTheMockup()
    {
        var now = new DateTime(2026, 1, 24, 14, 37, 0);
        Assert.Equal("Hoy 14:36", CaptureLibrary.When(new DateTime(2026, 1, 24, 14, 36, 0), now));
        Assert.Equal("Ayer 20:17", CaptureLibrary.When(new DateTime(2026, 1, 23, 20, 17, 0), now));
        Assert.StartsWith("20 ene", CaptureLibrary.When(new DateTime(2026, 1, 20, 9, 5, 0), now));
    }
}
