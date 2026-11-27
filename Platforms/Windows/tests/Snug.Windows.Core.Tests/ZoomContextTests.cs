using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class ZoomContextTests
{
    [Theory]
    [InlineData(1,100)]
    [InlineData(2,200)]
    [InlineData(.75,75)]
    [InlineData(1.333,133)]
    [InlineData(5,500)]
    [InlineData(0,null)]
    [InlineData(-1,null)]
    [InlineData(11,null)]
    public void ReadsOnlyValidBrowserApiFactor(double factor, int? expected) =>
        Assert.Equal(expected, ZoomContext.ActualPercent(factor));

    [Fact]
    public void NativeFallbackNeverInventsAnAbsolutePercentage()
    {
        Assert.Equal("缩放",new BrowserZoomObservation("native:tab",null).Label);
        Assert.Equal("100%",new BrowserZoomObservation("bridge:tab",100).Label);
        Assert.Equal("200%",new BrowserZoomObservation("bridge:tab",200).Label);
        Assert.Null(ZoomContext.ActualPercent(double.NaN));
        Assert.Null(ZoomContext.ActualPercent(double.PositiveInfinity));
    }

    [Fact]
    public void GestureCannotFollowAnotherWindowTabOrPlacementSession()
    {
        var target = new ZoomTarget(new(11,123),DateTimeOffset.UtcNow,"native:tabA",true,200);
        Assert.True(target.SameScope(target with { BrowserPercent=100 }));
        Assert.False(target.SameScope(target with { TabIdentity="native:tabB" }));
        Assert.False(target.SameScope(target with { Window=new(11,456) }));
        Assert.False(target.SameScope(target with { Session=target.Session.AddSeconds(1) }));
        Assert.False(target.SameScope(target with { TabIdentity=null }));
        Assert.False(target.SameScope(null));
    }

    [Fact]
    public void BridgeTokenBindsConnectionProfileWindowAndTab()
    {
        var token = new BridgeTabToken(Guid.NewGuid(),11,22);
        Assert.True(BridgeTabToken.TryParse(token.ToString(),out var parsed));
        Assert.Equal(token,parsed);
        Assert.NotEqual(token,token with { Connection=Guid.NewGuid() });
        Assert.NotEqual(token,token with { WindowId=12 });
        Assert.NotEqual(token,token with { TabId=23 });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("native:42:1234")]
    [InlineData("bridge:garbage:1:2")]
    [InlineData("bridge:00000000000000000000000000000000:-1:2")]
    [InlineData("bridge:00000000000000000000000000000000:1:-2")]
    public void RejectsUnboundOrMalformedBridgeTokens(string? value) =>
        Assert.False(BridgeTabToken.TryParse(value,out _));

    [Theory]
    [InlineData("Code.exe",2)]
    [InlineData("Cursor.exe",2)]
    [InlineData("Zed.exe",2)]
    [InlineData("WindowsTerminal.exe",0)]
    [InlineData("Antigravity.exe",0)]
    public void OnlyKnownRecoveryScopesReceiveAutomaticTemporaryZoom(string app,int expected) =>
        Assert.Equal(expected,ZoomContext.InitialTemporarySteps(app,2));
}