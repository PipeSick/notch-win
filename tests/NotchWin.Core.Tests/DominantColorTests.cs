using NotchWin.Core.Notch;
using NotchWin.Core.Settings;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class DominantColorTests
{
    private static byte[] Fill(int w, int h, params (int count, byte r, byte g, byte b)[] parts)
    {
        var px = new byte[w * h * 4]; int i = 0;
        foreach (var (count, r, g, b) in parts) for (int k = 0; k < count && i < w * h; k++, i++) { px[i * 4] = b; px[i * 4 + 1] = g; px[i * 4 + 2] = r; px[i * 4 + 3] = 255; }
        return px;
    }

    [Fact]
    public void A_red_cover_gives_a_red_tint()
    {
        var c = DominantColor.FromBgra(Fill(10, 10, (100, 220, 40, 50)), 10, 10);
        Assert.NotNull(c); Assert.True(c!.Value.R > 200 && c.Value.G < 120 && c.Value.B < 120);
    }

    [Fact]
    public void The_most_saturated_big_area_wins_over_a_gray_and_black_background()
    {
        var c = DominantColor.FromBgra(Fill(10, 10, (50, 20, 20, 20), (30, 128, 128, 128), (20, 30, 120, 230)), 10, 10);
        Assert.NotNull(c); Assert.True(c!.Value.B > c.Value.R);   // azul
    }

    [Fact]
    public void Black_white_and_gray_covers_give_no_tint() =>
        Assert.Null(DominantColor.FromBgra(Fill(10, 10, (40, 0, 0, 0), (30, 255, 255, 255), (30, 120, 120, 120)), 10, 10));

    [Fact]
    public void A_dark_color_is_lifted_so_the_border_is_visible()
    {
        var c = DominantColor.FromBgra(Fill(10, 10, (100, 60, 10, 10)), 10, 10);
        Assert.NotNull(c); Assert.True(Math.Max(c!.Value.R, Math.Max(c.Value.G, c.Value.B)) >= 170);
    }

    [Fact]
    public void Bad_input_is_ignored()
    {
        Assert.Null(DominantColor.FromBgra(new byte[4], 10, 10));
        Assert.Null(DominantColor.FromBgra([], 0, 0));
    }
}

public sealed class HomeSectionTests
{
    [Fact]
    public void Home_is_first_by_default_and_for_an_old_saved_layout()
    {
        Assert.Equal(NotchSections.Home, NotchSections.Defaults()[0].Id);
        var old = NotchSections.Normalize([new() { Id = NotchSections.Music, Enabled = true }, new() { Id = NotchSections.Timer, Enabled = false }]);
        Assert.Equal(NotchSections.Home, old[0].Id); Assert.True(old[0].Enabled);
        Assert.False(old.First(s => s.Id == NotchSections.Timer).Enabled);   // lo que el usuario apagó sigue apagado
    }

    [Fact]
    public void A_saved_layout_that_already_has_Home_keeps_its_position()
    {
        var saved = NotchSections.Normalize([new() { Id = NotchSections.Music, Enabled = true }, new() { Id = NotchSections.Home, Enabled = false }]);
        Assert.Equal(NotchSections.Music, saved[0].Id); Assert.Equal(NotchSections.Home, saved[1].Id); Assert.False(saved[1].Enabled);
    }
}
