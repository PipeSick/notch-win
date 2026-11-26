using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.Core.Notes;
using Windows.Graphics;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// Notas fijadas: cada nota con «Fijada» tiene su propia ventanita delante de todo (siempre visible), que se edita ahí mismo y se guarda sola. Cerrar la ventanita desfija la nota
/// (sigue en la lista de la Isla). Todo es local: ver <see cref="NotesStore"/>. Un solo gestor para todo el proceso, aunque haya una Isla por monitor.
/// </summary>
public sealed class StickyNotes
{
    private readonly NotesStore _store;
    private readonly DispatcherQueue _ui;
    private readonly Dictionary<string, StickyNoteWindow> _open = [];
    private bool _closingAll;

    public StickyNotes(NotesStore store, DispatcherQueue ui)
    {
        _store = store; _ui = ui;
        _store.Changed += () => _ui.TryEnqueue(Sync);
    }

    /// <summary>Abre las ventanas de las notas fijadas (al arrancar) y cierra las que ya no lo están o se borraron.</summary>
    public void Sync()
    {
        if (_closingAll) return;
        var pinned = _store.Pinned().ToDictionary(n => n.Id);
        foreach (var id in _open.Keys.Where(id => !pinned.ContainsKey(id)).ToList()) { var w = _open[id]; _open.Remove(id); w.CloseFromManager(); }
        foreach (var n in pinned.Values)
        {
            if (_open.TryGetValue(n.Id, out var w)) { w.Refresh(n); continue; }
            try
            {
                var win = new StickyNoteWindow(_store, n, onClosedByUser: id => { _open.Remove(id); if (!_closingAll) _store.SetPinned(id, false); });
                _open[n.Id] = win; win.Activate();
            }
            catch (Exception) { /* una ventana que no se pudo crear no tira las demás ni la Isla */ }
        }
    }

    public void CloseAll()
    {
        _closingAll = true;
        foreach (var w in _open.Values.ToList()) w.CloseFromManager();
        _open.Clear();
    }
}

internal sealed class StickyNoteWindow : Window
{
    private static readonly Dictionary<string, (Color Bg, Color Bar)> Palette = new()
    {
        ["Yellow"] = (Color.FromArgb(255, 0xFF, 0xF3, 0xB0), Color.FromArgb(255, 0xF2, 0xDF, 0x7A)),
        ["Pink"] = (Color.FromArgb(255, 0xFF, 0xD6, 0xE5), Color.FromArgb(255, 0xF2, 0xAE, 0xC8)),
        ["Blue"] = (Color.FromArgb(255, 0xD3, 0xE8, 0xFF), Color.FromArgb(255, 0xA9, 0xCF, 0xF5)),
        ["Green"] = (Color.FromArgb(255, 0xD6, 0xF3, 0xCC), Color.FromArgb(255, 0xA9, 0xDB, 0x9C)),
        ["Violet"] = (Color.FromArgb(255, 0xE6, 0xDB, 0xFF), Color.FromArgb(255, 0xC5, 0xB1, 0xF0)),
    };

    private static readonly Color Ink = Color.FromArgb(255, 0x26, 0x24, 0x1F), InkDim = Color.FromArgb(255, 0x5E, 0x5A, 0x4E);
    private readonly NotesStore _store;
    private readonly string _id;
    private readonly Action<string> _onClosedByUser;
    private readonly TextBox _box = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(12, 8, 12, 12), FontSize = 14, PlaceholderText = "Escribe tu nota…", MaxLength = Note.MaxTextLength };
    private readonly Grid _header = new() { Height = 34 };
    private readonly Grid _root = new();
    private readonly DispatcherQueueTimer _saveTimer, _boundsTimer;
    private string _color;
    private bool _managerClosing, _loading;

    public StickyNoteWindow(NotesStore store, Note note, Action<string> onClosedByUser)
    {
        _store = store; _id = note.Id; _onClosedByUser = onClosedByUser; _color = note.Color;
        Title = "Nota";
        var q = DispatcherQueue.GetForCurrentThread();
        _saveTimer = q.CreateTimer(); _saveTimer.Interval = TimeSpan.FromMilliseconds(500); _saveTimer.IsRepeating = false; _saveTimer.Tick += (_, _) => SaveText();
        _boundsTimer = q.CreateTimer(); _boundsTimer.Interval = TimeSpan.FromMilliseconds(600); _boundsTimer.IsRepeating = false; _boundsTimer.Tick += (_, _) => SaveBounds();

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        BuildHeader();
        Grid.SetRow(_header, 0); _root.Children.Add(_header);
        _box.Foreground = new SolidColorBrush(Ink); _box.Background = new SolidColorBrush(Colors.Transparent);
        foreach (var k in new[] { "TextControlBackgroundFocused", "TextControlBackgroundPointerOver", "TextControlBackground", "TextControlBorderBrush", "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver" })
            _box.Resources[k] = new SolidColorBrush(Colors.Transparent);
        _box.Resources["TextControlForegroundFocused"] = new SolidColorBrush(Ink); _box.Resources["TextControlForegroundPointerOver"] = new SolidColorBrush(Ink); _box.Resources["TextControlPlaceholderForeground"] = new SolidColorBrush(InkDim);
        _loading = true; _box.Text = note.Text; _loading = false;
        _box.TextChanged += (_, _) => { if (!_loading) { _saveTimer.Stop(); _saveTimer.Start(); } };
        Grid.SetRow(_box, 1); _root.Children.Add(_box);
        Content = _root;

        ExtendsContentIntoTitleBar = true; SetTitleBar(_header);
        if (AppWindow.Presenter is OverlappedPresenter p) { p.IsAlwaysOnTop = true; p.IsMaximizable = false; p.IsMinimizable = false; }
        PlaceWindow(note);
        ApplyColor();
        AppWindow.Changed += (_, a) => { if (a.DidPositionChange || a.DidSizeChange) { _boundsTimer.Stop(); _boundsTimer.Start(); } };
        Closed += (_, _) => { SaveText(); SaveBounds(); if (!_managerClosing) _onClosedByUser(_id); };
    }

    private void BuildHeader()
    {
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Nota fijada", FontSize = 11.5, Foreground = new SolidColorBrush(InkDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _header.Children.Add(title);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 140, 0) };   // el espacio de la derecha es de los botones de la ventana (cerrar)
        tools.Children.Add(Tool("", "Cambiar de color", NextColor));
        tools.Children.Add(Tool("", "Borrar la nota", () => { _managerClosing = true; _store.Delete(_id); Close(); }));
        Grid.SetColumn(tools, 1); _header.Children.Add(tools);
    }

    private static Button Tool(string glyph, string tip, Action act)
    {
        var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = new SolidColorBrush(Ink) }, Width = 30, Height = 28, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8) };
        ToolTipService.SetToolTip(b, tip); b.Click += (_, _) => act();
        return b;
    }

    private void NextColor()
    {
        int i = Array.IndexOf(Note.Colors, _color); _color = Note.Colors[(i + 1) % Note.Colors.Length];
        ApplyColor(); _store.SetColor(_id, _color);
    }

    private void ApplyColor()
    {
        var (bg, bar) = Palette.TryGetValue(_color, out var c) ? c : Palette["Yellow"];
        _root.Background = new SolidColorBrush(bg); _header.Background = new SolidColorBrush(bar);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var tb = AppWindow.TitleBar; tb.BackgroundColor = bar; tb.InactiveBackgroundColor = bar; tb.ForegroundColor = Ink; tb.InactiveForegroundColor = InkDim;
            tb.ButtonBackgroundColor = bar; tb.ButtonInactiveBackgroundColor = bar; tb.ButtonForegroundColor = Ink; tb.ButtonInactiveForegroundColor = InkDim;
            tb.ButtonHoverBackgroundColor = bg; tb.ButtonHoverForegroundColor = Ink; tb.ButtonPressedBackgroundColor = bg; tb.ButtonPressedForegroundColor = Ink;
        }
    }

    /// <summary>La nota cambió en el almacén (otra Isla, la lista): se actualiza el color y, si no estoy escribiendo, el texto.</summary>
    public void Refresh(Note n)
    {
        if (n.Color != _color) { _color = n.Color; ApplyColor(); }
        if (!_box.FocusState.Equals(FocusState.Unfocused)) return;
        if (_box.Text != n.Text) { _loading = true; _box.Text = n.Text; _loading = false; }
    }

    public void CloseFromManager() { _managerClosing = true; try { Close(); } catch (Exception) { } }

    private void SaveText() { _saveTimer.Stop(); if (!_managerClosing && _store.Get(_id) is { } n && n.Text != _box.Text) _store.SetText(_id, _box.Text); }

    private void SaveBounds()
    {
        _boundsTimer.Stop();
        try { var pos = AppWindow.Position; var size = AppWindow.Size; _store.SetBounds(_id, pos.X, pos.Y, size.Width, size.Height); } catch (Exception) { }
    }

    /// <summary>Donde la dejó la persona; si es nueva (o esa posición ya no existe, p. ej. otro monitor), en la esquina superior derecha del monitor principal, escalonada.</summary>
    private void PlaceWindow(Note n)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = n.W, h = n.H, x, y;
        if (n.X is { } sx && n.Y is { } sy && DisplayArea.GetFromPoint(new PointInt32(sx + 20, sy + 20), DisplayAreaFallback.None) is not null) { x = sx; y = sy; }
        else
        {
            int slot = Math.Max(0, _store.Pinned().ToList().FindIndex(p => p.Id == n.Id)) % 6;
            x = area.X + area.Width - w - 24 - slot * 28; y = area.Y + 64 + slot * 28;
        }

        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }
}
