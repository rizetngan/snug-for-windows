using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;
public sealed class PhoneDisplayHoldTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;

    [Fact] public void BackgroundedPhoneRetainsSameDisplayForFiveMinutes()
    {
        var hold = new PhoneDisplayHold();
        Assert.False(hold.ShouldKeep(false,true,false,T));
        Assert.True(hold.ShouldKeep(true,true,false,T));
        Assert.True(hold.ShouldKeep(true,true,true,T.AddSeconds(1)));
        Assert.True(hold.ShouldKeep(false,true,true,T.AddSeconds(5)));
        Assert.True(hold.IsPaused);
        Assert.True(hold.ShouldKeep(false,true,true,T.AddMinutes(5).AddSeconds(5)));
        Assert.True(hold.ShouldKeep(true,true,true,T.AddMinutes(6)));
        Assert.False(hold.IsPaused);
        Assert.True(hold.ShouldKeep(false,true,true,T.AddMinutes(6).AddSeconds(1)));
        Assert.True(hold.ShouldKeep(false,true,true,T.AddMinutes(15)));
    }

    [Fact] public void InactiveSessionIsNotAllowedToCreateDisplay()
    {
        var hold = new PhoneDisplayHold();
        Assert.False(hold.ShouldKeep(false,true,true,T));
        Assert.False(hold.IsPaused);
    }

    [Fact] public void TimeoutReleasesAndDoesNotRefreshUntilNewSession()
    {
        var hold=new PhoneDisplayHold();
        hold.ShouldKeep(true,true,true,T);
        Assert.True(hold.ShouldKeep(false,true,true,T.AddSeconds(1)));
        Assert.True(hold.ShouldKeep(false,true,true,T.AddMinutes(10).AddSeconds(1)));
        Assert.False(hold.ShouldKeep(false,true,true,T.AddMinutes(10).AddSeconds(2)));
        Assert.False(hold.ShouldKeep(false,true,true,T.AddMinutes(10).AddSeconds(3)));
        Assert.True(hold.ShouldKeep(true,true,true,T.AddMinutes(11)));
        Assert.True(hold.ShouldKeep(false,true,true,T.AddMinutes(12)));
    }

    [Fact] public void ExplicitDisableReleasesImmediatelyAndClearsHold()
    {
        var hold=new PhoneDisplayHold();
        hold.ShouldKeep(true,true,true,T);
        hold.ShouldKeep(false,true,true,T.AddSeconds(2));
        Assert.False(hold.ShouldKeep(false,false,true,T.AddSeconds(3)));
        Assert.False(hold.IsPaused);
        Assert.False(hold.ShouldKeep(false,true,true,T.AddSeconds(4)));
    }
    [Fact] public void UUBackgroundEpisodeExpiresInTrackerButKeepsSameDisplay()
    {
        var tracker = new UuConnectionTracker();
        var hold = new PhoneDisplayHold();
        var live = new UuSignals(false, new("iPhone", ControllerKind.Phone), true);
        var inactive = new UuSignals(false, null, false, CaptureVerifiedInactive: true);
        tracker.Update(live, T);
        Assert.True(hold.ShouldKeep(tracker.WantsPhoneScreen(T), true, true, T));

        tracker.Update(inactive, T.AddSeconds(2));
        tracker.Update(inactive, T.AddSeconds(4.5));
        Assert.False(tracker.IsConnected);
        Assert.True(hold.ShouldKeep(tracker.WantsPhoneScreen(T.AddSeconds(4.5)), true, true, T.AddSeconds(4.5)));

        tracker.Update(inactive, T.AddSeconds(7));
        Assert.False(tracker.WantsPhoneScreen(T.AddSeconds(7)));
        Assert.True(hold.ShouldKeep(false, true, true, T.AddSeconds(7)));
        Assert.True(hold.ShouldKeep(false, true, true, T.AddMinutes(5)));

        var resumed = tracker.Update(live, T.AddMinutes(5).AddSeconds(1));
        Assert.Contains(resumed, t => t.Kind == ConnectionTransitionKind.Connected);
        Assert.True(hold.ShouldKeep(tracker.WantsPhoneScreen(T.AddMinutes(5).AddSeconds(1)),
            true, true, T.AddMinutes(5).AddSeconds(1)));
        Assert.False(hold.IsPaused);
    }

    [Fact] public void DesktopScreenSwitchRemainsSameUUPhoneDisplay()
    {
        var hold = new PhoneDisplayHold();
        hold.ShouldKeep(true, true, true, T);
        // A desktop-only controller can make the phone-specific 'wants' false
        // while the UU session is still connected. The caller counts that
        // connected session when a Snug-owned display is already present.
        Assert.True(hold.ShouldKeep(true, true, true, T.AddHours(1)));
        Assert.False(hold.IsPaused);
    }

    [Fact] public void PhysicalDisplayLossResetsOwnership()
    {
        var hold=new PhoneDisplayHold();
        hold.ShouldKeep(true,true,true,T);
        Assert.True(hold.ShouldKeep(false,true,true,T.AddSeconds(1)));
        Assert.False(hold.ShouldKeep(false,true,false,T.AddSeconds(2)));
        Assert.False(hold.ShouldKeep(false,true,true,T.AddSeconds(3)));
    }
}
