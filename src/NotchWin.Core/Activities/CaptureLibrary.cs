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

    /// <summary>Gallery sort orders.</summary>
    public enum Sort { Newest, Oldest, Name, Largest }

    /// <summary>All images of the folder (metadata only), filtered by a text in the name (case/accents ignored) and sorted.</summary>
    public static IReadOnlyList<CaptureFile> Query(string folder, string? filter, Sort sort, int max = 500)
    {
        var all = List(folder, int.MaxValue);
        IEnumerable<CaptureFile> q = all;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = Fold(filter.Trim());
            q = q.Where(c => Fold(c.Name).Contains(f, StringComparison.Ordinal));
        }

        q = sort switch
        {
            Sort.Oldest => q.OrderBy(c => c.ModifiedUtc),
            Sort.Name => q.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            Sort.Largest => q.OrderByDescending(c => c.Bytes),
            _ => q.OrderByDescending(c => c.ModifiedUtc),
        };
        return q.Take(Math.Max(1, max)).ToList();
    }

    private static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    /// <summary>
    /// Validates a new name for a capture: same folder, no path, no characters Windows forbids, the original extension kept (added if missing),
    /// and never over an existing file. Returns the new full path, or null with the reason.
    /// </summary>
    public static (string? Path, string? Error) RenameTarget(string original, string newName, Func<string, bool> exists)
    {
        var name = (newName ?? "").Trim().TrimEnd('.');
        if (name.Length == 0) return (null, "Escribe un nombre");
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0) return (null, "El nombre tiene caracteres no permitidos");
        var stem = System.IO.Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) return (null, "Ese nombre está reservado por Windows");
        var ext = System.IO.Path.GetExtension(original);
        if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name += ext;
        if (name.Length > 200) return (null, "El nombre es demasiado largo");
        var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(original) ?? "", name);
        if (string.Equals(target, original, StringComparison.OrdinalIgnoreCase)) return (null, null);
        if (exists(target)) return (null, "Ya existe un archivo con ese nombre");
        return (target, null);
    }

    /// <summary>Pixel size read from the file header only (PNG, BMP, JPEG); null if unknown. Never decodes the image.</summary>
    public static (int W, int H)? ImageSize(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var h = new byte[64 * 1024]; int n = fs.Read(h, 0, h.Length);
            return ImageSize(h.AsSpan(0, n));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static (int W, int H)? ImageSize(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 24 && h[0] == 0x89 && h[1] == 'P' && h[2] == 'N' && h[3] == 'G')
            return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(h[16..]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(h[20..]));
        if (h.Length >= 26 && h[0] == 'B' && h[1] == 'M')
            return (BitConverter.ToInt32(h[18..22]), Math.Abs(BitConverter.ToInt32(h[22..26])));
        if (h.Length >= 4 && h[0] == 0xFF && h[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < h.Length)
            {
                if (h[i] != 0xFF) { i++; continue; }
                byte m = h[i + 1];
                if (m is >= 0xC0 and <= 0xCF && m is not (0xC4 or 0xC8 or 0xCC)) return ((h[i + 7] << 8) | h[i + 8], (h[i + 5] << 8) | h[i + 6]);
                if (m is 0xD8 or 0x01 || m is >= 0xD0 and <= 0xD7) { i += 2; continue; }
                i += 2 + ((h[i + 2] << 8) | h[i + 3]);
            }
        }
        return null;
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) + " KB",
        _ => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) + " MB",
    };

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
