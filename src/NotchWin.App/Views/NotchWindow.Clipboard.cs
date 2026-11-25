using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NotchWin.Core.Clipboard;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>The clipboard tab: search, clear, and a card per copied item with edit / copy / pin / delete.</summary>
public sealed partial class NotchWindow
{
    private bool _clipboardDirty = true;

    private void InitClipboard()
    {
        _viewModel.Clipboard.Changed += OnClipboardChanged;
    }

    private void OnClipboardChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_closed)
        {
            return;
        }

        _clipboardDirty = true;
        if (_expanded && !_customizing && _active == Core.Settings.NotchSections.Clipboard)
        {
            RebuildClipboard();
            RelayoutIfExpanded();
        }
    });

    private void RebuildClipboard()
    {
        _clipboardDirty = false;
        ClipboardList.Children.Clear();
        var items = _viewModel.Clipboard.View(ClipSearch.Text);
        ClipboardEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipboardEmpty.Text = string.IsNullOrWhiteSpace(ClipSearch.Text)
            ? "Aquí aparecerá lo que copies mientras Hatsu Notch esté abierto"
            : "Nada coincide con la búsqueda";
        if (_viewModel.Clipboard.Skipped > 0)
        {
            ClipboardEmpty.Text += $"\n{_viewModel.Clipboard.Skipped} copia(s) parecían contraseñas o claves y no se guardaron.";
        }

        foreach (var item in items)
        {
            ClipboardList.Children.Add(BuildClipboardCard(item));
        }
    }

    private void OnClipSearchChanged(object sender, TextChangedEventArgs e)
    {
        RebuildClipboard();
        RelayoutIfExpanded();
    }

    private void OnClipClearClicked(object sender, RoutedEventArgs e) => _viewModel.Clipboard.Clear();

    private UIElement BuildClipboardCard(ClipboardEntry item)
    {
        // V3: la tarjeta se presenta según el TIPO de contenido (se deduce del texto ya guardado; no se captura ni se guarda nada más)
        var kind = NotchWin.Core.Notch.V3.ClipClassifier.Classify(item.Text, item.Kind == ClipboardItemKind.Image);
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = V3.V3Ui.Tile(V3.V3Ui.ClipGlyph(kind), 38); tile.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(tile);

        var mid = new StackPanel { Spacing = 4 };
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        meta.Children.Add(V3.V3Ui.StatusBadge(NotchWin.Core.Notch.V3.ClipClassifier.KindLabel(kind), item.Pinned ? V3.BadgeTone.Warning : V3.BadgeTone.Neutral));
        if (item.Pinned) meta.Children.Add(CardUi.Text("Fijado", 10.5, Pal.C(255, 0xFF, 0xD6, 0x0A), bold: true));
        meta.Children.Add(CardUi.Text(NotchWin.Core.Notch.V3.NotificationView.Relative(item.At, DateTimeOffset.Now), 10.5, CardUi.Faint));
        mid.Children.Add(meta);
        var content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = BuildClipboardContent(item) };
        mid.Children.Add(content);
        Grid.SetColumn(mid, 1); grid.Children.Add(mid);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        if (item.Kind == ClipboardItemKind.Text) actions.Children.Add(ActionButton("\uE70F", "Editar", () => BeginEdit(item, content)));
        actions.Children.Add(ActionButton("\uE8C8", "Copiar", () => _ = CopyBackAsync(item)));
        actions.Children.Add(ActionButton(item.Pinned ? "\uE77A" : "\uE718", item.Pinned ? "Quitar fijado" : "Fijar", () => _viewModel.Clipboard.TogglePin(item.Id), item.Pinned ? Pal.C(255, 0xFF, 0xD6, 0x0A) : CardUi.Dim));
        actions.Children.Add(ActionButton("\uE74D", "Eliminar", () => _viewModel.Clipboard.Remove(item.Id)));
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);

        var card = V3.V3Ui.Surface(grid, 10, interactive: true);
        if (item.Pinned) card.BorderBrush = CardUi.Brush(Pal.C(0x66, 0xFF, 0xD6, 0x0A));
        return card;
    }

    private static UIElement BuildClipboardContent(ClipboardEntry item)
    {
        if (item.Kind == ClipboardItemKind.Image && item.ImagePng is { } png)
        {
            var image = new Image { Height = 90, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            _ = SetImageAsync(image, png);
            return image;
        }

        var text = item.Text ?? string.Empty; var kind = NotchWin.Core.Notch.V3.ClipClassifier.Classify(text);
        switch (kind)
        {
            case NotchWin.Core.Notch.V3.ClipKind.Link:
            {
                var s = new StackPanel { Spacing = 1 };
                if (NotchWin.Core.Notch.V3.ClipClassifier.LinkHost(text) is { } host) s.Children.Add(CardUi.Text(host, 13, Pal.Fg, bold: true, trim: true));
                s.Children.Add(CardUi.Text(text.Trim(), 11.5, CardUi.Dim, trim: true));
                return s;
            }

            case NotchWin.Core.Notch.V3.ClipKind.Code:
                return new TextBlock { Text = NotchWin.Core.Notch.V3.ClipClassifier.Preview(text, kind, 160), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Foreground = CardUi.Brush(Pal.Fg), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3 };
            case NotchWin.Core.Notch.V3.ClipKind.Path:
            {
                var s = new StackPanel { Spacing = 1 };
                s.Children.Add(CardUi.Text(System.IO.Path.GetFileName(text.Trim().TrimEnd('\\')) is { Length: > 0 } n ? n : text.Trim(), 13, Pal.Fg, bold: true, trim: true));
                s.Children.Add(CardUi.Text(text.Trim(), 11.5, CardUi.Dim, trim: true));
                return s;
            }

            default:
                return new TextBlock { Text = text, Foreground = CardUi.Brush(Pal.Fg), FontSize = 13, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3 };
        }
    }

    private static async Task SetImageAsync(Image target, byte[] png)
    {
        try
        {
            var bitmap = await ToBitmapImageAsync(png);
            bitmap.DecodePixelHeight = 180;
            target.Source = bitmap;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // A corrupt image just shows an empty slot.
        }
    }

    private static Button ActionButton(string glyph, string tooltip, Action onClick, Color? color = null)
    {
        var button = new Button
        {
            Content = CardUi.Icon(glyph, 13, color ?? CardUi.Dim),
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = CardUi.Brush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Swaps the text for an editable box; Enter (or leaving the box) saves, Escape cancels.</summary>
    private void BeginEdit(ClipboardEntry item, ContentControl host)
    {
        var box = new TextBox
        {
            Text = item.Text ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 110,
            FontSize = 13,
        };
        var finished = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                finished = true;
                _viewModel.Clipboard.UpdateText(item.Id, box.Text);
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                finished = true;
                RebuildClipboard();
            }
        };
        box.LostFocus += (_, _) =>
        {
            if (!finished)
            {
                finished = true;
                _viewModel.Clipboard.UpdateText(item.Id, box.Text);
            }
        };
        host.Content = box;
        box.Focus(FocusState.Programmatic);
        RelayoutIfExpanded();
    }

    private async Task CopyBackAsync(ClipboardEntry item)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            if (item.Kind == ClipboardItemKind.Image && item.ImagePng is { } png)
            {
                var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(png.AsBuffer());
                stream.Seek(0);
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            }
            else if (item.Text is { } text)
            {
                package.SetText(text);
            }

            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Another app is holding the clipboard; the user can press copy again.
        }
    }
}
