using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace NotchWin.App.Views;

/// <summary>
/// Thumbnails for the gallery, the activities and the pill. The file is read into memory (sharing it with whoever is still writing it) and decoded at the
/// size it is drawn. Not loaded by URI: WinUI caches images per URI, so a screenshot decoded while it was still being written stayed blank for the session.
/// A failed read or decode is retried a few times (a file that has just appeared may not be complete yet). Must be called on the UI thread.
/// </summary>
internal static class ThumbLoader
{
    public static BitmapImage Load(string path, int decodeWidth)
    {
        var bmp = new BitmapImage { DecodePixelWidth = Math.Max(16, decodeWidth) };
        _ = FillAsync(bmp, path);
        return bmp;
    }

    private static async Task FillAsync(BitmapImage bmp, string path)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var bytes = await Task.Run(() =>
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var buf = new byte[fs.Length]; fs.ReadExactly(buf); return buf;
                });
                if (bytes.Length == 0) throw new IOException("vacío");
                using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await ms.WriteAsync(bytes.AsBuffer());
                ms.Seek(0);
                await bmp.SetSourceAsync(ms);
                return;
            }
            catch (FileNotFoundException) { return; }
            catch (Exception) { await Task.Delay(350 * (attempt + 1)); }
        }
    }
}
