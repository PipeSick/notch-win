using Microsoft.UI;
using NotchWin.Core.Notch;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// It7: el tema de la Isla en código. Una sola fuente de verdad (<see cref="IslandPalette"/> en NotchWin.Core): los colores que el código escribe «para el cristal oscuro» pasan por
/// <see cref="C"/> y salen en el tema vigente; el tema se sigue en caliente (<see cref="Set"/>) y las vistas dinámicas se reconstruyen al cambiar. No hay selector manual: manda
/// la preferencia de aplicaciones de Windows (AppsUseLightTheme), la misma que sigue WinMac.
/// </summary>
internal static class Pal
{
    public static bool Light { get; private set; }

    /// <summary>Cambia el tema vigente. Devuelve true si cambió.</summary>
    public static bool Set(bool light)
    {
        if (Light == light) return false;
        Light = light; return true;
    }

    public static Color C(byte a, byte r, byte g, byte b)
    {
        var m = IslandPalette.Map(new Argb(a, r, g, b), Light);
        return Color.FromArgb(m.A, m.R, m.G, m.B);
    }

    public static Color Map(Color c) => C(c.A, c.R, c.G, c.B);

    /// <summary>Texto e iconos principales (blanco en oscuro, grafito en claro).</summary>
    public static Color Fg => Map(Colors.White);

    /// <summary>La preferencia de Windows para las aplicaciones. Sin dato → oscuro (el aspecto de siempre).</summary>
    public static bool SystemIsLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception) { return false; }
    }
}
