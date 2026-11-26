using NotchWin.Core.Notes;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class NotesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notchwin-notes-" + Guid.NewGuid().ToString("N"));
    private string P => Path.Combine(_dir, "notes.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

    [Fact]
    public void Notes_survive_a_restart_and_keep_pin_color_and_position()
    {
        var a = new NotesStore(P); var n = a.Add("Comprar pan\nleche", "Pink")!;
        a.SetPinned(n.Id, true); a.SetBounds(n.Id, 120, 80, 320, 240);
        var b = new NotesStore(P); var back = b.Get(n.Id)!;
        Assert.Equal("Comprar pan\nleche", back.Text); Assert.Equal("Pink", back.Color); Assert.True(back.Pinned); Assert.Equal((120, 80, 320, 240), (back.X, back.Y, back.W, back.H));
        Assert.Single(b.Pinned());
    }

    [Fact]
    public void Titles_previews_and_order_are_readable()
    {
        Assert.Equal("Nota vacía", NoteView.Title("  \n \n")); Assert.Equal("Hola", NoteView.Title("\n  Hola \nmundo")); Assert.Equal("mundo · y más", NoteView.Preview("Hola\nmundo\n\ny más"));
        Assert.EndsWith("…", NoteView.Title(new string('a', 100))); Assert.Equal("", NoteView.Preview("solo una línea"));
        var s = new NotesStore(P); var old = s.Add("vieja")!; var fresh = s.Add("nueva")!; var pinned = s.Add("fijada", pinned: true)!;
        s.SetText(old.Id, "vieja editada");   // editar la sube
        var order = s.All().Select(x => x.Id).ToList(); Assert.Equal(pinned.Id, order[0]); Assert.Equal(old.Id, order[1]); Assert.Equal(fresh.Id, order[2]);
    }

    [Fact]
    public void Moving_a_window_does_not_count_as_editing_and_bad_values_are_sanitized()
    {
        var s = new NotesStore(P); var n = s.Add("x")!; var t0 = s.Get(n.Id)!.UpdatedUtc; Thread.Sleep(15);
        s.SetBounds(n.Id, 5, 6, 9999, 5); var moved = s.Get(n.Id)!; Assert.Equal(t0, moved.UpdatedUtc); Assert.Equal(900, moved.W); Assert.Equal(140, moved.H);
        Assert.Equal("Yellow", s.SetColor(n.Id, "rojo-fuego")!.Color); Assert.Equal(Note.MaxTextLength, s.SetText(n.Id, new string('z', 30000))!.Text.Length);
    }

    [Fact]
    public void A_corrupt_file_starts_empty_keeps_a_copy_and_the_note_limit_is_enforced()
    {
        Directory.CreateDirectory(_dir); File.WriteAllText(P, "{ esto no es json");
        var s = new NotesStore(P); Assert.Empty(s.All()); Assert.True(File.Exists(P + ".bad")); Assert.NotNull(s.Add("sigue funcionando"));
        for (int i = 0; i < Note.MaxNotes; i++) s.Add("n" + i);
        Assert.Null(s.Add("una más")); Assert.Equal(Note.MaxNotes, s.All().Count);
        Assert.True(s.Delete(s.All()[0].Id)); Assert.False(s.Delete("no-existe")); Assert.NotNull(s.Add("ahora sí"));
    }

    [Fact]
    public void Changed_fires_on_edits_but_not_on_window_moves()
    {
        var s = new NotesStore(P); int fired = 0; s.Changed += () => fired++;
        var n = s.Add("a")!; s.SetText(n.Id, "b"); s.SetPinned(n.Id, true); s.SetBounds(n.Id, 1, 2, 300, 260); s.Delete(n.Id);
        Assert.Equal(4, fired);
    }
}
