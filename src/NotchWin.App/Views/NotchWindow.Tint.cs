using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NotchWin.Core.Notch;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// Propuesta A: el borde de la Isla, las barras de audio y (abierta) un resplandor suave toman el color dominante de la carátula.
/// Sin carátula, o con una en blanco y negro, no cambia nada: queda el aspecto de siempre.
/// </summary>
public sealed partial class NotchWindow
{
    private Color? _tint;
    private byte[]? _tintSource;
    private Brush? _eqOriginal, _xeqOriginal;

    private static async Task<Color?> ExtractTintAsync(byte[] data)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(data.AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform { ScaledWidth = 24, ScaledHeight = 24 }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return DominantColor.FromBgra(pixels.DetachPixelData(), 24, 24) is { } c ? Color.FromArgb(255, c.R, c.G, c.B) : null;
        }
        catch (Exception)
        {
            return null;   // una carátula que no se puede leer no tiñe nada; la Isla sigue igual
        }
    }

    /// <summary>Llamar tras cargar la carátula (con los bytes de la canción actual, o null si no hay).</summary>
    private async Task UpdateTintAsync(byte[]? art)
    {
        if (art is null) { _tintSource = null; _tint = null; }
        else if (!ReferenceEquals(art, _tintSource)) { _tintSource = art; _tint = await ExtractTintAsync(art); }
        UpdateSurface();
    }

    private static Color Lift(Color c) => Color.FromArgb(255, (byte)(c.R + (255 - c.R) * 0.25), (byte)(c.G + (255 - c.G) * 0.25), (byte)(c.B + (255 - c.B) * 0.25));

    private Brush? TintRim(bool barLook)
    {
        if (_tint is not { } t || _info is null) return null;
        if (barLook) return new SolidColorBrush(Color.FromArgb(0x70, t.R, t.G, t.B));
        var g = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xB0, t.R, t.G, t.B), Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x60, t.R, t.G, t.B), Offset = 0.4 });
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x90, t.R, t.G, t.B), Offset = 1 });
        return g;
    }

    /// <summary>Barras de audio y resplandor de la Isla abierta; el borde lo pone <see cref="UpdateSurface"/> con <see cref="TintRim"/>.</summary>
    private void UpdateTintVisuals()
    {
        _eqOriginal ??= Eq0.Fill; _xeqOriginal ??= XEq0.Fill;
        var on = _tint is not null && _info is not null;
        Brush? eq = on ? new SolidColorBrush(Lift(_tint!.Value)) : null;
        foreach (var r in new[] { Eq0, Eq1, Eq2, Eq3 }) r.Fill = eq ?? _eqOriginal;
        foreach (var r in new[] { XEq0, XEq1, XEq2, XEq3 }) r.Fill = eq ?? _xeqOriginal;
        if (on && _expanded)
        {
            var t = _tint!.Value; var a = Pal.Light ? (byte)0x30 : (byte)0x50;
            var g = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
            g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(a, t.R, t.G, t.B), Offset = 0 });
            g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, t.R, t.G, t.B), Offset = 0.7 });
            TintGlow.Fill = g;
            TintGlow.Visibility = Visibility.Visible;
        }
        else TintGlow.Visibility = Visibility.Collapsed;
    }
}
