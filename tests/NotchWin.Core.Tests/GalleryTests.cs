using System.Text.Json;
using NotchWin.Core.Activities;
using NotchWin.Core.Hosting;
using Xunit;

namespace NotchWin.Core.Tests;

// Galería de capturas (WinMac 2.0, docs/26): búsqueda, orden, renombrado seguro y carpeta configurada en WinMac. Archivos reales en una carpeta temporal.
public class GalleryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notch-gallery-" + Guid.NewGuid().ToString("N"));

    public GalleryTests()
    {
        Directory.CreateDirectory(_dir);
        Make("Captura 2026-01-01 a las 10.00.00.png", 300, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        Make("Factura cámara.png", 900, new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc));
        Make("apunte.jpg", 100, new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc));
        Make("notas.txt", 50, new DateTime(2026, 1, 4, 10, 0, 0, DateTimeKind.Utc));
    }

    private void Make(string name, int bytes, DateTime when)
    {
        var p = Path.Combine(_dir, name); File.WriteAllBytes(p, new byte[bytes]); File.SetLastWriteTimeUtc(p, when);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void QueryListsOnlyImagesAndSortsEveryWay()
    {
        Assert.Equal(["Factura cámara.png", "apunte.jpg", "Captura 2026-01-01 a las 10.00.00.png"], CaptureLibrary.Query(_dir, null, CaptureLibrary.Sort.Newest).Select(f => f.Name));
        Assert.Equal("Captura 2026-01-01 a las 10.00.00.png", CaptureLibrary.Query(_dir, null, CaptureLibrary.Sort.Oldest)[0].Name);
        Assert.Equal("Factura cámara.png", CaptureLibrary.Query(_dir, null, CaptureLibrary.Sort.Largest)[0].Name);
        Assert.Equal("apunte.jpg", CaptureLibrary.Query(_dir, null, CaptureLibrary.Sort.Name)[0].Name);
    }

    [Fact]
    public void SearchIgnoresCaseAndAccents()
    {
        Assert.Single(CaptureLibrary.Query(_dir, "CAMARA", CaptureLibrary.Sort.Newest));
        Assert.Empty(CaptureLibrary.Query(_dir, "notas", CaptureLibrary.Sort.Newest));   // not an image
        Assert.Empty(CaptureLibrary.Query(Path.Combine(_dir, "no-existe"), null, CaptureLibrary.Sort.Newest));
    }

    [Fact]
    public void RenameKeepsFolderAndExtensionAndNeverOverwrites()
    {
        var original = Path.Combine(_dir, "apunte.jpg");
        Assert.Equal((Path.Combine(_dir, "idea.jpg"), (string?)null), CaptureLibrary.RenameTarget(original, " idea ", File.Exists));
        Assert.NotNull(CaptureLibrary.RenameTarget(original, "..\\fuera", File.Exists).Error);
        Assert.NotNull(CaptureLibrary.RenameTarget(original, "a/b", File.Exists).Error);
        Assert.NotNull(CaptureLibrary.RenameTarget(original, "CON", File.Exists).Error);
        Assert.NotNull(CaptureLibrary.RenameTarget(original, "   ", File.Exists).Error);
        Assert.Equal((null, null), CaptureLibrary.RenameTarget(original, "apunte", File.Exists));   // same name: nothing to do
    }

    [Fact]
    public void RenameRejectsAnExistingTarget()
    {
        var original = Path.Combine(_dir, "apunte.jpg");
        Make("ocupado.jpg", 10, DateTime.UtcNow);
        Assert.Equal("Ya existe un archivo con ese nombre", CaptureLibrary.RenameTarget(original, "ocupado", File.Exists).Error);
    }

    [Theory]
    [InlineData("{\"captureFolder\":\"D:\\\\Capturas\"}", "D:\\Capturas", true)]
    [InlineData("{\"captureFolder\":\"relativa\"}", null, false)]
    [InlineData("{\"captureFolder\":null}", null, false)]
    [InlineData("{}", null, false)]
    public void CaptureFolderFromTheBarSettingsOnlyAcceptsAbsolutePaths(string json, string? expected, bool windowsOnly)
    {
        if (windowsOnly && !OperatingSystem.IsWindows()) expected = null;   // «D:\…» is only fully qualified on Windows
        using var d = JsonDocument.Parse(json);
        Assert.Equal(expected, HostedSession.ParseCaptureFolder(d.RootElement));
    }

    [Fact]
    public void EditIsOnlyOfferedForCapturesInsideWinMac()
    {
        var cap = new Activity("c", ActivityKind.Capture, "Captura") { Path = "x.png", State = ActivityState.Completed };
        Assert.False(ActivityActions.Supported(cap).HasFlag(ActivityAction.Edit));
        Assert.True(ActivityActions.Supported(cap, canEdit: true).HasFlag(ActivityAction.Edit));
        var dl = new Activity("d", ActivityKind.Download, "x.zip") { Path = "x.zip", State = ActivityState.Completed };
        Assert.False(ActivityActions.Supported(dl, canEdit: true).HasFlag(ActivityAction.Edit));
    }
}
