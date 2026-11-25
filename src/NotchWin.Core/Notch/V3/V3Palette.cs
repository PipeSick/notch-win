namespace NotchWin.Core.Notch.V3;

/// <summary>Tokens de color del sistema visual V3 (glass oscuro con acento violeta/lavanda), con su variante clara. Puro: las pruebas comprueban el contraste de cada pareja texto/fondo.</summary>
public sealed record V3Colors(
    Argb Accent,          // relleno de la pestaña activa y del botón principal (texto blanco encima)
    Argb OnAccent,
    Argb AccentText,      // texto e iconos con acento sobre el cristal (lavanda en oscuro, violeta profundo en claro)
    Argb GradientFrom,    // degradado discreto: barras de progreso y bordes; NUNCA lleva texto encima
    Argb GradientTo,
    Argb Surface,         // tarjeta translúcida
    Argb SurfaceHover,
    Argb SurfaceBorder,
    Argb Track,           // fondo de barras y anillos
    Argb Success, Argb Warning, Argb Danger);

public static class V3Palette
{
    /// <summary>Colores de acento elegibles en Ajustes (nombre estable para el cable, no se traduce). «Violet» es el de siempre.</summary>
    public static readonly string[] AccentNames = ["Violet", "Blue", "Green", "Pink", "Orange", "Graphite"];

    /// <summary>Acento vigente (lo fija la app al recibir los ajustes centrales). Un nombre desconocido vale «Violet».</summary>
    public static string AccentName { get; set; } = "Violet";

    public static string NormalizeAccent(string? name) => AccentNames.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) ?? "Violet";

    // (relleno, texto con acento sobre el cristal, degradado hasta) por tema; el relleno lleva texto blanco encima (≥ 4,5:1, lo comprueban las pruebas).
    private static (Argb Fill, Argb Text, Argb To) AccentFor(string name, bool light) => (NormalizeAccent(name), light) switch
    {
        ("Blue", true) => (Argb.Opaque(0x1F, 0x5F, 0xD1), Argb.Opaque(0x1A, 0x4F, 0xAD), Argb.Opaque(0x5B, 0x9B, 0xF5)),
        ("Blue", false) => (Argb.Opaque(0x2F, 0x6B, 0xF0), Argb.Opaque(0x9D, 0xC1, 0xFF), Argb.Opaque(0x7F, 0xB0, 0xFF)),
        ("Green", true) => (Argb.Opaque(0x17, 0x80, 0x43), Argb.Opaque(0x13, 0x6B, 0x38), Argb.Opaque(0x4C, 0xB8, 0x7A)),
        ("Green", false) => (Argb.Opaque(0x17, 0x80, 0x43), Argb.Opaque(0x86, 0xE3, 0xA9), Argb.Opaque(0x5F, 0xD6, 0x8E)),
        ("Pink", true) => (Argb.Opaque(0xC2, 0x18, 0x5B), Argb.Opaque(0xA3, 0x12, 0x4C), Argb.Opaque(0xF0, 0x6A, 0x9B)),
        ("Pink", false) => (Argb.Opaque(0xD6, 0x33, 0x6C), Argb.Opaque(0xFF, 0xA8, 0xC8), Argb.Opaque(0xFF, 0x85, 0xB0)),
        ("Orange", true) => (Argb.Opaque(0xB4, 0x53, 0x09), Argb.Opaque(0x92, 0x40, 0x0E), Argb.Opaque(0xE8, 0x93, 0x3F)),
        ("Orange", false) => (Argb.Opaque(0xC2, 0x57, 0x0C), Argb.Opaque(0xFF, 0xC2, 0x85), Argb.Opaque(0xFF, 0xA0, 0x4D)),
        ("Graphite", true) => (Argb.Opaque(0x47, 0x55, 0x69), Argb.Opaque(0x33, 0x41, 0x55), Argb.Opaque(0x8A, 0x99, 0xAD)),
        ("Graphite", false) => (Argb.Opaque(0x64, 0x74, 0x8B), Argb.Opaque(0xCB, 0xD5, 0xE1), Argb.Opaque(0xA8, 0xB4, 0xC6)),
        _ => default,
    };

    public static V3Colors For(bool light) => For(light, AccentName);

    public static V3Colors For(bool light, string accent)
    {
        var b = Base(light);
        if (NormalizeAccent(accent) == "Violet") return b;
        var (fill, text, to) = AccentFor(accent, light);
        return b with { Accent = fill, AccentText = text, GradientFrom = fill, GradientTo = to };
    }

    private static V3Colors Base(bool light) => light
        ? new V3Colors(Argb.Opaque(0x6D, 0x3F, 0xD9), Argb.Opaque(255, 255, 255), Argb.Opaque(0x5B, 0x34, 0xB8), Argb.Opaque(0x6D, 0x3F, 0xD9), Argb.Opaque(0xA0, 0x6B, 0xF0),
            new Argb(0x0F, 0, 0, 0), new Argb(0x1A, 0, 0, 0), new Argb(0x24, 0, 0, 0), new Argb(0x26, 0, 0, 0), Argb.Opaque(0x1E, 0x8E, 0x3E), Argb.Opaque(0x9A, 0x5B, 0x00), Argb.Opaque(0xC6, 0x28, 0x28))
        : new V3Colors(Argb.Opaque(0x7C, 0x4D, 0xFF), Argb.Opaque(255, 255, 255), Argb.Opaque(0xC4, 0xB5, 0xFD), Argb.Opaque(0x7C, 0x4D, 0xFF), Argb.Opaque(0xC0, 0x84, 0xFC),
            new Argb(0x14, 255, 255, 255), new Argb(0x22, 255, 255, 255), new Argb(0x1F, 255, 255, 255), new Argb(0x33, 255, 255, 255), Argb.Opaque(0x34, 0xC7, 0x59), Argb.Opaque(0xFF, 0x9F, 0x0A), Argb.Opaque(0xFF, 0x45, 0x3A));

    /// <summary>Radios del sistema (DIP): un solo lenguaje de esquinas.</summary>
    public const double RadiusCard = 16, RadiusControl = 12, RadiusPill = 999, RadiusThumb = 12;

    /// <summary>Espaciado base (DIP) para que todas las vistas respiren igual.</summary>
    public const double Gap = 8, Pad = 12;
}
