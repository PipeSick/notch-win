namespace NotchWin.Core.Notch;

/// <summary>
/// Propuesta A: al ABRIR (crecer) la Isla se pasa un poco del tamaño final y vuelve (resorte suave), como una Dynamic Island; al PLEGAR no hay rebote
/// (pasarse por debajo recortaría el contenido y el borde de la píldora). Puro: se prueba sin Windows. t y el resultado van de 0 a 1 (en 1 vale exactamente 1).
/// </summary>
public static class NotchEasing
{
    public const double Overshoot = 1.25;   // ≈ 7 % de rebote; sube aquí si se quiere más vivo

    public static double Eased(double t, bool growing)
    {
        t = Math.Clamp(t, 0, 1);
        if (!growing) return 1 - Math.Pow(1 - t, 5);   // ease-out quint, el de siempre
        var u = t - 1;
        return 1 + (Overshoot + 1) * u * u * u + Overshoot * u * u;   // ease-out-back
    }

    /// <summary>El resorte necesita un poco más de tiempo para verse; plegar sigue igual de rápido.</summary>
    public static int DurationMs(bool growing) => growing ? 340 : 260;
}
