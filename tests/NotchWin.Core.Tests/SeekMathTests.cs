using NotchWin.Core.Music;
using Xunit;

namespace NotchWin.Core.Tests;

public class SeekMathTests
{
    private static readonly TimeSpan Four = TimeSpan.FromMinutes(4);

    [Fact] public void A_click_in_the_middle_lands_in_the_middle_of_the_track() => Assert.Equal(TimeSpan.FromMinutes(2), SeekMath.PositionAt(100, 200, Four));

    [Fact] public void Clicks_past_the_ends_are_clamped_to_the_track()
    {
        Assert.Equal(TimeSpan.Zero, SeekMath.PositionAt(-30, 200, Four)); Assert.Equal(Four, SeekMath.PositionAt(500, 200, Four));
    }

    [Fact] public void Without_a_duration_or_a_width_there_is_nothing_to_seek()
    {
        Assert.Null(SeekMath.PositionAt(10, 200, null)); Assert.Null(SeekMath.PositionAt(10, 200, TimeSpan.Zero)); Assert.Null(SeekMath.PositionAt(10, 0, Four)); Assert.Null(SeekMath.PositionAt(double.NaN, 200, Four));
    }
}
