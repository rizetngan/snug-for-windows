using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class PhoneLayoutValidationTests
{
    [Fact]
    public void RejectsStaleLargeWindowAfterModeReset()
    {
        var old = UuMobileOverlaySafeArea.AppBounds(new(1920, 0, 1640, 3170));
        var current = UuMobileOverlaySafeArea.AppBounds(new(1920, 0, 1080, 2088));
        Assert.False(PhoneLayoutValidation.Fits(old, current));
        Assert.True(PhoneLayoutValidation.Fits(current, current));
    }

    [Theory]
    [InlineData(720, 1392, false)]
    [InlineData(1080, 2088, true)]
    [InlineData(1640, 3170, false)]
    public void AcceptsOnlyRequestedPhysicalMode(int width, int height, bool expected) =>
        Assert.Equal(expected, PhoneLayoutValidation.MatchesMode(new(1920, 0, width, height),1080,2088));

    [Theory]
    [InlineData(0, 0, 1080, 1700, true)]
    [InlineData(3, 3, 1082, 1702, true)]
    [InlineData(4, 0, 1080, 1700, false)]
    [InlineData(0, -4, 1080, 1700, false)]
    [InlineData(0, 0, 1083, 1700, false)]
    [InlineData(0, 0, 1080, 1703, false)]
    [InlineData(0, 0, 0, 1700, false)]
    [InlineData(0, 0, 1080, -1, false)]
    public void FitValidationRespectsPhysicalSafeArea(int dx,int dy,int w,int h,bool expected)
    {
        var area = new WindowBounds(1920,130,1080,1700);
        Assert.Equal(expected, PhoneLayoutValidation.Fits(new(area.X+dx,area.Y+dy,w,h),area));
    }

    [Fact]
    public void DetectsSameModeDpiAndPositionChanges()
    {
        var previous = new DisplayInfo("DISPLAY2","Snug Phone","SNG00F0",new(1920,0,1080,2088),false,true,1.5,1.5);
        Assert.False(PhoneLayoutValidation.GeometryChanged(previous,previous with {}));
        Assert.True(PhoneLayoutValidation.GeometryChanged(previous,previous with { DpiScaleX=2,DpiScaleY=2 }));
        Assert.True(PhoneLayoutValidation.GeometryChanged(previous,previous with { Bounds=new(0,1080,1080,2088) }));
        Assert.True(PhoneLayoutValidation.GeometryChanged(previous,previous with { DeviceName="DISPLAY3" }));
    }
}