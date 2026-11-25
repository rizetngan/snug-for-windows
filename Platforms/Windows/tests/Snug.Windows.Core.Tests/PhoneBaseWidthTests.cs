using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class PhoneBaseWidthTests
{
    private static readonly PhoneScreen Iphone13Pro = new(1170, 2532, 390);
    private static readonly PhoneChoice Choice = new("Apple iPhone 13 Pro", Iphone13Pro);

    [Fact] public void DefaultPreservesPreviousGeometry()
    {
        foreach (var inset in new[] { 91, 45, 0 })
        {
            var oldFit = new ScreenFit(Iphone13Pro, inset);
            var newFit = new ScreenFit(Iphone13Pro, inset, 0);
            Assert.Equal(oldFit.Ratio, newFit.Ratio);
            Assert.Equal(oldFit.BaseWidth, newFit.BaseWidth);
            Assert.Equal(VddResolutionProfile.Create(Choice, verticalInsetPoints: inset),
                         VddResolutionProfile.Create(Choice, verticalInsetPoints: inset, preferredBaseWidth: 0));
        }
    }

    [Theory]
    [InlineData(0, 540)]
    [InlineData(600, 600)]
    [InlineData(720, 720)]
    [InlineData(840, 840)]
    public void ExplicitWidthKeepsAspectAndMatchesDriver(int choice, int expected)
    {
        var fit = new ScreenFit(Iphone13Pro, 91, choice);
        Assert.Equal(expected, fit.BaseWidth);
        Assert.Equal(new ScreenFit(Iphone13Pro, 91).Ratio, fit.Ratio);
        var widths = VddResolutionProfile.SupportedWidths(Choice, 91, choice);
        Assert.Equal(expected, widths[0]);
        var driver = VddResolutionProfile.Create(Choice, verticalInsetPoints: 91, preferredBaseWidth: choice);
        Assert.Equal(expected * 2, driver[0].Width);
        Assert.Equal((int)fit.PixelSizeFor(expected).Height, driver[0].Height);
        Assert.Equal(widths[0], VddResolutionProfile.SnapWidth(Choice, 1, 91, choice));
    }

    [Theory]
    [InlineData(91)]
    [InlineData(45)]
    [InlineData(0)]
    public void HighDensityWorksWithAllUuViewportPresets(int inset)
    {
        var fit = new ScreenFit(Iphone13Pro, inset, 720);
        var supported = VddResolutionProfile.SupportedWidths(Choice, inset, 720);
        Assert.Equal(fit.BaseWidth, supported[0]);
        var modes = VddResolutionProfile.Create(Choice, verticalInsetPoints: inset, preferredBaseWidth: 720);
        Assert.Equal(supported.Count, modes.Count);
        for (var i = 0; i < modes.Count; i++)
        {
            Assert.Equal((int)fit.PixelSizeFor(supported[i]).Width, modes[i].Width);
            Assert.Equal((int)fit.PixelSizeFor(supported[i]).Height, modes[i].Height);
        }
    }

    [Fact] public void InvalidSettingsFallbackAndUpperBound()
    {
        Assert.Equal(0, PhoneBaseWidth.Normalize(759));
        Assert.Equal(0, PhoneBaseWidth.AtIndex(-1));
        Assert.Equal(840, PhoneBaseWidth.AtIndex(99));
        Assert.Equal(2, PhoneBaseWidth.IndexOf(720));
        Assert.Equal(0, PhoneBaseWidth.IndexOf(999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenFit(Iphone13Pro, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenFit(Iphone13Pro, 0, 1081));
        Assert.Equal(1080, new ScreenFit(Iphone13Pro, 0, 1080).BaseWidth);
    }
}
