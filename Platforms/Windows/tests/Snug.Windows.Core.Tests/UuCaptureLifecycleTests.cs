using Snug.Windows.Core;
namespace Snug.Windows.Core.Tests;

public sealed class UuCaptureLifecycleTests
{
    private static readonly UuServerGeneration Server = new(10,100);
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;
    private static UuCaptureLifecycle Start(bool empty = true)
    {
        var c=new UuCaptureLifecycle();c.Begin(Server,empty);return c;
    }
    [Fact] public void ObservedSwitchNeverBecomesInactive()
    {
        var c=Start();
        Assert.Equal(UuCapturePhase.Inactive,c.Snapshot(T).Phase);
        c.Observe(Server,1,true,T.AddSeconds(1));
        c.Observe(Server,1,false,T.AddSeconds(9));
        Assert.Equal(UuCapturePhase.Rebuilding,c.Snapshot(T.AddSeconds(9)).Phase);
        c.Observe(Server,2,true,T.AddSeconds(9.022));
        Assert.Equal(UuCapturePhase.Active,c.Snapshot(T.AddSeconds(10)).Phase);
        c.Observe(Server,2,false,T.AddSeconds(19));
        c.Observe(Server,3,true,T.AddSeconds(19.024));
        c.Observe(Server,3,false,T.AddSeconds(30));
        Assert.Equal(UuCapturePhase.Rebuilding,c.Snapshot(T.AddSeconds(32.99)).Phase);
        Assert.Equal(UuCapturePhase.Inactive,c.Snapshot(T.AddSeconds(33)).Phase);
    }
    [Fact] public void OverlappingOldAndNewCaptureStreamsNeverDisconnect()
    {
        var c = Start();
        c.Observe(Server, 11, true, T);
        c.Observe(Server, 22, true, T.AddSeconds(5));
        c.Observe(Server, 11, false, T.AddSeconds(10));
        Assert.Equal(UuCapturePhase.Active, c.Snapshot(T.AddSeconds(13)).Phase);
        Assert.Equal(1, c.Snapshot(T.AddSeconds(13)).ObservedContexts);
        c.Observe(Server, 22, false, T.AddSeconds(20));
        Assert.Equal(UuCapturePhase.Rebuilding, c.Snapshot(T.AddSeconds(20)).Phase);
        Assert.Equal(UuCapturePhase.Inactive, c.Snapshot(T.AddSeconds(23)).Phase);
    }

    [Fact] public void RestartWithinGraceIsNotAVerifiedDisconnect()
    {
        var c = Start();
        c.Observe(Server, 11, true, T);
        c.Observe(Server, 11, false, T.AddSeconds(10));
        Assert.True(c.Snapshot(T.AddSeconds(11)).CaptureActive);
        c.Observe(Server, 22, true, T.AddSeconds(12.95));
        Assert.Equal(UuCapturePhase.Active, c.Snapshot(T.AddSeconds(13)).Phase);
        Assert.Equal(1, c.Snapshot(T.AddSeconds(13)).ObservedContexts);
    }

    [Fact] public void LateAttachDoesNotInventDisconnectedBaseline()
    {
        var c=Start(false);Assert.Null(c.Snapshot(T).CaptureActive);
        c.Observe(Server,1,true,T);
        Assert.True(c.Snapshot(T).CaptureActive);
        c.Observe(Server,1,false,T.AddSeconds(1));
        Assert.Null(c.Snapshot(T.AddSeconds(5)).CaptureActive);
    }
    [Fact] public void OtherServerAndFailedConnectAreIgnored()
    {
        var c=Start();
        Assert.False(c.Observe(new(10,101),1,true,T));
        Assert.False(c.Observe(Server,1,true,T,false));
        Assert.False(c.Snapshot(T).CaptureActive);
    }
    [Fact] public void ConcurrentContextsAndDuplicateOpen()
    {
        var c=Start();c.Observe(Server,1,true,T);c.Observe(Server,1,true,T);
        c.Observe(Server,2,true,T);Assert.Equal(2,c.Snapshot(T).ObservedContexts);
        c.Observe(Server,1,false,T.AddSeconds(1));
        Assert.Equal(1,c.Snapshot(T.AddSeconds(10)).ObservedContexts);
        Assert.True(c.Snapshot(T.AddSeconds(10)).CaptureActive); // static content remains active
        c.Observe(Server,2,false,T.AddSeconds(11));
        Assert.False(c.Snapshot(T.AddSeconds(14)).CaptureActive);
    }
    [Fact] public void UnmatchedCloseInvalidatesCompleteInventory()
    {
        var c=Start();Assert.False(c.Observe(Server,9,false,T));
        Assert.Null(c.Snapshot(T).CaptureActive);
    }
    [Fact] public void LostTraceAndServerReplacementReturnUnknown()
    {
        var c=Start();c.Observe(Server,1,true,T);c.Invalidate();
        Assert.Null(c.Snapshot(T).CaptureActive);
        Assert.False(c.Observe(Server,1,false,T));
        c.Begin(new(10,101),false);Assert.Null(c.Snapshot(T).CaptureActive);
    }
    [Fact] public void OutOfOrderEventFailsUnknown()
    {
        var c=Start();c.Observe(Server,1,true,T.AddSeconds(2));
        Assert.False(c.Observe(Server,1,false,T));
        Assert.Null(c.Snapshot(T.AddSeconds(4)).CaptureActive);
    }
    [Fact] public void CompleteInactiveSupersedesStickyLogsButNotActualController()
    {
        var c=Start();var old=new UuSignals(false,null,null,LogFallback:true);
        var fused=UuCaptureLifecycle.Combine(old,c.Snapshot(T));
        Assert.False(fused.IsPresent);Assert.False(fused.LogFallback);Assert.True(fused.CaptureVerifiedInactive);
        var phone=old with { Controllers=[new("iPhone",ControllerKind.Phone)] };
        Assert.True(UuCaptureLifecycle.Combine(phone,c.Snapshot(T)).IsPresent);
        var peer = new UuSignals(false,null,false,PeerConnection:true);
        Assert.False(UuCaptureLifecycle.Combine(peer,c.Snapshot(T)).IsPresent);
        c.Invalidate();Assert.Equal(old,UuCaptureLifecycle.Combine(old,c.Snapshot(T)));
    }
    [Fact] public void IncompleteInventoryDoesNotAuthorizeEarlyExpiry()
    {
        var current = new UuSignals(false,null,null,LogFallback:true,CaptureVerifiedInactive:true);
        var incomplete = UuCaptureLifecycle.Combine(current,new(UuCapturePhase.Inactive,0,false));
        Assert.False(incomplete.CaptureVerifiedInactive);
        Assert.True(incomplete.LogFallback);
        var unknown = UuCaptureLifecycle.Combine(current,new(UuCapturePhase.Unknown,0,false));
        Assert.False(unknown.CaptureVerifiedInactive);
        var rebuilding = UuCaptureLifecycle.Combine(current,new(UuCapturePhase.Rebuilding,0,true));
        Assert.False(rebuilding.CaptureVerifiedInactive);
        Assert.True(rebuilding.IsPresent);
    }
    [Fact] public void RealCaptureSupersedesLogOnlyWithoutClaimingPhone()
    {
        var c=Start(false);c.Observe(Server,1,true,T);
        var fused=UuCaptureLifecycle.Combine(new(false,null,null,LogFallback:true),c.Snapshot(T));
        Assert.Equal(UuEvidenceKind.Session,fused.Evidence);
        Assert.Null(fused.Controller);
    }
}