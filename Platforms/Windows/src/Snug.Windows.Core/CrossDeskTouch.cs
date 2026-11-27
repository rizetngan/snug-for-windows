// Adapted from nicocart/snug-win v1.5.1 (03c4491), MIT.
// Copyright (c) 2026 AoyamaShou, zephyr4123, nicocat. See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;



namespace Snug.Windows.Core;

public enum CrossDeskPointerEventKind { Move, LeftDown, LeftDrag, LeftUp }
public readonly record struct CrossDeskPointerEvent(CrossDeskPointerEventKind Kind, PointD Location, double Time);
public enum CrossDeskTouchInputKind { Press, Drag, Release, Recognized }
public enum CrossDeskTouchGesture { Slide, Hold }
public readonly record struct CrossDeskTouchInput(CrossDeskTouchInputKind Kind, PointD Location, double Time, CrossDeskTouchGesture? Gesture = null)
{
    public static CrossDeskTouchInput Recognized(CrossDeskTouchGesture gesture, PointD point, double time) =>
        new(CrossDeskTouchInputKind.Recognized, point, time, gesture);
}
public sealed record CrossDeskNormalized(IReadOnlyList<CrossDeskTouchInput> Before, CrossDeskTouchInput? Current);


/// <summary>
/// CrossDesk mouse-event normalizer: converts CrossDesk's absolute-mouse touch stream
/// (move-only slides, move+down+up taps, 0.35s client long-press) into standard finger inputs
/// for <see cref="TouchTranslator"/>, while passing relative-mouse cursor feeds and virtual mouse bar
/// clicks through untouched. Faithful port of nicocart/snug-win v1.5.1 <c>CrossDeskTouch.cs</c>.
/// </summary>
public sealed class CrossDeskTouch
{
    public enum FeedKind
    {
        Unknown,
        Finger,
        Cursor
    }

    public const double ClientHoldDelay = 0.35;
    public const double HoldAfterDown = TouchTranslator.HoldDuration - ClientHoldDelay; // 0.15s
    public const double TouchMoveLead = 0.02;
    public const double SlideEnd = 0.3;
    public const double LandingGap = 0.15;
    public const double LandingJump = 80.0;
    public const double PauseGap = 0.06;
    public const double GestureGap = 0.25;
    public const double VoteWindow = 10.0;
    public const double SamePlace = 0.5;
    public const int CursorVotes = 3;

    private static readonly IReadOnlySet<CrossDeskPointerEventKind> ConsumedKinds = new HashSet<CrossDeskPointerEventKind>
    {
        CrossDeskPointerEventKind.Move,
        CrossDeskPointerEventKind.LeftDown,
        CrossDeskPointerEventKind.LeftDrag,
        CrossDeskPointerEventKind.LeftUp
    };

    private bool _hasPress;
    private bool _pressIsVirtual;
    private CrossDeskPointerEvent _pressDown;
    private bool _pressHeld;

    private CrossDeskPointerEvent? _slide;
    private CrossDeskPointerEvent? _paused;
    private CrossDeskPointerEvent? _candidate;
    private CrossDeskPointerEvent? _last;
    private int _cursorRun;

    public FeedKind Feed { get; private set; } = FeedKind.Unknown;

    public IReadOnlySet<CrossDeskPointerEventKind> Consumes => ConsumedKinds;

    public double? Deadline
    {
        get
        {
            if (_paused is { } p) return p.Time + TouchMoveLead;
            if (_slide is { } s) return s.Time + SlideEnd;
            return null;
        }
    }

    public IReadOnlyList<CrossDeskTouchInput> Tick(double time)
    {
        var inputs = new List<CrossDeskTouchInput>();
        if (_paused is { } p && time >= p.Time + TouchMoveLead)
        {
            _paused = null;
            _slide = p;
            inputs.Add(new CrossDeskTouchInput(CrossDeskTouchInputKind.Drag, p.Location, p.Time));
        }

        if (_slide is { } s && time >= s.Time + SlideEnd)
        {
            _slide = null;
            inputs.Add(new CrossDeskTouchInput(CrossDeskTouchInputKind.Release, s.Location, s.Time));
        }

        return inputs;
    }

    public CrossDeskNormalized Normalize(CrossDeskPointerEvent ev)
    {
        try
        {
            var before = SettlePaused(ev);
            Vote(ev);

            switch (ev.Kind)
            {
                case CrossDeskPointerEventKind.Move:
                    return HandleMove(ev, before);

                case CrossDeskPointerEventKind.LeftDown:
                    if (_slide is { } s)
                    {
                        before.Add(new CrossDeskTouchInput(CrossDeskTouchInputKind.Release, s.Location, ev.Time));
                        _slide = null;
                    }
                    _candidate = null;

                    if (_last is { } l
                        && l.Kind == CrossDeskPointerEventKind.Move
                        && Distance(l.Location, ev.Location) <= SamePlace
                        && ev.Time - l.Time <= TouchMoveLead)
                    {
                        _hasPress = true;
                        _pressIsVirtual = false;
                        _pressDown = ev;
                        _pressHeld = false;
                        return new CrossDeskNormalized(before, new CrossDeskTouchInput(CrossDeskTouchInputKind.Press, ev.Location, ev.Time));
                    }

                    _hasPress = true;
                    _pressIsVirtual = true;
                    return new CrossDeskNormalized(before, null);

                case CrossDeskPointerEventKind.LeftDrag:
                    if (!_hasPress || _pressIsVirtual)
                        return new CrossDeskNormalized(before, null);

                    // Windows SendInput(MOUSEEVENTF_MOVE | LEFTUP) emits a 0-pixel WM_MOUSEMOVE
                    // right before WM_LBUTTONUP; do not treat stationary pointer noise as a drag/Hold.
                    if (!_pressHeld && Distance(ev.Location, _pressDown.Location) <= SamePlace)
                        return new CrossDeskNormalized(before, null);

                    if (!_pressHeld)
                    {
                        _pressHeld = true;
                        before.Add(CrossDeskTouchInput.Recognized(CrossDeskTouchGesture.Hold, ev.Location, ev.Time));
                    }
                    return new CrossDeskNormalized(before, new CrossDeskTouchInput(CrossDeskTouchInputKind.Drag, ev.Location, ev.Time));

                case CrossDeskPointerEventKind.LeftUp:
                    try
                    {
                        if (!_hasPress || _pressIsVirtual)
                            return new CrossDeskNormalized(before, null);

                        if (!_pressHeld && ev.Time - _pressDown.Time >= HoldAfterDown)
                            before.Add(CrossDeskTouchInput.Recognized(CrossDeskTouchGesture.Hold, ev.Location, ev.Time));

                        return new CrossDeskNormalized(before, new CrossDeskTouchInput(CrossDeskTouchInputKind.Release, ev.Location, ev.Time));
                    }
                    finally
                    {
                        _hasPress = false;
                        _pressIsVirtual = false;
                        _pressHeld = false;
                    }

                default:
                    return new CrossDeskNormalized(before, null);
            }
        }
        finally
        {
            _last = ev;
        }
    }

    private CrossDeskNormalized HandleMove(CrossDeskPointerEvent ev, List<CrossDeskTouchInput> before)
    {
        _hasPress = false;
        _pressIsVirtual = false;
        _pressHeld = false;

        if (Feed != FeedKind.Finger)
        {
            _slide = null;
            _candidate = null;
            return new CrossDeskNormalized(before, null);
        }

        if (_slide is { } s)
        {
            if (Landed(ev, s))
            {
                before.Add(new CrossDeskTouchInput(CrossDeskTouchInputKind.Release, s.Location, ev.Time));
                _slide = null;
                _candidate = ev;
                return new CrossDeskNormalized(before, null);
            }

            if (ev.Time - s.Time >= PauseGap)
            {
                _paused = ev;
                return new CrossDeskNormalized(before, new CrossDeskTouchInput(CrossDeskTouchInputKind.Drag, s.Location, ev.Time));
            }

            _slide = ev;
            return new CrossDeskNormalized(before, new CrossDeskTouchInput(CrossDeskTouchInputKind.Drag, ev.Location, ev.Time));
        }

        if (_candidate is { } c && ev.Time - c.Time < SlideEnd && !Landed(ev, c))
        {
            _candidate = null;
            _slide = ev;
            before.Add(new CrossDeskTouchInput(CrossDeskTouchInputKind.Press, c.Location, c.Time));
            before.Add(CrossDeskTouchInput.Recognized(CrossDeskTouchGesture.Slide, c.Location, c.Time));
            return new CrossDeskNormalized(before, new CrossDeskTouchInput(CrossDeskTouchInputKind.Drag, ev.Location, ev.Time));
        }

        _candidate = ev;
        return new CrossDeskNormalized(before, null);
    }

    private List<CrossDeskTouchInput> SettlePaused(CrossDeskPointerEvent ev)
    {
        var list = new List<CrossDeskTouchInput>();
        if (_paused is not { } p)
            return list;

        _paused = null;
        if (ev.Kind == CrossDeskPointerEventKind.LeftDown
            && Distance(p.Location, ev.Location) <= SamePlace
            && ev.Time - p.Time <= TouchMoveLead)
        {
            return list;
        }

        _slide = p;
        list.Add(new CrossDeskTouchInput(CrossDeskTouchInputKind.Drag, p.Location, p.Time));
        return list;
    }

    private void Vote(CrossDeskPointerEvent ev)
    {
        if (ev.Kind != CrossDeskPointerEventKind.Move || _hasPress || _slide is not null || _last is not { } l)
            return;

        double gap = ev.Time - l.Time;
        if (gap < GestureGap || gap > VoteWindow)
            return;

        double d = Distance(l.Location, ev.Location);
        if (d <= SamePlace)
        {
            _cursorRun++;
            if (_cursorRun >= CursorVotes)
                Feed = FeedKind.Cursor;
        }
        else
        {
            _cursorRun = 0;
            if (d >= LandingJump)
                Feed = FeedKind.Finger;
        }
    }

    private static bool Landed(CrossDeskPointerEvent ev, CrossDeskPointerEvent previous) =>
        ev.Time - previous.Time >= LandingGap && Distance(ev.Location, previous.Location) >= LandingJump;

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
}
