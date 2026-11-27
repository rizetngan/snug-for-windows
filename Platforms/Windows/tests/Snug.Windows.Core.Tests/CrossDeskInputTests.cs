using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class CrossDeskInputTests
{
    private static CrossDeskPointerEvent E(CrossDeskPointerEventKind kind, double x, double y, double time) =>
        new(kind, new PointD(x,y), time);

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
}
