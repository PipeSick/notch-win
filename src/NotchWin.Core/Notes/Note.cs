namespace NotchWin.Core.Notes;

/// <summary>Una nota: texto libre, color, si está fijada (ventana flotante delante de todo) y dónde está esa ventana. Todo es local; nada sale del equipo.</summary>
public sealed record Note
{
    public const int MaxTextLength = 20000, MaxNotes = 200;
    public static readonly string[] Colors = ["Yellow", "Pink", "Blue", "Green", "Violet"];

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Text { get; init; } = "";
    public string Color { get; init; } = "Yellow";
    public bool Pinned { get; init; }
    /// <summary>Posición y tamaño de la ventana fijada, en píxeles de pantalla. Null = aún no se colocó (se coloca sola).</summary>
    public int? X { get; init; }
    public int? Y { get; init; }
    public int W { get; init; } = 300;
    public int H { get; init; } = 260;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Corrige lo que venga de un archivo editado a mano o de otra versión.</summary>
    public Note Sanitize() => this with
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
        Text = (Text ?? "").Length > MaxTextLength ? Text![..MaxTextLength] : Text ?? "",
        Color = Colors.FirstOrDefault(c => string.Equals(c, Color, StringComparison.OrdinalIgnoreCase)) ?? "Yellow",
        W = Math.Clamp(W, 200, 900),
        H = Math.Clamp(H, 140, 900),
        X = X is { } x ? Math.Clamp(x, -20000, 20000) : null,
        Y = Y is { } y ? Math.Clamp(y, -20000, 20000) : null,
    };
}

/// <summary>Cómo se muestra una nota en listas y tarjetas (puro).</summary>
public static class NoteView
{
    /// <summary>Primera línea con texto, recortada. Una nota vacía se llama «Nota vacía».</summary>
    public static string Title(string? text, int max = 40)
    {
        var line = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null) return "Nota vacía";
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Lo que sigue a la primera línea con texto, en una sola línea; vacío si no hay más.</summary>
    public static string Preview(string? text, int max = 90)
    {
        var lines = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Skip(1);
        var joined = string.Join(" · ", lines);
        return joined.Length <= max ? joined : joined[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Primero las fijadas, luego por la última edición (la más reciente arriba).</summary>
    public static IReadOnlyList<Note> Ordered(IEnumerable<Note> notes) => notes.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.UpdatedUtc).ToList();
}
