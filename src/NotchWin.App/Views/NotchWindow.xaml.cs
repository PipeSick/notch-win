using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using NotchWin.App.Modules.Notch;
using NotchWin.App.ViewModels;
using NotchWin.Core.Music;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace NotchWin.App.Views;

public sealed partial class NotchWindow : Window
{
    private readonly NotchViewModel _viewModel;
    private NotchWindowController? _controller;

    public NotchWindow(NotchViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        AgentsList.ItemsSource = _viewModel.AgentSnapshots;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Set once by the composition root right after construction — the controller needs this
    /// window's HWND, which only exists once the base <see cref="Window"/> constructor has run.
    /// </summary>
    public void AttachController(NotchWindowController controller) => _controller = controller;

    private void OnBarTapped(object sender, TappedRoutedEventArgs e) => _viewModel.IsExpanded = !_viewModel.IsExpanded;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotchViewModel.NowPlaying))
        {
            _ = UpdateNowPlayingUiAsync(_viewModel.NowPlaying);
        }
        else if (e.PropertyName is nameof(NotchViewModel.IsExpanded))
        {
            ExpandedPanel.Visibility = _viewModel.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (_viewModel.IsExpanded)
            {
                _controller?.Expand();
            }
            else
            {
                _controller?.Collapse();
            }
        }
    }

    private async Task UpdateNowPlayingUiAsync(NowPlayingInfo? info)
    {
        if (info is null)
        {
            CollapsedTrackText.Text = "NotchWin";
            TitleText.Text = "Sin reproducción";
            ArtistText.Text = string.Empty;
            AlbumArtImage.Source = null;
            return;
        }

        CollapsedTrackText.Text = string.IsNullOrEmpty(info.Title) ? "NotchWin" : $"{info.Title} · {info.Artist}";
        TitleText.Text = info.Title;
        ArtistText.Text = info.Artist;

        AlbumArtImage.Source = info.AlbumArt is null ? null : await ToBitmapImageAsync(info.AlbumArt);
    }

    private static async Task<BitmapImage> ToBitmapImageAsync(byte[] data)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
