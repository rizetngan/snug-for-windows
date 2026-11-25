using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class PhysicalDisplayBoundsTests
{
    [Fact]
    public void VirtualizedPhoneUsesRawVddDesktopGeometry()
    {
        var logical = new WindowBounds(1920, 0, 720, 1392);
        var raw = new WindowBounds(1920, 0, 1080, 2088);
        Assert.Equal(raw, PhysicalDisplayBounds.Resolve(logical, raw));
        Assert.Equal(1080, PhysicalDisplayBounds.Resolve(logical, raw).Width);
    }

    [Fact]
    public void PrimaryMonitorUnchanged()
    {
        var a = new WindowBounds(0, 0, 1920, 1080);
        Assert.Equal(a, PhysicalDisplayBounds.Resolve(a, a));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 800)]
    [InlineData(32768, 2048)]
    [InlineData(1080, 32768)]
    public void InvalidRawModesFallBackToWindowsScreen(int w,int h)
    {
        var logical = new WindowBounds(1920, 0, 720, 1392);
        Assert.Equal(logical, PhysicalDisplayBounds.Resolve(logical,
            new WindowBounds(1920, 0, w, h)));
        Assert.Equal(logical, PhysicalDisplayBounds.Resolve(logical, null));
    }

    [Fact]
    public void RawInsetAndHomeBarRemainInsidePhone()
    {
        var logical = new WindowBounds(1920, 0, 720, 1392);
        var raw = PhysicalDisplayBounds.Resolve(logical,
            new WindowBounds(1920, 0, 1080, 2088));
        var bottom = UuMobileOverlaySafeArea.ReservedBottom(raw.Width);
        var appHeight = UuMobileOverlaySafeArea.ContentHeight(raw.Width, raw.Height);
        Assert.Equal(raw.Height - bottom, appHeight);
        Assert.True(appHeight > 1600);
        Assert.True(bottom > 150);
    }
}
