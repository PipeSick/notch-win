using System.Text.Json;

namespace NotchWin.Core.Notes;

/// <summary>
/// Las notas de Hatsu Notch: un archivo JSON local (<c>notes.json</c>), escritura atómica (temporal + reemplazo) y lectura tolerante (un archivo ilegible no tira la app: arranca vacío y
/// el archivo dañado se conserva como <c>notes.json.bad</c>). Seguro entre hilos. No hay red ni telemetría: el texto nunca sale del equipo.
/// </summary>
public sealed class NotesStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private List<Note> _notes = [];

    public NotesStore(string path)
    {
        _path = path;
        Load();
    }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchWin", "notes.json");

    /// <summary>Algo cambió (alta, edición, borrado, fijar). Se dispara fuera del bloqueo.</summary>
    public event Action? Changed;

    public IReadOnlyList<Note> All() { lock (_gate) return NoteView.Ordered(_notes); }

    public IReadOnlyList<Note> Pinned() { lock (_gate) return NoteView.Ordered(_notes.Where(n => n.Pinned)); }

    public Note? Get(string id) { lock (_gate) return _notes.FirstOrDefault(n => n.Id == id); }

    /// <summary>Crea una nota. Con el tope alcanzado devuelve null (no se pierde ninguna existente).</summary>
    public Note? Add(string text = "", string color = "Yellow", bool pinned = false)
    {
        Note n;
        lock (_gate)
        {
            if (_notes.Count >= Note.MaxNotes) return null;
            n = new Note { Text = text, Color = color, Pinned = pinned }.Sanitize();
            _notes.Add(n); Save();
        }

        Changed?.Invoke();
        return n;
    }

    /// <summary>Aplica un cambio a una nota existente (siempre sanea y actualiza la fecha). Devuelve la nota nueva o null si no existe.</summary>
    public Note? Update(string id, Func<Note, Note> change)
    {
        Note? result = null;
        lock (_gate)
        {
            int i = _notes.FindIndex(n => n.Id == id);
            if (i < 0) return null;
            var before = _notes[i];
            result = change(before) with { Id = before.Id, CreatedUtc = before.CreatedUtc, UpdatedUtc = DateTime.UtcNow };
            result = result.Sanitize(); _notes[i] = result; Save();
        }

        Changed?.Invoke();
        return result;
    }

    public Note? SetText(string id, string text) => Update(id, n => n with { Text = text });

    public Note? SetPinned(string id, bool pinned) => Update(id, n => n with { Pinned = pinned });

    public Note? SetColor(string id, string color) => Update(id, n => n with { Color = color });

    /// <summary>La posición de la ventana no cuenta como edición del texto: no cambia la fecha ni el orden de la lista.</summary>
    public void SetBounds(string id, int x, int y, int w, int h)
    {
        lock (_gate)
        {
            int i = _notes.FindIndex(n => n.Id == id);
            if (i < 0) return;
            _notes[i] = (_notes[i] with { X = x, Y = y, W = w, H = h }).Sanitize(); Save();
        }
    }

    public bool Delete(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _notes.RemoveAll(n => n.Id == id) > 0;
            if (removed) Save();
        }

        if (removed) Changed?.Invoke();
        return removed;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var list = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(_path)) ?? [];
            var seen = new HashSet<string>();
            _notes = list.Where(n => n is not null).Select(n => n.Sanitize()).Where(n => seen.Add(n.Id)).Take(Note.MaxNotes).ToList();
        }
        catch (Exception)
        {
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (Exception) { }
            _notes = [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(_notes, Json)); File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception) { /* sin disco la nota sigue en memoria esta sesión; no se tira la app */ }
    }
}
