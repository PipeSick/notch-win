using NotchWin.Core.Shelf;

namespace NotchWin.Core.Tests;

public class ShelfStoreTests
{
    private static readonly string Own = Path.Combine(Path.GetTempPath(), "nw-shelf-test", "own");
    private static ShelfStore S(int max = 20) => new(Own, max);

    [Fact] public void Files_and_folders_are_references_with_their_name_and_duplicates_are_ignored()
    {
        var s = S(); Assert.Equal(AddResult.Added, s.AddPath(@"C:\Users\a\Doc.pdf", false, out _)); Assert.Equal(AddResult.Added, s.AddPath(@"C:\Users\a\Fotos\", true, out _));
        Assert.Equal(AddResult.Duplicate, s.AddPath(@"c:\users\a\doc.pdf", false, out _)); Assert.Equal(AddResult.Rejected, s.AddPath(" ", false, out _));
        Assert.Equal(["Doc.pdf", "Fotos"], s.Items.Select(i => i.Title)); Assert.Equal([ShelfKind.File, ShelfKind.Folder], s.Items.Select(i => i.Kind));
        Assert.All(s.Items, i => Assert.Null(i.OwnedPath));
    }

    [Fact] public void Text_is_stored_trimmed_to_the_limit_with_a_one_line_title_and_no_duplicates()
    {
        var s = S(); s.AddText("hola\r\nmundo " + new string('x', 100), out _); Assert.DoesNotContain('\n', s.Items[0].Title); Assert.True(s.Items[0].Title.Length <= 61);
        s.AddText(new string('a', 50_000), out _); Assert.Equal(ShelfStore.MaxTextChars, s.Items[1].Text!.Length);
        Assert.Equal(AddResult.Duplicate, s.AddText("hola\r\nmundo " + new string('x', 100), out _)); Assert.Equal(AddResult.Rejected, s.AddText("   ", out _));
    }

    [Fact] public void Over_the_limit_the_oldest_goes_and_only_its_own_copy_is_reported_for_deletion()
    {
        var s = S(2); var img = Path.Combine(Own, "a.png");
        s.AddOwnedImage(img, out _); s.AddPath(@"C:\x\1.txt", false, out _); s.AddPath(@"C:\x\2.txt", false, out var del);
        Assert.Equal(2, s.Items.Count); Assert.Equal([img], del); Assert.DoesNotContain(s.Items, i => i.Kind == ShelfKind.Image);
        s.AddPath(@"C:\x\3.txt", false, out var del2); Assert.Empty(del2);                     // retirar una referencia no borra nada
    }

    [Fact] public void Clearing_and_removing_never_report_an_original_file_for_deletion()
    {
        var s = S(); s.AddPath(@"C:\x\original.docx", false, out _); s.AddText("t", out _); var img = Path.Combine(Own, "i.png"); s.AddOwnedImage(img, out _);
        Assert.Null(s.Remove(s.Items[0].Id)); Assert.Equal([img], s.Clear()); Assert.Empty(s.Items);
    }

    [Fact] public void An_image_outside_the_shelf_folder_is_rejected_so_nothing_foreign_can_ever_be_deleted()
    {
        var s = S(); Assert.Equal(AddResult.Rejected, s.AddOwnedImage(@"C:\Users\a\Mis fotos\foto.png", out _)); Assert.Equal(AddResult.Rejected, s.AddOwnedImage(Path.Combine(Own, "..", "otra", "x.png"), out _));
        Assert.True(s.IsOwned(Path.Combine(Own, "sub", "x.png"))); Assert.False(s.IsOwned(null)); Assert.False(s.IsOwned(Own + "-vecino"));
    }

    [Fact] public void A_moved_or_deleted_original_is_marked_missing_but_kept_until_the_user_removes_it()
    {
        var s = S(); s.AddPath(@"C:\x\a.txt", false, out _); s.AddPath(@"C:\x\dir", true, out _); s.AddText("t", out _);
        Assert.Equal(ShelfStatus.Ok, s.StatusOf(s.Items[0], f => true, d => true)); Assert.Equal(ShelfStatus.Missing, s.StatusOf(s.Items[0], f => false, d => true));
        Assert.Equal(ShelfStatus.Missing, s.StatusOf(s.Items[1], f => true, d => false)); Assert.Equal(ShelfStatus.Ok, s.StatusOf(s.Items[2], f => false, d => false)); Assert.Equal(3, s.Items.Count);
    }

    [Fact] public void The_limit_is_clamped_and_lowering_it_trims_the_oldest_and_announces_the_change()
    {
        var s = S(); for (int i = 0; i < 5; i++) s.AddText("t" + i, out _);
        int changes = 0; s.Changed += () => changes++; s.SetMax(2); Assert.Equal(2, s.Items.Count); Assert.Equal("t4", s.Items[1].Title); Assert.True(changes > 0);
        s.SetMax(0); Assert.Equal(1, s.Max); s.SetMax(9999); Assert.Equal(ShelfStore.MaxMax, s.Max); Assert.Equal(1, ShelfStore.ClampMax(-4));
    }
}

public class ShelfSettingsTests
{
    [Fact] public void The_shelf_limit_travels_through_the_central_settings_is_clamped_and_reported_as_a_change()
    {
        var json = System.Text.Json.JsonDocument.Parse("{\"shelfMaxItems\": 500}").RootElement;
        var c = NotchWin.Core.Hosting.CentralSettings.FromJson(json)!; Assert.Equal(ShelfStore.MaxMax, c.ShelfMaxItems);
        var a = new NotchWin.Core.Hosting.CentralSettings(); var b = new NotchWin.Core.Hosting.CentralSettings { ShelfMaxItems = 5 };
        Assert.True(NotchWin.Core.Hosting.CentralSettings.Diff(a, b).HasFlag(NotchWin.Core.Hosting.CentralChanges.Shelf)); Assert.False(NotchWin.Core.Hosting.CentralSettings.Diff(a, a).HasFlag(NotchWin.Core.Hosting.CentralChanges.Shelf));
        var app = new NotchWin.Core.Settings.AppSettings(); b.ApplyTo(app); Assert.Equal(5, app.ShelfMaxItems);
    }

    [Fact] public void Deleting_copies_only_touches_files_inside_the_shelf_folder_and_cleaning_leftovers_spares_everything_else()
    {
        var root = Path.Combine(Path.GetTempPath(), "nw-shelf-" + Guid.NewGuid()); var own = Path.Combine(root, "own"); Directory.CreateDirectory(own);
        var mine = Path.Combine(own, "a.png"); var foreign = Path.Combine(root, "original.txt"); File.WriteAllText(mine, "x"); File.WriteAllText(foreign, "x");
        var s = new ShelfStore(own); Assert.Equal(1, s.DeleteOwnedCopies([mine, foreign, null])); Assert.False(File.Exists(mine)); Assert.True(File.Exists(foreign));
        File.WriteAllText(mine, "x"); Assert.Equal(1, s.CleanLeftovers()); Assert.True(File.Exists(foreign)); Directory.Delete(root, true);
    }
}
