using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Snug.Windows.Core;
using Snug.Windows.Interop;
using Snug.Windows.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Snug.Windows;

/// <summary>
/// 手机屏左侧通用缩放轨道。浏览器使用原生倍率档位，支持的桌面应用使用相对缩放级别。
/// </summary>
public partial class ZoomRailWindow : Window
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    private enum ZoomMode { Browser, Application }

    private readonly AppController _controller;
    private readonly DispatcherTimer _autoHide;
    private DisplayInfo? _targetDisplay;
    private DateTimeOffset _lastInteraction;
    private ZoomMode _mode = ZoomMode.Browser;
    private int _minimum = 0;
    private int _maximum = BrowserZoomLevels.Percents.Length - 1;
    private int _selectedValue = BrowserZoomLevels.DefaultIndex;
    private ZoomTarget? _displayedTarget;
    private ZoomTarget? _dragTarget;
    private bool _commitPending;
    private bool _expanded;
    private bool _dragging;
    private bool _lastButtonDown;
    private double? _dragProgress;
    private double? _swipeStartX;
    private TouchDevice? _capturedTouchDevice;

    public ZoomRailWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        RefreshText();
        UpdateTrackVisual();
        _autoHide = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(90) };
        _autoHide.Tick += AutoHideTick;
        _autoHide.Start();
        Loaded += (_, _) =>
        {
            ApplyPlacement();
            UpdateTrackVisual();
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Collapse(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    public void PlaceOn(DisplayInfo display)
    {
        _targetDisplay = display;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (IsLoaded) ApplyPlacement();
    }

    public void SetBrowserState(WindowKey window, DateTimeOffset session, BrowserZoomObservation? observation)
    {
        var target = observation is null ? null :
            new ZoomTarget(window, session, observation.TabIdentity, true);
        if (_dragging && _dragTarget?.SameScope(target) != true) CancelDrag();
        _mode = ZoomMode.Browser;
        _minimum = 0;
        _maximum = BrowserZoomLevels.Percents.Length - 1;
        _displayedTarget = target;
        if (!_dragging) ResetTrackFromObservation();
    }

    public void SetAppRelative(WindowKey window, DateTimeOffset session)
    {
        var target = new ZoomTarget(window, session, null, false);
        if (_dragging && _dragTarget?.SameScope(target) != true) CancelDrag();
        _mode = ZoomMode.Application;
        _minimum = ZoomBook.MinimumSteps;
        _maximum = -ZoomBook.MinimumSteps; // Relative gestures have a neutral centre.
        _displayedTarget = target;
        if (!_dragging) ResetTrackFromObservation();
    }

    private void ResetTrackFromObservation()
    {
        _selectedValue = _mode == ZoomMode.Browser ? BrowserZoomLevels.DefaultIndex : 0;
        RefreshText();
        UpdateTrackVisual();
    }

    private static string RelativeLabel(int delta) => delta == 0 ? "缩放" : $"{delta:+#;-#;0} 级";

    private void RefreshText()
    {
        if (_mode == ZoomMode.Browser)
        {
            ExpandButton.ToolTip = "相对调整当前标签页缩放";
            LargeValue.Text = _dragging
                ? RelativeLabel(BrowserZoomLevels.StepsFromDefault(_selectedValue))
                : "缩放";
            CompactValue.Text = "缩放";
            ResetLabel.Text = "默认";
            ResetButton.Visibility = Visibility.Visible;
            ResetButton.IsEnabled = _displayedTarget is not null && !_commitPending;
            ResetButton.ToolTip = "恢复浏览器默认缩放";
            return;
        }
        ExpandButton.ToolTip = "相对调整当前应用缩放";
        LargeValue.Text = _dragging ? RelativeLabel(_selectedValue) : "缩放";
        CompactValue.Text = "缩放";
        ResetButton.Visibility = Visibility.Collapsed;
    }

    private bool BeginGesture()
    {
        if (_commitPending || _controller.GetCurrentZoomTarget() is not { } target ||
            _displayedTarget?.SameScope(target) != true) return false;
        _dragTarget = target;
        _dragging = true;
        return true;
    }

    private void Touch() => _lastInteraction = DateTimeOffset.UtcNow;

    private void Expand()
    {
        if (_expanded) { Collapse(); return; }
        _expanded = true;
        _controller.RefreshBrowserZoom(force: true);
        CompactBody.Visibility = Visibility.Collapsed;
        ExpandedBody.Visibility = Visibility.Visible;
        _lastButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        Touch();
        ApplyPlacement();
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        CancelDrag();
        _swipeStartX = null;
        ExpandedBody.Visibility = Visibility.Collapsed;
        CompactBody.Visibility = Visibility.Visible;
        ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        if (_targetDisplay is not { } display) return;

        const double compactWidthDip = 46;
        const double expandedWidthDip = 82;
        const double compactHeightDip = 86;
        var scaleX = Math.Max(1, display.DpiScaleX);
        var scaleY = Math.Max(1, display.DpiScaleY);
        var topGuard = display.Bounds.Y + (int)Math.Round(24 * scaleY);
        var bottomGuard = display.Bounds.Y + display.Bounds.Height
            - UuMobileOverlaySafeArea.ReservedBottom(display.Bounds.Width)
            - (int)Math.Round(24 * scaleY);
        var usableHeight = Math.Max(1, bottomGuard - topGuard);
        var height = _expanded
            ? Math.Clamp((int)Math.Round(usableHeight * .64),
                Math.Min((int)Math.Round(420 * scaleY), usableHeight), usableHeight)
            : Math.Min((int)Math.Round(compactHeightDip * scaleY), usableHeight);
        var widthDip = _expanded ? expandedWidthDip : compactWidthDip;
        var width = Math.Max(1, (int)Math.Round(widthDip * scaleX));
        var y = topGuard + Math.Max(0, (usableHeight - height) / 2);
        var x = display.Bounds.X + (int)Math.Round(8 * scaleX);

        Width = widthDip;
        Height = height / scaleY;
        if (!IsLoaded)
        {
            Left = x / scaleX;
            Top = y / scaleY;
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            NativeMethods.SetPhysicalWindowPos(hwnd, new IntPtr(-1), x, y, width, height,
                NativeMethods.SWP_NOACTIVATE |
                (IsVisible ? NativeMethods.SWP_SHOWWINDOW : 0));
    }

    private void UpdateFromTrack(Point point)
    {
        const double thumbInset = 16;
        var trackHeight = TrackHitArea.ActualHeight - thumbInset * 2;
        if (trackHeight <= 0) return;

        _dragProgress = Math.Clamp(
            (point.Y - thumbInset) / trackHeight, 0, 1);
        _selectedValue = ZoomRailScale.ValueAt(
            point.Y, thumbInset, trackHeight, _minimum, _maximum);
        RefreshText();
        UpdateTrackVisual();
        Touch();
    }

    private async void CommitSelected(ZoomTarget? target, int value, bool reset = false)
    {
        if (_targetDisplay is null || !IsVisible || _commitPending || target is null)
        {
            ResetTrackFromObservation();
            return;
        }
        _commitPending = true;
        RefreshText();
        try
        {
            await _controller.ApplyZoomGestureAsync(target, value, reset);
        }
        finally
        {
            _commitPending = false;
            if (!_dragging) ResetTrackFromObservation();
            Touch();
        }
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        var target = _dragTarget;
        var value = _selectedValue;
        CancelDrag();
        CommitSelected(target, value);
    }

    private void CancelDrag()
    {
        _dragging = false;
        _dragTarget = null;
        _dragProgress = null;
        if (Mouse.Captured == TrackInputSurface) Mouse.Capture(null);
        _capturedTouchDevice?.Capture(null);
        _capturedTouchDevice = null;
        ResetTrackFromObservation();
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => Expand();
    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_mode != ZoomMode.Browser || _commitPending) return;
        CommitSelected(_controller.GetCurrentZoomTarget(), BrowserZoomLevels.DefaultIndex, reset: true);
    }

    private void TrackHitArea_SizeChanged(
        object sender, SizeChangedEventArgs e) => UpdateTrackVisual();

    private void UpdateTrackVisual()
    {
        if (TrackCanvas is null || TrackCanvas.ActualWidth <= 0
            || TrackCanvas.ActualHeight <= 0) return;

        const double inset = 16;
        const double thumbHeight = 28;
        var trackHeight = Math.Max(1, TrackCanvas.ActualHeight - inset * 2);
        var progress = _dragging && _dragProgress.HasValue
            ? _dragProgress.Value
            : _maximum == _minimum
                ? 0
                : (_maximum - _selectedValue) / (double)(_maximum - _minimum);
        var centerY = inset + progress * trackHeight;
        var trackLeft = (TrackCanvas.ActualWidth - TrackBase.Width) / 2;

        TrackBase.Height = trackHeight;
        TrackFill.Height = progress * trackHeight;
        Canvas.SetLeft(TrackBase, trackLeft);
        Canvas.SetTop(TrackBase, inset);
        Canvas.SetLeft(TrackFill, trackLeft);
        Canvas.SetTop(TrackFill, inset);
        Canvas.SetLeft(TrackThumb, (TrackCanvas.ActualWidth - TrackThumb.Width) / 2);
        Canvas.SetTop(TrackThumb,
            Math.Clamp(centerY - thumbHeight / 2, 0, TrackCanvas.ActualHeight - thumbHeight));
    }

    // 输入事件直接绑定到覆盖整个轨道的透明层，避免只有视觉滑块接收命中。
    private void TrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_expanded) return;

        if (!BeginGesture()) return;
        _swipeStartX = e.GetPosition(this).X;
        if (!Mouse.Capture(TrackInputSurface, CaptureMode.SubTree))
        {
            CancelDrag();
            return;
        }

        UpdateFromTrack(e.GetPosition(TrackHitArea));
        e.Handled = true;
    }

    private void TrackMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateFromTrack(e.GetPosition(TrackHitArea));
        e.Handled = true;
    }

    private void TrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_dragging)
        {
            UpdateFromTrack(e.GetPosition(TrackHitArea));
            FinishDrag();
            e.Handled = true;
        }
        CheckSwipe(e.GetPosition(this).X);
    }

    private void TrackTouchDown(object sender, TouchEventArgs e)
    {
        if (!_expanded) return;

        if (!BeginGesture()) return;
        _swipeStartX = e.GetTouchPoint(this).Position.X;
        _capturedTouchDevice = e.TouchDevice;
        if (!e.TouchDevice.Capture(TrackInputSurface))
        {
            CancelDrag();
            return;
        }
        UpdateFromTrack(e.GetTouchPoint(TrackHitArea).Position);
        e.Handled = true;
    }

    private void TrackTouchMove(object sender, TouchEventArgs e)
    {
        if (!_dragging || _capturedTouchDevice != e.TouchDevice) return;
        UpdateFromTrack(e.GetTouchPoint(TrackHitArea).Position);
        e.Handled = true;
    }

    private void TrackTouchUp(object sender, TouchEventArgs e)
    {
        if (_dragging && _capturedTouchDevice == e.TouchDevice)
        {
            UpdateFromTrack(e.GetTouchPoint(TrackHitArea).Position);
            FinishDrag();
            e.Handled = true;
        }
        CheckSwipe(e.GetTouchPoint(this).Position.X);
    }

    private void CheckSwipe(double endX)
    {
        if (_expanded && _swipeStartX is { } start && start - endX >= 24)
            Collapse();
        _swipeStartX = null;
    }

    private void AutoHideTick(object? sender, EventArgs e)
    {
        if (!_expanded || !IsVisible) return;
        var down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        if (!_dragging && down && !_lastButtonDown && ClickedOutside())
        {
            Collapse();
        }
        else if (!_dragging
            && DateTimeOffset.UtcNow - _lastInteraction >= TimeSpan.FromSeconds(2))
        {
            Collapse();
        }
        _lastButtonDown = down;
    }

    private bool ClickedOutside()
    {
        using var dpi = new PhysicalDpiScope();
        if (!dpi.IsValid) return false;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetCursorPos(out var cursor)
            || !NativeMethods.GetPhysicalWindowRect(hwnd, out var rect)) return false;
        return cursor.X < rect.Left || cursor.X >= rect.Right
            || cursor.Y < rect.Top || cursor.Y >= rect.Bottom;
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoHide.Stop();
        CancelDrag();
        base.OnClosed(e);
    }
}
