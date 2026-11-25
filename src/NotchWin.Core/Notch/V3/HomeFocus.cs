namespace NotchWin.Core.Notch.V3;

public enum HomePrimary { None, Music, Timer, Download }

/// <summary>Lo que la Isla sabe ahora mismo, sin tipos de interfaz. <see cref="MusicPlaying"/> = suena; <see cref="HasTrack"/> = hay sesión con canción (suena o en pausa).</summary>
public sealed record HomeContext(bool HasTrack, bool MusicPlaying, bool TimerRunning, bool TimerPaused, int ActiveDownloads, int Notifications);

public enum HomeSecondary { Notifications, Downloads, Timer, Music }

/// <summary>
/// Inicio V3 (minimalista y contextual): UNA actividad principal y, como mucho, unas pocas secundarias; nunca un tablero con todo.
/// Prioridad (de más a menos): música sonando → temporizador en marcha o en pausa → descarga activa → música en pausa → nada.
/// Un temporizador a medias le gana a una canción en pausa (la canción no pide atención; el temporizador sí). Puro: se prueba sin Windows.
/// </summary>
public static class HomeFocus
{
    public static HomePrimary Primary(HomeContext c)
    {
        if (c.HasTrack && c.MusicPlaying) return HomePrimary.Music;
        if (c.TimerRunning || c.TimerPaused) return HomePrimary.Timer;
        if (c.ActiveDownloads > 0) return HomePrimary.Download;
        if (c.HasTrack) return HomePrimary.Music;
        return HomePrimary.None;
    }

    /// <summary>Secundarias en orden de relevancia, sin repetir la principal. Con <paramref name="max"/> = 3 la pantalla no se convierte en un panel de control.</summary>
    public static IReadOnlyList<HomeSecondary> Secondary(HomeContext c, int max = 3)
    {
        var primary = Primary(c); var list = new List<HomeSecondary>();
        if (c.Notifications > 0) list.Add(HomeSecondary.Notifications);
        if (c.ActiveDownloads > 0 && primary != HomePrimary.Download) list.Add(HomeSecondary.Downloads);
        if ((c.TimerRunning || c.TimerPaused) && primary != HomePrimary.Timer) list.Add(HomeSecondary.Timer);
        if (c.HasTrack && primary != HomePrimary.Music) list.Add(HomeSecondary.Music);
        return list.Take(Math.Max(0, max)).ToList();
    }
}
