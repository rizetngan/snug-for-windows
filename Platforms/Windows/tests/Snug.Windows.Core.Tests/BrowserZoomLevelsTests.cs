using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;
public sealed class BrowserZoomLevelsTests
{
    [Theory]
    [InlineData("C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", true)]
    [InlineData("C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", true)]
    [InlineData("chrome", true)]
    [InlineData("Code.exe", false)]
    [InlineData("firefox", false)]
    public void OnlySupportedBrowsersExposeSlider(string path, bool expected) =>
        Assert.Equal(expected, BrowserZoomLevels.IsBrowser(path));

    [Fact] public void ExplicitNativeZoomStopsRoundTrip()
    {
        Assert.Equal(100, BrowserZoomLevels.PercentAt(BrowserZoomLevels.DefaultIndex));
        Assert.Equal(50, BrowserZoomLevels.PercentAt(-12));
        Assert.Equal(200, BrowserZoomLevels.PercentAt(200));
        Assert.Equal(-5, BrowserZoomLevels.StepsFromDefault(0));
        Assert.Equal(5, BrowserZoomLevels.StepsFromDefault(10));
        for(int i=1;i<BrowserZoomLevels.Percents.Length;i++)
            Assert.True(BrowserZoomLevels.Percents[i]>BrowserZoomLevels.Percents[i-1]);
    }
}
