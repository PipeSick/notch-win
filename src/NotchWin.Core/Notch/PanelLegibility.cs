namespace NotchWin.Core.Notch;

/// <summary>
/// Legibilidad del panel «Personalizar» (WM-037, corrección de la iteración 6). Puro: se prueba sin Windows.
/// <para>El cristal de la Isla deja pasar lo que hay detrás. Con el control «Cristal» al mínimo y una ventana clara detrás (una página web blanca, con Windows en tema
/// OSCURO o claro) el fondo del panel quedaba gris claro con texto y controles blancos: ilegible. El tema de Windows no es la causa — lo es el color de lo que está detrás —,
/// así que ningún mínimo por tema basta. El panel lleva un fondo propio casi opaco y oscuro: el texto se lee con cualquier nivel de cristal y cualquier cosa detrás, y el
/// cristal sigue viéndose (y respondiendo al control) en el resto de la Isla, que no cambia.</para>
/// </summary>
public static class PanelLegibility
{
    /// <summary>Fondo del panel (A, R, G, B): grafito de la Isla al 92 %.</summary>
    public static readonly (byte A, byte R, byte G, byte B) Scrim = (0xEB, 0x1C, 0x1C, 0x1E);
    /// <summary>Rótulos de sección («CRISTAL», «SECCIONES»): antes #636366 (2,8:1 sobre grafito).</summary>
    public static readonly (byte R, byte G, byte B) Label = (0xA1, 0xA1, 0xA6);
    /// <summary>Texto de una sección desactivada: antes #636366.</summary>
    public static readonly (byte R, byte G, byte B) Disabled = (0x8E, 0x8E, 0x93);
    public static readonly (byte R, byte G, byte B) Text = (0xFF, 0xFF, 0xFF);

    /// <summary>Contraste WCAG entre dos colores opacos.</summary>
    public static double Contrast((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double la = Luminance(a), lb = Luminance(b); if (la < lb) (la, lb) = (lb, la);
        return (la + 0.05) / (lb + 0.05);
    }

    public static double Luminance((byte R, byte G, byte B) c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>Color del fondo del panel una vez compuesto sobre lo que haya detrás (<paramref name="backdrop"/>: gris 0–255 de la ventana de detrás, ya desenfocada).</summary>
    public static (byte R, byte G, byte B) Composite((byte A, byte R, byte G, byte B) scrim, byte backdrop)
    {
        double a = scrim.A / 255.0; byte Mix(byte c) => (byte)Math.Round(a * c + (1 - a) * backdrop);
        return (Mix(scrim.R), Mix(scrim.G), Mix(scrim.B));
    }
}
