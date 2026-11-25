namespace Snug.Windows.Core;

/// <summary>
/// Prevents an incidental touch on the overlapping HomeBar camera hit area
/// from capturing a screenshot. Camera capture requires a deliberate hold.
/// </summary>
public sealed class HomeBarCameraGesture
{
    public const long HoldMilliseconds = 500;
    public const double MovementSlop = 8;

    private bool _active;
    private double _startX;
    private double _startY;
    private long _startedAt;

    public bool IsActive => _active;

    public void Begin(double x, double y, long timestampMilliseconds)
    {
        _active = true;
        _startX = x;
        _startY = y;
        _startedAt = timestampMilliseconds;
    }

    public void Move(double x, double y)
    {
        if (!_active) return;
        var dx = x - _startX;
        var dy = y - _startY;
        if (dx * dx + dy * dy > MovementSlop * MovementSlop)
            _active = false;
    }

    public bool End(double x, double y, long timestampMilliseconds, bool insideButton)
    {
        if (!_active) return false;
        Move(x, y);
        var accepted = _active && insideButton &&
            timestampMilliseconds >= _startedAt &&
            timestampMilliseconds - _startedAt >= HoldMilliseconds;
        Cancel();
        return accepted;
    }

    public void Cancel() => _active = false;
}
