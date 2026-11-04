namespace NotchWin.Core.Music;

/// <summary>Glifos (Segoe MDL2) del botón de Reproducir/Pausar: muestra la ACCIÓN que hará (reproduciendo → Pausa). Una sola fuente para el reproductor y las tarjetas.</summary>
public static class MediaGlyphs
{
    public static string PlayPause(bool isPlaying) => isPlaying ? "" : "";
    public static string PlayPauseTip(bool isPlaying) => isPlaying ? "Pausar" : "Reproducir";
}

/// <summary>
/// It7: orden de publicación del estado de reproducción. Las lecturas de Windows (propiedades, carátula…) son asíncronas y pueden terminar DESORDENADAS: una lectura
/// anterior («reproduciendo») que acaba después de una posterior («en pausa») pisaba el estado correcto y las tarjetas se quedaban mostrando lo contrario de la realidad.
/// Cada lectura toma un número al empezar; solo se publica si no hay ya publicada una posterior.
/// </summary>
public sealed class PlaybackPublishGate
{
    private long _issued, _published;
    private readonly object _gate = new();

    public long Begin() { lock (_gate) return ++_issued; }

    public bool TryPublish(long ticket)
    {
        lock (_gate)
        {
            if (ticket < _published) return false;
            _published = ticket; return true;
        }
    }
}

/// <summary>Reglas puras para reflejar el estado REAL: no se asume que un comando se ejecutó; tras él (y periódicamente) se vuelve a leer y solo se publica si cambió algo.</summary>
public static class PlaybackReconcile
{
    /// <summary>¿Lo leído ahora difiere de lo último publicado en algo que el usuario ve (pista, reproduciendo/pausa, sesión)?</summary>
    public static bool Differs(NowPlayingInfo? published, NowPlayingInfo? actual)
    {
        if (published is null || actual is null) return published is null != (actual is null);
        return published.IsPlaying != actual.IsPlaying || published.Title != actual.Title || published.Artist != actual.Artist || published.SourceAppName != actual.SourceAppName
            || published.Duration != actual.Duration;
    }

    /// <summary>Esperas (ms) para releer tras un comando de transporte: el reproductor tarda en cambiar su estado y algunos no avisan.</summary>
    public static readonly int[] AfterCommandDelaysMs = [250, 900, 2500];
}
