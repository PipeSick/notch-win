using NotchWin.Core.Notifications;

namespace NotchWin.Core.Notch.V3;

public sealed record NotificationGroup(string App, string? AppId, IReadOnlyList<NotchNotification> Items, DateTimeOffset Latest, int NewCount);

/// <summary>
/// Bandeja de notificaciones de la Isla. Windows NO informa qué avisos ya leyó el usuario, así que «nuevo» aquí significa solo «aún no lo viste EN LA ISLA»: es un estado
/// LOCAL, en memoria, que se reinicia al cerrar Hatsu y nunca se presenta como confirmación de la app de origen. El historial conserva, mientras dure la sesión, los avisos que
/// ya desaparecieron del centro de Windows (hasta <see cref="MaxHistory"/>); no se escribe a disco (los textos de los avisos pueden ser privados).
/// </summary>
public sealed class NotificationInbox
{
    public const int MaxHistory = 100;
    private readonly HashSet<string> _seen = []; private readonly List<NotchNotification> _history = []; private Dictionary<string, NotchNotification> _last = [];
    private readonly object _gate = new();

    public static string IdOf(NotchNotification n) => n.Key ?? $"{n.Source}|{n.Title}|{n.At.UtcTicks}";

    /// <summary>Se llama con cada instantánea de la lista actual: lo que estaba y ya no está pasa al historial.</summary>
    public void Observe(IReadOnlyList<NotchNotification> current)
    {
        lock (_gate)
        {
            var now = current.ToDictionary(IdOf, n => n, StringComparer.Ordinal);
            foreach (var (id, n) in _last) if (!now.ContainsKey(id)) { _history.Insert(0, n); _seen.Remove(id); }
            if (_history.Count > MaxHistory) _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            _last = now;
        }
    }

    public IReadOnlyList<NotchNotification> History { get { lock (_gate) return [.. _history]; } }

    public bool IsNew(NotchNotification n) { lock (_gate) return !_seen.Contains(IdOf(n)); }

    public int NewCount(IEnumerable<NotchNotification> current) { lock (_gate) return current.Count(n => !_seen.Contains(IdOf(n))); }

    /// <summary>El usuario vio estos avisos en la Isla (abrió la pestaña o tocó uno).</summary>
    public void MarkSeen(IEnumerable<NotchNotification> items) { lock (_gate) foreach (var n in items) _seen.Add(IdOf(n)); }

    public void ClearHistory() { lock (_gate) _history.Clear(); }

    /// <summary>Agrupa por aplicación, el grupo con el aviso más reciente primero y, dentro, lo más reciente primero.</summary>
    public IReadOnlyList<NotificationGroup> GroupByApp(IEnumerable<NotchNotification> items)
    {
        lock (_gate)
            return items.GroupBy(n => string.IsNullOrWhiteSpace(n.Source) ? "App" : n.Source, StringComparer.OrdinalIgnoreCase)
                .Select(g => { var list = g.OrderByDescending(n => n.At).ToList(); return new NotificationGroup(g.Key, list.Select(n => n.AppId).FirstOrDefault(a => a is not null), list, list[0].At, list.Count(n => !_seen.Contains(IdOf(n)))); })
                .OrderByDescending(g => g.Latest).ToList();
    }
}
