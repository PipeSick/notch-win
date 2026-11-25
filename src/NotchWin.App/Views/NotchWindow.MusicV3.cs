using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Views.V3;
using NotchWin.Core.Music;
using NotchWin.Core.Notch.V3;

namespace NotchWin.App.Views;

/// <summary>
/// Pestaña Música V3: carátula grande, aleatorio y repetir (solo si la sesión los habilita), origen del reproductor y segmentos Letras · Info. «Cola» solo aparece si algún proveedor
/// la ofrece (Windows no la expone) y «me gusta» tampoco: no se dibujan controles que no puedan funcionar. Las letras sincronizadas siguen siendo las de siempre (UpdateLyricsCore).
/// </summary>
public sealed partial class NotchWindow
{
    private int _musicSegment;
    private List<string> _musicSegments = ["Letras", "Info"];
    private bool _lyricsSegmentOn = true;

    private async void OnShuffleClicked(object sender, RoutedEventArgs e)
    {
        var on = _info?.Extras?.Shuffle == true;
        await RunControl(c => c.SetShuffleAsync(!on));
    }

    private async void OnRepeatClicked(object sender, RoutedEventArgs e)
    {
        var mode = _info?.Extras?.Repeat ?? RepeatMode.Off;
        await RunControl(c => c.SetRepeatAsync(MediaFeatureResolver.NextRepeat(mode)));
    }

    /// <summary>Se llama cuando cambia la canción o su estado (aleatorio/repetir cambiados desde el propio reproductor incluidos).</summary>
    private void UpdateMusicV3()
    {
        var accent = new SolidColorBrush(V3Ui.AccentText);
        var x = _info?.Extras; var caps = x?.Capabilities ?? MediaCapabilities.None;

        var shuffleOn = x?.Shuffle == true;
        ShuffleButton.Visibility = _hasTrack && caps.HasFlag(MediaCapabilities.Shuffle) ? Visibility.Visible : Visibility.Collapsed;
        ShuffleIcon.Foreground = shuffleOn ? accent : new SolidColorBrush(Pal.Fg);
        ToolTipService.SetToolTip(ShuffleButton, shuffleOn ? "Aleatorio: activado" : "Aleatorio: desactivado");

        var mode = x?.Repeat ?? RepeatMode.Off;
        RepeatButton.Visibility = _hasTrack && caps.HasFlag(MediaCapabilities.Repeat) ? Visibility.Visible : Visibility.Collapsed;
        RepeatIcon.Glyph = mode == RepeatMode.Track ? "" : "";
        RepeatIcon.Foreground = mode != RepeatMode.Off ? accent : new SolidColorBrush(Pal.Fg);
        ToolTipService.SetToolTip(RepeatButton, MediaFeatureResolver.RepeatLabel(mode));

        ProgressFillBar.Background = accent; NowFill.Background = accent;   // el progreso lleva el acento del sistema visual

        MusicSourceHost.Children.Clear();
        if (_hasTrack && _info is { } info && MediaFeatureResolver.SourceName(info.SourceAppName) is { Length: > 0 } src) MusicSourceHost.Children.Add(V3Ui.StatusBadge(src, BadgeTone.Accent));

        _musicSegments = ["Letras", "Info"];
        if (caps.HasFlag(MediaCapabilities.Queue)) _musicSegments.Insert(1, "Cola");
        _musicSegment = Math.Clamp(_musicSegment, 0, _musicSegments.Count - 1);
        MusicSegmentsHost.Visibility = _hasTrack ? Visibility.Visible : Visibility.Collapsed;
        MusicSegmentsHost.Child = V3Ui.SegmentedControl(_musicSegments, _musicSegment, i => { _musicSegment = i; UpdateMusicV3(); RelayoutIfExpanded(); });
        ApplyMusicSegment();
    }

    private void ApplyMusicSegment()
    {
        var name = _musicSegments[Math.Clamp(_musicSegment, 0, _musicSegments.Count - 1)];
        _lyricsSegmentOn = name == "Letras";
        MusicExtraHost.Children.Clear();
        LyricCur.Foreground = new SolidColorBrush(V3Ui.AccentText);   // la línea actual de la letra, con el acento

        if (!_hasTrack || _info is null) { MusicExtraHost.Visibility = Visibility.Collapsed; return; }
        switch (name)
        {
            case "Letras":
                if (_viewModel.Lyrics is null)
                {
                    MusicExtraHost.Children.Add(V3Ui.EmptyState("", "Las letras están desactivadas", "Son opcionales: se buscan en un servicio externo. Actívalas en los ajustes de Hatsu Notch."));
                    MusicExtraHost.Visibility = Visibility.Visible;
                }
                else MusicExtraHost.Visibility = Visibility.Collapsed;   // la caja de letras (LyricsBox) muestra la letra, «buscando…» o «sin letra»
                break;
            case "Cola":
                MusicExtraHost.Children.Add(V3Ui.EmptyState("", "Cola de reproducción no disponible", "Windows no la expone y no hay un proveedor conectado para este reproductor."));
                MusicExtraHost.Visibility = Visibility.Visible;
                break;
            default:
                MusicExtraHost.Children.Add(MusicInfoCard(_info));
                MusicExtraHost.Visibility = Visibility.Visible;
                break;
        }

        UpdateLyrics();
    }

    /// <summary>Información de reproducción: solo las filas que la fuente realmente entrega.</summary>
    private static UIElement MusicInfoCard(NowPlayingInfo info)
    {
        var x = info.Extras; var rows = new StackPanel { Spacing = 8 };
        void Row(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var g = new Grid { ColumnSpacing = 12 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(CardUi.Text(label, 12, CardUi.Dim));
            var v = CardUi.Text(value, 12.5, Pal.Fg, bold: false, trim: true); Grid.SetColumn(v, 1); g.Children.Add(v);
            rows.Children.Add(g);
        }

        Row("Canción", info.Title); Row("Artista", info.Artist); Row("Álbum", info.Album); Row("Artista del álbum", x?.AlbumArtist != info.Artist ? x?.AlbumArtist : null);
        Row("Pista", x?.TrackNumber is { } n ? (x.TrackCount is { } c ? $"{n} de {c}" : n.ToString()) : null);
        Row("Género", x?.Genres is { Count: > 0 } g2 ? string.Join(", ", g2) : null);
        Row("Reproductor", MediaFeatureResolver.SourceName(info.SourceAppName)); Row("Duración", info.Duration is { } d ? V3Ui.Clock(d) : null);
        return V3Ui.Surface(rows, 14);
    }
}
