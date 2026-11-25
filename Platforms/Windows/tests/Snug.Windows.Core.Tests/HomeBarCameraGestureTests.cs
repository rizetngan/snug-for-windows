using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class HomeBarCameraGestureTests
{
    [Fact]
    public void ShortTapNeverCaptures()
    {
        var gesture = new HomeBarCameraGesture();
        gesture.Begin(20, 14, 1000);
        Assert.False(gesture.End(20, 14, 1499, insideButton: true));
    }

    [Fact]
    public void StationaryLongPressCapturesAtThreshold()
    {
        var gesture = new HomeBarCameraGesture();
        gesture.Begin(20, 14, 1000);
        Assert.False(gesture.End(20, 14, 1499, insideButton: true));
        gesture.Begin(20, 14, 2000);
        Assert.True(gesture.End(20, 14, 2500, insideButton: true));
    }

    [Fact]
    public void UpwardSwipeCancelsCameraCapture()
    {
        var gesture = new HomeBarCameraGesture();
        gesture.Begin(20, 14, 1000);
        gesture.Move(20, 1);
        Assert.False(gesture.End(20, 1, 2000, insideButton: false));
        Assert.False(gesture.IsActive);
    }

    [Fact]
    public void TinyPointerJitterIsAllowedButLargeMovementCancels()
    {
        var gesture = new HomeBarCameraGesture();
        gesture.Begin(20, 14, 1000);
        gesture.Move(24, 17);
        Assert.True(gesture.End(24, 17, 1500, insideButton: true));

        gesture.Begin(20, 14, 2000);
        gesture.Move(29, 14);
        Assert.False(gesture.End(29, 14, 3000, insideButton: true));
    }

    [Fact]
    public void ReleasingOutsideCameraDoesNotCapture()
    {
        var gesture = new HomeBarCameraGesture();
        gesture.Begin(20, 14, 1000);
        Assert.False(gesture.End(20, 14, 1800, insideButton: false));
    }

    [Fact]
    public void CancelAndClockRegressionDoNotCapture()
    {
        var gesture = new HomeBarCameraGesture();
        gesture.Begin(20, 14, 1000);
        gesture.Cancel();
        Assert.False(gesture.End(20, 14, 2000, insideButton: true));

        gesture.Begin(20, 14, 2000);
        Assert.False(gesture.End(20, 14, 1900, insideButton: true));
    }
}
