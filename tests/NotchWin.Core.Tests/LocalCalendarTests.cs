using NotchWin.Core.Calendar;
using WinMac.Calendar;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class LocalCalendarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatsu-cal-" + Guid.NewGuid().ToString("N"));
    private string File1 => Path.Combine(_dir, "local-calendar.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }
    private static DateTimeOffset At(int d, int h, int m = 0) => new(new DateTime(2026, 11, d, h, m, 0, DateTimeKind.Local));

    [Fact]
    public void Events_are_created_persisted_reloaded_updated_and_removed_locally()
    {
        var s = new LocalCalendarStore(File1); int changes = 0; s.Changed += () => changes++;
        var e = s.Add("  Reunión  ", At(24, 10), At(24, 11), false, "#34c759")!;
        Assert.Equal("Reunión", e.Title); Assert.Equal("#34C759", e.ColorHex); Assert.True(e.Editable); Assert.Equal(CalendarSource.Local, e.Source);
        var again = new LocalCalendarStore(File1); Assert.Single(again.All()); Assert.Equal("Reunión", again.All()[0].Title);
        Assert.True(s.Update(e.Id, "Reunión larga", At(24, 10), At(24, 12), false, "#FF9F0A")); Assert.Equal("Reunión larga", new LocalCalendarStore(File1).All()[0].Title);
        Assert.True(s.Remove(e.Id)); Assert.Empty(new LocalCalendarStore(File1).All()); Assert.False(s.Remove("no-existe")); Assert.Equal(3, changes);
    }

    [Fact]
    public void Invalid_events_are_rejected_titles_are_bounded_and_a_bad_color_falls_back()
    {
        var s = new LocalCalendarStore(File1);
        Assert.Null(s.Add("  ", At(24, 10), At(24, 11), false)); Assert.Null(s.Add(null, At(24, 10), At(24, 11), false)); Assert.Null(s.Add("x", At(24, 12), At(24, 11), false));   // el fin va antes del inicio
        Assert.Equal(LocalCalendarStore.MaxTitle, s.Add(new string('a', 500), At(24, 10), At(24, 11), false)!.Title.Length);
        Assert.Equal(CalendarColors.Palette[3], s.Add("c", At(24, 10), At(24, 11), false, "rojo")!.ColorHex);
        var day = s.Add("todo el día", At(25, 15), At(25, 15), true)!; Assert.True(day.AllDay); Assert.Equal(At(25, 0).DateTime, day.Start.DateTime); Assert.True(day.End > day.Start);
    }

    [Fact]
    public void A_corrupt_file_is_ignored_and_not_overwritten_until_something_is_saved()
    {
        Directory.CreateDirectory(_dir); File.WriteAllText(File1, "{ esto no es json");
        var s = new LocalCalendarStore(File1); Assert.Empty(s.All()); Assert.Equal("{ esto no es json", File.ReadAllText(File1));
        Assert.NotNull(s.Add("nuevo", At(24, 9), At(24, 10), false)); Assert.Single(new LocalCalendarStore(File1).All());
    }

    [Fact]
    public void Colors_are_stable_for_the_same_title_and_valid_hex()
    {
        Assert.Equal(CalendarColors.For("Almuerzo"), CalendarColors.For("almuerzo")); Assert.All(new[] { "a", "b", "Reunión", "" }, t => Assert.True(CalendarColors.IsValid(CalendarColors.For(t))));
        Assert.False(CalendarColors.IsValid("#12")); Assert.False(CalendarColors.IsValid("123456")); Assert.False(CalendarColors.IsValid("#GGGGGG"));
    }

    [Fact]
    public void Merge_sorts_ics_and_local_together_and_only_local_entries_are_editable()
    {
        var local = new LocalCalendarStore(File1); local.Add("Local", At(24, 9), At(24, 10), false);
        var all = CalendarAgenda.Merge([new CalendarEvent("Ics", At(24, 8), At(24, 8, 30), false)], local.All());
        Assert.Equal(["Ics", "Local"], all.Select(e => e.Title)); Assert.False(all[0].Editable); Assert.True(all[1].Editable);
        Assert.Equal(2, CalendarAgenda.CountOn(all, new DateOnly(2026, 11, 24))); Assert.Equal(0, CalendarAgenda.CountOn(all, new DateOnly(2026, 11, 25)));
    }

    [Fact]
    public void The_next_event_is_the_one_in_progress_or_the_next_to_start()
    {
        var all = CalendarAgenda.Merge([new CalendarEvent("Pasado", At(24, 8), At(24, 9), false), new CalendarEvent("En curso", At(24, 9), At(24, 11), false), new CalendarEvent("Luego", At(24, 14), At(24, 15), false)], []);
        Assert.Equal("En curso", CalendarAgenda.Next(all, At(24, 10))!.Title); Assert.Equal("Luego", CalendarAgenda.Next(all, At(24, 12))!.Title); Assert.Null(CalendarAgenda.Next(all, At(24, 16)));
    }
}
