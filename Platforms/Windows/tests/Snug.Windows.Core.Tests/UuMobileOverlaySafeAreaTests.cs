using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;
public sealed class UuMobileOverlaySafeAreaTests
{
    [Theory]
    [InlineData(540, 65)]
    [InlineData(720, 86)]
    [InlineData(820, 98)]
    [InlineData(1080, 130)]
    public void PhoneUUBarAlwaysHasClearSpace(double width, int expected)
    {
        Assert.Equal(expected, UuMobileOverlaySafeArea.BottomInset(width));
    }

    [Theory]
    [InlineData(720, 1223)]
    [InlineData(820, 1392)]
    [InlineData(540, 950)]
    public void BrowserContentAndHomeBarUseSameInsets(int width, int height)
    {
        var content = UuMobileOverlaySafeArea.ContentHeight(width, height);
        var home = ScreenFit.HomeBarHeight(width);
        var nativeOverlay = UuMobileOverlaySafeArea.BottomInset(width);
        Assert.Equal(height, content + home + nativeOverlay);
        Assert.True(nativeOverlay > home);
        Assert.True(content > 200);
    }

    [Theory]
    [InlineData(1080, 130)]
    [InlineData(1640, 198)]
    [InlineData(2160, 260)]
    public void IPhoneNotchReceivesPhysicalTopSafeInset(double width, int expected)
    {
        Assert.Equal(expected, UuMobileOverlaySafeArea.TopInset(width));
    }

    [Theory]
    [InlineData(1080, 2088)]
    [InlineData(1640, 3170)]
    [InlineData(2160, 4176)]
    public void ManagedAppStaysBelowNotchAndAboveBottomControls(int width, int height)
    {
        var screen = new WindowBounds(1920, 0, width, height);
        var area = UuMobileOverlaySafeArea.AppBounds(screen);
        Assert.Equal(screen.X, area.X);
        Assert.Equal(screen.Y + UuMobileOverlaySafeArea.TopInset(width), area.Y);
        Assert.Equal(screen.Width, area.Width);
        Assert.Equal(screen.Height - UuMobileOverlaySafeArea.ReservedBottom(width),
            area.Y + area.Height - screen.Y);
        Assert.True(area.Height > 200);
    }

    [Fact]
    public void IPhone13ProRawModeProvidesTopAndBottomProtection()
    {
        var area = UuMobileOverlaySafeArea.AppBounds(
            new WindowBounds(1920, 0, 1640, 3170));
        Assert.Equal(new WindowBounds(1920, 198, 1640, 2751), area);
        Assert.Equal(2949, area.Y + area.Height);
        Assert.Equal(0, UuMobileOverlaySafeArea.TopInset(0));
    }

    [Theory]
    [InlineData(1080, 540, 65)]
    [InlineData(720, 540, 64.5)]
    [InlineData(540, 540, 65)]
    public void PhoneCanvasScrollEndsBeforeUuNativeToolbar(
        double actualWidth, double canvasWidth, double expected)
    {
        Assert.Equal(expected,
            UuMobileOverlaySafeArea.PhoneCanvasInset(actualWidth, canvasWidth), 3);
        Assert.Equal(UuMobileOverlaySafeArea.BottomInset(actualWidth),
            UuMobileOverlaySafeArea.PhoneCanvasInset(actualWidth, canvasWidth)
                * actualWidth / canvasWidth, 3);
    }

    [Fact]
    public void InvalidCanvasDimensionsDoNotCreateNegativePadding()
    {
        Assert.Equal(0, UuMobileOverlaySafeArea.PhoneCanvasInset(0, 540));
        Assert.Equal(0, UuMobileOverlaySafeArea.PhoneCanvasInset(1080, 0));
        Assert.Equal(0, UuMobileOverlaySafeArea.PhoneCanvasInset(-100, 540));
    }

    [Fact]
    public void TooSmallDisplaysRetainMinimumUsableContent()
    {
        Assert.Equal(200, UuMobileOverlaySafeArea.ContentHeight(720, 100));
        Assert.Equal(0, UuMobileOverlaySafeArea.BottomInset(0));
    }
}
