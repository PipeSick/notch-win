using NotchWin.Core.Notch;
using NotchWin.Core.Settings;
using Xunit;

namespace NotchWin.Core.Tests;

// Duplicate → Extend with two 2560×1440 screens: the bug left the notch centred on the 5120-wide union, i.e. on the seam between the screens.
public sealed class NotchPlacementTests
{
    private static readonly PixelRect Left = new(0, 0, 2560, 1440), Right = new(2560, 0, 2560, 1440), Union = new(0, 0, 5120, 1440);

    [Fact]
    public void The_live_monitor_rectangle_wins_over_a_stale_display_area()
    {
        Assert.Equal(Left, NotchPlacement.ChooseBounds(Union, Left));
        Assert.Equal(Union, NotchPlacement.ChooseBounds(Union, null));               // no live data: fall back to what DisplayArea said
        Assert.Equal(Union, NotchPlacement.ChooseBounds(Union, new PixelRect(0, 0, 0, 0)));
    }

    [Fact]
    public void A_centred_notch_on_the_union_crosses_the_seam_but_on_the_live_monitor_it_does_not()
    {
        var onUnion = NotchPlacement.Dock(Union, 400, 40, NotchPosition.TopCenter, 1);
        Assert.Equal(2360, onUnion.X); Assert.True(onUnion.X < 2560 && onUnion.Right > 2560);   // what the user saw
        var onLeft = NotchPlacement.Dock(NotchPlacement.ChooseBounds(Union, Left), 400, 40, NotchPosition.TopCenter, 1);
        Assert.Equal(1080, onLeft.X); Assert.True(Left.Contains(onLeft));
        var corner = NotchPlacement.Dock(Right, 400, 40, NotchPosition.TopRight, 1.5);
        Assert.Equal(5120 - 400 - 24, corner.X); Assert.Equal(0, corner.Y);
        Assert.Equal(2560 + 24, NotchPlacement.Dock(Right, 400, 40, NotchPosition.TopLeft, 1.5).X);
    }

    [Fact]
    public void Settled_only_when_every_notch_sits_inside_a_real_monitor()
    {
        PixelRect[] real = [Left, Right];
        Assert.True(NotchPlacement.IsSettled([(Left, new PixelRect(1080, 0, 400, 40)), (Right, new PixelRect(3640, 0, 400, 40))], real));
        Assert.False(NotchPlacement.IsSettled([(Union, new PixelRect(2360, 0, 400, 40))], real));   // half-way snapshot
        Assert.False(NotchPlacement.IsSettled([(Left, new PixelRect(2360, 0, 400, 40))], real));    // right monitor, window still on the seam
        Assert.True(NotchPlacement.IsSettled([(Left, default)], real));                              // a window not shown yet does not block
        Assert.False(NotchPlacement.IsSettled([(Left, new PixelRect(1080, 0, 400, 40))], []));      // no monitors read: not settled
    }

    [Fact]
    public void Rechecks_continue_until_settled_with_a_long_tail_and_then_stop()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(900), NotchPlacement.NextResyncDelay(0, settled: true));   // even if it looks right, one confirming pass
        Assert.Null(NotchPlacement.NextResyncDelay(1, settled: true));
        Assert.Equal(TimeSpan.FromMilliseconds(1500), NotchPlacement.NextResyncDelay(1, settled: false));
        Assert.Equal(TimeSpan.FromMilliseconds(4000), NotchPlacement.NextResyncDelay(4, settled: false));
        Assert.Null(NotchPlacement.NextResyncDelay(5, settled: false));                                   // 12 s after the change: give up until the next change
        Assert.Equal(12000, NotchPlacement.ResyncDelaysMs[^1]);
    }
}

// The notch should read as part of WinMac's top bar: same height, tight bottom corners, shoulders at the top, and no wallpaper gaps at its corners.
public sealed class NotchShapeTests
{
    [Fact]
    public void Inside_winmac_the_collapsed_notch_is_as_tall_as_the_bar_with_tight_corners()
    {
        Assert.Equal(28, NotchPlacement.CollapsedHeightDip(hostedByWinMac: true)); Assert.Equal(32, NotchPlacement.CollapsedHeightDip(false));
        Assert.Equal(10.08, NotchPlacement.CornerRadiusDip(28), 2);     // before: 20 DIP clipped to a 16-DIP half-circle
        Assert.Equal(20, NotchPlacement.CornerRadiusDip(400)); Assert.Equal(8, NotchPlacement.CornerRadiusDip(10));
    }

    [Fact]
    public void The_silhouette_covers_the_body_and_follows_the_rounded_corners_instead_of_the_whole_window()
    {
        var win = new PixelRect(1000, 0, 180, 28); var parts = NotchPlacement.Silhouette(win, radiusPx: 10, shoulderPx: 6, slices: 3);
        Assert.Equal(new PixelRect(1000, 0, 180, 6), parts[0]);           // shoulders
        Assert.Equal(new PixelRect(1006, 0, 168, 18), parts[1]);          // straight body
        Assert.Equal(5, parts.Count);
        Assert.All(parts, p => Assert.True(win.Contains(p)));
        Assert.Equal(1006, parts[2].X);                                    // top of the corner: full width
        Assert.True(parts[^1].X > parts[2].X && parts[^1].Width < parts[2].Width);   // narrower towards the bottom, like the rounded corner
        Assert.Equal(28, parts.Max(p => p.Bottom));
        Assert.Empty(NotchPlacement.Silhouette(default, 10, 6));
        Assert.Single(NotchPlacement.Silhouette(new PixelRect(0, 0, 100, 20), 0, 0));   // no radius, no shoulders: just the window
    }
}
