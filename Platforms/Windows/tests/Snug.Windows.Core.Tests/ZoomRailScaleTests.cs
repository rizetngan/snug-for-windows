using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class ZoomRailScaleTests
{
    [Theory]
    [InlineData(10, 8)]
    [InlineData(90, -4)]
    public void MapsTrackEndsToZoomLimits(double position, int expected)
    {
        Assert.Equal(expected, ZoomRailScale.ValueAt(position, 10, 80, -4, 8));
    }

    [Fact]
    public void MapsMiddleToNearestDiscreteLevel()
    {
        Assert.Equal(2, ZoomRailScale.ValueAt(50, 10, 80, -4, 8));
    }

    [Theory]
    [InlineData(-100, 8)]
    [InlineData(500, -4)]
    public void ClampsTouchesOutsideTrack(double position, int expected)
    {
        Assert.Equal(expected, ZoomRailScale.ValueAt(position, 10, 80, -4, 8));
    }

    [Fact]
    public void SupportsSingleLevelRange()
    {
        Assert.Equal(3, ZoomRailScale.ValueAt(50, 10, 80, 3, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidTrackHeight(double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ZoomRailScale.ValueAt(10, 10, height, 0, 10));
    }

    [Fact]
    public void RejectsInvertedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ZoomRailScale.ValueAt(10, 10, 80, 8, -4));
    }
}
