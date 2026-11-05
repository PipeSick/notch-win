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
                return new PillItem(PillKind.Notification, title ?? app!, body ?? (title is null ? null : app), 0, false, icon, w, h) { App = app, NotificationKey = Long(e, "id") is { } id ? "win:" + id : null };
            case "call":
                return CallFull(e);
            default:
                return null;
        }
    }

    /// <summary>
    /// WM-038: <c>{ "kind": "call", "state": "ringing"|"missed"|"ended"|"none", "caller": "Mamá" }</c> from WinMac Connect. Ringing stays (up to the arbiter's safety time);
    /// missed and ended show for a few seconds; none takes the call away. Only a notice: no answer/reject controls (not validated) and no audio.
    /// </summary>
    public static PillItem? Call(string? state, string? caller) => state?.ToLowerInvariant() switch
    {
        "ringing" => new PillItem(PillKind.Call, caller is null ? "Llamada entrante" : "Llamada de " + caller, "iPhone") { App = caller ?? "Teléfono" },
        "missed" => new PillItem(PillKind.Call, "Llamada perdida", caller ?? "iPhone") { App = caller ?? "Teléfono", Duration = TimeSpan.FromSeconds(5) },
        "ended" => new PillItem(PillKind.Call, "Llamada terminada", caller ?? "iPhone") { App = caller ?? "Teléfono", Duration = TimeSpan.FromSeconds(3) },
        "none" => new PillItem(PillKind.Call, "") { Withdraw = true },
        _ => null,
    };

    /// <summary>
    /// Docs/60: the full call event from Hatsu Connect:
    /// <c>{ "kind":"call", "state":"ringing|active|missed|ended|none", "caller":"Mamá", "device":"iPhone", "since":1730600000000, "controls":{"answer":true,"reject":true,"hangup":false,"mute":false}, "audioOnPc":true, "muted":false, "notice":"…" }</c>.
    /// Untrusted like everything else: unknown fields are ignored, missing controls mean no buttons, an active call without a start time is shown without duration.
    /// </summary>
    public static PillItem? CallFull(JsonElement e)
    {
        string? state = Str(e, "state")?.ToLowerInvariant(), caller = Short(Str(e, "caller"), MaxTitle), device = Short(Str(e, "device"), 24) ?? "Teléfono", notice = Short(Str(e, "notice"), MaxBody);
        long? since = Long(e, "since"); bool pc = Bool(e, "audioOnPc"), muted = Bool(e, "muted");
        CallControls ctl = default;
        if (e.TryGetProperty("controls", out var c) && c.ValueKind == JsonValueKind.Object) ctl = new(Bool(c, "answer"), Bool(c, "reject"), Bool(c, "hangup"), Bool(c, "mute"));
        switch (state)
        {
            case "ringing":
                return new PillItem(PillKind.Call, caller is null ? "Llamada entrante" : "Llamada de " + caller, notice ?? device) { App = caller ?? "Teléfono", Controls = ctl };
            case "active":
                return new PillItem(PillKind.Call, caller is null ? "En llamada" : "En llamada · " + caller, notice ?? ActiveSubtitle(since, pc, muted, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                { App = caller ?? "Teléfono", Controls = ctl, IsActiveCall = true, HasNotice = notice is not null, CallSinceMs = since, AudioOnPc = pc, CallMuted = muted, Duration = TimeSpan.FromHours(6) };
            case "missed": case "ended": case "none":
                return Call(state, caller) is { } p ? (state == "none" ? p : p with { Subtitle = state == "missed" ? caller ?? device : device }) : null;
            default: return null;
        }
    }

    /// <summary>«03:25 · Audio en el PC». The duration only if the start is known; where the audio is, always (so the user knows whether to speak to the PC).</summary>
    public static string ActiveSubtitle(long? sinceMs, bool audioOnPc, bool muted, long nowMs)
    {
        string audio = audioOnPc ? "Audio en el PC" : "Audio en el teléfono";
        if (muted) audio += " · Silenciado";
        if (sinceMs is not { } s || nowMs < s) return audio;
        var d = TimeSpan.FromMilliseconds(nowMs - s);
        string t = d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{d.Minutes:00}:{d.Seconds:00}";
        return t + " · " + audio;
    }

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public const int MaxSnapshot = 60, MaxFullBody = 2000;

    /// <summary>
    /// WM-025: the snapshot of Windows' notification centre the bar sends on change:
    /// <c>{ "kind": "notifications", "items": [ { "id": 12, "app": "Teams", "appId": "…", "title": "Ana", "body": "…", "at": 1730600000000 } ] }</c>.
    /// Null if <paramref name="e"/> is not one. Texts keep their line breaks (the expanded view shows them in full), within limits.
    /// </summary>
    public static IReadOnlyList<Notifications.NotchNotification>? ParseSnapshot(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !string.Equals(Str(e, "kind"), "notifications", StringComparison.OrdinalIgnoreCase)) return null;
        var list = new List<Notifications.NotchNotification>();
        if (!e.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in items.EnumerateArray())
        {
            if (list.Count >= MaxSnapshot || it.ValueKind != JsonValueKind.Object || Long(it, "id") is not { } id) continue;
            string app = Short(Str(it, "app"), MaxTitle) ?? "App", title = Short(Str(it, "title"), MaxTitle) ?? app;
            string body = Str(it, "body") is { } b ? (b.Length > MaxFullBody ? b[..MaxFullBody] + "…" : b).Trim() : "";
            var at = Long(it, "at") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.Now;
            list.Add(new Notifications.NotchNotification(app, title, body, at, "win:" + id, Short(Str(it, "appId"), 300)));
        }

        return list;
    }

    private static long? Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

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
