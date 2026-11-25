using NotchWin.Core.Notifications;

namespace NotchWin.Core.Notch.V3;

public enum NotificationFilter { All, Apps, Hatsu }

/// <summary>
/// Notificaciones V3. Filtros solo sobre lo que los datos permiten: «Apps» = lo que Windows tiene en su centro de notificaciones; «Hatsu» = avisos propios (temporizador,
/// descargas, periféricos…). NO hay «no leído» ni «Mensajes»: Windows no entrega esa información.
/// </summary>
public static class NotificationView
{
    public static bool IsFromWindows(NotchNotification n) => n.Key is { } k && k.StartsWith("win:", StringComparison.Ordinal);

    public static IReadOnlyList<NotchNotification> Filter(IEnumerable<NotchNotification> items, NotificationFilter f) => f switch
    {
        NotificationFilter.Apps => items.Where(IsFromWindows).ToList(),
        NotificationFilter.Hatsu => items.Where(n => !IsFromWindows(n)).ToList(),
        _ => items.ToList(),
    };

    public static string FilterLabel(NotificationFilter f) => f switch { NotificationFilter.Apps => "Apps", NotificationFilter.Hatsu => "Hatsu", _ => "Todas" };

    /// <summary>«ahora», «Hace 2 min», «Hace 3 h», «Ayer», «Hace 4 d» o la fecha corta. Nunca devuelve un tiempo negativo (relojes desfasados).</summary>
    public static string Relative(DateTimeOffset at, DateTimeOffset now)
    {
        var d = now - at; if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d < TimeSpan.FromSeconds(45)) return "ahora";
        if (d < TimeSpan.FromMinutes(60)) return $"Hace {Math.Max(1, (int)Math.Round(d.TotalMinutes))} min";
        if (d < TimeSpan.FromHours(24)) return $"Hace {(int)d.TotalHours} h";
        var days = (now.Date - at.Date).Days;
        if (days == 1) return "Ayer";
        return days < 7 ? $"Hace {days} d" : at.ToString("dd-MM-yyyy");
    }
}
