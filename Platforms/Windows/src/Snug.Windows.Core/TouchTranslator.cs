namespace Snug.Windows.Core;

public enum PointerKind { Press, Move, Release }
public readonly record struct PointD(double X, double Y);
public readonly record struct TouchInput(PointerKind Kind, PointD Location, double TimeSeconds);
public enum ScrollPhase { None, Began, Changed, Ended }
public enum MomentumPhase { None, Began, Continued, Ended }

public readonly record struct ScrollStep(
    int PixelsX, int PixelsY, double DeltaX, double DeltaY,
    ScrollPhase Phase, MomentumPhase Momentum, PointD Location);

public enum TouchCommandKind { ReplayPress, ReplayCurrent, RightClick, Scroll }

public readonly record struct TouchCommand(TouchCommandKind Kind, PointD Location, ScrollStep? Scroll = null);
public sealed record TouchResponse(bool PassOriginal, IReadOnlyList<TouchCommand> Commands)
{
    public static TouchResponse Pass { get; } = new(true, Array.Empty<TouchCommand>());
    public static TouchResponse Swallow(params TouchCommand[] commands) => new(false, commands);
}

/// <summary>把 UU 注入的单指鼠标操作翻译成手机习惯的滚动、拖选和右键。</summary>
public sealed class TouchTranslator
{
    public const double Slop = 10;
    public const double HoldDuration = 0.5;
    private const double FlingWindow = 0.1;
    private const double MinFlingSpan = 0.04;
    private const double MaxFlingSpeed = 4000;

    private enum GesturePhase { Idle, Pending, Scrolling, Selecting }
    private GesturePhase _phase;
    private PointD _origin;
    private PointD _last;
    private double _pressedAt;
    private readonly List<(double Time, PointD Point)> _samples = [];
    private readonly ScrollWheel _wheel = new();
    private Momentum? _momentum;
    private bool _pressStoppedCoast;
    private double _gestureScale = 1;

    public bool IsCoasting => _momentum is not null;

    public TouchResponse Handle(TouchInput input, bool insidePhoneScreen, double scale = 1)
    {
        return input.Kind switch
        {
            PointerKind.Press => Press(input, insidePhoneScreen, scale),
            PointerKind.Move => Move(input),
            PointerKind.Release => Release(input),
            _ => TouchResponse.Pass
        };
    }

    // A provider may recognize a slide or hold before the ordinary mouse
    // timing threshold. UU continues to use the unmodified Handle path.
    public TouchResponse RecognizeCrossDesk(CrossDeskTouchGesture gesture, double time)
    {
        if (_phase != GesturePhase.Pending) return TouchResponse.Pass;
        if (gesture == CrossDeskTouchGesture.Hold)
        {
            _pressedAt = Math.Min(_pressedAt, time - HoldDuration - 0.001);
            return TouchResponse.Swallow();
        }
        _phase = GesturePhase.Scrolling;
        var began = _wheel.Step(0, 0, ScrollPhase.Began, MomentumPhase.None, _origin);
        return TouchResponse.Swallow(new TouchCommand(TouchCommandKind.Scroll, _origin, began));
    }

    public ScrollStep? MomentumFrame()
    {
        if (_momentum is null) return null;
        if (_momentum.Next() is { } frame)
        {
            return _wheel.Step(frame.Dx, frame.Dy, ScrollPhase.None, frame.Phase, _origin);
        }
        _momentum = null;
        return _wheel.Step(0, 0, ScrollPhase.None, MomentumPhase.Ended, _origin);
    }

    private TouchResponse Press(TouchInput input, bool inside, double scale)
    {
        var stop = StopMomentum();
        if (!inside)
        {
            _phase = GesturePhase.Idle;
            return new(true, stop);
        }

        _pressStoppedCoast = stop.Count > 0;
        _gestureScale = Math.Max(1, scale);
        _phase = GesturePhase.Pending;
        _pressedAt = input.TimeSeconds;
        _origin = _last = input.Location;
        _samples.Clear();
        _samples.Add((input.TimeSeconds, input.Location));
        _wheel.Reset();
        return new(false, stop);
    }

    private TouchResponse Move(TouchInput input)
    {
        if (_phase == GesturePhase.Idle) return TouchResponse.Pass;
        // Windows replays selection through the same serial lane as the
        // synthetic press. An original move/up must not overtake that press.
        if (_phase == GesturePhase.Selecting)
            return TouchResponse.Swallow(new TouchCommand(TouchCommandKind.ReplayCurrent, input.Location));
        if (_phase == GesturePhase.Pending)
        {
            if (Distance(input.Location, _origin) < Slop * _gestureScale) return TouchResponse.Swallow();
            if (input.TimeSeconds - _pressedAt >= HoldDuration)
            {
                _phase = GesturePhase.Selecting;
                return TouchResponse.Swallow(
                    new TouchCommand(TouchCommandKind.ReplayPress, _origin),
                    new TouchCommand(TouchCommandKind.ReplayCurrent, input.Location));
            }

            _phase = GesturePhase.Scrolling;
            var began = _wheel.Step(0, 0, ScrollPhase.Began, MomentumPhase.None, _origin);
            return TouchResponse.Swallow(
                new TouchCommand(TouchCommandKind.Scroll, _origin, began),
                new TouchCommand(TouchCommandKind.Scroll, _origin, ScrollTo(input)));
        }

        return TouchResponse.Swallow(new TouchCommand(TouchCommandKind.Scroll, _origin, ScrollTo(input)));
    }

    private TouchResponse Release(TouchInput input)
    {
        if (_phase == GesturePhase.Idle) return TouchResponse.Pass;
        if (_phase == GesturePhase.Selecting)
        {
            _phase = GesturePhase.Idle;
            return TouchResponse.Swallow(new TouchCommand(TouchCommandKind.ReplayCurrent, input.Location));
        }

        if (_phase == GesturePhase.Pending)
        {
            _phase = GesturePhase.Idle;
            if (input.TimeSeconds - _pressedAt >= HoldDuration)
                return TouchResponse.Swallow(new TouchCommand(TouchCommandKind.RightClick, _origin));
            return _pressStoppedCoast
                ? TouchResponse.Swallow()
                : TouchResponse.Swallow(
                    new TouchCommand(TouchCommandKind.ReplayPress, _origin),
                    new TouchCommand(TouchCommandKind.ReplayCurrent, input.Location));
        }

        _phase = GesturePhase.Idle;
        var ended = _wheel.Step(0, 0, ScrollPhase.Ended, MomentumPhase.None, _origin);
        _momentum = Momentum.TryCreate(FlingVelocity(input.TimeSeconds), _gestureScale);
        return TouchResponse.Swallow(new TouchCommand(TouchCommandKind.Scroll, _origin, ended));
    }
    private ScrollStep ScrollTo(TouchInput input)
    {
        var dx = input.Location.X - _last.X;
        var dy = input.Location.Y - _last.Y;
        _last = input.Location;
        _samples.Add((input.TimeSeconds, input.Location));
        if (_samples.Count > 30) _samples.RemoveRange(0, _samples.Count - 30);
        return _wheel.Step(dx, dy, ScrollPhase.Changed, MomentumPhase.None, _origin);
    }

    private List<TouchCommand> StopMomentum()
    {
        if (_momentum is null) return [];
        _momentum = null;
        var ended = _wheel.Step(0, 0, ScrollPhase.None, MomentumPhase.Ended, _origin);
        return [new TouchCommand(TouchCommandKind.Scroll, _origin, ended)];
    }

    private (double X, double Y) FlingVelocity(double time)
    {
        var recent = _samples.Where(s => time - s.Time <= FlingWindow).ToList();
        if (recent.Count < 2) return (0, 0);
        var a = recent[0];
        var b = recent[^1];
        if (b.Time <= a.Time) return (0, 0);
        if (b.Time - a.Time < MinFlingSpan)
        {
            var earlier = _samples.LastOrDefault(s => b.Time - s.Time >= MinFlingSpan);
            if (earlier != default) a = earlier;
        }

        var dt = b.Time - a.Time;
        if (dt <= 0) return (0, 0);
        var vx = (b.Point.X - a.Point.X) / dt;
        var vy = (b.Point.Y - a.Point.Y) / dt;
        var speed = Math.Sqrt(vx * vx + vy * vy);
        if (speed <= MaxFlingSpeed * _gestureScale) return (vx, vy);
        return (vx / speed * MaxFlingSpeed * _gestureScale, vy / speed * MaxFlingSpeed * _gestureScale);
    }

    private static double Distance(PointD a, PointD b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed class ScrollWheel
    {
        private double _remainderX;
        private double _remainderY;

        public void Reset()
        {
            _remainderX = 0;
            _remainderY = 0;
        }

        public ScrollStep Step(double dx, double dy, ScrollPhase phase, MomentumPhase momentum, PointD at)
        {
            _remainderX += dx;
            _remainderY += dy;
            var wholeX = (int)Math.Truncate(_remainderX);
            var wholeY = (int)Math.Truncate(_remainderY);
            _remainderX -= wholeX;
            _remainderY -= wholeY;
            return new(wholeX, wholeY, dx, dy, phase, momentum, at);
        }
    }

    private sealed class Momentum
    {
        private const double Decay = 0.95;
        private const double MinSpeed = 200;
        private const double StopSpeed = 40;
        private double _vx;
        private double _vy;
        private bool _started;
        private readonly double _stopSpeed;

        private Momentum(double vx, double vy, double scale) { _vx = vx; _vy = vy; _stopSpeed = StopSpeed * scale; }

        public static Momentum? TryCreate((double X, double Y) velocity, double scale)
        {
            return Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y) > MinSpeed * scale
                ? new Momentum(velocity.X, velocity.Y, scale)
                : null;
        }

        public (double Dx, double Dy, MomentumPhase Phase)? Next()
        {
            _vx *= Decay;
            _vy *= Decay;
            if (Math.Sqrt(_vx * _vx + _vy * _vy) < _stopSpeed) return null;
            var phase = _started ? MomentumPhase.Continued : MomentumPhase.Began;
            _started = true;
            return (_vx / 60, _vy / 60, phase);
        }
    }
}
