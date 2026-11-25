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

    [Fact]
    public void TooSmallDisplaysRetainMinimumUsableContent()
    {
        Assert.Equal(200, UuMobileOverlaySafeArea.ContentHeight(720, 100));
        Assert.Equal(0, UuMobileOverlaySafeArea.BottomInset(0));
    }
}
