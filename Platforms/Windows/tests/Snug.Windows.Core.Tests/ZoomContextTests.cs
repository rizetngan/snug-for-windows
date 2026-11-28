using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class ZoomContextTests
{
    [Theory]
    [InlineData(0,-5)]
    [InlineData(5,0)]
    [InlineData(10,5)]
    [InlineData(-1,-5)]
    [InlineData(11,5)]
    public void BrowserRailRepresentsRelativeStepsNotAnAbsolutePercentage(int index, int expected) =>
        Assert.Equal(expected, BrowserZoomLevels.StepsFromDefault(index));

    [Fact]
    public void NativeObservationStoresOnlySelectedTabIdentity()
    {
        Assert.Equal("native:tab", new BrowserZoomObservation("native:tab").TabIdentity);
    }
    [Fact]
    public void GestureCannotFollowAnotherWindowTabOrPlacementSession()
    {
        var target = new ZoomTarget(new(11,123),DateTimeOffset.UtcNow,"native:tabA",true);
        Assert.True(target.SameScope(target));
        Assert.False(target.SameScope(target with { TabIdentity="native:tabB" }));
        Assert.False(target.SameScope(target with { Window=new(11,456) }));
        Assert.False(target.SameScope(target with { Session=target.Session.AddSeconds(1) }));
        Assert.False(target.SameScope(target with { TabIdentity=null }));
        Assert.False(target.SameScope(target with { IsBrowser=false }));
        Assert.False(target.SameScope(null));
    }

    [Theory]
    [InlineData("Code.exe",2)]
    [InlineData("Cursor.exe",2)]
    [InlineData("Zed.exe",2)]
    [InlineData("WindowsTerminal.exe",0)]
    [InlineData("Antigravity.exe",0)]
    public void OnlyKnownRecoveryScopesReceiveAutomaticTemporaryZoom(string app,int expected) =>
        Assert.Equal(expected,ZoomContext.InitialTemporarySteps(app,2));
}