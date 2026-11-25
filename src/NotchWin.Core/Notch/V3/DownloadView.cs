using NotchWin.Core.Activities;
using NotchWin.Core.Downloads;

namespace NotchWin.Core.Notch.V3;

public enum FileKind { Other, Installer, Archive, Document, Image, Video, Audio, Code }

/// <summary>
/// Descargas V3. De cada descarga solo se muestra lo que el módulo REALMENTE sabe: nombre, bytes descargados, velocidad medida y estado. NO se conoce el tamaño total
/// (el sistema de archivos solo ve el archivo parcial), así que no hay porcentaje, total ni tiempo restante: la barra es indeterminada. Pausar y cancelar no existen.
/// </summary>
public static class DownloadView
{
    private static readonly Dictionary<string, FileKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".msix"] = FileKind.Installer, [".msi"] = FileKind.Installer, [".exe"] = FileKind.Installer, [".appx"] = FileKind.Installer, [".msixbundle"] = FileKind.Installer, [".appinstaller"] = FileKind.Installer,
        [".zip"] = FileKind.Archive, [".7z"] = FileKind.Archive, [".rar"] = FileKind.Archive, [".tar"] = FileKind.Archive, [".gz"] = FileKind.Archive, [".iso"] = FileKind.Archive,
        [".pdf"] = FileKind.Document, [".docx"] = FileKind.Document, [".doc"] = FileKind.Document, [".xlsx"] = FileKind.Document, [".pptx"] = FileKind.Document, [".txt"] = FileKind.Document, [".csv"] = FileKind.Document,
        [".png"] = FileKind.Image, [".jpg"] = FileKind.Image, [".jpeg"] = FileKind.Image, [".gif"] = FileKind.Image, [".webp"] = FileKind.Image,
        [".mp4"] = FileKind.Video, [".mkv"] = FileKind.Video, [".mov"] = FileKind.Video, [".avi"] = FileKind.Video,
        [".mp3"] = FileKind.Audio, [".flac"] = FileKind.Audio, [".wav"] = FileKind.Audio, [".m4a"] = FileKind.Audio,
        [".cs"] = FileKind.Code, [".js"] = FileKind.Code, [".json"] = FileKind.Code, [".py"] = FileKind.Code,
    };

    public static FileKind Classify(string? fileName) => Kinds.TryGetValue(Path.GetExtension(fileName ?? ""), out var k) ? k : FileKind.Other;

    public static string KindLabel(FileKind k) => k switch
    {
        FileKind.Installer => "Instalador", FileKind.Archive => "Comprimido", FileKind.Document => "Documento", FileKind.Image => "Imagen", FileKind.Video => "Vídeo", FileKind.Audio => "Audio", FileKind.Code => "Código", _ => "Archivo",
    };

    /// <summary>El estado va SIEMPRE en palabras (no solo en color).</summary>
    public static string StateLabel(ActivityState s) => s switch
    {
        ActivityState.Active => "Descargando", ActivityState.Paused => "Pausada", ActivityState.Completed => "Completada", ActivityState.Failed => "Fallida", _ => "Cancelada",
    };

    /// <summary>Activas (descargando o en pausa) arriba, la más reciente primero; el resto —completadas, fallidas, canceladas— en «Completadas».</summary>
    public static (IReadOnlyList<Activity> Active, IReadOnlyList<Activity> Finished) Split(IEnumerable<Activity> activities)
    {
        var downloads = activities.Where(a => a.Kind == ActivityKind.Download).OrderByDescending(a => a.CreatedUtc).ToList();
        return (downloads.Where(a => a.State is ActivityState.Active or ActivityState.Paused).ToList(), downloads.Where(a => a.State is not (ActivityState.Active or ActivityState.Paused)).ToList());
    }

    // ───────────── filas unificadas (observadas + administradas)

    /// <summary>Una fila de la pestaña Descargas. <see cref="ManagedId"/> != null = la administra Hatsu (se puede pausar, reanudar y cancelar de verdad); null = solo observada.</summary>
    public sealed record Row(string Key, string Name, FileKind Kind, long ReceivedBytes, long? TotalBytes, double? BytesPerSecond, TimeSpan? Remaining, ManagedState State, string? ManagedId, string? Path, string? Error)
    {
        public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)ReceivedBytes / TotalBytes.Value, 0, 1) : null;
        public bool IsManaged => ManagedId is not null;
        public bool IsActive => State is ManagedState.Queued or ManagedState.Downloading or ManagedState.Paused;
        public bool CanPause => IsManaged && State == ManagedState.Downloading;
        public bool CanResume => IsManaged && State is ManagedState.Paused or ManagedState.Failed;
        public bool CanCancel => IsManaged && IsActive;
        public bool CanOpen => State == ManagedState.Completed && Path is not null;
    }

    /// <summary>
    /// Filas para la interfaz: las administradas (con total, ETA y control real) y las observadas en la carpeta de descargas (solo bytes y velocidad: barra indeterminada, sin pausar ni cancelar).
    /// </summary>
    public static IReadOnlyList<Row> Rows(IEnumerable<NotchWin.Core.Downloads.ActiveDownload> observed, IEnumerable<ManagedDownload> managed)
    {
        var rows = new List<Row>();
        foreach (var m in managed) rows.Add(new Row("m:" + m.Id, m.FileName, Classify(m.FileName), m.ReceivedBytes, m.TotalBytes, m.BytesPerSecond, m.Remaining, m.State, m.Id, m.State == ManagedState.Completed ? m.TargetPath : null, m.Error));
        foreach (var o in observed) rows.Add(new Row("o:" + o.FileName, o.FileName, Classify(o.FileName), o.Bytes, null, o.BytesPerSecond, null, ManagedState.Downloading, null, null, null));
        return rows;
    }

    /// <summary>«328 MB / 512 MB» si hay total; «328 MB» si no.</summary>
    public static string SizeText(Row r) => r.TotalBytes is { } t ? $"{Bytes(r.ReceivedBytes)} / {Bytes(t)}" : Bytes(r.ReceivedBytes);

    public static string Bytes(long b) => b switch { < 1024 => $"{b} B", < 1024 * 1024 => $"{b / 1024.0:0} KB", < 1024L * 1024 * 1024 => $"{b / 1048576.0:0.#} MB", _ => $"{b / 1073741824.0:0.##} GB" };

    /// <summary>«~00:45» (mm:ss) o «~1 h 05 min»; null si no se puede estimar.</summary>
    public static string? EtaText(TimeSpan? remaining) => remaining switch
    {
        null => null,
        { TotalHours: >= 1 } t => $"~{(int)t.TotalHours} h {t.Minutes:00} min",
        { } t => $"~{(int)t.TotalMinutes:00}:{t.Seconds:00}",
    };

    public static string StateLabel(ManagedState s) => s switch { ManagedState.Queued => "En cola", ManagedState.Downloading => "Descargando", ManagedState.Paused => "Pausada", ManagedState.Completed => "Completada", ManagedState.Failed => "Fallida", _ => "Cancelada" };
}
