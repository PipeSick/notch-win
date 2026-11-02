namespace NotchWin.Core.Activities;

/// <summary>A saved screenshot: only metadata (the image is decoded at thumbnail size when drawn, never loaded in full).</summary>
public sealed record CaptureFile(string Path, string Name, DateTime ModifiedUtc, long Bytes);

/// <summary>
/// The capture gallery reads the folder where Windows saves screenshots (Win+PrtScn, Snipping Tool: «Imágenes\Capturas de pantalla»). WinMac's own capture
/// engine (next phase) will save there too. Read-only listing, newest first, capped; an unreadable folder gives an empty list.
/// </summary>
public static class CaptureLibrary
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    public static string DefaultFolder() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static bool IsImage(string path) => Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CaptureFile> List(string folder, int max = 12)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            return new DirectoryInfo(folder).EnumerateFiles().Where(f => IsImage(f.Name)).OrderByDescending(f => f.LastWriteTimeUtc).Take(Math.Max(1, max))
                .Select(f => new CaptureFile(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>«Hoy 14:36», «Ayer 20:17» or «24 ene 13:55», like the mockup.</summary>
    public static string When(DateTime localTime, DateTime localNow)
    {
        var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        string hm = localTime.ToString("HH:mm", es);
        if (localTime.Date == localNow.Date) return "Hoy " + hm;
        if (localTime.Date == localNow.Date.AddDays(-1)) return "Ayer " + hm;
        return localTime.ToString("d MMM", es).Replace(".", "") + " " + hm;
    }
}
