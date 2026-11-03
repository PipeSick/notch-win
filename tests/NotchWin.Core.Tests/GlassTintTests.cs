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
