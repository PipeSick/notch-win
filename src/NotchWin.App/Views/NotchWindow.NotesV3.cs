using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Views.V3;
using NotchWin.Core.Notes;
using NotchWin.Core.Settings;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// Notas: la lista de todas las notas (las fijadas arriba) con «Nueva nota», fijar/desfijar y borrar. Una nota fijada abre su propia ventanita delante de todo (ver <see cref="StickyNotes"/>), que es donde se escribe:
/// la Isla no pide el teclado. Las fijadas también salen como tarjeta en Inicio. Todo local.
/// </summary>
public sealed partial class NotchWindow
{
    private const int NotesShown = 12;

    private static Color NoteColor(string color) => color switch
    {
        "Pink" => Color.FromArgb(255, 0xF2, 0xAE, 0xC8),
        "Blue" => Color.FromArgb(255, 0xA9, 0xCF, 0xF5),
        "Green" => Color.FromArgb(255, 0xA9, 0xDB, 0x9C),
        "Violet" => Color.FromArgb(255, 0xC5, 0xB1, 0xF0),
        _ => Color.FromArgb(255, 0xF2, 0xDF, 0x7A),
    };

    private void NewNote() { _viewModel.Notes?.Add("", Note.Colors[Random.Shared.Next(Note.Colors.Length)], pinned: true); }

    private void RebuildNotesV3()
    {
        if (NotesPane is null) return;
        NotesPane.Children.Clear();
        var notes = _viewModel.Notes?.All() ?? [];
        if (_viewModel.Notes is null) { NotesPane.Children.Add(V3Ui.EmptyState("", "Notas no disponibles", "Se activan al terminar de iniciar la Isla.")); return; }

        int pinned = notes.Count(n => n.Pinned);
        var head = V3Ui.ModuleHeader("", "Notas", notes.Count == 0 ? "Fíjalas delante de todo" : $"{notes.Count} · {pinned} fijada" + (pinned == 1 ? "" : "s"), V3Ui.TextButton("Nueva nota", NewNote, ActionStyle.Primary));
        NotesPane.Children.Add(head);
        if (notes.Count == 0)
        {
            NotesPane.Children.Add(V3Ui.EmptyState("", "Sin notas", "Crea una: se abre una ventanita delante de todo donde escribes. Cerrarla la desfija, no la borra.", ("Nueva nota", () => NewNote())));
            return;
        }

        var list = new StackPanel { Spacing = 6 };
        foreach (var n in notes.Take(NotesShown)) list.Children.Add(NoteRow(n));
        if (notes.Count > NotesShown) list.Children.Add(CardUi.Text($"y {notes.Count - NotesShown} más (las más antiguas)", 11.5, CardUi.Dim));
        NotesPane.Children.Add(list);
    }

    private UIElement NoteRow(Note n)
    {
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new Border { Width = 6, Height = 34, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(NoteColor(n.Color)), VerticalAlignment = VerticalAlignment.Center });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(CardUi.Text(NoteView.Title(n.Text), 13, Pal.Fg, bold: true, trim: true));
        var prev = NoteView.Preview(n.Text); if (prev.Length > 0) texts.Children.Add(CardUi.Text(prev, 11.5, CardUi.Dim, trim: true));
        Grid.SetColumn(texts, 1); g.Children.Add(texts);
        string id = n.Id;
        var pin = V3Ui.ActionButton(n.Pinned ? "" : "", n.Pinned ? "Desfijar (cierra la ventanita)" : "Fijar delante de todo", () => _viewModel.Notes?.SetPinned(id, !n.Pinned), n.Pinned ? ActionStyle.Primary : ActionStyle.Secondary, size: 30);
        Grid.SetColumn(pin, 2); g.Children.Add(pin);
        var del = V3Ui.ActionButton("", "Borrar la nota", () => _viewModel.Notes?.Delete(id), ActionStyle.Ghost, size: 30);
        Grid.SetColumn(del, 3); g.Children.Add(del);
        var row = V3Ui.Surface(g, 8, interactive: true);
        row.Tapped += (_, e) => { if (!n.Pinned) _viewModel.Notes?.SetPinned(id, true); };   // tocar una nota la abre (la fija); los botones no pasan por aquí
        return row;
    }

    /// <summary>Inicio: las notas fijadas (hasta 2) con su color y primera línea; tocar abre la pestaña Notas. Sin fijadas no se muestra nada.</summary>
    private UIElement? HomeNotesCard()
    {
        if (!SectionOn(NotchSections.Notes)) return null;
        var pinned = _viewModel.Notes?.Pinned();
        if (pinned is null || pinned.Count == 0) return null;
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(new TextBlock { Text = "NOTAS FIJADAS", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 80, Foreground = new SolidColorBrush(CardUi.Dim) });
        foreach (var n in pinned.Take(2))
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border { Width = 5, Height = 28, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(NoteColor(n.Color)), VerticalAlignment = VerticalAlignment.Center });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(CardUi.Text(NoteView.Title(n.Text, 60), 12.5, Pal.Fg, bold: true, trim: true));
            var prev = NoteView.Preview(n.Text, 70); if (prev.Length > 0) t.Children.Add(CardUi.Text(prev, 11, CardUi.Dim, trim: true));
            Grid.SetColumn(t, 1); row.Children.Add(t);
            s.Children.Add(row);
        }

        if (pinned.Count > 2) s.Children.Add(CardUi.Text($"+{pinned.Count - 2} más", 11, CardUi.Dim));
        var card = V3Ui.Surface(s, 10, interactive: true); card.Tapped += (_, _) => OpenSection(NotchSections.Notes);
        ToolTipService.SetToolTip(card, "Notas");
        return card;
    }
}
