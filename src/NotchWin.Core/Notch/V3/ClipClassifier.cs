using System.Text.RegularExpressions;

namespace NotchWin.Core.Notch.V3;

public enum ClipKind { Text, Link, Code, Path, Image }

/// <summary>
/// Portapapeles V3: tipo de contenido para la vista previa. Se DEDUCE del texto ya guardado (no se captura nada más ni se guarda nada nuevo). Las heurísticas son
/// conservadoras: ante la duda es texto normal, porque llamar «código» a una frase es peor que no distinguirlo.
/// </summary>
public static partial class ClipClassifier
{
    public static ClipKind Classify(string? text, bool isImage = false)
    {
        if (isImage) return ClipKind.Image;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return ClipKind.Text;
        bool oneLine = !t.Contains('\n');
        if (oneLine && !t.Contains(' ') && IsLink(t)) return ClipKind.Link;
        if (oneLine && PathRx().IsMatch(t)) return ClipKind.Path;
        return LooksLikeCode(t) ? ClipKind.Code : ClipKind.Text;
    }

    private static bool IsLink(string t) =>
        (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        && t.Length > 6;

    private static readonly string[] CommandStarts = ["npm ", "npx ", "yarn ", "pnpm ", "git ", "dotnet ", "pip ", "python ", "docker ", "cargo ", "sudo ", "cd ", "winget ", "powershell ", "curl "];
    private static readonly string[] Keywords = ["function ", "const ", "let ", "var ", "def ", "class ", "import ", "from ", "return ", "public ", "private ", "using ", "namespace ", "#include", "SELECT ", "INSERT ", "UPDATE ", "<div", "<?xml", "</"];

    private static bool LooksLikeCode(string t)
    {
        var low = t.TrimStart();
        if (CommandStarts.Any(c => low.StartsWith(c, StringComparison.Ordinal))) return true;
        int score = 0; var lines = t.Split('\n');
        if (lines.Length > 1 && lines.Count(l => l.StartsWith("    ") || l.StartsWith('\t')) >= Math.Max(2, lines.Length / 3)) score += 2;   // sangría repetida
        if (lines.Any(l => Keywords.Any(k => l.TrimStart().StartsWith(k, StringComparison.Ordinal)))) score += 2;
        if (lines.Count(l => l.TrimEnd().EndsWith(';') || l.TrimEnd().EndsWith('{') || l.TrimEnd().EndsWith('}')) >= (lines.Length > 1 ? 2 : 1) && t.Length >= 6) score += 2;
        if (t.Contains("=>") || t.Contains("==") || t.Contains("!=") || t.Contains("&&") || t.Contains("||")) score += 1;
        int symbols = t.Count(c => c is '{' or '}' or '(' or ')' or ';' or '<' or '>' or '=' or '[' or ']');
        if (t.Length >= 8 && symbols * 100 / t.Length >= 12) score += 1;
        if ((t.StartsWith('{') && t.EndsWith('}')) || (t.StartsWith('[') && t.EndsWith(']'))) score += 2;   // JSON suelto
        return score >= 2;
    }

    [GeneratedRegex(@"^([A-Za-z]:\\|\\\\)[^\r\n]+$")]
    private static partial Regex PathRx();

    /// <summary>Vista previa de una línea para filas compactas: espacios colapsados salvo en código, acotada con «…».</summary>
    public static string Preview(string? text, ClipKind kind, int max = 120)
    {
        var t = (text ?? "").Trim();
        if (kind != ClipKind.Code) t = Regex.Replace(t, @"\s+", " ");
        else t = string.Join("  ", t.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(2));
        return t.Length <= max ? t : t[..Math.Max(1, max - 1)] + "…";
    }

    /// <summary>Sitio de un enlace para mostrar («hatsu.app»), solo con lo que ya está en el texto; null si no se puede leer.</summary>
    public static string? LinkHost(string? text)
    {
        var t = (text ?? "").Trim(); if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme is "http" or "https") ? (u.Host.StartsWith("www.") ? u.Host[4..] : u.Host) : null;
    }

    public static string KindLabel(ClipKind k) => k switch { ClipKind.Link => "Enlace", ClipKind.Code => "Código", ClipKind.Path => "Ruta", ClipKind.Image => "Imagen", _ => "Texto" };
}
