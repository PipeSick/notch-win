using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotchWin.Core.Clipboard;

/// <summary>Clipboard options (central: WinMac's settings). Persistence is OFF by default; secrets and excluded apps are never stored.</summary>
public sealed record ClipboardOptions
{
    public int Limit { get; init; } = 50;
    public bool Persist { get; init; }
    public bool SkipSecrets { get; init; } = true;
    /// <summary>Process names (without .exe) whose copies are ignored. Password managers by default.</summary>
    public IReadOnlyList<string> ExcludedApps { get; init; } = DefaultExcluded;

    public static readonly IReadOnlyList<string> DefaultExcluded = ["KeePass", "KeePassXC", "1Password", "Bitwarden", "LastPass", "Dashlane", "NordPass", "ProtonPass", "Enpass"];

    public ClipboardOptions Sanitize() => this with
    {
        Limit = Math.Clamp(Limit, 5, 500),
        ExcludedApps = (ExcludedApps ?? DefaultExcluded).Select(a => (a ?? "").Trim()).Select(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a[..^4] : a)
            .Where(a => a.Length is > 0 and <= 64).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList(),
    };

    public bool IsExcluded(string? processName) => processName is { Length: > 0 } p && ExcludedApps.Any(a => string.Equals(a, Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Heuristics for text that must not be kept: private keys, well-known token formats, JWTs and long random-looking single tokens (passwords, API keys).
/// Conservative on purpose: a false positive only means one copy is not kept in the history. Nothing here logs or transmits the text.
/// </summary>
public static partial class SecretDetector
{
    [GeneratedRegex(@"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----")] private static partial Regex PrivateKey();
    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")] private static partial Regex Aws();
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b")] private static partial Regex GitHub();
    [GeneratedRegex(@"\b(?:sk|pk|rk)[-_](?:live|test|proj|ant)?[-_]?[A-Za-z0-9_\-]{20,}\b")] private static partial Regex ApiKey();
    [GeneratedRegex(@"\bxox[abpors]-[A-Za-z0-9-]{10,}\b")] private static partial Regex Slack();
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")] private static partial Regex Jwt();
    [GeneratedRegex(@"(?i)\b(?:password|passwd|contraseña|pwd|secret|token|api[_-]?key)\s*[:=]\s*\S{6,}")] private static partial Regex Assignment();

    public static bool LooksSecret(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        if (PrivateKey().IsMatch(t) || Aws().IsMatch(t) || GitHub().IsMatch(t) || ApiKey().IsMatch(t) || Slack().IsMatch(t) || Jwt().IsMatch(t) || Assignment().IsMatch(t)) return true;
        return LooksLikeRandomToken(t);
    }

    /// <summary>One "word" of 12–128 chars mixing at least three character classes with high entropy and no spaces: typical of generated passwords and keys.</summary>
    public static bool LooksLikeRandomToken(string t)
    {
        if (t.Length is < 12 or > 128 || t.Any(char.IsWhiteSpace)) return false;
        if (Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme is "http" or "https" or "file" or "mailto")) return false;   // a link is not a password
        if (t.Contains('\\') || (t.Length > 2 && t[1] == ':')) return false;                                                    // a path
        int classes = (t.Any(char.IsLower) ? 1 : 0) + (t.Any(char.IsUpper) ? 1 : 0) + (t.Any(char.IsDigit) ? 1 : 0) + (t.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        if (classes < 3) return false;
        var freq = t.GroupBy(c => c).Select(g => g.Count() / (double)t.Length);
        double entropy = -freq.Sum(p => p * Math.Log2(p));
        return entropy >= 3.3;
    }
}

/// <summary>Encrypts and decrypts the persisted history for the current Windows user (DPAPI in the app). Abstract so the format is tested on Linux.</summary>
public interface IClipboardProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>
/// Optional persistence (off by default): pinned and recent entries, ENCRYPTED for the current user, in NotchWin's local data folder. Secrets are never
/// written (they are never in the history). Turning persistence off deletes the file. Any unreadable file is ignored (and replaced on the next save).
/// </summary>
public static class ClipboardStore
{
    private sealed record Row(string Kind, string? Text, string? Png, DateTimeOffset At, bool Pinned);

    public static byte[] Serialize(IEnumerable<ClipboardEntry> entries, IClipboardProtector p)
    {
        var rows = entries.Select(e => new Row(e.Kind.ToString(), e.Text, e.ImagePng is null ? null : Convert.ToBase64String(e.ImagePng), e.At, e.Pinned)).ToList();
        return p.Protect(JsonSerializer.SerializeToUtf8Bytes(new { v = 1, rows }));
    }

    public static IReadOnlyList<ClipboardEntry> Deserialize(byte[] blob, IClipboardProtector p)
    {
        try
        {
            using var doc = JsonDocument.Parse(p.Unprotect(blob));
            if (!doc.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
            var list = new List<ClipboardEntry>();
            foreach (var r in rows.EnumerateArray())
            {
                var row = r.Deserialize<Row>(); if (row is null) continue;
                if (row.Kind == nameof(ClipboardItemKind.Text) && !string.IsNullOrWhiteSpace(row.Text)) list.Add(new ClipboardEntry { Kind = ClipboardItemKind.Text, Text = row.Text, At = row.At, Pinned = row.Pinned });
                else if (row.Kind == nameof(ClipboardItemKind.Image) && row.Png is { Length: > 0 } b64) list.Add(new ClipboardEntry { Kind = ClipboardItemKind.Image, ImagePng = Convert.FromBase64String(b64), At = row.At, Pinned = row.Pinned });
            }

            return list;
        }
        catch (Exception e) when (e is JsonException or FormatException or System.Security.Cryptography.CryptographicException or ArgumentException) { return []; }
    }
}

/// <summary>
/// What Spotlight may see of the history (docs/28): only entries already admitted by the privacy rules, as one-line previews (max 160 characters, images
/// only as «Imagen»), newest/pinned first. The full text never leaves the Island; copying is done by the Island itself. Never logged.
/// </summary>
public static class ClipboardQuery
{
    public sealed record Item(string Id, string Kind, string Preview, bool Pinned, DateTimeOffset At);

    public static IReadOnlyList<Item> Search(ClipboardHistory h, string? query, int max = 8)
    {
        var q = (query ?? "").Trim();
        bool recent = q.Length == 0 || q.Equals("portapapeles", StringComparison.OrdinalIgnoreCase) || q.Equals("clipboard", StringComparison.OrdinalIgnoreCase);
        if (q.StartsWith("cb ", StringComparison.OrdinalIgnoreCase)) q = q[3..].Trim();
        var src = recent ? h.View() : h.View(q);
        return src.Take(Math.Clamp(max, 1, 20)).Select(e => new Item(e.Id.ToString("N"), e.Kind == ClipboardItemKind.Image ? "image" : "text",
            e.Kind == ClipboardItemKind.Image ? "Imagen" : Preview(e.Text ?? ""), e.Pinned, e.At)).ToList();
    }

    public static string Preview(string text)
    {
        var one = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())).Trim();
        return one.Length > 160 ? one[..159] + "…" : one;
    }
}
