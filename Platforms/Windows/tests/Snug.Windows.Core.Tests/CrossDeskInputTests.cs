using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class CrossDeskInputTests
{
    private static CrossDeskPointerEvent E(CrossDeskPointerEventKind kind, double x, double y, double time) =>
        new(kind, new PointD(x,y), time);

    [Fact]
    public void Android177FastButtonDragScrollsInsteadOfSelecting()
    {
        var p = new CrossDeskInputPipeline();
        p.Take(E(CrossDeskPointerEventKind.Move,2455,1927,1), _=>true,1);
        var down = p.Take(E(CrossDeskPointerEventKind.LeftDown,2455,1927,1), _=>true,1);
        var drag = p.Take(E(CrossDeskPointerEventKind.LeftDrag,2455,1742,1.016), _=>true,1);
        var up = p.Take(E(CrossDeskPointerEventKind.LeftUp,2455,709,1.407), _=>true,1);
        Assert.False(down.PassOriginal);
        Assert.False(drag.PassOriginal);
        Assert.False(up.PassOriginal);
        Assert.Contains(drag.Batches.SelectMany(b=>b.Commands), c=>c.Scroll is { PixelsY: < 0 });
        Assert.DoesNotContain(drag.Batches.Concat(up.Batches).SelectMany(b=>b.Commands), c=>c.Kind == TouchCommandKind.ReplayPress || c.Kind == TouchCommandKind.ReplayCurrent || c.Kind == TouchCommandKind.RightClick);
    }

    [Fact]
    public void DelayedButtonDragPreservesSelection()
    {
        var p = new CrossDeskInputPipeline();
        p.Take(E(CrossDeskPointerEventKind.Move,200,300,1), _=>true,1);
        p.Take(E(CrossDeskPointerEventKind.LeftDown,200,300,1.001), _=>true,1);
        var drag = p.Take(E(CrossDeskPointerEventKind.LeftDrag,200,350,1.2), _=>true,1);
        Assert.Contains(drag.Batches.SelectMany(b=>b.Commands), c=>c.Kind == TouchCommandKind.ReplayPress);
    }

    [Fact]
    public void AbsoluteTapAndStationaryDragReplayExactlyOneClick()
    {
        var pipeline = new CrossDeskInputPipeline();
        var move = pipeline.Take(E(CrossDeskPointerEventKind.Move,200,300,1), _=>true, 1);
        var down = pipeline.Take(E(CrossDeskPointerEventKind.LeftDown,200,300,1.001), _=>true,1);
        var stationary = pipeline.Take(E(CrossDeskPointerEventKind.LeftDrag,200,300,1.002), _=>true,1);
        var up = pipeline.Take(E(CrossDeskPointerEventKind.LeftUp,200,300,1.003), _=>true,1);
        Assert.True(move.PassOriginal);
        Assert.False(down.PassOriginal);
        Assert.True(stationary.PassOriginal);
        Assert.False(up.PassOriginal);
        Assert.Equal(new[] {TouchCommandKind.ReplayPress, TouchCommandKind.ReplayCurrent},
            up.Batches.SelectMany(x=>x.Commands).Select(c=>c.Kind));
    }

    [Fact]
    public void NativeControlsAndVirtualMousePassOriginalButtons()
    {
        var pipeline = new CrossDeskInputPipeline();
        pipeline.Take(E(CrossDeskPointerEventKind.Move,200,300,1), _=>false,1);
        Assert.True(pipeline.Take(E(CrossDeskPointerEventKind.LeftDown,200,300,1.001), _=>false,1).PassOriginal);
        Assert.True(pipeline.Take(E(CrossDeskPointerEventKind.LeftUp,200,300,1.002), _=>false,1).PassOriginal);
        Assert.True(pipeline.Take(E(CrossDeskPointerEventKind.LeftDown,200,300,2), _=>true,1).PassOriginal);
        Assert.True(pipeline.Take(E(CrossDeskPointerEventKind.LeftUp,200,300,2.2), _=>true,1).PassOriginal);
    }

    [Fact]
    public void NativeClientHoldProducesRightClickWithoutWaitingAnotherHalfSecond()
    {
        var pipeline = new CrossDeskInputPipeline();
        pipeline.Take(E(CrossDeskPointerEventKind.Move,200,300,1), _=>true,1);
        pipeline.Take(E(CrossDeskPointerEventKind.LeftDown,200,300,1.001), _=>true,1);
        var up = pipeline.Take(E(CrossDeskPointerEventKind.LeftUp,200,300,1.2), _=>true,1);
        Assert.False(up.PassOriginal);
        Assert.Contains(up.Batches.SelectMany(x=>x.Commands), c=>c.Kind == TouchCommandKind.RightClick);
    }

    [Fact]
    public void MoveOnlyFingerSlideScrollsAndCompletesWithoutSyntheticButton()
    {
        var pipeline = new CrossDeskInputPipeline();
        pipeline.Take(E(CrossDeskPointerEventKind.Move,100,100,1), _=>true,1);
        pipeline.Take(E(CrossDeskPointerEventKind.LeftDown,100,100,1.001), _=>true,1);
        pipeline.Take(E(CrossDeskPointerEventKind.LeftUp,100,100,1.002), _=>true,1);
        pipeline.Take(E(CrossDeskPointerEventKind.Move,300,400,2), _=>true,1);
        var slide = pipeline.Take(E(CrossDeskPointerEventKind.Move,300,450,2.02), _=>true,1);
        Assert.True(slide.PassOriginal);
        Assert.Contains(slide.Batches.SelectMany(x=>x.Commands), c=>c.Scroll is { PixelsY: > 0 });
        Assert.True(pipeline.NeedsTick);
        var end = pipeline.Tick(2.4,_=>true,1);
        Assert.Contains(end.Batches.SelectMany(x=>x.Commands), c=>c.Scroll is { Phase:ScrollPhase.Ended });
        Assert.DoesNotContain(slide.Batches.Concat(end.Batches).SelectMany(x=>x.Commands), c=>c.Kind!=TouchCommandKind.Scroll);
    }

    [Fact]
    public void MoveOnlyFingerFeedProducesSlideAndTimedRelease()
    {
        var normalizer = new CrossDeskTouch();
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(100,100), 1.0));
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(250,100), 1.3));
        var slide = normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(260,100), 1.32));
        Assert.Equal(CrossDeskTouch.FeedKind.Finger, normalizer.Feed);
        Assert.Contains(slide.Before, x => x.Kind == CrossDeskTouchInputKind.Press);
        Assert.Contains(slide.Before, x => x.Kind == CrossDeskTouchInputKind.Recognized && x.Gesture == CrossDeskTouchGesture.Slide);
        Assert.Equal(CrossDeskTouchInputKind.Drag, slide.Current?.Kind);
        Assert.Contains(normalizer.Tick(1.621), x => x.Kind == CrossDeskTouchInputKind.Release);
    }

    [Fact]
    public void RepeatedStationaryMovesClassifyRelativeCursorFeed()
    {
        var normalizer = new CrossDeskTouch();
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(100,100), 1.0));
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(100,100), 1.3));
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(100,100), 1.6));
        var cursor = normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(100,100), 1.9));
        Assert.Equal(CrossDeskTouch.FeedKind.Cursor, normalizer.Feed);
        Assert.Empty(cursor.Before);
        Assert.Null(cursor.Current);
    }

    [Fact]
    public void PausedSlideResumesOnTickAndEndsAfterInactivity()
    {
        var normalizer = new CrossDeskTouch();
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(100,100), 1.0));
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(250,100), 1.3));
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(260,100), 1.32));
        var paused = normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(260,100), 1.40));
        Assert.Equal(CrossDeskTouchInputKind.Drag, paused.Current?.Kind);
        Assert.Contains(normalizer.Tick(1.421), x => x.Kind == CrossDeskTouchInputKind.Drag);
        Assert.Contains(normalizer.Tick(1.701), x => x.Kind == CrossDeskTouchInputKind.Release);
    }

    [Fact]
    public void LargeLandingJumpReleasesPreviousSlideBeforeStartingNext()
    {
        var normalizer = new CrossDeskTouch();
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(100,100), 1.0));
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(250,100), 1.3));
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(260,100), 1.32));
        var landed = normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(500,500), 1.50));
        Assert.Contains(landed.Before, x => x.Kind == CrossDeskTouchInputKind.Release);
        Assert.Null(landed.Current);
        var next = normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(510,500), 1.52));
        Assert.Contains(next.Before, x => x.Kind == CrossDeskTouchInputKind.Press);
        Assert.Equal(CrossDeskTouchInputKind.Drag, next.Current?.Kind);
    }

    [Fact]
    public void TapRequiresNearbyMoveButVirtualMouseClickRemainsUnnormalized()
    {
        var normalizer = new CrossDeskTouch();
        normalizer.Normalize(new(CrossDeskPointerEventKind.Move, new(200,300), 1.0));
        var down = normalizer.Normalize(new(CrossDeskPointerEventKind.LeftDown, new(200,300), 1.01));
        var up = normalizer.Normalize(new(CrossDeskPointerEventKind.LeftUp, new(200,300), 1.10));
        Assert.Equal(CrossDeskTouchInputKind.Press, down.Current?.Kind);
        Assert.Equal(CrossDeskTouchInputKind.Release, up.Current?.Kind);

        var cursor = new CrossDeskTouch();
        var virtualDown = cursor.Normalize(new(CrossDeskPointerEventKind.LeftDown, new(200,300), 2.0));
        var virtualUp = cursor.Normalize(new(CrossDeskPointerEventKind.LeftUp, new(200,300), 2.1));
        Assert.Null(virtualDown.Current);
        Assert.Null(virtualUp.Current);
    }
}