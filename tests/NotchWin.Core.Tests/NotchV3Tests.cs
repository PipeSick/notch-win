using NotchWin.Core.Activities;
using NotchWin.Core.Notch.V3;
using NotchWin.Core.Notifications;
using NotchWin.Core.Timers;
using WinMac.Calendar;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class HomeFocusTests
{
    private static HomeContext C(bool track = false, bool playing = false, bool run = false, bool paused = false, int dl = 0, int notif = 0) => new(track, playing, run, paused, dl, notif);

    [Fact]
    public void Playing_music_wins_then_a_timer_then_a_download_then_paused_music_then_nothing()
    {
        Assert.Equal(HomePrimary.Music, HomeFocus.Primary(C(track: true, playing: true, run: true, dl: 2)));
        Assert.Equal(HomePrimary.Timer, HomeFocus.Primary(C(track: true, playing: false, run: true, dl: 2)));   // una canción en pausa no le gana a un temporizador
        Assert.Equal(HomePrimary.Timer, HomeFocus.Primary(C(paused: true)));
        Assert.Equal(HomePrimary.Download, HomeFocus.Primary(C(dl: 1)));
        Assert.Equal(HomePrimary.Music, HomeFocus.Primary(C(track: true)));
        Assert.Equal(HomePrimary.None, HomeFocus.Primary(C()));
    }

    [Fact]
    public void Secondaries_never_repeat_the_primary_follow_a_fixed_order_and_are_bounded()
    {
        var c = C(track: true, playing: true, run: true, dl: 1, notif: 4);
        Assert.Equal([HomeSecondary.Notifications, HomeSecondary.Downloads, HomeSecondary.Timer], HomeFocus.Secondary(c));
        Assert.DoesNotContain(HomeSecondary.Music, HomeFocus.Secondary(c, 10));
        Assert.Single(HomeFocus.Secondary(c, 1));
        Assert.Empty(HomeFocus.Secondary(C()));
        Assert.Equal([HomeSecondary.Music], HomeFocus.Secondary(C(track: true, run: true)));   // principal = temporizador; la canción en pausa queda como secundaria
    }
}

public sealed class TimerViewTests
{
    private static readonly TimeSpan Total = TimeSpan.FromMinutes(25);

    [Fact]
    public void The_phase_follows_the_real_timer_through_its_whole_life()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); var t = new CountdownTimer(() => now); t.SetDuration(Total);
        TimerPhase P() => TimerView.Phase(TimerStates.Of(t));
        Assert.Equal(TimerPhase.Ready, P()); Assert.False(TimerView.ShowsInCapsule(P()));
        t.Start(); Assert.Equal(TimerPhase.Running, P()); Assert.True(TimerView.ShowsInCapsule(P()));
        now = now.AddMinutes(10); t.Pause(); Assert.Equal(TimerPhase.Paused, P()); Assert.True(TimerView.ShowsInCapsule(P()));
        t.Start(); now = now.AddMinutes(16); Assert.True(t.TryComplete()); Assert.Equal(TimerPhase.Finished, P());
        t.Cancel(); Assert.Equal(TimerPhase.Ready, P());
    }

    [Fact]
    public void Restart_goes_back_to_the_full_time_and_keeps_running_while_cancel_discards_it()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); var t = new CountdownTimer(() => now); t.SetDuration(Total);
        t.Start(); now = now.AddMinutes(20); Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining);
        t.Restart(); Assert.True(t.IsRunning); Assert.Equal(Total, t.Remaining);   // vuelve a 25:00 y sigue corriendo
        now = now.AddMinutes(1); t.Pause(); t.Restart(); Assert.True(t.IsRunning); Assert.Equal(Total, t.Remaining);   // también desde la pausa
        t.Cancel(); Assert.False(t.IsRunning); Assert.False(t.IsArmed); Assert.Equal(Total, t.Remaining); Assert.Equal(TimerPhase.Ready, TimerView.Phase(TimerStates.Of(t)));
    }

    [Fact]
    public void Buttons_depend_on_the_phase_and_presets_are_locked_while_running()
    {
        Assert.Equal([TimerAction.Start], TimerView.Actions(TimerPhase.Ready));
        Assert.Equal([TimerAction.Pause, TimerAction.Restart, TimerAction.Cancel], TimerView.Actions(TimerPhase.Running));
        Assert.Equal([TimerAction.Resume, TimerAction.Restart, TimerAction.Cancel], TimerView.Actions(TimerPhase.Paused));
        Assert.Equal([TimerAction.Restart, TimerAction.Cancel], TimerView.Actions(TimerPhase.Finished));
        Assert.False(TimerView.PresetsEnabled(TimerPhase.Running)); Assert.True(TimerView.PresetsEnabled(TimerPhase.Paused));
        Assert.Equal("Volver a empezar", TimerView.ActionLabel(TimerAction.Restart, TimerPhase.Finished)); Assert.Equal("Reiniciar", TimerView.ActionLabel(TimerAction.Restart, TimerPhase.Running));
        Assert.Equal("Descartar", TimerView.ActionLabel(TimerAction.Cancel, TimerPhase.Finished)); Assert.Equal("Cancelar", TimerView.ActionLabel(TimerAction.Cancel, TimerPhase.Paused));
        Assert.Equal([5, 15, 25, 50, 90], TimerView.PresetMinutes);
    }
}

public sealed class CalendarMonthTests
{
    [Fact]
    public void November_2026_starts_on_a_Sunday_so_a_monday_first_grid_begins_on_26_October_and_has_six_weeks()
    {
        var g = CalendarMonth.Grid(2026, 11, new DateOnly(2026, 11, 24));
        Assert.Equal(6, g.Count); Assert.All(g, w => Assert.Equal(7, w.Count));   // el mockup aprobado también muestra 6 filas (26 oct … 6 dic)
        Assert.Equal(new DateOnly(2026, 10, 26), g[0][0].Date); Assert.False(g[0][0].InMonth);
        Assert.Equal(new DateOnly(2026, 11, 1), g[0][6].Date); Assert.True(g[0][6].InMonth);
        Assert.Equal(new DateOnly(2026, 11, 24), g.SelectMany(w => w).Single(c => c.IsToday).Date);
        Assert.Equal(new DateOnly(2026, 12, 6), g[5][6].Date);
    }

    [Fact]
    public void Event_counts_reach_the_cells_and_a_four_week_february_gets_four_rows()
    {
        var g = CalendarMonth.Grid(2026, 8, new DateOnly(2026, 8, 1), d => d.Day == 15 ? 2 : 0);   // agosto de 2026 empieza en sábado y tiene 31 días
        Assert.Equal(6, g.Count); Assert.Equal(2, g.SelectMany(w => w).Single(c => c.InMonth && c.Date.Day == 15).Events);
        Assert.Equal(4, CalendarMonth.Grid(2027, 2, new DateOnly(2027, 2, 1)).Count);   // febrero de 2027: 28 días y empieza en lunes
        Assert.Equal("noviembre", CalendarMonth.MonthName(11));
    }

    [Fact]
    public void Events_of_a_day_are_sorted_all_day_first_and_a_late_event_that_crosses_midnight_shows_on_both_days()
    {
        var day = new DateOnly(2026, 11, 24); DateTimeOffset At(int d, int h, int m = 0) => new(new DateTime(2026, 11, d, h, m, 0, DateTimeKind.Local));
        var evs = new List<CalendarEvent> { new("Cena", At(24, 21), At(25, 1), false), new("Reunión", At(24, 10, 30), At(24, 11, 15), false), new("Feriado", At(24, 0), At(25, 0), true), new("Otro día", At(26, 9), At(26, 10), false) };
        Assert.Equal(["Feriado", "Reunión", "Cena"], CalendarMonth.EventsOn(evs, day).Select(e => e.Title));
        Assert.Contains(CalendarMonth.EventsOn(evs, day.AddDays(1)), e => e.Title == "Cena");
        Assert.DoesNotContain(CalendarMonth.EventsOn(evs, day), e => e.Title == "Otro día");
    }
}

public sealed class ClipClassifierTests
{
    [Theory]
    [InlineData("https://hatsu.app/descarga", ClipKind.Link)]
    [InlineData("www.ejemplo.cl", ClipKind.Link)]
    [InlineData(@"C:\Users\ana\Documentos\informe.pdf", ClipKind.Path)]
    [InlineData("npm run build", ClipKind.Code)]
    [InlineData("const x = 5;", ClipKind.Code)]
    [InlineData("{ \"a\": 1, \"b\": [2, 3] }", ClipKind.Code)]
    [InlineData("Reunión viernes 10:30, sala 2", ClipKind.Text)]
    [InlineData("Compré pan, leche y huevos para la semana.", ClipKind.Text)]
    [InlineData("visita https://hatsu.app y mira", ClipKind.Text)]   // una frase con un enlace dentro sigue siendo texto
    [InlineData("", ClipKind.Text)]
    public void Content_is_classified_conservatively(string text, ClipKind expected) => Assert.Equal(expected, ClipClassifier.Classify(text));

    [Fact]
    public void Multi_line_indented_code_is_code_images_win_and_previews_are_compact()
    {
        Assert.Equal(ClipKind.Code, ClipClassifier.Classify("function f(a) {\n    return a + 1;\n}"));
        Assert.Equal(ClipKind.Image, ClipClassifier.Classify(null, isImage: true));
        Assert.Equal("hola mundo", ClipClassifier.Preview("  hola \n   mundo ", ClipKind.Text));
        Assert.Equal(20, ClipClassifier.Preview(new string('x', 200), ClipKind.Text, 20).Length);
        Assert.Equal("hatsu.app", ClipClassifier.LinkHost("https://www.hatsu.app/descarga")); Assert.Null(ClipClassifier.LinkHost("no es un enlace"));
        Assert.Equal("Enlace", ClipClassifier.KindLabel(ClipKind.Link));
    }
}

public sealed class DownloadViewTests
{
    [Fact]
    public void File_kinds_come_from_the_extension_and_states_are_always_words()
    {
        Assert.Equal(FileKind.Installer, DownloadView.Classify("instalador.MSIX")); Assert.Equal(FileKind.Archive, DownloadView.Classify("a.zip")); Assert.Equal(FileKind.Other, DownloadView.Classify("sin_extension")); Assert.Equal(FileKind.Other, DownloadView.Classify(null));
        Assert.Equal("Descargando", DownloadView.StateLabel(ActivityState.Active)); Assert.Equal("Fallida", DownloadView.StateLabel(ActivityState.Failed)); Assert.Equal("Cancelada", DownloadView.StateLabel(ActivityState.Cancelled));
        Assert.Equal("Instalador", DownloadView.KindLabel(FileKind.Installer));
    }

    [Fact]
    public void Downloads_split_into_active_and_finished_newest_first_ignoring_other_kinds()
    {
        Activity A(string id, ActivityKind k, ActivityState s, int min) => new(id, k, id) { State = s, CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(min) };
        var (active, finished) = DownloadView.Split([A("viejo", ActivityKind.Download, ActivityState.Active, 1), A("nuevo", ActivityKind.Download, ActivityState.Paused, 5), A("hecho", ActivityKind.Download, ActivityState.Completed, 3), A("roto", ActivityKind.Download, ActivityState.Failed, 4), A("musica", ActivityKind.Media, ActivityState.Active, 9)]);
        Assert.Equal(["nuevo", "viejo"], active.Select(a => a.Id)); Assert.Equal(["roto", "hecho"], finished.Select(a => a.Id));
    }
}

public sealed class NotificationViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
    private static NotchNotification N(string src, string? key, DateTimeOffset at) => new(src, "t", "m", at, key);

    [Fact]
    public void Filters_use_only_what_the_data_says_windows_entries_versus_hatsu_own_notices()
    {
        var items = new[] { N("Teams", "win:1", Now), N("Temporizador", null, Now), N("WhatsApp", "win:2", Now) };
        Assert.Equal(2, NotificationView.Filter(items, NotificationFilter.Apps).Count); Assert.Single(NotificationView.Filter(items, NotificationFilter.Hatsu)); Assert.Equal(3, NotificationView.Filter(items, NotificationFilter.All).Count);
        Assert.Equal("Todas", NotificationView.FilterLabel(NotificationFilter.All));
    }

    [Theory]
    [InlineData(10, "ahora")]
    [InlineData(120, "Hace 2 min")]
    [InlineData(60 * 90, "Hace 1 h")]
    [InlineData(-30, "ahora")]   // reloj desfasado: nunca negativo
    public void Relative_time_reads_naturally(int secondsAgo, string expected) => Assert.Equal(expected, NotificationView.Relative(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void Older_notifications_say_yesterday_days_or_the_date()
    {
        Assert.Equal("Ayer", NotificationView.Relative(Now.AddDays(-1).AddHours(-2), Now)); Assert.Equal("Hace 3 d", NotificationView.Relative(Now.AddDays(-3), Now)); Assert.Equal("10-11-2026", NotificationView.Relative(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), Now));
    }
}

public sealed class MediaCapabilityTests
{
    private sealed class Q(string app) : NotchWin.Core.Music.IMediaQueueSource { public string ProviderName => "Q"; public bool Handles(string a) => a.Contains(app, StringComparison.OrdinalIgnoreCase); public Task<IReadOnlyList<NotchWin.Core.Music.QueueItem>> GetQueueAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<NotchWin.Core.Music.QueueItem>>([]); }

    [Fact]
    public void Queue_and_favorites_only_appear_when_a_provider_recognizes_the_source_app()
    {
        var session = NotchWin.Core.Music.MediaCapabilities.PlayPause | NotchWin.Core.Music.MediaCapabilities.Next | NotchWin.Core.Music.MediaCapabilities.Shuffle;
        Assert.Equal(session, NotchWin.Core.Music.MediaFeatureResolver.Effective(session, "Spotify.exe", null, null));   // sin proveedor: nada inventado
        var withQueue = NotchWin.Core.Music.MediaFeatureResolver.Effective(session, "Spotify.exe", [new Q("spotify")], null);
        Assert.True(withQueue.HasFlag(NotchWin.Core.Music.MediaCapabilities.Queue)); Assert.False(withQueue.HasFlag(NotchWin.Core.Music.MediaCapabilities.Favorite));
        Assert.False(NotchWin.Core.Music.MediaFeatureResolver.Effective(session, "chrome.exe", [new Q("spotify")], null).HasFlag(NotchWin.Core.Music.MediaCapabilities.Queue));   // otra app: sin cola
    }

    [Fact]
    public void Repeat_cycles_off_list_track_and_back_and_the_source_gets_a_readable_name()
    {
        var m = NotchWin.Core.Music.RepeatMode.Off; var seen = new List<NotchWin.Core.Music.RepeatMode>();
        for (int i = 0; i < 4; i++) { m = NotchWin.Core.Music.MediaFeatureResolver.NextRepeat(m); seen.Add(m); }
        Assert.Equal([NotchWin.Core.Music.RepeatMode.List, NotchWin.Core.Music.RepeatMode.Track, NotchWin.Core.Music.RepeatMode.Off, NotchWin.Core.Music.RepeatMode.List], seen);
        Assert.Equal("Spotify", NotchWin.Core.Music.MediaFeatureResolver.SourceName("Spotify.exe")); Assert.Equal("Edge", NotchWin.Core.Music.MediaFeatureResolver.SourceName("MSEdge")); Assert.Equal("", NotchWin.Core.Music.MediaFeatureResolver.SourceName(null));
        Assert.Equal("Repetir canción", NotchWin.Core.Music.MediaFeatureResolver.RepeatLabel(NotchWin.Core.Music.RepeatMode.Track));
    }

    [Fact]
    public void A_shuffle_or_repeat_change_made_in_the_player_counts_as_a_difference_to_repaint()
    {
        NotchWin.Core.Music.NowPlayingInfo I(bool? shuffle, NotchWin.Core.Music.RepeatMode? rep) => new("t", "a", true, TimeSpan.Zero, TimeSpan.FromMinutes(3), "Spotify", null, null, new NotchWin.Core.Music.MediaExtras(NotchWin.Core.Music.MediaCapabilities.Shuffle, shuffle, rep, Genres: ["pop"]));
        Assert.False(NotchWin.Core.Music.PlaybackReconcile.Differs(I(false, NotchWin.Core.Music.RepeatMode.Off), I(false, NotchWin.Core.Music.RepeatMode.Off)));   // listas de géneros iguales por valor
        Assert.True(NotchWin.Core.Music.PlaybackReconcile.Differs(I(false, NotchWin.Core.Music.RepeatMode.Off), I(true, NotchWin.Core.Music.RepeatMode.Off)));
        Assert.True(NotchWin.Core.Music.PlaybackReconcile.Differs(I(false, NotchWin.Core.Music.RepeatMode.Off), I(false, NotchWin.Core.Music.RepeatMode.Track)));
    }
}

public sealed class NotificationInboxTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static NotchNotification N(string app, string key, int minAgo, string? aumid = null) => new(app, "título", "mensaje", T.AddMinutes(-minAgo), key, aumid);

    [Fact]
    public void New_is_only_a_local_seen_state_and_marking_seen_never_touches_the_items()
    {
        var inbox = new NotificationInbox(); var a = N("Teams", "win:1", 5); var b = N("WhatsApp", "win:2", 1); var list = new[] { a, b };
        Assert.Equal(2, inbox.NewCount(list)); Assert.True(inbox.IsNew(a));
        inbox.MarkSeen([a]); Assert.False(inbox.IsNew(a)); Assert.True(inbox.IsNew(b)); Assert.Equal(1, inbox.NewCount(list));
        inbox.MarkSeen(list); Assert.Equal(0, inbox.NewCount(list));
    }

    [Fact]
    public void What_disappears_from_the_windows_list_goes_to_the_session_history_newest_first_and_is_bounded()
    {
        var inbox = new NotificationInbox(); var a = N("Teams", "win:1", 5); var b = N("WhatsApp", "win:2", 1);
        inbox.Observe([a, b]); Assert.Empty(inbox.History);
        inbox.Observe([b]); Assert.Equal([a], inbox.History);
        inbox.Observe([]); Assert.Equal([b, a], inbox.History);
        var many = Enumerable.Range(0, 150).Select(i => N("App", "win:x" + i, i)).ToList(); inbox.Observe(many); inbox.Observe([]);
        Assert.Equal(NotificationInbox.MaxHistory, inbox.History.Count); inbox.ClearHistory(); Assert.Empty(inbox.History);
    }

    [Fact]
    public void Groups_by_app_with_the_most_recent_group_first_and_per_group_new_counts()
    {
        var inbox = new NotificationInbox(); var items = new[] { N("Teams", "win:1", 30, "MSTeams!App"), N("WhatsApp", "win:2", 1), N("Teams", "win:3", 10), N("whatsapp", "win:4", 20) };
        inbox.MarkSeen([items[3]]);
        var g = inbox.GroupByApp(items);
        Assert.Equal(["WhatsApp", "Teams"], g.Select(x => x.App)); Assert.Equal(2, g[0].Items.Count); Assert.Equal(1, g[0].NewCount); Assert.Equal(2, g[1].Items.Count); Assert.Equal("MSTeams!App", g[1].AppId);
        Assert.Equal("win:2", g[0].Items[0].Key);   // dentro del grupo, lo más reciente primero
    }
}
