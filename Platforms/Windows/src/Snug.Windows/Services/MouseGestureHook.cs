using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Snug.Windows.Core;
using Snug.Windows.Interop;

namespace Snug.Windows.Services;

/// <summary>只接管 UU 注入且发生在手机屏区域内的左键操作。</summary>
public sealed class MouseGestureHook : IDisposable
{
    private static readonly UIntPtr InjectionMarker = new(0x534E5547);
    private static readonly UIntPtr CrossDeskMarker = new(0x4353444D); // CrossDesk kInjectedMouseInputMarker (CSDM)
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly Dispatcher _uiDispatcher;
    private MouseReplayLane? _replay;
    private void UpdateRegion(GestureRegion? next)
    {
        if (Equals(_region, next)) return;
        void Apply()
        {
            if (Equals(_region, next)) return;
            _replay?.Invalidate();
            _translator = new TouchTranslator();
            _crossDeskInput = new CrossDeskInputPipeline();
            _crossDeskLeftDown = false;
            _coastTimer?.Stop();
            _closeTap.Reset();
            _passthroughBrowserChrome = false;
            _region = next;
        }
        // Region changes and gesture production are serialized on the hook
        // thread, so an obsolete gesture cannot enter a new replay generation.
        if (_inputDispatcher is { } dispatcher && _running)
            dispatcher.Invoke(Apply, DispatcherPriority.Send);
        else Apply();
    }
    // Install the hook on its own message pump, as upstream does.
    private Dispatcher? _inputDispatcher;
    private Thread? _inputThread;
    private DispatcherTimer? _coastTimer;
    private volatile bool _running;
    private sealed record GestureRegion(WindowBounds Bounds, double Scale, bool CrossDesk);
    private volatile GestureRegion? _region;
    private TouchTranslator _translator = new();
    private CrossDeskInputPipeline _crossDeskInput = new();
    private bool _crossDeskLeftDown;
    private IntPtr _hook;
    private bool _passthroughBrowserChrome;
    private bool _disposed;
    private readonly CloseTapGuard _closeTap = new();
    public Func<WindowKey?>? PlacedWindowKey { get; set; }
    public event Action<CloseButtonHit>? NativeCloseTapped;

    public MouseGestureHook()
    {
        _callback = OnMouse;
        _uiDispatcher = Dispatcher.CurrentDispatcher;
    }

    public bool IsRunning => _running;

    public void SetRegion(DisplayInfo? display, double scale = 1, bool crossDesk = false)
    {
        if (display is null || !PhoneDisplayModeSwitcher.IsSnugDisplay(display))
        {
            UpdateRegion(null);
            return;
        }
        var mode = new NativeMethods.DEVMODE
        {
            Size = checked((ushort)Marshal.SizeOf<NativeMethods.DEVMODE>()),
            DeviceName = string.Empty,
            FormName = string.Empty
        };
        // EnumDisplaySettings returns raw physical dimensions and position.
        // Never compare hook pixels with the DPI-virtualized Screen.Bounds.
        if (!NativeMethods.EnumDisplaySettings(display.DeviceName, -1, ref mode) ||
            mode.PelsWidth == 0 || mode.PelsHeight == 0)
        {
            UpdateRegion(null);
            return;
        }
        UpdateRegion(new GestureRegion(
            new WindowBounds(mode.PositionX, mode.PositionY,
                checked((int)mode.PelsWidth), checked((int)mode.PelsHeight)),
            Math.Max(1, scale) *
                Math.Max(1, (double)mode.PelsWidth / Math.Max(1, display.Bounds.Width)), crossDesk));
    }

    private Dispatcher InputDispatcher()
    {
        if (_inputDispatcher is { } existing) return existing;
        var ready = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _inputThread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                _coastTimer = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(1000.0 / 60)
                };
                _coastTimer.Tick += OnCoastTick;
                ready.TrySetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception error)
            {
                if (!ready.TrySetException(error)) throw;
            }
        })
        {
            IsBackground = true,
            Name = "Snug.Windows.Input",
            Priority = ThreadPriority.AboveNormal
        };
        _inputThread.SetApartmentState(ApartmentState.STA);
        _inputThread.Start();
        return _inputDispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public bool Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MouseGestureHook));
        if (IsRunning) return true;
        _replay ??= new MouseReplayLane(InjectionMarker);
        return InputDispatcher().Invoke(() =>
        {
            if (_running) return true;
            using var process = Process.GetCurrentProcess();
            var module = process.MainModule?.ModuleName;
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _callback,
                NativeMethods.GetModuleHandle(module), 0);
            _running = _hook != IntPtr.Zero;
            return _running;
        }, DispatcherPriority.Send);
    }

    public void Stop()
    {
        if (_inputDispatcher is not { } dispatcher || !_running) return;
        dispatcher.Invoke(StopOnInputThread, DispatcherPriority.Send);
    }

    private void StopOnInputThread()
    {
        _coastTimer?.Stop();
        _passthroughBrowserChrome = false;
        _closeTap.Reset();
        _translator = new TouchTranslator();
        _crossDeskInput = new CrossDeskInputPipeline();
        _crossDeskLeftDown = false;
        _replay?.Invalidate();
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _running = false;
    }

    private void OnCoastTick(object? sender, EventArgs e)
    {
        using var dpi = new PhysicalDpiScope();
        if (_region is { CrossDesk: true } region)
        {
            if (dpi.IsValid)
            {
                var now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                var result = _crossDeskInput.Tick(now, point => AcceptCrossDeskPoint(region, point), region.Scale);
                foreach (var batch in result.Batches) DispatchCommands(batch.Commands, batch.Kind);
            }
            if (!_crossDeskInput.NeedsTick) _coastTimer?.Stop();
            return;
        }
        if (dpi.IsValid && _translator.MomentumFrame() is { } step) PostScroll(step);
        if (!_translator.IsCoasting) _coastTimer?.Stop();
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _region is not { } snapshot) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        using var dpi = new PhysicalDpiScope();
        if (!dpi.IsValid) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);

        var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        if ((info.Flags & NativeMethods.LLMHF_INJECTED) == 0
            || info.ExtraInfo == InjectionMarker)
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);

        var message = wParam.ToInt32();
        var kind = message switch
        {
            NativeMethods.WM_LBUTTONDOWN => PointerKind.Press,
            NativeMethods.WM_MOUSEMOVE => PointerKind.Move,
            NativeMethods.WM_LBUTTONUP => PointerKind.Release,
            _ => (PointerKind?)null
        };
        if (kind is null) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);

        var point = new PointD(info.Point.X, info.Point.Y);
        var region = snapshot.Bounds;
        var inside = point.X >= region.X && point.X < region.X + region.Width
            && point.Y >= region.Y && point.Y < region.Y + region.Height;
        if (snapshot.CrossDesk != (info.ExtraInfo == CrossDeskMarker))
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        if (snapshot.CrossDesk)
        {
            var raw = kind.Value switch
            {
                PointerKind.Press => CrossDeskPointerEventKind.LeftDown,
                PointerKind.Release => CrossDeskPointerEventKind.LeftUp,
                _ => _crossDeskLeftDown ? CrossDeskPointerEventKind.LeftDrag : CrossDeskPointerEventKind.Move
            };
            if (kind == PointerKind.Press) _crossDeskLeftDown = true;
            if (kind == PointerKind.Release) _crossDeskLeftDown = false;
            var time = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            var result = _crossDeskInput.Take(new(raw, point, time),
                p => AcceptCrossDeskPoint(snapshot, p), snapshot.Scale);
            foreach (var batch in result.Batches) DispatchCommands(batch.Commands, batch.Kind);
            if (_crossDeskInput.NeedsTick && _coastTimer is { IsEnabled: false } cdTimer) cdTimer.Start();
            return result.PassOriginal ? NativeMethods.CallNextHookEx(_hook, code, wParam, lParam) : new IntPtr(1);
        }

        // Native caption close is distinct from browser tabs, including tab
        // close icons. Check only the actual placed foreground HWND.
        var seconds = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        if (kind == PointerKind.Press)
        {
            _closeTap.Reset();
            if (inside && PlacedWindowKey?.Invoke() is { } key &&
                NativeCloseHitTest.Capture(key, point) is { } close)
            {
                _passthroughBrowserChrome = false;
                var stop = _translator.Handle(new TouchInput(kind.Value, point, seconds), false);
                DispatchCommands(stop.Commands, kind.Value);
                _closeTap.Begin(close, seconds, snapshot.Scale);
                return new IntPtr(1);
            }
        }
        if (_closeTap.Tracking)
        {
            if (kind == PointerKind.Move) _closeTap.Move(point);
            else if (kind == PointerKind.Release)
            {
                var current = PlacedWindowKey?.Invoke() is { } key
                    ? NativeCloseHitTest.Capture(key, point) : null;
                if (_closeTap.Release(current, point, seconds) is { } tap)
                    _uiDispatcher.BeginInvoke(new Action(() => NativeCloseTapped?.Invoke(tap)),
                        DispatcherPriority.Input);
            }
            // Never replay a stale/dragged/long-held close tap onto another app.
            return new IntPtr(1);
        }

        // Native browser tabs and address controls must receive the original
        // UU press/release. Replay inside WH_MOUSE_LL loses Chrome tab clicks.
        if (kind == PointerKind.Press)
        {
            _passthroughBrowserChrome = inside && IsNativeControl(point, snapshot.Scale);
            if (_passthroughBrowserChrome)
            {
                var stop = _translator.Handle(new TouchInput(kind.Value, point, seconds), false);
                DispatchCommands(stop.Commands, kind.Value);
            }
        }
        if (_passthroughBrowserChrome)
        {
            if (kind == PointerKind.Release) _passthroughBrowserChrome = false;
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }
        var response = _translator.Handle(new TouchInput(kind.Value, point, seconds), inside, snapshot.Scale);

        DispatchCommands(response.Commands, kind.Value);
        if (_translator.IsCoasting && _coastTimer is { IsEnabled: false } timer) timer.Start();

        return response.PassOriginal
            ? NativeMethods.CallNextHookEx(_hook, code, wParam, lParam)
            : new IntPtr(1);
    }

    private static bool AcceptCrossDeskPoint(GestureRegion region, PointD point)
    {
        var bounds = region.Bounds;
        return point.X >= bounds.X && point.X < bounds.X + bounds.Width &&
            point.Y >= bounds.Y && point.Y < bounds.Y + bounds.Height &&
            !IsNativeControl(point, region.Scale);
    }

    private static bool IsNativeControl(PointD point, double scale)
    {
        var hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT
        {
            X = (int)point.X, Y = (int)point.Y
        });
        if (hit == IntPtr.Zero) return false;
        var window = NativeMethods.GetAncestor(hit, 2); // GA_ROOT
        if (window == IntPtr.Zero || !NativeMethods.GetWindowRect(window, out var rect))
            return false;

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        // Snug's own controls already handle their native mouse input.
        if (processId == Environment.ProcessId) return true;
        // Skip process-name lookup on ordinary content taps.
        if (!BrowserChromeHitTest.IsHeader(
            new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
            point, scale)) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return BrowserChromeHitTest.ShouldPassThrough(
                process.ProcessName,
                new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                point, scale);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
    private void DispatchCommands(IReadOnlyList<TouchCommand> commands, PointerKind currentKind)
    {
        List<MouseReplayAction>? replay = null;
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case TouchCommandKind.ReplayPress:
                    (replay ??= []).Add(new(MouseReplayKind.LeftDown, command.Location));
                    break;
                case TouchCommandKind.ReplayCurrent:
                    (replay ??= []).Add(new(currentKind == PointerKind.Release
                        ? MouseReplayKind.LeftUp : MouseReplayKind.Move, command.Location));
                    break;
                case TouchCommandKind.RightClick:
                    (replay ??= []).Add(new(MouseReplayKind.RightDown, command.Location));
                    replay.Add(new(MouseReplayKind.RightUp, command.Location));
                    break;
                case TouchCommandKind.Scroll:
                    if (command.Scroll is { } step) PostScroll(step);
                    break;
            }
        }
        // Never call SetCursorPos, mouse_event or SendInput from WH_MOUSE_LL.
        // Windows waits for its hook chain before delivering injected input.
        // An independent serial lane can inject while this hook keeps pumping.
        if (replay is not null) _replay?.Enqueue(replay.ToArray());
    }

    private static void PostScroll(ScrollStep step)
    {
        var delta = WheelDeltas.From(step);
        if (delta.Horizontal == 0 && delta.Vertical == 0) return;
        var point = new NativeMethods.POINT { X = (int)step.Location.X, Y = (int)step.Location.Y };
        var target = NativeMethods.WindowFromPoint(point);
        if (target == IntPtr.Zero) return;

        var packedPoint = ((long)(point.Y & 0xFFFF) << 16) | (uint)(point.X & 0xFFFF);
        if (delta.Vertical != 0)
            NativeMethods.PostMessage(target, NativeMethods.WM_MOUSEWHEEL,
                new IntPtr((long)(ushort)delta.Vertical << 16), new IntPtr(packedPoint));
        if (delta.Horizontal != 0)
            NativeMethods.PostMessage(target, NativeMethods.WM_MOUSEHWHEEL,
                new IntPtr((long)(ushort)delta.Horizontal << 16), new IntPtr(packedPoint));
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        if (_inputDispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() =>
            {
                if (_coastTimer is { } timer) timer.Tick -= OnCoastTick;
                _coastTimer = null;
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }, DispatcherPriority.Send);
            _inputThread?.Join(TimeSpan.FromSeconds(2));
        }
        _replay?.Dispose();
        _replay = null;
        _disposed = true;
    }
}
