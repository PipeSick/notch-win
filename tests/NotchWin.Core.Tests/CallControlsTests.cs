using System.Text.Json;
using NotchWin.Core.Pill;
using Xunit;

namespace NotchWin.Core.Tests;

/// <summary>Docs/60 (Hatsu Desktop): the call pill with controls, duration and where the audio is.</summary>
public class CallControlsTests
{
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact] public void Ringing_shows_only_the_buttons_connect_says_work()
    {
        var p = PillFeed.Parse(J("""{"kind":"call","state":"ringing","caller":"Fernando Noc","device":"Android","controls":{"answer":true,"reject":true,"hangup":false,"mute":false}}"""))!;
        Assert.Equal("Llamada de Fernando Noc", p.Title); Assert.Equal("Android", p.Subtitle); Assert.True(p.Controls!.Value.Answer); Assert.True(p.Controls.Value.Reject); Assert.False(p.Controls.Value.Hangup);
        var none = PillFeed.Parse(J("""{"kind":"call","state":"ringing","caller":"X"}"""))!; Assert.False(none.Controls?.Any ?? false);   // old Connect / no controls → just a notice
        var bad = PillFeed.Parse(J("""{"kind":"call","state":"ringing","controls":{"answer":"yes","reject":1}}"""))!; Assert.False(bad.Controls!.Value.Any);   // only real booleans count
    }

    [Fact] public void Active_call_has_duration_audio_location_and_long_life()
    {
        var p = PillFeed.Parse(J("""{"kind":"call","state":"active","caller":"Mamá","since":1000,"audioOnPc":true,"controls":{"hangup":true,"mute":true}}"""))!;
        Assert.True(p.IsActiveCall); Assert.Equal("En llamada · Mamá", p.Title); Assert.True(p.Controls!.Value.Hangup); Assert.Equal(TimeSpan.FromHours(6), p.Duration); Assert.Equal(1000, p.CallSinceMs);
        Assert.Equal("03:25 · Audio en el PC", PillFeed.ActiveSubtitle(1000, true, false, 1000 + 205_000));
        Assert.Equal("1:01:05 · Audio en el teléfono · Silenciado", PillFeed.ActiveSubtitle(0, false, true, 3_665_000));
        Assert.Equal("Audio en el teléfono", PillFeed.ActiveSubtitle(null, false, false, 5));   // unknown start: no invented duration
        Assert.Equal("Audio en el PC", PillFeed.ActiveSubtitle(9_000, true, false, 1_000));     // clock skew: no negative duration
    }

    [Fact] public void A_notice_replaces_the_subtitle_so_the_user_always_learns_when_audio_did_not_move()
    {
        var p = PillFeed.Parse(J("""{"kind":"call","state":"active","since":1,"audioOnPc":false,"notice":"Contestada, pero Windows no pasó el audio al PC: sigue en el teléfono"}"""))!;
        Assert.Contains("sigue en el teléfono", p.Subtitle); Assert.False(p.AudioOnPc);
    }

    [Fact] public void Missed_ended_none_and_unknown_states_keep_their_old_behaviour()
    {
        Assert.Equal("Llamada perdida", PillFeed.Parse(J("""{"kind":"call","state":"missed","caller":"Ana"}"""))!.Title);
        Assert.Equal("Llamada terminada", PillFeed.Parse(J("""{"kind":"call","state":"ended","caller":"Ana","device":"iPhone"}"""))!.Title);
        Assert.True(PillFeed.Parse(J("""{"kind":"call","state":"none"}"""))!.Withdraw);
        Assert.Null(PillFeed.Parse(J("""{"kind":"call","state":"contestar"}""")));
    }

    private static PillArbiter A(Func<DateTime> clock) => new(PillPolicy.FromSettings(), clock);

    [Fact] public void An_active_call_outranks_everything_and_a_notification_or_volume_may_borrow_the_pill_without_destroying_it()
    {
        var t = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc); var a = A(() => t);
        a.Post(PillFeed.Parse(J("""{"kind":"call","state":"active","caller":"Mamá","since":1,"controls":{"hangup":true}}"""))!);
        Assert.True(a.Current!.IsActiveCall);
        a.Post(new PillItem(PillKind.Notification, "WhatsApp", "Hola") { App = "WhatsApp", NotificationKey = "x" });
        Assert.Equal(PillKind.Notification, a.Current!.Kind);                       // shown for its few seconds
        t = t.AddSeconds(6); a.Tick();
        Assert.True(a.Current is { IsActiveCall: true });                             // the call is back, not destroyed
        a.Post(new PillItem(PillKind.Volume, "Altavoces", null, 40)); Assert.Equal(PillKind.Volume, a.Current!.Kind);
        t = t.AddSeconds(3); a.Tick(); Assert.True(a.Current is { IsActiveCall: true });
        a.Withdraw(PillKind.Call); Assert.Null(a.Current);   // when the call ends the notch returns to its usual behaviour
    }

    [Fact] public void Only_the_known_commands_can_be_sent()
    {
        Assert.Equal(["answer", "reject", "hangup", "mute", "route"], NotchWin.Core.Hosting.HostedSession.CallCommands);
        Assert.Equal("connect.callcmd", NotchWin.Core.Hosting.HostedSession.CallCommandTopic);
    }

    [Fact] public void The_route_button_exists_only_for_an_active_call_whose_audio_is_still_on_the_phone()
    {
        var p = PillFeed.Parse(J("""{"kind":"call","state":"active","since":1,"audioOnPc":false,"controls":{"hangup":true,"route":true}}"""))!;
        Assert.True(p.Controls!.Value.Route); Assert.Contains("Audio en el teléfono", PillFeed.ActiveSubtitle(p.CallSinceMs, p.AudioOnPc, false, 5000));
        Assert.False(PillFeed.Parse(J("""{"kind":"call","state":"active","since":1,"audioOnPc":true,"controls":{"hangup":true,"mute":true}}"""))!.Controls!.Value.Route);
    }
}
