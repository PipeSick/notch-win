using System.Text.Json;

namespace NotchWin.Core.Pill;

/// <summary>
/// Wire format of the events WinMac's top bar relays to the notch on the <see cref="Topic"/> event topic (camelCase JSON, one event per message):
/// <code>
/// { "kind": "volume", "level": 65, "muted": false, "device": "Altavoces" }
/// { "kind": "brightness", "level": 80, "display": "Pantalla integrada" }
/// { "kind": "notification", "app": "Teams", "title": "Ana", "body": "¿Vienes?", "icon": "&lt;base64 BGRA&gt;", "iconW": 32, "iconH": 32 }
/// </code>
/// Every value is untrusted: unknown kinds are ignored, levels are clamped, text is trimmed and shortened, and an icon whose size does not match its
/// pixels is dropped (the item is still shown, with the app's initial).
/// </summary>
public static class PillFeed
{
    public const string Topic = "notch.feed";
    public const int MaxTitle = 120, MaxBody = 240, MaxIconSide = 64;

    public static PillItem? Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || Str(e, "kind") is not { } kind) return null;
        switch (kind.ToLowerInvariant())
        {
            case "volume" when Int(e, "level") is { } v:
                bool muted = e.TryGetProperty("muted", out var m) && m.ValueKind == JsonValueKind.True;
                return new PillItem(PillKind.Volume, Short(Str(e, "device"), MaxTitle) ?? "Volumen", null, Math.Clamp(v, 0, 100), muted);
            case "brightness" when Int(e, "level") is { } b:
                return new PillItem(PillKind.Brightness, Short(Str(e, "display"), MaxTitle) ?? "Brillo", null, Math.Clamp(b, 0, 100));
            case "notification":
                string? app = Short(Str(e, "app"), MaxTitle), title = Short(Str(e, "title"), MaxTitle), body = Short(Str(e, "body"), MaxBody);
                if (app is null && title is null && body is null) return null;
                var (icon, w, h) = Icon(e);
                // Title line: the notification's own title, or the app when it has none. Second line: the text (or the app, so it is never empty).
                return new PillItem(PillKind.Notification, title ?? app!, body ?? (title is null ? null : app), 0, false, icon, w, h) { App = app };
            default:
                return null;
        }
    }

    private static (byte[]? Px, int W, int H) Icon(JsonElement e)
    {
        if (Str(e, "icon") is not { } b64 || Int(e, "iconW") is not { } w || Int(e, "iconH") is not { } h) return (null, 0, 0);
        if (w <= 0 || h <= 0 || w > MaxIconSide || h > MaxIconSide) return (null, 0, 0);
        try
        {
            var px = Convert.FromBase64String(b64);
            return px.Length == w * h * 4 ? (px, w, h) : (null, 0, 0);
        }
        catch (FormatException) { return (null, 0, 0); }
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && !double.IsNaN(d)
        ? (int)Math.Round(Math.Clamp(d, int.MinValue, int.MaxValue)) : null;

    /// <summary>One line, trimmed; null when empty. Line breaks become spaces (the pill has one line per field).</summary>
    public static string? Short(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = string.Join(' ', s.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();
        return s.Length == 0 ? null : s.Length > max ? s[..(max - 1)].TrimEnd() + "…" : s;
    }
}
