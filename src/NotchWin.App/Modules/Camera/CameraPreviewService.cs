using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;

namespace NotchWin.App.Modules.Camera;

/// <summary>
/// Opens the default webcam as a live <see cref="MediaSource"/> for the notch's mirror tab. Frames are
/// read through a shared, read-only frame source so another app already using the camera isn't
/// disturbed. Dispose it (or call <see cref="Stop"/>) as soon as the tab is left, so the camera's
/// indicator light goes off rather than staying on while the notch is closed.
/// </summary>
public sealed class CameraPreviewService : IDisposable
{
    private MediaCapture? _capture;

    /// <summary>Returns the live source, or null with a reason in <paramref name="error"/> (no camera, access denied).</summary>
    public async Task<(MediaSource? Source, string? Error)> StartAsync()
    {
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var group = groups.FirstOrDefault(g => g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color));
            if (group is null)
            {
                return (null, "No se encontró ninguna cámara.");
            }

            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });

            var frameSource = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (frameSource is null)
            {
                capture.Dispose();
                return (null, "La cámara no entrega imagen en color.");
            }

            _capture = capture;
            return (MediaSource.CreateFromMediaFrameSource(frameSource), null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, "Windows bloqueó la cámara. Actívala en Configuración › Privacidad › Cámara, incluida la opción para apps de escritorio.");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return (null, "No se pudo abrir la cámara (¿la está usando otra app?).");
        }
    }

    public void Stop()
    {
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose() => Stop();
}
