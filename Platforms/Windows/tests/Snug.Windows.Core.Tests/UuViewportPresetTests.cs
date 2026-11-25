using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class UuViewportPresetTests
{
    private static readonly PhoneScreen Iphone13Pro = new(1170,2532,390);
    private static readonly PhoneChoice Choice = new("Apple iPhone 13 Pro",Iphone13Pro);

    [Fact] public void ClassicMaintainsOriginalGeometryAndExistingVddModes()
    {
        var classic = new ScreenFit(Iphone13Pro);
        var explicitClassic = new ScreenFit(Iphone13Pro,UuViewportPreset.VerticalInsetPoints("Classic"));
        Assert.Equal(classic.Ratio,explicitClassic.Ratio);
        Assert.Equal(classic.BaseWidth,explicitClassic.BaseWidth);
        Assert.Equal(VddResolutionProfile.Create(Choice),VddResolutionProfile.Create(Choice,verticalInsetPoints:91));
    }

    [Fact] public void NewUUViewportsMatchPhoneProportionsWithoutReducingHeight()
    {
        var classic = new ScreenFit(Iphone13Pro,UuViewportPreset.VerticalInsetPoints("Classic"));
        var expanded = new ScreenFit(Iphone13Pro,UuViewportPreset.VerticalInsetPoints("Expanded"));
        var full = new ScreenFit(Iphone13Pro,UuViewportPreset.VerticalInsetPoints("Full"));
        Assert.True(classic.Ratio < expanded.Ratio);
        Assert.True(expanded.Ratio < full.Ratio);
        Assert.Equal(2532d/1170d,full.Ratio,precision:8);
        Assert.Equal(540,full.BaseWidth);
        Assert.Equal(new PhoneSize(540,Math.Round(540d*full.Ratio,MidpointRounding.AwayFromZero)),full.SizeFor(540));
        Assert.Equal(1080,VddResolutionProfile.Create(Choice,verticalInsetPoints:0)[0].Width);
    }

    [Fact] public void DriverModeAndWindowModeAreIdenticalForEachViewport()
    {
        foreach(var preset in new[]{"Classic","Expanded","Full"})
        {
            var inset=UuViewportPreset.VerticalInsetPoints(preset);
            var fit=new ScreenFit(Iphone13Pro,inset);
            var modes=VddResolutionProfile.Create(Choice,verticalInsetPoints:inset);
            var widths=VddResolutionProfile.SupportedWidths(Choice,inset);
            Assert.Equal(widths.Count,modes.Count);
            for(var i=0;i<modes.Count;i++)
            {
                Assert.Equal(fit.PixelSizeFor(widths[i]).Width,modes[i].Width);
                Assert.Equal(fit.PixelSizeFor(widths[i]).Height,modes[i].Height);
                Assert.Equal(widths[i],VddResolutionProfile.SnapWidth(Choice,widths[i],inset));
            }
        }
    }

    [Fact] public void UnknownSettingsFallBackToClassicAndInvalidInsetRejected()
    {
        Assert.Equal("Classic",UuViewportPreset.Normalize("Unknown"));
        Assert.Equal(91,UuViewportPreset.VerticalInsetPoints(null));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new ScreenFit(Iphone13Pro,-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new ScreenFit(Iphone13Pro,181));
    }
}
