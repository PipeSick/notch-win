using NotchWin.Core.Activities;

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
}
