namespace NotchWin.Core.Notch;

/// <summary>Color ARGB (0–255). Puro: no depende de WinUI.</summary>
public readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    public static Argb Opaque(byte r, byte g, byte b) => new(255, r, g, b);
    public (byte R, byte G, byte B) Rgb => (R, G, B);
}

/// <summary>
/// It7: paleta de la Isla en tema claro y oscuro (un solo sistema de tema para todo NotchWin). Los colores de la Isla se escribieron para el cristal OSCURO; cada uno tiene aquí su
/// equivalente claro con el mismo papel (texto, texto secundario, tarjeta, pista, acento…). <see cref="Map"/> convierte un color «oscuro» al tema pedido; en el oscuro devuelve el mismo.
/// <list type="bullet">
/// <item>Superposiciones blancas translúcidas (tarjetas, hovers) → negras translúcidas, algo más tenues.</item>
/// <item>Blanco de texto/iconos → grafito. Grises de texto → el gris que conserva su jerarquía con contraste suficiente sobre el cristal claro (tabla comprobada en las pruebas).</item>
/// <item>Amarillo y azul de acento → versiones más oscuras que se leen sobre claro. Colores de marca y de estado (rojo, verde…) no cambian.</item>
/// </list>
/// </summary>
public static class IslandPalette
{
    /// <summary>Cristal de la Isla, tinte base del tema (el que se compone con el fondo difuminado).</summary>
    public static Argb GlassBase(bool light, bool matchBar) => light ? (matchBar ? Argb.Opaque(0xF5, 0xF5, 0xF7) : Argb.Opaque(0xF2, 0xF2, 0xF7)) : (matchBar ? Argb.Opaque(31, 33, 41) : Argb.Opaque(24, 24, 28));
    public static Argb GlassFallback(bool light, bool matchBar) => light ? Argb.Opaque(0xEC, 0xEC, 0xF0) : (matchBar ? Argb.Opaque(38, 40, 48) : Argb.Opaque(30, 30, 34));

    /// <summary>Texto principal e iconos.</summary>
    public static Argb Fg(bool light) => light ? Argb.Opaque(0x1C, 0x1C, 0x1E) : Argb.Opaque(255, 255, 255);
    /// <summary>Fondo del botón principal (reproducir) y el color de su icono.</summary>
    public static Argb Solid(bool light) => light ? Argb.Opaque(0x1C, 0x1C, 0x1E) : Argb.Opaque(255, 255, 255);
    public static Argb OnSolid(bool light) => light ? Argb.Opaque(255, 255, 255) : Argb.Opaque(0, 0, 0);

    // Equivalentes claros de los grises y acentos que la Isla ya usa (oscuro → claro). Cada pareja conserva el papel y la jerarquía.
    private static readonly Dictionary<uint, Argb> LightOf = new()
    {
        [0xFFE5E5EA] = Argb.Opaque(0x2C, 0x2C, 0x2E), [0xFFD1D1D6] = Argb.Opaque(0x32, 0x32, 0x34), [0xFFB4B4BA] = Argb.Opaque(0x38, 0x38, 0x3A),
        [0xFFA1A1A6] = Argb.Opaque(0x42, 0x42, 0x44), [0xFF98989D] = Argb.Opaque(0x48, 0x48, 0x4A), [0xFF8E8E93] = Argb.Opaque(0x4E, 0x4E, 0x52),
        [0xFF636366] = Argb.Opaque(0x62, 0x62, 0x66), [0xFF5A5A5E] = Argb.Opaque(0x70, 0x70, 0x74), [0xFF48484A] = Argb.Opaque(0xB0, 0xB0, 0xB6),
        [0xFF3A3A3C] = Argb.Opaque(0xD1, 0xD1, 0xD6), [0xFF2C2C2E] = Argb.Opaque(0xE5, 0xE5, 0xEA), [0xFF1C1C1E] = Argb.Opaque(0xEC, 0xEC, 0xF1),
        [0xFFFFD60A] = Argb.Opaque(0x75, 0x50, 0x00), [0xFFFFB340] = Argb.Opaque(0x85, 0x48, 0x00), [0xFF0A84FF] = Argb.Opaque(0x09, 0x50, 0xB8),
        [0xFF3395FF] = Argb.Opaque(0x1A, 0x62, 0xC4), [0xFF086DD6] = Argb.Opaque(0x07, 0x40, 0x9C),
        [0x33FF9F0A] = new Argb(0x33, 0xB2, 0x6A, 0x00),
    };

    /// <summary>El color (pensado para el cristal oscuro) en el tema pedido. Oscuro: tal cual.</summary>
    public static Argb Map(Argb c, bool light)
    {
        if (!light) return c;
        uint key = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
        if (LightOf.TryGetValue(key, out var m)) return m;
        if (c.A == 0) return c;
        bool neutral = Math.Abs(c.R - c.G) <= 6 && Math.Abs(c.G - c.B) <= 6;
        if (c is { R: 255, G: 255, B: 255 }) return c.A == 255 ? Fg(true) : new Argb((byte)Math.Round(c.A * 0.85), 0, 0, 0);   // blanco opaco → grafito; velo blanco → velo negro
        if (c is { R: 0, G: 0, B: 0, A: 255 }) return c;                                                                       // negro sobre insignias de color: se mantiene
        if (neutral) { byte v = (byte)(255 - c.R); return new Argb(c.A, v, v, v); }                                            // gris desconocido: espejo de luminosidad
        return c;                                                                                                              // marca y estado
    }

    /// <summary>Contraste WCAG entre dos colores opacos.</summary>
    public static double Contrast(Argb a, Argb b)
    {
        double la = Lum(a), lb = Lum(b); if (la < lb) (la, lb) = (lb, la);
        return (la + 0.05) / (lb + 0.05);
    }

    private static double Lum(Argb c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>Color compuesto de <paramref name="top"/> (con su alfa) sobre un fondo opaco.</summary>
    public static Argb Over(Argb top, Argb bg)
    {
        double a = top.A / 255.0; byte Mix(byte t, byte b) => (byte)Math.Round(a * t + (1 - a) * b);
        return Argb.Opaque(Mix(top.R, bg.R), Mix(top.G, bg.G), Mix(top.B, bg.B));
    }

    /// <summary>Superficie real sobre la que se lee el texto: el tinte del cristal compuesto con lo que haya detrás (0 = negro, 255 = blanco), a la opacidad mínima del tema.</summary>
    public static Argb Surface(bool light, byte behindGray, double tintOpacity) => Over(new Argb((byte)Math.Round(tintOpacity * 255), GlassBase(light, false).R, GlassBase(light, false).G, GlassBase(light, false).B), Argb.Opaque(behindGray, behindGray, behindGray));
}
