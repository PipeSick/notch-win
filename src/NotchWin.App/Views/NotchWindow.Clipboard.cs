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
            ? "Aquí aparecerá lo que copies mientras NotchWin esté abierto"
            : "Nada coincide con la búsqueda";

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
        var body = new StackPanel { Spacing = 8 };
        var content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        content.Content = BuildClipboardContent(item);
        body.Children.Add(content);

        var footer = CardUi.Columns(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        var stamp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stamp.Children.Add(CardUi.Icon(item.Kind == ClipboardItemKind.Image ? "" : "", 12, CardUi.Faint));
        stamp.Children.Add(CardUi.Text(item.At.ToLocalTime().ToString("d MMM, h:mm tt", CultureInfo.CurrentCulture), 11, CardUi.Faint));
        footer.Children.Add(stamp);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        if (item.Kind == ClipboardItemKind.Text)
        {
            actions.Children.Add(ActionButton("", "Editar", () => BeginEdit(item, content)));
        }

        actions.Children.Add(ActionButton("", "Copiar", () => _ = CopyBackAsync(item)));
        actions.Children.Add(ActionButton(item.Pinned ? "" : "", item.Pinned ? "Quitar fijado" : "Fijar",
            () => _viewModel.Clipboard.TogglePin(item.Id), item.Pinned ? Color.FromArgb(255, 0xFF, 0xD6, 0x0A) : CardUi.Dim));
        actions.Children.Add(ActionButton("", "Eliminar", () => _viewModel.Clipboard.Remove(item.Id)));
        footer.Children.Add(CardUi.At(actions, 2));
        body.Children.Add(footer);

        var card = CardUi.Card(body, 10);
        if (item.Pinned)
        {
            card.BorderBrush = CardUi.Brush(Color.FromArgb(0x66, 0xFF, 0xD6, 0x0A));
        }

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

        return new TextBlock
        {
            Text = item.Text ?? string.Empty,
            Foreground = CardUi.Brush(Colors.White),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3,
        };
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
