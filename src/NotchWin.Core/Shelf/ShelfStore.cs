namespace NotchWin.Core.Shelf;

public enum ShelfKind { File, Folder, Text, Image }

/// <summary>
/// Un elemento de la estantería. Archivos y carpetas son REFERENCIAS (<see cref="Path"/> apunta al original, que la estantería nunca copia ni borra). Texto vive en el propio elemento. Una imagen sin archivo
/// (p. ej. arrastrada desde un navegador) se guarda como copia propia en la carpeta de la estantería: <see cref="OwnedPath"/> es lo ÚNICO que la estantería puede borrar.
/// </summary>
public sealed record ShelfItem(Guid Id, ShelfKind Kind, string Title, DateTime AddedUtc, string? Path = null, string? Text = null, string? OwnedPath = null);

public enum ShelfStatus { Ok, Missing }
public enum AddResult { Added, Duplicate, Rejected }

/// <summary>
/// Estantería temporal (pura, se prueba en Linux): límite configurable de elementos (el más antiguo se retira al pasarse), texto acotado, sin duplicados, validación de referencias (un original movido o borrado
/// se marca «no encontrado» pero se conserva hasta que el usuario lo quita) y borrado de copias propias ÚNICAMENTE dentro de la carpeta de la estantería.
/// </summary>
public sealed class ShelfStore
{
    public const int DefaultMax = 20, MinMax = 1, MaxMax = 100, MaxTextChars = 20_000;
    private readonly List<ShelfItem> _items = []; private readonly string _ownDir;
    public ShelfStore(string ownDir, int max = DefaultMax) { _ownDir = ownDir; Max = Math.Clamp(max, MinMax, MaxMax); }

    public int Max { get; private set; }
    public IReadOnlyList<ShelfItem> Items => _items;
    public event Action? Changed;
    public static int ClampMax(int v) => Math.Clamp(v, MinMax, MaxMax);

    /// <summary>Cambia el límite; si ahora hay de más, se retiran los más antiguos (devuelve sus copias propias para borrarlas).</summary>
    public IReadOnlyList<string> SetMax(int max)
    {
        Max = ClampMax(max); var removed = Trim(); if (removed.Count > 0 || true) Changed?.Invoke(); return removed;
    }

    public AddResult AddPath(string? path, bool isFolder, out IReadOnlyList<string> toDelete)
    {
        toDelete = [];
        if (string.IsNullOrWhiteSpace(path)) return AddResult.Rejected;
        if (_items.Any(i => i.Path is not null && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return AddResult.Duplicate;
        string name = NameOf(path);
        _items.Add(new ShelfItem(Guid.NewGuid(), isFolder ? ShelfKind.Folder : ShelfKind.File, name, DateTime.UtcNow, Path: path)); toDelete = Trim(); Changed?.Invoke(); return AddResult.Added;
    }

    /// <summary>Último componente de una ruta, con \\ o / (igual en cualquier sistema: se prueba en Linux).</summary>
    public static string NameOf(string path)
    {
        var t = path.TrimEnd('\\', '/'); int i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/')); var n = i >= 0 ? t[(i + 1)..] : t; return n.Length == 0 ? path : n;
    }

    public AddResult AddText(string? text, out IReadOnlyList<string> toDelete)
    {
        toDelete = [];
        if (string.IsNullOrWhiteSpace(text)) return AddResult.Rejected;
        if (text.Length > MaxTextChars) text = text[..MaxTextChars];
        if (_items.Any(i => i.Kind == ShelfKind.Text && i.Text == text)) return AddResult.Duplicate;
        string title = text.Trim().Replace("\r", "").Replace('\n', ' '); if (title.Length > 60) title = title[..60] + "…";
        _items.Add(new ShelfItem(Guid.NewGuid(), ShelfKind.Text, title, DateTime.UtcNow, Text: text)); toDelete = Trim(); Changed?.Invoke(); return AddResult.Added;
    }

    /// <summary>Una imagen guardada como copia propia en la carpeta de la estantería (la copia la hace quien llama; aquí solo se registra).</summary>
    public AddResult AddOwnedImage(string ownedPath, out IReadOnlyList<string> toDelete)
    {
        toDelete = [];
        if (!IsOwned(ownedPath)) return AddResult.Rejected;                      // nunca se registra como propia una ruta de fuera de la carpeta
        _items.Add(new ShelfItem(Guid.NewGuid(), ShelfKind.Image, "Imagen " + DateTime.Now.ToString("HH:mm:ss"), DateTime.UtcNow, OwnedPath: ownedPath)); toDelete = Trim(); Changed?.Invoke(); return AddResult.Added;
    }

    /// <summary>Quita un elemento. Devuelve la copia propia a borrar (null si era una referencia: el original NO se toca).</summary>
    public string? Remove(Guid id)
    {
        int i = _items.FindIndex(x => x.Id == id); if (i < 0) return null;
        var it = _items[i]; _items.RemoveAt(i); Changed?.Invoke(); return OwnedToDelete(it);
    }

    /// <summary>Vacía la estantería. Devuelve las copias propias a borrar (los originales referenciados no se tocan).</summary>
    public IReadOnlyList<string> Clear()
    {
        var owned = _items.Select(OwnedToDelete).Where(p => p is not null).Select(p => p!).ToList(); _items.Clear(); Changed?.Invoke(); return owned;
    }

    public ShelfStatus StatusOf(ShelfItem it, Func<string, bool>? fileExists = null, Func<string, bool>? dirExists = null)
    {
        fileExists ??= System.IO.File.Exists; dirExists ??= System.IO.Directory.Exists;
        return it.Kind switch
        {
            ShelfKind.File => it.Path is not null && fileExists(it.Path) ? ShelfStatus.Ok : ShelfStatus.Missing,
            ShelfKind.Folder => it.Path is not null && dirExists(it.Path) ? ShelfStatus.Ok : ShelfStatus.Missing,
            ShelfKind.Image => it.OwnedPath is not null && fileExists(it.OwnedPath) ? ShelfStatus.Ok : ShelfStatus.Missing,
            _ => ShelfStatus.Ok,
        };
    }

    /// <summary>¿Está <paramref name="path"/> dentro de la carpeta propia de la estantería? Es la única condición para poder borrarla.</summary>
    public bool IsOwned(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string root = System.IO.Path.GetFullPath(_ownDir).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, full = System.IO.Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    /// <summary>Borra copias propias (comprobando de nuevo que están dentro de la carpeta de la estantería). Jamás toca otra ruta. Devuelve cuántas borró.</summary>
    public int DeleteOwnedCopies(IEnumerable<string?> paths)
    {
        int n = 0;
        foreach (var p in paths) { if (p is null || !IsOwned(p)) continue; try { if (System.IO.File.Exists(p)) { System.IO.File.Delete(p); n++; } } catch (Exception) { /* en uso: se reintenta al arrancar */ } }
        return n;
    }

    /// <summary>Al arrancar: la estantería es temporal, así que las copias propias que quedaron de la sesión anterior se borran (solo dentro de su carpeta).</summary>
    public int CleanLeftovers()
    {
        try { if (!System.IO.Directory.Exists(_ownDir)) return 0; return DeleteOwnedCopies(System.IO.Directory.EnumerateFiles(_ownDir, "*", System.IO.SearchOption.AllDirectories)); } catch (Exception) { return 0; }
    }

    private string? OwnedToDelete(ShelfItem it) => it.OwnedPath is { } p && IsOwned(p) ? p : null;

    private List<string> Trim()
    {
        var removed = new List<string>();
        while (_items.Count > Max) { var old = _items[0]; _items.RemoveAt(0); if (OwnedToDelete(old) is { } p) removed.Add(p); }
        return removed;
    }
}
