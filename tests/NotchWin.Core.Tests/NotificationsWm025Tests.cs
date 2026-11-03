using System.Text.Json;
using NotchWin.Core.Notifications;
using NotchWin.Core.Pill;

namespace NotchWin.Core.Tests;

public sealed class NotificationsWm025Tests
{
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void The_tab_shows_what_windows_holds_merged_with_own_events_newest_first()
    {
        var log = new NotificationLog(); int changes = 0, added = 0; log.Changed += () => changes++; log.Added += _ => added++;
        log.Add("Descargas", "informe.pdf", "Descarga terminada", DateTimeOffset.FromUnixTimeSeconds(2000));
        var snap = PillFeed.ParseSnapshot(J("""{"kind":"notifications","items":[{"id":7,"app":"Teams","appId":"MSTeams!App","title":"Ana","body":"¿Vienes?\nA las 5","at":3000000},{"id":8,"app":"Mail","title":"Factura","at":1000000}]}"""))!;
        log.SetWindows(snap);
        Assert.Equal(["Ana", "informe.pdf", "Factura"], log.Items.Select(n => n.Title));   // beta: «Sin notificaciones» although toasts had arrived
        Assert.Equal("¿Vienes?\nA las 5", log.Items[0].Message);                             // full text (the expanded view shows it)
        Assert.Equal("MSTeams!App", log.Find("win:7")!.AppId);
        Assert.Equal(1, added);                                                                // the snapshot does not raise toasts again (the bar sent the pill)
        log.SetWindows(snap); Assert.Equal(2, changes);                                       // same snapshot: no change
    }

    [Fact]
    public void Clear_hides_windows_items_in_the_island_only_and_forgets_them_when_windows_drops_them()
    {
        var log = new NotificationLog();
        var snap = PillFeed.ParseSnapshot(J("""{"kind":"notifications","items":[{"id":7,"app":"Teams","title":"Ana","at":3000000}]}"""))!;
        log.SetWindows(snap); log.Clear(); Assert.Empty(log.Items);
        log.SetWindows(snap); Assert.Empty(log.Items);                                        // still hidden (it is still in Windows)
        log.SetWindows([]); log.SetWindows(PillFeed.ParseSnapshot(J("""{"kind":"notifications","items":[{"id":9,"app":"Teams","title":"Nueva","at":4000000}]}"""))!);
        Assert.Equal(["Nueva"], log.Items.Select(n => n.Title));
    }

    [Fact]
    public void Pill_notifications_carry_the_windows_id_and_other_feeds_are_not_snapshots()
    {
        var p = PillFeed.Parse(J("""{"kind":"notification","id":7,"app":"Teams","title":"Ana","body":"Hola"}"""))!;
        Assert.Equal("win:7", p.NotificationKey);
        Assert.Null(PillFeed.ParseSnapshot(J("""{"kind":"volume","level":5}""")));
        Assert.Empty(PillFeed.ParseSnapshot(J("""{"kind":"notifications","items":[{"app":"sin id"}]}"""))!);   // untrusted input: items without id are dropped
    }
}
