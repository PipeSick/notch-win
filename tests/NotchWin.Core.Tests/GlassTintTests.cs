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
