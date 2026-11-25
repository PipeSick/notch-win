namespace NotchWin.Core.Music;

/// <summary>Qué puede hacer REALMENTE la fuente de música actual. La interfaz muestra un control solo si su bandera está puesta: nada se simula.</summary>
[Flags]
public enum MediaCapabilities
{
    None = 0, PlayPause = 1, Next = 2, Previous = 4, Seek = 8, Shuffle = 16, Repeat = 32,
    /// <summary>Solo con un proveedor que lea la cola (Windows no la expone).</summary>
    Queue = 64,
    /// <summary>Solo con un proveedor con «me gusta» (Windows no lo expone).</summary>
    Favorite = 128,
}

public enum RepeatMode { Off, Track, List }

/// <summary>Datos de la sesión multimedia más allá de título y artista; todo opcional (cada reproductor entrega lo que quiere).</summary>
public sealed record MediaExtras(
    MediaCapabilities Capabilities = MediaCapabilities.None,
    bool? Shuffle = null,
    RepeatMode? Repeat = null,
    string? AlbumArtist = null,
    int? TrackNumber = null,
    int? TrackCount = null,
    IReadOnlyList<string>? Genres = null)
{
    /// <summary>Lo mismo en igualdad por valor, incluida la lista de géneros (un record compara las listas por referencia).</summary>
    public bool SameAs(MediaExtras? other) =>
        other is not null && Capabilities == other.Capabilities && Shuffle == other.Shuffle && Repeat == other.Repeat && AlbumArtist == other.AlbumArtist && TrackNumber == other.TrackNumber && TrackCount == other.TrackCount &&
        (Genres ?? []).SequenceEqual(other.Genres ?? []);
}

/// <summary>Una canción de la cola (solo la entrega un proveedor que la tenga).</summary>
public sealed record QueueItem(string Title, string Artist, TimeSpan? Duration = null);

/// <summary>Proveedor opcional de cola. Windows (GSMTC) no tiene ninguno; un servicio concreto (p. ej. la API web de un reproductor, con inicio de sesión del usuario) podría implementarlo.</summary>
public interface IMediaQueueSource
{
    string ProviderName { get; }
    bool Handles(string sourceAppId);
    Task<IReadOnlyList<QueueItem>> GetQueueAsync(CancellationToken ct = default);
}

/// <summary>Proveedor opcional de «me gusta» para la canción actual.</summary>
public interface IMediaFavoriteSource
{
    string ProviderName { get; }
    bool Handles(string sourceAppId);
    Task<bool?> IsFavoriteAsync(NowPlayingInfo track, CancellationToken ct = default);
    Task<bool> SetFavoriteAsync(NowPlayingInfo track, bool favorite, CancellationToken ct = default);
}

public static class MediaFeatureResolver
{
    /// <summary>
    /// Capacidades efectivas: las de la sesión de Windows más las que aporte algún proveedor que reconozca la app de origen. Sin proveedor no hay Queue ni Favorite,
    /// y el aleatorio y el repetir solo si la sesión los habilita (Windows lo informa por sesión).
    /// </summary>
    public static MediaCapabilities Effective(MediaCapabilities session, string? sourceApp, IEnumerable<IMediaQueueSource>? queues, IEnumerable<IMediaFavoriteSource>? favorites)
    {
        var caps = session; var app = sourceApp ?? "";
        if (queues?.Any(q => q.Handles(app)) == true) caps |= MediaCapabilities.Queue;
        if (favorites?.Any(f => f.Handles(app)) == true) caps |= MediaCapabilities.Favorite;
        return caps;
    }

    /// <summary>Apagado → toda la lista → una canción → apagado (el orden que usan los reproductores).</summary>
    public static RepeatMode NextRepeat(RepeatMode m) => m switch { RepeatMode.Off => RepeatMode.List, RepeatMode.List => RepeatMode.Track, _ => RepeatMode.Off };

    public static string RepeatLabel(RepeatMode m) => m switch { RepeatMode.Track => "Repetir canción", RepeatMode.List => "Repetir lista", _ => "No repetir" };

    /// <summary>Nombre amable de la app de origen a partir de su identificador (AUMID o nombre de proceso).</summary>
    public static string SourceName(string? sourceApp)
    {
        var s = (sourceApp ?? "").ToLowerInvariant();
        if (s.Contains("spotify")) return "Spotify";
        if (s.Contains("zune") || s.Contains("media.player") || s.Contains("microsoft.media")) return "Reproductor multimedia";
        if (s.Contains("chrome")) return "Chrome"; if (s.Contains("msedge") || s.Contains("edge")) return "Edge"; if (s.Contains("firefox")) return "Firefox";
        if (s.Contains("youtube")) return "YouTube Music"; if (s.Contains("itunes") || s.Contains("apple")) return "Apple Music";
        return string.IsNullOrWhiteSpace(sourceApp) ? "" : sourceApp!.Split('!', '.', '\\', '/').LastOrDefault(p => p.Length > 1) ?? sourceApp!;
    }
}
