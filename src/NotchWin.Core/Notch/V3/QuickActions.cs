namespace NotchWin.Core.Notch.V3;

public sealed record QuickAction(string Id, string Label, string Glyph, bool LocalOnly);

/// <summary>
/// Accesos rápidos de Inicio: LISTA CERRADA. «clipboard» es de la propia Isla (abre su pestaña); el resto los ejecuta Hatsu (la Barra) y solo se ofrecen si la Barra dice que el módulo está
/// disponible. El canal Isla→Barra solo acepta estos identificadores: nunca un comando libre.
/// </summary>
public static class QuickActions
{
    public static readonly IReadOnlyList<QuickAction> All =
    [
        new("capture", "Captura", "\uE722", false), new("clipboard", "Portapapeles", "\uE77F", true), new("snap", "Snap", "\uE8A1", false),
        new("mission", "Mission Control", "\uE7C4", false), new("livetext", "Live Text", "\uE8D2", false), new("gestures", "Gestures", "\uE7C9", false),
    ];

    public const string Topic = "notch.quick", AvailableKind = "quick";

    public static QuickAction? ById(string? id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>Lo que se muestra: el de la propia Isla (si su pestaña está encendida) y los de Hatsu que la Barra declaró disponibles, en el orden de <see cref="All"/>.</summary>
    public static IReadOnlyList<QuickAction> Visible(IEnumerable<string>? availableFromHatsu, bool clipboardTabOn)
    {
        var avail = new HashSet<string>(availableFromHatsu ?? [], StringComparer.Ordinal);
        return All.Where(a => a.LocalOnly ? clipboardTabOn : avail.Contains(a.Id)).ToList();
    }

    /// <summary>Formato del aviso de disponibilidad que manda la Barra: <c>{ "kind": "quick", "available": ["capture", ...] }</c>. Se descarta lo desconocido y los repetidos.</summary>
    public static IReadOnlyList<string>? ParseAvailable(System.Text.Json.JsonElement e)
    {
        if (e.ValueKind != System.Text.Json.JsonValueKind.Object || !e.TryGetProperty("kind", out var k) || k.GetString() != AvailableKind) return null;
        if (!e.TryGetProperty("available", out var a) || a.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
        return a.EnumerateArray().Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String).Select(x => x.GetString()!).Where(id => ById(id) is { LocalOnly: false }).Distinct().ToList();
    }
}
