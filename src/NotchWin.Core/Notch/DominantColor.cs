namespace NotchWin.Core.Notch;

/// <summary>
/// Propuesta A: color dominante de la carátula para el borde, el resplandor y las barras de la Isla. Puro (sin Windows): se prueba con píxeles sintéticos.
/// Prefiere colores saturados y con luz; ignora casi-negros, casi-blancos y grises (una carátula en blanco y negro no tiñe nada: devuelve null).
/// </summary>
public static class DominantColor
{
    /// <param name="bgra">Píxeles BGRA de 8 bits (4 bytes por píxel), ya reducidos (p. ej. 24×24).</param>
    public static (byte R, byte G, byte B)? FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;
        // 12 matices × peso por saturación y luminosidad; gana el matiz con más peso, y su color es el promedio ponderado de sus píxeles
        const int Bins = 12;
        var weight = new double[Bins]; var sr = new double[Bins]; var sg = new double[Bins]; var sb = new double[Bins];
        for (int i = 0; i < width * height; i++)
        {
            double b = bgra[i * 4] / 255.0, g = bgra[i * 4 + 1] / 255.0, r = bgra[i * 4 + 2] / 255.0; double a = bgra[i * 4 + 3] / 255.0;
            if (a < 0.5) continue;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (max < 0.18 || d < 0.12) continue;                   // casi negro o gris/blanco: sin matiz
            double sat = d / max, lum = (max + min) / 2;
            double w = sat * sat * (1 - Math.Abs(lum - 0.55));      // saturado y ni oscuro ni lavado
            double h = max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            int bin = Math.Min(Bins - 1, (int)(h / 6 * Bins));
            weight[bin] += w; sr[bin] += r * w; sg[bin] += g * w; sb[bin] += b * w;
        }
        int best = 0; for (int k = 1; k < Bins; k++) if (weight[k] > weight[best]) best = k;
        if (weight[best] <= 0) return null;
        double R = sr[best] / weight[best], G = sg[best] / weight[best], Bl = sb[best] / weight[best];
        // luz mínima para que el borde se vea sobre negro
        double m = Math.Max(R, Math.Max(G, Bl)); if (m < 0.7) { double f = 0.7 / m; R = Math.Min(1, R * f); G = Math.Min(1, G * f); Bl = Math.Min(1, Bl * f); }
        return ((byte)Math.Round(R * 255), (byte)Math.Round(G * 255), (byte)Math.Round(Bl * 255));
    }
}
