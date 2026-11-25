using Microsoft.UI.Dispatching;
using NotchWin.Core.Clipboard;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NotchWin.App.Modules.Clipboard;

/// <summary>
/// Feeds the clipboard history with whatever the user copies while NotchWin runs. Text wins over images (copying
/// from a web page offers both). Nothing is written to disk, and a clipboard that can't be read right now is skipped.
/// </summary>
public sealed class ClipboardWatcher(ClipboardHistory history, DispatcherQueue ui) : IDisposable
{
    private const int MaxImageBytes = 24 * 1024 * 1024;

    private bool _running;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        Windows.ApplicationModel.DataTransfer.Clipboard.ContentChanged += OnContentChanged;
        _ = CaptureAsync();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        Windows.ApplicationModel.DataTransfer.Clipboard.ContentChanged -= OnContentChanged;
    }

    public void Dispose() => Stop();

    private void OnContentChanged(object? sender, object e) => ui.TryEnqueue(() => _ = CaptureAsync());

    private async Task CaptureAsync()
    {
        try
        {
            var view = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            // Apps that copy passwords mark their data so clipboard managers skip it (documented formats); and the user's excluded apps.
            if (view.AvailableFormats.Any(f => f is "ExcludeClipboardContentFromMonitorProcessing" or "Clipboard Viewer Ignore")
                || (view.Contains("CanIncludeInClipboardHistory") && await IsZeroAsync(view, "CanIncludeInClipboardHistory"))
                || history.Options.IsExcluded(OwnerProcess()))
            {
                return;
            }

            // El texto gana a la imagen (copiar de una página ofrece ambos), salvo que el «texto» venga vacío: una captura o «Copiar imagen» suelen traer solo el mapa de bits.
            string? text = null;
            if (view.Contains(StandardDataFormats.Text)) { try { text = await view.GetTextAsync(); } catch (Exception) { } }
            if (!string.IsNullOrWhiteSpace(text))
            {
                history.AddText(text, DateTimeOffset.Now);
            }
            else if (view.Contains(StandardDataFormats.Bitmap))
            {
                var png = await ToPngAsync(await view.GetBitmapAsync());
                if (png is { Length: > 0 and <= MaxImageBytes }) { history.AddImage(png, DateTimeOffset.Now); Log($"imagen guardada ({png.Length / 1024} KB)"); }
                else Log(png is null ? "imagen: no se pudo convertir" : png.Length == 0 ? "imagen vacía" : $"imagen ignorada por tamaño ({png.Length / 1024} KB)");
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The clipboard is locked by the app that is still writing to it, or holds something unreadable.
            Log("portapapeles ocupado o ilegible: " + ex.GetType().Name);
        }
        catch (Exception ex)
        {
            Log("error al leer el portapapeles: " + ex.GetType().Name + " " + ex.Message);   // antes se perdía en silencio (p. ej. un formato de imagen que el decodificador no abre)
        }
    }

    /// <summary>Diagnóstico local sin contenido (solo tipo y tamaño): %LOCALAPPDATA%\\NotchWin\\portapapeles.log.</summary>
    private static void Log(string what)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchWin"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "portapapeles.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {what}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    private static async Task<bool> IsZeroAsync(DataPackageView view, string format)
    {
        try
        {
            if (await view.GetDataAsync(format) is IRandomAccessStream s)
            {
                using var r = new DataReader(s.GetInputStreamAt(0)); await r.LoadAsync(4); return r.UnconsumedBufferLength >= 4 && r.ReadUInt32() == 0;
            }
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>Process that owns the clipboard now (documented GetClipboardOwner); only its name is used, to honour the exclusion list.</summary>
    private static string? OwnerProcess()
    {
        try
        {
            var w = GetClipboardOwner(); if (w == IntPtr.Zero) return null;
            GetWindowThreadProcessId(w, out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid); return p.ProcessName;
        }
        catch (Exception) { return null; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    private static async Task<byte[]?> ToPngAsync(RandomAccessStreamReference reference)
    {
        using var source = await reference.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(source);
        using var bitmap = await decoder.GetSoftwareBitmapAsync();

        using var target = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, target);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        var bytes = new byte[target.Size];
        target.Seek(0);
        using var reader = new DataReader(target);
        await reader.LoadAsync((uint)target.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
