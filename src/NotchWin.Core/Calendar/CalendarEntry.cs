using System.Text.Json;
using System.Text.Json.Serialization;
using WinMac.Calendar;

namespace NotchWin.Core.Calendar;

public enum CalendarSource { Ics, Local }

/// <summary>Un evento listo para mostrar: venga del archivo .ics (solo lectura) o del calendario local de Hatsu (editable).</summary>
public sealed record CalendarEntry(string Id, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string ColorHex, CalendarSource Source)
{
    public bool Editable => Source == CalendarSource.Local;
}

public static class CalendarColors
{
    /// <summary>Violeta primero (acento de Hatsu); el resto, colores del mockup aprobado.</summary>
    public static readonly IReadOnlyList<string> Palette = ["#0A84FF", "#34C759", "#FF9F0A", "#BF5AF2", "#FF375F", "#64D2FF"];

    public static bool IsValid(string? hex) => hex is { Length: 7 } h && h[0] == '#' && h.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Color estable para un evento sin color propio (los del .ics): mismo título → mismo color en cada sesión (FNV-1a; <c>string.GetHashCode</c> cambia en cada arranque).</summary>
    public static string For(string? seed)
    {
        uint h = 2166136261; foreach (var c in (seed ?? "").ToLowerInvariant()) { h ^= c; h *= 16777619; }
        return Palette[(int)(h % (uint)Palette.Count)];
    }
}

/// <summary>
/// Calendario LOCAL de Hatsu: los eventos que el usuario crea desde la Isla. Un archivo JSON en la carpeta de datos de NotchWin; nada sale del equipo ni se sincroniza.
/// Es la única fuente editable: un .ics (menos aún uno remoto) no admite escritura y nunca se modifica. Guarda con escritura atómica; un archivo dañado se ignora (queda vacío) sin tocarlo.
/// </summary>
public sealed class LocalCalendarStore
{
    public const int MaxEvents = 2000, MaxTitle = 120;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path; private readonly object _gate = new(); private List<Stored> _items = [];

    public LocalCalendarStore(string path) { _path = path; Load(); }

    public event Action? Changed;

    private sealed record Stored(string Id, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string Color);
    private sealed record FileModel([property: JsonPropertyName("version")] int Version, [property: JsonPropertyName("events")] List<Stored> Events);

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), Json);
            if (m is { Version: 1, Events: { } list }) _items = list.Where(Valid).Take(MaxEvents).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _items = []; }
    }

    private static bool Valid(Stored s) => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Title) && s.End >= s.Start;

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(new FileModel(1, _items), Json)); File.Move(tmp, _path, overwrite: true);
    }

    public IReadOnlyList<CalendarEntry> All() { lock (_gate) return _items.Select(ToEntry).ToList(); }

    private static CalendarEntry ToEntry(Stored s) => new(s.Id, s.Title, s.Start, s.End, s.AllDay, CalendarColors.IsValid(s.Color) ? s.Color : CalendarColors.For(s.Title), CalendarSource.Local);

    /// <summary>Crea un evento. Devuelve null si el título está vacío, el fin es anterior al inicio o se alcanzó el máximo.</summary>
    public CalendarEntry? Add(string? title, DateTimeOffset start, DateTimeOffset end, bool allDay, string? colorHex = null)
    {
        var t = (title ?? "").Trim(); if (t.Length == 0 || end < start) return null; if (t.Length > MaxTitle) t = t[..MaxTitle];
        var color = CalendarColors.IsValid(colorHex) ? colorHex!.ToUpperInvariant() : CalendarColors.Palette[3];
        if (allDay) { start = new DateTimeOffset(start.Date, start.Offset); end = end <= start ? start.AddDays(1) : end; }
        Stored s;
        lock (_gate) { if (_items.Count >= MaxEvents) return null; s = new Stored(Guid.NewGuid().ToString("N"), t, start, end, allDay, color); _items.Add(s); Save(); }
        Changed?.Invoke(); return ToEntry(s);
    }

    public bool Remove(string id)
    {
        bool removed; lock (_gate) { removed = _items.RemoveAll(i => i.Id == id) > 0; if (removed) Save(); }
        if (removed) Changed?.Invoke(); return removed;
    }

    public bool Update(string id, string? title, DateTimeOffset start, DateTimeOffset end, bool allDay, string? colorHex)
    {
        var t = (title ?? "").Trim(); if (t.Length == 0 || end < start) return false; if (t.Length > MaxTitle) t = t[..MaxTitle];
        lock (_gate)
        {
            int i = _items.FindIndex(x => x.Id == id); if (i < 0) return false;
            _items[i] = new Stored(id, t, start, end, allDay, CalendarColors.IsValid(colorHex) ? colorHex!.ToUpperInvariant() : _items[i].Color); Save();
        }

        Changed?.Invoke(); return true;
    }
}

public static class CalendarAgenda
{
    /// <summary>Junta lo del .ics (solo lectura) con lo local (editable), ordenado por inicio.</summary>
    public static IReadOnlyList<CalendarEntry> Merge(IEnumerable<CalendarEvent> ics, IEnumerable<CalendarEntry> local) =>
        ics.Select(e => new CalendarEntry("ics:" + e.Start.ToUnixTimeSeconds() + ":" + e.Title, e.Title, e.Start, e.End, e.AllDay, CalendarColors.For(e.Title), CalendarSource.Ics))
           .Concat(local).OrderBy(e => e.Start).ToList();

    /// <summary>Eventos de un día (los de todo el día primero; uno que cruza medianoche aparece en cada día que toca).</summary>
    public static IReadOnlyList<CalendarEntry> On(IEnumerable<CalendarEntry> entries, DateOnly day)
    {
        var from = day.ToDateTime(TimeOnly.MinValue); var to = from.AddDays(1);
        return entries.Where(e => e.Start.LocalDateTime < to && (e.End.LocalDateTime > from || (e.End == e.Start && e.Start.LocalDateTime >= from)))
            .OrderByDescending(e => e.AllDay).ThenBy(e => e.Start).ToList();
    }

    /// <summary>Cuántos eventos tiene cada día, para los puntos del mes.</summary>
    public static int CountOn(IEnumerable<CalendarEntry> entries, DateOnly day) => On(entries, day).Count;

    /// <summary>El próximo evento: uno en curso, o el siguiente que empieza (los de todo el día de hoy cuentan como en curso).</summary>
    public static CalendarEntry? Next(IEnumerable<CalendarEntry> entries, DateTimeOffset now) =>
        entries.Where(e => e.End > now || (e.AllDay && e.Start.Date == now.Date)).OrderBy(e => e.Start).FirstOrDefault();
}
