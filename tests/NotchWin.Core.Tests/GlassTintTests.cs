using NotchWin.Core.Notch;
using Xunit;

namespace NotchWin.Core.Tests;

/// <summary>WM-037: la Isla es legible (oscura) también con el tema claro de Windows, sin cambiar su aspecto con el tema oscuro.</summary>
public class GlassTintTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    [InlineData(1.0)]
    public void In_light_theme_the_glass_never_goes_below_a_dark_floor(double level)
    {
        var (tint, lum) = GlassTint.For(level, matchBar: false, lightSurroundings: true);
        Assert.True(tint >= GlassTint.LightMinTint); Assert.True(lum >= GlassTint.LightMinLuminosity);
        var (bt, bl) = GlassTint.For(level, matchBar: true, lightSurroundings: true);
        Assert.True(bt >= GlassTint.LightMinBarTint); Assert.True(bl >= GlassTint.LightMinBarLuminosity);
    }

    [Fact]
    public void In_dark_theme_the_approved_look_is_unchanged()
    {
        var (tint, lum) = GlassTint.For(0.6, false, false);
        Assert.Equal(0.08 + 0.82 * 0.36, tint, 3); Assert.Equal(0.12 + 0.78 * 0.36, lum, 3);   // la curva de antes
        Assert.Equal((0.38f, 0.12f), GlassTint.For(0.6, true, false));   // plegada dentro de WinMac: el tinte de la barra, como antes
        var (full, _) = GlassTint.For(1.0, false, true); Assert.Equal(0.90, full, 3);   // el máximo elegido por el usuario se respeta
    }
}

/// <summary>WM-038: la llamada del iPhone en la Isla.</summary>
public class CallPillTests
{
    private DateTime _t = new(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
    private NotchWin.Core.Pill.PillArbiter A() => new(NotchWin.Core.Pill.PillPolicy.FromSettings(), () => _t);

    [Fact]
    public void A_ringing_call_outlasts_a_notification_and_ends_when_told()
    {
        var a = A();
        Assert.True(a.Post(NotchWin.Core.Pill.PillFeed.Call("ringing", "Mamá")!));
        Assert.Equal("Llamada de Mamá", a.Current!.Title);
        _t = _t.AddSeconds(30); a.Tick(); Assert.NotNull(a.Current);   // una notificación ya se habría ido (5 s)
        a.Post(new NotchWin.Core.Pill.PillItem(NotchWin.Core.Pill.PillKind.Notification, "Ana", "Hola"));
        Assert.Equal(NotchWin.Core.Pill.PillKind.Call, a.Current!.Kind);   // la llamada tiene prioridad
        a.Post(NotchWin.Core.Pill.PillFeed.Call("missed", "Mamá")!); Assert.Equal("Llamada perdida", a.Current!.Title);   // el estado nuevo sustituye al anterior
        _t = _t.AddSeconds(6); a.Tick(); Assert.Equal(NotchWin.Core.Pill.PillKind.Notification, a.Current?.Kind);   // después, lo que esperaba
    }

    [Fact]
    public void An_ended_call_without_missed_is_withdrawn_and_the_safety_time_applies()
    {
        var a = A(); a.Post(NotchWin.Core.Pill.PillFeed.Call("ringing", null)!);
        Assert.Equal("Llamada entrante", a.Current!.Title);   // sin nombre (privacidad o sin datos): texto genérico
        var none = NotchWin.Core.Pill.PillFeed.Call("none", null)!; Assert.True(none.Withdraw);
        a.Withdraw(none.Kind); Assert.Null(a.Current);
        var b = A(); b.Post(NotchWin.Core.Pill.PillFeed.Call("ringing", "X")!); _t = _t.AddSeconds(96); b.Tick(); Assert.Null(b.Current);   // nunca queda fijada
        Assert.Null(NotchWin.Core.Pill.PillFeed.Call("contestar", "X"));   // estados desconocidos: nada (no hay controles de llamada)
    }
}

/// <summary>WM-037 (iteración 6): el panel «Personalizar» se lee con cualquier nivel de cristal y cualquier cosa detrás, con Windows en tema claro y oscuro.</summary>
public class PanelLegibilityTests
{
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    [Theory]
    [InlineData(0)] [InlineData(64)] [InlineData(128)] [InlineData(192)] [InlineData(255)]   // lo que hay detrás: de negro a blanco puro (una página web)
    public void Text_labels_and_disabled_rows_are_readable_over_any_backdrop(byte backdrop)
    {
        var bg = PanelLegibility.Composite(PanelLegibility.Scrim, backdrop);
        Assert.True(PanelLegibility.Contrast(PanelLegibility.Text, bg) >= 7.0);        // texto principal (AAA)
        Assert.True(PanelLegibility.Contrast(PanelLegibility.Label, bg) >= 4.5);       // rótulos «CRISTAL» y «SECCIONES» (AA)
        Assert.True(PanelLegibility.Contrast(PanelLegibility.Disabled, bg) >= 3.0);    // sección desactivada (componente de interfaz, AA)
    }

    [Fact]
    public void The_old_label_colour_was_below_the_minimum_and_the_new_one_is_not()
    {
        var oldLabel = ((byte)0x63, (byte)0x63, (byte)0x66); var graphite = ((byte)0x1C, (byte)0x1C, (byte)0x1E);
        Assert.True(PanelLegibility.Contrast(oldLabel, graphite) < 3.0);                // 2,8:1: la causa de «ilegible»
        Assert.True(PanelLegibility.Contrast(PanelLegibility.Label, graphite) >= 4.5);
    }

    [Theory]
    [InlineData(0.0, false)] [InlineData(0.3, false)] [InlineData(1.0, false)] [InlineData(0.0, true)] [InlineData(0.6, true)]
    public void The_scrim_does_not_depend_on_the_windows_theme_or_the_glass_level(double level, bool lightTheme)
    {
        // El fondo del panel es un valor fijo: el nivel de cristal y el tema del sistema solo afectan al resto de la Isla (GlassTint), que no se toca.
        _ = GlassTint.For(level, false, lightTheme);
        var worst = PanelLegibility.Composite(PanelLegibility.Scrim, 255);              // peor caso: blanco puro detrás
        Assert.True(PanelLegibility.Contrast(PanelLegibility.Text, worst) >= 7.0);
    }

    [Fact]
    public void The_scrim_is_nearly_opaque_but_lets_a_little_glass_through()
    {
        Assert.InRange(PanelLegibility.Scrim.A, 0xD9, 0xF5);   // 85–96 %
    }

    [Fact]
    public void The_island_glass_curve_is_unchanged_by_this_fix()
    {
        var (tint, lum) = GlassTint.For(0.0, false, false);
        Assert.Equal(0.08, tint, 3); Assert.Equal(0.12, lum, 3);
    }

    [Fact]
    public void Composite_of_a_black_backdrop_is_the_scrim_colour_itself()
    {
        var c = PanelLegibility.Composite(PanelLegibility.Scrim, 0);
        Assert.Equal(((byte)(0.92157 * 0x1C + 0.5), (byte)(0.92157 * 0x1C + 0.5), (byte)(0.92157 * 0x1E + 0.5)), (c.R, c.G, c.B));
    }
}
