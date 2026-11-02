using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NotchWin.App.ViewModels;
using NotchWin.Core.Activities;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace NotchWin.App.Views;

/// <summary>
/// WinMac 2.0 — the full capture gallery (docs/26): every image of the capture folder (WinMac's configured one or Windows' default), with search, sort,
/// multi-selection, an enlarged view and real actions: open, edit (in WinMac), copy image, copy path, show in folder, rename and delete (to the Recycle Bin, asked first).
/// Only metadata is listed; thumbnails are decoded small and only for the visible items (virtualized grid), the preview at its displayed size.
/// A normal window of the Island's process (no separate app); it reads the disk again whenever the folder changes, so it survives restarts.
/// </summary>
public sealed class GalleryWindow : Window
{
    private static readonly string? SelfTestPath = Environment.GetEnvironmentVariable("WINMAC_PILL_SELFTEST");
    private readonly NotchViewModel _vm;
    private readonly TextBox _search = new() { PlaceholderText = "Buscar por nombre", Width = 260 };
    private readonly ComboBox _sort = new() { Width = 170 };
    private readonly GridView _grid = new() { SelectionMode = ListViewSelectionMode.Extended, IsMultiSelectCheckBoxEnabled = true, Padding = new Thickness(12, 0, 12, 12) };
    private readonly TextBlock _folder = new() { Foreground = Gray(0x8E), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _empty = new() { Foreground = Gray(0x8E), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 260 };
    private readonly TextBlock _pName = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.White) };
    private readonly TextBlock _pInfo = new() { FontSize = 12, Foreground = Gray(0x98), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { FontSize = 12, Foreground = Gray(0xC7), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _actions = new() { Spacing = 6 };
    private readonly Grid _zoom = new() { Background = new SolidColorBrush(Color.FromArgb(0xE6, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly Image _zoomImage = new() { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
    private List<CaptureFile> _items = [];

    public GalleryWindow(NotchViewModel vm)
    {
        _vm = vm;
        Title = "Capturas — WinMac";
        // 1120×740 or 90 % of the monitor's work area, whichever is smaller, centred
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        // between WinMac's top bar (~40 px) and its Dock (~130 px) so neither covers the search box or the last row
        double sc = Math.Max(1, Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).OuterBounds.Height / 1080.0);
        int winTop = area.Y + (int)(44 * sc), room = Math.Max(420, area.Height - (int)(184 * sc));
        int gw = Math.Min(1120, (int)(area.Width * 0.9)), gh = Math.Min(740, room);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - gw) / 2, winTop + (room - gh) / 2, gw, gh));
        foreach (var s in new[] { "Más recientes", "Más antiguas", "Nombre", "Tamaño" }) _sort.Items.Add(s);
        _sort.SelectedIndex = 0;

        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 0x1C, 0x1C, 0x1E)), RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

        var top = new Grid { Padding = new Thickness(16, 14, 16, 10), ColumnSpacing = 10 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(_search);
        Grid.SetColumn(_sort, 1); top.Children.Add(_sort);
        Grid.SetColumn(_folder, 2); top.Children.Add(_folder);
        var openFolder = Pill("Abrir carpeta", false); openFolder.Click += (_, _) => Shell("explorer.exe", Quote(_vm.CaptureFolder));
        Grid.SetColumn(openFolder, 3); top.Children.Add(openFolder);
        Grid.SetColumnSpan(top, 2); root.Children.Add(top);

        _grid.ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><StackPanel Width=\"196\" Spacing=\"4\" Padding=\"4\">" +
            "<Border Width=\"188\" Height=\"112\" CornerRadius=\"8\" Background=\"#2C2C2E\"><Image Stretch=\"UniformToFill\" /></Border>" +
            "<TextBlock FontSize=\"12\" TextTrimming=\"CharacterEllipsis\" Foreground=\"White\" /><TextBlock FontSize=\"11\" Foreground=\"#8E8E93\" /></StackPanel></DataTemplate>");
        _grid.ContainerContentChanging += OnContainer;
        _grid.SelectionChanged += (_, _) => { UpdatePreview(); if (!string.IsNullOrEmpty(SelfTestPath)) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SelfTestLog); };
        _grid.DoubleTapped += (_, _) => Zoom(true);
        _grid.KeyDown += OnKey;
        var gridHost = new Grid(); gridHost.Children.Add(_grid); gridHost.Children.Add(_empty);
        Grid.SetRow(gridHost, 1); root.Children.Add(gridHost);

        var side = new StackPanel { Spacing = 10, Padding = new Thickness(16, 4, 16, 16) };
        side.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(255, 0x2C, 0x2C, 0x2E)), MinHeight = 160, Child = _preview });
        side.Children.Add(_pName); side.Children.Add(_pInfo); side.Children.Add(_actions); side.Children.Add(_status);
        var sideScroll = new ScrollViewer { Content = side };
        Grid.SetRow(sideScroll, 1); Grid.SetColumn(sideScroll, 1); root.Children.Add(sideScroll);

        _zoom.Children.Add(_zoomImage);
        _zoom.Children.Add(new TextBlock { Text = "Clic o Esc para cerrar", Foreground = Gray(0xC7), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) });
        _zoom.Tapped += (_, _) => Zoom(false);
        Grid.SetRowSpan(_zoom, 2); Grid.SetColumnSpan(_zoom, 2); root.Children.Add(_zoom);
        root.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            if (_zoom.Visibility == Visibility.Visible) Zoom(false); else Close();   // Esc: first the enlarged view, then the window
            e.Handled = true;
        };

        Content = root;
        _search.TextChanged += (_, _) => { Reload(); if (!string.IsNullOrEmpty(SelfTestPath)) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SelfTestLog); };
        _sort.SelectionChanged += (_, _) => Reload();
        _vm.CapturesChanged += Reload;
        Closed += (_, _) => _vm.CapturesChanged -= Reload;
        Reload();
        root.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SelfTestLog);
    }

    private static SolidColorBrush Gray(byte v) => new(Color.FromArgb(255, v, v, (byte)Math.Min(255, v + 5)));
    private static string Quote(string p) => "\"" + p + "\"";

    private static Button Pill(string text, bool accent) => new()
    {
        Content = text, FontSize = 12.5, Padding = new Thickness(14, 6, 14, 7), HorizontalContentAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = new SolidColorBrush(accent ? Color.FromArgb(255, 0x0A, 0x84, 0xFF) : Color.FromArgb(0x26, 255, 255, 255)),
    };

    private CaptureLibrary.Sort SortOrder => _sort.SelectedIndex switch { 1 => CaptureLibrary.Sort.Oldest, 2 => CaptureLibrary.Sort.Name, 3 => CaptureLibrary.Sort.Largest, _ => CaptureLibrary.Sort.Newest };

    private void Reload()
    {
        var keep = _grid.SelectedItems.OfType<CaptureFile>().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _items = CaptureLibrary.Query(_vm.CaptureFolder, _search.Text, SortOrder).ToList();
        _grid.ItemsSource = _items;
        _folder.Text = _vm.CaptureFolder + "  ·  " + (_items.Count == 1 ? "1 captura" : _items.Count + " capturas");
        _empty.Text = string.IsNullOrWhiteSpace(_search.Text) ? "Todavía no hay capturas en esta carpeta." : "Ninguna captura coincide con «" + _search.Text.Trim() + "».";
        _empty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var f in _items.Where(f => keep.Contains(f.Path))) _grid.SelectedItems.Add(f);
        UpdatePreview();
    }

    /// <summary>Fills a recycled grid cell: thumbnail decoded at its drawn size, released when the cell scrolls away.</summary>
    private void OnContainer(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is not StackPanel cell || cell.Children.Count < 3) return;
        var img = (Image)((Border)cell.Children[0]).Child;
        if (args.InRecycleQueue || args.Item is not CaptureFile f) { img.Source = null; return; }
        img.Source = Thumb(f.Path, 376);
        ((TextBlock)cell.Children[1]).Text = f.Name;
        ((TextBlock)cell.Children[2]).Text = CaptureLibrary.When(f.ModifiedUtc.ToLocalTime(), DateTime.Now) + " · " + CaptureLibrary.Size(f.Bytes);
        ToolTipService.SetToolTip(cell, f.Name);
    }

    private static BitmapImage? Thumb(string path, int width)
    {
        try { return ThumbLoader.Load(path, width); } catch (Exception) { return null; }
    }

    private List<CaptureFile> Selected => _grid.SelectedItems.OfType<CaptureFile>().ToList();

    private void UpdatePreview()
    {
        var sel = Selected; _actions.Children.Clear();
        if (sel.Count == 0)
        {
            _preview.Source = null; _pName.Text = _items.Count == 0 ? "" : "Selecciona una captura";
            _pInfo.Text = _items.Count == 0 ? "" : "Doble clic para verla en grande · Ctrl o Mayús para elegir varias · Supr elimina · F2 renombra";
            return;
        }

        if (sel.Count > 1)
        {
            _preview.Source = Thumb(sel[0].Path, 600);
            _pName.Text = sel.Count + " capturas seleccionadas"; _pInfo.Text = CaptureLibrary.Size(sel.Sum(f => f.Bytes)) + " en total";
            Add("Copiar rutas", CopyPaths); Add("Eliminar " + sel.Count + " (a la Papelera)", Delete);
            return;
        }

        var one = sel[0];
        var bmp = Thumb(one.Path, 600); _preview.Source = bmp;
        _pName.Text = one.Name;
        string info = CaptureLibrary.When(one.ModifiedUtc.ToLocalTime(), DateTime.Now) + " · " + CaptureLibrary.Size(one.Bytes);
        _pInfo.Text = info;
        if (CaptureLibrary.ImageSize(one.Path) is { } px) _pInfo.Text = $"{px.W} × {px.H} px · {info}";
        Add("Abrir", () => Shell(one.Path, null), accent: true);
        if (_vm.EditCapture is { } edit) Add("Editar", () => { edit(one.Path); _status.Text = "Abriendo el editor de WinMac…"; });
        Add("Ver en grande", () => Zoom(true));
        Add("Copiar imagen", () => _ = CopyImage(one.Path));
        Add("Copiar ruta", CopyPaths);
        Add("Mostrar en la carpeta", () => Shell("explorer.exe", "/select," + Quote(one.Path)));
        Add("Renombrar", () => _ = Rename(one));
        Add("Eliminar (a la Papelera)", Delete);
    }

    private void Add(string text, Action run, bool accent = false)
    {
        var b = Pill(text, accent);
        b.Click += (_, _) => { try { run(); } catch (Exception e) { _status.Text = "No se pudo: " + e.Message; } };
        _actions.Children.Add(b);
    }

    private void Zoom(bool on)
    {
        if (on && Selected is [var f])
        {
            _zoomImage.Source = Thumb(f.Path, Math.Max(800, AppWindow.Size.Width)); _zoom.Visibility = Visibility.Visible;
        }
        else { _zoom.Visibility = Visibility.Collapsed; _zoomImage.Source = null; }
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Delete: Delete(); break;
            case Windows.System.VirtualKey.F2 when Selected is [var f]: _ = Rename(f); break;
            case Windows.System.VirtualKey.Enter when Selected is [var f]: Shell(f.Path, null); break;
            case Windows.System.VirtualKey.Space when Selected is [_]: Zoom(_zoom.Visibility != Visibility.Visible); break;
            case Windows.System.VirtualKey.C when ctrl && Selected is [var f]: _ = CopyImage(f.Path); break;
            default: return;
        }

        e.Handled = true;
    }

    private void Shell(string file, string? args)
    {
        Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = args is null })?.Dispose();
    }

    private void CopyPaths()
    {
        var data = new DataPackage(); data.SetText(string.Join(Environment.NewLine, Selected.Select(f => f.Path)));
        Clipboard.SetContent(data); _status.Text = Selected.Count == 1 ? "Ruta copiada" : "Rutas copiadas";
    }

    private async Task CopyImage(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var data = new DataPackage(); data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            Clipboard.SetContent(data); _status.Text = "Imagen copiada";
        }
        catch (Exception e) { _status.Text = "No se pudo copiar: " + e.Message; }
    }

    private async Task Rename(CaptureFile f)
    {
        var box = new TextBox { Text = Path.GetFileNameWithoutExtension(f.Name), SelectionStart = 0 };
        box.SelectAll();
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Renombrar captura", Content = box, PrimaryButtonText = "Renombrar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Primary, RequestedTheme = ElementTheme.Dark };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        var (target, error) = CaptureLibrary.RenameTarget(f.Path, box.Text, File.Exists);
        if (error is not null) { _status.Text = error; return; }
        if (target is null) return;   // same name
        try { File.Move(f.Path, target); _status.Text = "Renombrada a " + Path.GetFileName(target); Reload(); }
        catch (Exception e) { _status.Text = "No se pudo renombrar: " + e.Message; }
    }

    private async void Delete()
    {
        var sel = Selected; if (sel.Count == 0) return;
        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = sel.Count == 1 ? "¿Mover la captura a la Papelera?" : $"¿Mover {sel.Count} capturas a la Papelera?",
            Content = "Podrás recuperarlas desde la Papelera de reciclaje.", PrimaryButtonText = "Mover a la Papelera", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close, RequestedTheme = ElementTheme.Dark,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        int done = 0;
        foreach (var f in sel)
        {
            try { var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(f.Path); await file.DeleteAsync(Windows.Storage.StorageDeleteOption.Default); done++; }   // Recycle Bin, never permanently
            catch (Exception) { /* vanished or locked: skipped */ }
        }

        _status.Text = done == 1 ? "1 captura movida a la Papelera" : $"{done} capturas movidas a la Papelera";
        Reload();
    }

    /// <summary>Activate() alone may leave it behind the window the user was in; the click that opened it gives this process the right to come to the front.</summary>
    public void BringToFront()
    {
        Activate();
        var h = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetForegroundWindow(h);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    private struct PT { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref PT p);

    /// <summary>CI only (WINMAC_PILL_SELFTEST): screen positions of the search box and the first item, so the audit can use the window like a user.</summary>
    private void SelfTestLog()
    {
        if (string.IsNullOrEmpty(SelfTestPath)) return;
        try
        {
            double s = Content.XamlRoot?.RasterizationScale ?? 1;
            var o = new PT(); ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(this), ref o);   // client origin in screen px
            string P(FrameworkElement e) { var p = e.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(e.ActualWidth / 2, e.ActualHeight / 2)); return $"{o.X + (int)(p.X * s)},{o.Y + (int)(p.Y * s)}"; }
            var buttons = string.Join(" ", _actions.Children.OfType<Button>().Select(b => $"act:{(b.Content as string ?? "").Replace(' ', '_')}@{P(b)}"));
            string first = _grid.ContainerFromIndex(0) is FrameworkElement c0 && c0.ActualWidth > 0 ? " first@" + P(c0) : "";
            File.AppendAllText(SelfTestPath!, $"{DateTime.UtcNow:HH:mm:ss.fff} gallery items={_items.Count} selected={Selected.Count} search@{P(_search)} sort@{P(_sort)} grid@{P(_grid)}{first} {buttons}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
