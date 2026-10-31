using System.Text.Json;
using NotchWin.Core.Hosting;
using NotchWin.Core.Music;
using NotchWin.Core.Settings;
using WinMac.Ipc;
using Xunit;

namespace NotchWin.Core.Tests;

public class CentralSettingsTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void FromJson_ReadsEveryCentralFieldAndIgnoresUnknownOnes()
    {
        var c = CentralSettings.FromJson(J("""
            {"position":"TopRight","glassLevel":80,"musicEnabled":false,"agentsEnabled":true,"musicPlayer":"Feishin","customPlayerPath":"C:\\x.exe",
             "lyricsEnabled":true,"lyricsInPill":true,"pollIntervalSeconds":45,"mañana":1,"sections":[{"id":"timer","enabled":true},{"id":"ghost","enabled":true}]}
            """))!;
        Assert.Equal(NotchPosition.TopRight, c.Position);
        Assert.Equal(80, c.GlassLevel);
        Assert.False(c.MusicEnabled);
        Assert.Equal(MusicPlayerKind.Feishin, c.MusicPlayer);
        Assert.Equal("C:\\x.exe", c.CustomPlayerPath);
        Assert.True(c.LyricsEnabled && c.LyricsInPill);
        Assert.Equal(45, c.PollIntervalSeconds);
        Assert.Equal("timer", c.Sections[0].Id);
        Assert.DoesNotContain(c.Sections, s => s.Id == "ghost");
        Assert.Equal(NotchSections.AllIds.Count, c.Sections.Count);
    }

    [Fact]
    public void FromJson_ClampsRangesAndKeepsDefaultsForInvalidValues()
    {
        var c = CentralSettings.FromJson(J("""{"glassLevel":900,"pollIntervalSeconds":1,"position":"Bottom","musicPlayer":"Winamp","lyricsEnabled":"sí"}"""))!;
        Assert.Equal(100, c.GlassLevel);
        Assert.Equal(5, c.PollIntervalSeconds);
        Assert.Equal(NotchPosition.TopCenter, c.Position);
        Assert.Equal(MusicPlayerKind.Spotify, c.MusicPlayer);
        Assert.False(c.LyricsEnabled);
        Assert.Null(CentralSettings.FromJson(J("[1]")));
    }

    [Fact]
    public void ApplyTo_And_From_RoundTripTheCentralFieldsWithoutTouchingLocalOnes()
    {
        var local = new AppSettings { ClaudeHomeOverride = "D:\\claude", CalendarIcsPath = "D:\\cal.ics", DownloadsFolder = "D:\\dl", GlassLevel = 10 };
        var c = new CentralSettings { GlassLevel = 77, LyricsEnabled = true, Position = NotchPosition.TopLeft, MusicEnabled = false };
        c.ApplyTo(local);

        Assert.Equal(77, local.GlassLevel);
        Assert.True(local.LyricsEnabled);
        Assert.False(local.MusicModuleEnabled);
        Assert.Equal("D:\\claude", local.ClaudeHomeOverride);   // lo local no se toca
        Assert.Equal("D:\\cal.ics", local.CalendarIcsPath);
        Assert.Equal("D:\\dl", local.DownloadsFolder);
        Assert.Equal(CentralChanges.None, CentralSettings.Diff(c, CentralSettings.From(local)));
    }

    [Fact]
    public void Diff_ReportsExactlyWhatChanged()
    {
        var a = new CentralSettings();
        Assert.Equal(CentralChanges.None, CentralSettings.Diff(a, new CentralSettings()));
        Assert.Equal(CentralChanges.Glass, CentralSettings.Diff(a, new CentralSettings { GlassLevel = 5 }));
        Assert.Equal(CentralChanges.Position | CentralChanges.Poll, CentralSettings.Diff(a, new CentralSettings { Position = NotchPosition.TopLeft, PollIntervalSeconds = 99 }));
        Assert.Equal(CentralChanges.Player, CentralSettings.Diff(a, new CentralSettings { CustomPlayerPath = "x" }));
        Assert.Equal(CentralChanges.Lyrics, CentralSettings.Diff(a, new CentralSettings { LyricsInPill = true }));
        var moved = new CentralSettings { Sections = [.. a.Sections.AsEnumerable().Reverse()] };
        Assert.Equal(CentralChanges.Sections, CentralSettings.Diff(a, moved));
        Assert.Equal(CentralChanges.MusicModule | CentralChanges.AgentsModule, CentralSettings.Diff(a, new CentralSettings { MusicEnabled = false, AgentsEnabled = false }));
    }

    [Fact]
    public void Patches_UseTheWireNamesTheHostExpects()
    {
        Assert.Equal("{\"glassLevel\":100}", JsonSerializer.Serialize(CentralSettings.GlassPatch(500)));
        var json = JsonSerializer.Serialize(CentralSettings.SectionsPatch([new NotchSectionConfig { Id = "music", Enabled = false }]));
        Assert.Equal("{\"sections\":[{\"id\":\"music\",\"enabled\":false}]}", json);
    }
}

public class HostedSessionTests
{
    private static async Task<bool> Until(Func<bool> c, int ms = 8000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { if (c()) return true; await Task.Delay(20); }
        return c();
    }

    [Fact]
    public void WithoutHostedArgsThereIsNoSessionSoNotchWinRunsStandalone()
    {
        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, null);
        Assert.Null(HostedSession.FromArgs([]));
        Assert.Null(HostedSession.FromArgs(["--hosted"]));
        Assert.Null(HostedSession.FromArgs(["--hosted", "--pipe", "x"]));   // sin token tampoco
    }

    [Fact]
    public async Task ItReceivesTheHostSettingsOnConnectSendsPatchesBackAndHandlesHostCommands()
    {
        var pipe = "notchwin-test-" + Guid.NewGuid().ToString("N");
        await using var server = new IpcServer(new ServerOptions(pipe, "tok"));
        server.Start();
        var patches = new List<string>();
        server.MessageReceived += (m, e) => { if (e.T == MsgType.Evt && e.Topic == CentralSettings.SetTopic) lock (patches) patches.Add(m + ":" + e.Data); };
        server.Publish(CentralSettings.Topic, new { glassLevel = 33, position = "TopLeft", lyricsEnabled = true });   // el host ya tenía estado antes de que el notch conectara

        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, "tok");
        HostedSession session;
        try { session = HostedSession.FromArgs(["--hosted", "--pipe", pipe], "1")!; }
        finally { Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, null); }
        Assert.NotNull(session);
        await using var _ = session;

        var got = new List<CentralSettings>();
        var open = 0;
        var shutdown = 0;
        session.CentralReceived += c => { lock (got) got.Add(c); };
        session.OpenLocalSettingsRequested += () => Interlocked.Increment(ref open);
        session.ShutdownRequested += () => Interlocked.Increment(ref shutdown);
        session.HealthCheck = () => true;
        session.Start();

        Assert.True(await Until(() => { lock (got) return got.Count >= 1; }), "no recibió los ajustes retenidos");
        lock (got) { Assert.Equal(33, got[0].GlassLevel); Assert.Equal(NotchPosition.TopLeft, got[0].Position); Assert.True(got[0].LyricsEnabled); }

        session.SendPatch(CentralSettings.GlassPatch(55));
        Assert.True(await Until(() => { lock (patches) return patches.Count == 1; }));
        Assert.Equal("notch:{\"glassLevel\":55}", patches[0]);

        server.Publish(CentralSettings.Topic, new { glassLevel = 90 });
        Assert.True(await Until(() => { lock (got) return got.Any(c => c.GlassLevel == 90); }), "no recibió el cambio posterior");

        server.Emit(CentralSettings.CommandTopic, new { cmd = "open-local-settings" });
        server.Emit(CentralSettings.CommandTopic, new { cmd = "algo-desconocido" });   // un comando que esta versión no conoce se ignora
        Assert.True(await Until(() => open == 1));
        await Task.Delay(150);
        Assert.Equal(1, open);

        Assert.True(server.Send("notch", MsgType.Shutdown));
        Assert.True(await Until(() => shutdown == 1));
    }
}
