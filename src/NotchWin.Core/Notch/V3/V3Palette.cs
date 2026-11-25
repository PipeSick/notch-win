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
    public static V3Colors For(bool light) => light
        ? new V3Colors(Argb.Opaque(0x6D, 0x3F, 0xD9), Argb.Opaque(255, 255, 255), Argb.Opaque(0x5B, 0x34, 0xB8), Argb.Opaque(0x6D, 0x3F, 0xD9), Argb.Opaque(0xA0, 0x6B, 0xF0),
            new Argb(0x0F, 0, 0, 0), new Argb(0x1A, 0, 0, 0), new Argb(0x24, 0, 0, 0), new Argb(0x26, 0, 0, 0), Argb.Opaque(0x1E, 0x8E, 0x3E), Argb.Opaque(0x9A, 0x5B, 0x00), Argb.Opaque(0xC6, 0x28, 0x28))
        : new V3Colors(Argb.Opaque(0x7C, 0x4D, 0xFF), Argb.Opaque(255, 255, 255), Argb.Opaque(0xC4, 0xB5, 0xFD), Argb.Opaque(0x7C, 0x4D, 0xFF), Argb.Opaque(0xC0, 0x84, 0xFC),
            new Argb(0x14, 255, 255, 255), new Argb(0x22, 255, 255, 255), new Argb(0x1F, 255, 255, 255), new Argb(0x33, 255, 255, 255), Argb.Opaque(0x34, 0xC7, 0x59), Argb.Opaque(0xFF, 0x9F, 0x0A), Argb.Opaque(0xFF, 0x45, 0x3A));

    /// <summary>Radios del sistema (DIP): un solo lenguaje de esquinas.</summary>
    public const double RadiusCard = 16, RadiusControl = 12, RadiusPill = 999, RadiusThumb = 12;

    /// <summary>Espaciado base (DIP) para que todas las vistas respiren igual.</summary>
    public const double Gap = 8, Pad = 12;
}
