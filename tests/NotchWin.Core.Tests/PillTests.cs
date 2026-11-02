using System.Text.Json;
using NotchWin.Core.Hosting;
using NotchWin.Core.Pill;
using Xunit;

namespace NotchWin.Core.Tests;

public class PillArbiterTests
{
    private DateTime _now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private PillArbiter Make(PillPolicy? p = null) => new(p ?? PillPolicy.FromSettings(), () => _now);

    private void Wait(double seconds) => _now = _now.AddSeconds(seconds);

    private static PillItem Vol(int level, bool muted = false) => new(PillKind.Volume, "Altavoces", null, level, muted);
    private static PillItem Bright(int level) => new(PillKind.Brightness, "Pantalla integrada", null, level);
    private static PillItem Note(string title, string body = "texto") => new(PillKind.Notification, title, body) { App = "Teams" };
    private static PillItem Track(string t) => new(PillKind.Track, t, "Artista");

    [Fact]
    public void ConsecutiveVolumeChanges_UpdateTheSameIndicatorAndRestartItsTimer()
    {
        var a = Make(); int changes = 0; a.Changed += () => changes++;
        a.Post(Vol(40)); Wait(1.5); a.Post(Vol(45)); Wait(1.5); a.Post(Vol(50));
        Assert.Equal(50, a.Current!.Level);
        Assert.Equal(0, a.Pending);   // never a queue of stale levels
        Assert.Equal(3, changes);
        Wait(1.9); a.Tick(); Assert.NotNull(a.Current);   // 2 s after the LAST change, not the first
        Wait(0.2); a.Tick(); Assert.Null(a.Current);
    }

    [Fact]
    public void Muted_IsShownAsSilenceWithTheCrossedSpeaker()
    {
        var a = Make(); a.Post(Vol(65, muted: true));
        Assert.True(a.Current!.Muted);
        Assert.Equal("Silencio", a.Current.LevelText);
        Assert.Equal("", a.Current.Glyph);
        a.Post(Vol(65));
        Assert.Equal("65 %", a.Current!.LevelText);
        Assert.Equal("", a.Current.Glyph);
    }

    [Fact]
    public void Brightness_ReplacesItselfAndEndsBackOnTheUsualContent()
    {
        var a = Make(); a.Post(Bright(70)); a.Post(Bright(80));
        Assert.Equal(PillKind.Brightness, a.Current!.Kind); Assert.Equal(80, a.Current.Level); Assert.Equal("", a.Current.Glyph);
        Wait(2.1); a.Tick(); Assert.Null(a.Current);
    }

    [Fact]
    public void SimultaneousNotifications_AreShownOneAfterTheOtherInArrivalOrder()
    {
        var a = Make(); a.Post(Note("uno")); a.Post(Note("dos")); a.Post(Note("tres"));
        Assert.Equal("uno", a.Current!.Title); Assert.Equal(2, a.Pending);
        Wait(5); a.Tick(); Assert.Equal("dos", a.Current!.Title);
        Wait(5); a.Tick(); Assert.Equal("tres", a.Current!.Title);
        Wait(5); a.Tick(); Assert.Null(a.Current);
    }

    [Fact]
    public void VolumeInterruptsANotification_WhichResumesWithTheTimeItHadLeft()
    {
        var a = Make(); a.Post(Note("importante")); Wait(1);
        a.Post(Vol(30));
        Assert.Equal(PillKind.Volume, a.Current!.Kind);
        Wait(2); a.Tick();
        Assert.Equal("importante", a.Current!.Title);   // not lost to the volume animation
        Wait(3.9); a.Tick(); Assert.NotNull(a.Current);
        Wait(0.2); a.Tick(); Assert.Null(a.Current);
    }

    [Fact]
    public void ANotificationWaitsWhileTheVolumeIndicatorIsOn()
    {
        var a = Make(); a.Post(Vol(30)); a.Post(Note("hola"));
        Assert.Equal(PillKind.Volume, a.Current!.Kind); Assert.Equal(1, a.Pending);
        Wait(2); a.Tick(); Assert.Equal("hola", a.Current!.Title);
    }

    [Fact]
    public void NewTrack_DoesNotInterruptANotificationAndOnlyTheLatestTrackIsKept()
    {
        var a = Make(); a.Post(Note("hola")); a.Post(Track("A")); a.Post(Track("B"));
        Assert.Equal("hola", a.Current!.Title); Assert.Equal(1, a.Pending);
        Wait(5); a.Tick(); Assert.Equal("B", a.Current!.Title);
        Wait(3.2); a.Tick(); Assert.Null(a.Current);   // back to the playing track (the usual content)
    }

    [Fact]
    public void RepeatedNotifications_AreShownOnceWithinTheRepeatWindow()
    {
        var a = Make();
        Assert.True(a.Post(Note("x")));
        Assert.False(a.Post(Note("x")));
        Wait(5); a.Tick(); Wait(4); Assert.False(a.Post(Note("x")));
        Wait(2); Assert.True(a.Post(Note("x")));
    }

    [Fact]
    public void AFlood_IsCappedAndTheMostImportantItemsSurvive()
    {
        var a = Make(PillPolicy.FromSettings());
        a.Post(Vol(10));
        for (int i = 0; i < 10; i++) a.Post(Note("n" + i));
        a.Post(Track("t"));
        Assert.Equal(6, a.Pending);
        Assert.True(a.Dropped >= 5);
        var seen = new List<PillItem>();
        for (int i = 0; i < 20 && a.Current is not null; i++) { seen.Add(a.Current); Wait(10); a.Tick(); }
        Assert.DoesNotContain(seen, s => s.Kind == PillKind.Track);   // the least important kind goes first
        Assert.Equal("n4", seen[1].Title);                            // the oldest notifications are dropped, the newest are kept in order
    }

    [Fact]
    public void KindsTurnedOffInSettings_AreIgnoredAndRemovedLive()
    {
        var a = Make(PillPolicy.FromSettings(volume: false));
        Assert.False(a.Post(Vol(10)));
        Assert.Null(a.Current);
        a.Post(Note("hola")); a.Post(Note("adiós"));
        a.SetPolicy(PillPolicy.FromSettings(notifications: false));
        Assert.Null(a.Current); Assert.Equal(0, a.Pending);
    }

    [Fact]
    public void Durations_ComeFromTheSettingsAndAreClamped()
    {
        var p = PillPolicy.FromSettings(hudSeconds: 99, notifySeconds: 0);
        Assert.Equal(TimeSpan.FromSeconds(10), p.Rule(PillKind.Volume).Duration);
        Assert.Equal(TimeSpan.FromSeconds(2), p.Rule(PillKind.Notification).Duration);
    }

    [Fact]
    public void OpeningTheNotch_HidesThePillAndFreezesANotification()
    {
        var a = Make(); a.Post(Note("hola")); Wait(1);
        a.SetPaused(true);
        Assert.Null(a.Current); Assert.Null(a.NextDeadline);
        a.Post(Vol(50));   // the open notch has its own controls: no stale indicator later
        Wait(30); a.Tick();
        a.SetPaused(false);
        Assert.Equal("hola", a.Current!.Title);
        Wait(3.9); a.Tick(); Assert.NotNull(a.Current);
        Wait(0.2); a.Tick(); Assert.Null(a.Current);
    }

    [Fact]
    public void ClosingAndRestartingTheNotch_StartsFromTheUsualContent()
    {
        var a = Make(); a.Post(Note("hola")); a.Post(Note("adiós"));
        a.Clear();
        Assert.Null(a.Current); Assert.Equal(0, a.Pending);
        var fresh = Make();   // a new notch process starts empty: nothing is replayed
        Assert.Null(fresh.Current);
        Assert.True(a.Post(Note("hola")));   // and Clear forgot the repeat window too
    }
}

public class PillFeedTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Volume_ParsesLevelAndMuteAndClamps()
    {
        var v = PillFeed.Parse(J("""{"kind":"volume","level":130.4,"muted":true,"device":"Auriculares"}"""))!;
        Assert.Equal(PillKind.Volume, v.Kind); Assert.Equal(100, v.Level); Assert.True(v.Muted); Assert.Equal("Auriculares", v.Title);
        Assert.Null(PillFeed.Parse(J("""{"kind":"volume"}""")));
    }

    [Fact]
    public void Brightness_WithoutALevelIsNotShown()
    {
        Assert.Null(PillFeed.Parse(J("""{"kind":"brightness","display":"Pantalla 2"}""")));   // a monitor that does not report its value: no invented percentage
        var b = PillFeed.Parse(J("""{"kind":"brightness","level":-5,"display":"Pantalla 2"}"""))!;
        Assert.Equal(0, b.Level); Assert.Equal("Pantalla 2", b.Title);
    }

    [Fact]
    public void Notification_UsesTitleAndBodyAndValidatesTheIcon()
    {
        var px = Convert.ToBase64String(new byte[2 * 2 * 4]);
        var n = PillFeed.Parse(J($$"""{"kind":"notification","app":"Teams","title":"Ana\nLópez","body":"¿Vienes?","icon":"{{px}}","iconW":2,"iconH":2}"""))!;
        Assert.Equal("Ana López", n.Title); Assert.Equal("¿Vienes?", n.Subtitle); Assert.Equal("Teams", n.App); Assert.Equal(16, n.Icon!.Length);
        var bad = PillFeed.Parse(J($$"""{"kind":"notification","app":"Teams","title":"x","icon":"{{px}}","iconW":3,"iconH":2}"""))!;
        Assert.Null(bad.Icon);
        var appOnly = PillFeed.Parse(J("""{"kind":"notification","app":"Correo"}"""))!;
        Assert.Equal("Correo", appOnly.Title);
        Assert.Null(PillFeed.Parse(J("""{"kind":"notification"}""")));
        Assert.Null(PillFeed.Parse(J("""{"kind":"rm -rf"}""")));
        Assert.Null(PillFeed.Parse(J("""[1,2]""")));
    }

    [Fact]
    public void LongText_IsShortened()
    {
        var n = PillFeed.Parse(J($$"""{"kind":"notification","title":"{{new string('a', 500)}}"}"""))!;
        Assert.Equal(PillFeed.MaxTitle, n.Title.Length);
        Assert.EndsWith("…", n.Title);
    }

    [Fact]
    public void CentralSettings_CarryThePillOptions()
    {
        var c = CentralSettings.FromJson(J("""{"pillVolume":false,"pillBrightness":true,"pillNotifications":false,"pillHudSeconds":50,"pillNotifySeconds":7,"pillAnimations":false}"""))!;
        Assert.False(c.PillVolume); Assert.False(c.PillNotifications); Assert.False(c.PillAnimations);
        Assert.Equal(10, c.PillHudSeconds); Assert.Equal(7, c.PillNotifySeconds);
        Assert.False(c.PillPolicy().Rule(PillKind.Volume).Enabled);
        Assert.Equal(CentralChanges.Pill, CentralSettings.Diff(new CentralSettings(), new CentralSettings { PillAnimations = false }));
        var d = new CentralSettings(); Assert.True(d.PillVolume && d.PillBrightness && d.PillNotifications && d.PillAnimations);
    }
}
