namespace NotchWin.Core.Notch;

/// <summary>
/// It7: ya no hay una sola Isla oscura: con el tema claro de Windows el cristal es CLARO (tinte <see cref="IslandPalette.GlassBase"/>) con texto grafito; el contrato de legibilidad es el mismo
/// — los mínimos de opacidad siguen valiendo para que el texto se lea sobre cualquier cosa detrás (ver <see cref="IslandPalette.Surface"/> y sus pruebas).
/// </summary>
/// <remarks>Historia (WM-037, superada por It7 en lo de «siempre oscura»): tinte del cristal de la Isla (WM-037). Puro: lo usa <c>GlassBackdrop</c> y se prueba sin Windows.
/// <para>La Isla es SIEMPRE oscura y con texto blanco. Con el tema oscuro de Windows lo que hay detrás suele ser oscuro y un cristal ligero basta. Con el tema claro
/// lo que hay detrás es blanco: el mismo cristal quedaba casi transparente y el texto, ilegible (no era una transición: no se recuperaba nunca). Con el tema claro
/// el tinte tiene un mínimo de opacidad, así que la Isla conserva su aspecto oscuro aprobado; con el tema oscuro nada cambia.</para>
/// </remarks>
public static class GlassTint
{
    /// <summary>Mínimos con el tema claro: suficientes para texto grafito sobre cualquier cosa detrás, hasta un fondo negro (contraste WCAG ≥ 4,5 comprobado en IslandThemeTests; ajustables aquí).</summary>
    public const float LightMinTint = 0.86f, LightMinLuminosity = 0.88f, LightMinBarTint = 0.88f, LightMinBarLuminosity = 0.88f;

    /// <param name="level">Nivel del cristal elegido en Ajustes (0 = claro, 1 = casi sólido).</param>
    /// <param name="matchBar">Plegada dentro de WinMac: el tinte de la barra superior.</param>
    /// <param name="lightSurroundings">Windows (sus apps) está en tema claro.</param>
    public static (float Tint, float Luminosity) For(double level, bool matchBar, bool lightSurroundings)
    {
        float tint, lum;
        if (matchBar) { tint = 0.38f; lum = 0.12f; }
        else { var t = Math.Clamp(level, 0, 1); t *= t; tint = (float)(0.08 + 0.82 * t); lum = (float)(0.12 + 0.78 * t); }
        if (!lightSurroundings) return (tint, lum);
        return matchBar ? (Math.Max(tint, LightMinBarTint), Math.Max(lum, LightMinBarLuminosity)) : (Math.Max(tint, LightMinTint), Math.Max(lum, LightMinLuminosity));
    }
}
