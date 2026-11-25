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
    private int _committedValue = BrowserZoomLevels.DefaultIndex;
    private bool _expanded;
    private bool _dragging;
    private bool _lastButtonDown;
    private bool _known;
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

    public void SetBrowserLevel(int? level)
    {
        _mode = ZoomMode.Browser;
        _minimum = 0;
        _maximum = BrowserZoomLevels.Percents.Length - 1;
        _known = level.HasValue;
        _selectedValue = BrowserZoomLevels.ClampIndex(
            level ?? BrowserZoomLevels.DefaultIndex);
        _committedValue = _selectedValue;
        ApplyTrackState();
    }

    public void SetAppLevel(int level)
    {
        _mode = ZoomMode.Application;
        _minimum = ZoomBook.MinimumSteps;
        _maximum = ZoomBook.MaximumSteps;
        _known = true;
        _selectedValue = Math.Clamp(level, _minimum, _maximum);
        _committedValue = _selectedValue;
        ApplyTrackState();
    }

    private void ApplyTrackState()
    {
        RefreshText();
        UpdateTrackVisual();
    }

    private void RefreshText()
    {
        if (_mode == ZoomMode.Browser)
        {
            var percent = BrowserZoomLevels.PercentAt(_selectedValue);
            LargeValue.Text = $"{percent}%";
            CompactValue.Text = _known ? $"{percent}%" : "缩放";
            ResetLabel.Text = "100%";
            ResetButton.ToolTip = "重置到 100%";
            return;
        }

        var label = _selectedValue == 0
            ? "默认"
            : $"{_selectedValue:+#;-#;0} 级";
        LargeValue.Text = label;
        CompactValue.Text = _selectedValue == 0
            ? "缩放"
            : $"{_selectedValue:+#;-#;0}";
        ResetLabel.Text = "默认";
        ResetButton.ToolTip = "重置应用缩放级别";
    }

    private void Touch() => _lastInteraction = DateTimeOffset.UtcNow;

    private void Expand()
    {
        if (_expanded) { Collapse(); return; }
        _expanded = true;
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

    private bool TryGetTrackPoint(Point point) =>
        _expanded && TrackHitArea.IsVisible
        && point.X >= 0 && point.X <= TrackHitArea.ActualWidth
        && point.Y >= 0 && point.Y <= TrackHitArea.ActualHeight;

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

    private void CommitSelected()
    {
        if (_targetDisplay is null || !IsVisible) return;
        var applied = _mode == ZoomMode.Browser
            ? _controller.SetCurrentBrowserZoomIndex(_selectedValue)
            : _controller.SetCurrentAppZoomSteps(_selectedValue);
        if (applied)
        {
            _committedValue = _selectedValue;
        }
        else
        {
            _selectedValue = _committedValue;
            RefreshText();
            UpdateTrackVisual();
        }
        Touch();
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragProgress = null;
        UpdateTrackVisual();
        if (Mouse.Captured == TrackHitArea) Mouse.Capture(null);
        _capturedTouchDevice?.Capture(null);
        _capturedTouchDevice = null;
        CommitSelected();
    }

    private void CancelDrag()
    {
        _dragging = false;
        _dragProgress = null;
        if (Mouse.Captured == TrackHitArea) Mouse.Capture(null);
        _capturedTouchDevice?.Capture(null);
        _capturedTouchDevice = null;
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => Expand();
    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _selectedValue = _mode == ZoomMode.Browser
            ? BrowserZoomLevels.DefaultIndex
            : 0;
        RefreshText();
        UpdateTrackVisual();
        CommitSelected();
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

    private void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _swipeStartX = e.GetPosition(this).X;
        if (!TryGetTrackPoint(e.GetPosition(TrackHitArea))) return;

        _dragging = true;
        TrackHitArea.CaptureMouse();
        UpdateFromTrack(e.GetPosition(TrackHitArea));
        e.Handled = true;
    }

    private void WindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateFromTrack(e.GetPosition(TrackHitArea));
        e.Handled = true;
    }

    private void WindowMouseUp(object sender, MouseButtonEventArgs e)
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

    private void WindowTouchDown(object sender, TouchEventArgs e)
    {
        _swipeStartX = e.GetTouchPoint(this).Position.X;
        var point = e.GetTouchPoint(TrackHitArea).Position;
        if (!TryGetTrackPoint(point)) return;

        _dragging = true;
        _capturedTouchDevice = e.TouchDevice;
        e.TouchDevice.Capture(TrackHitArea);
        UpdateFromTrack(point);
        e.Handled = true;
    }

    private void WindowTouchMove(object sender, TouchEventArgs e)
    {
        if (!_dragging || _capturedTouchDevice != e.TouchDevice) return;
        UpdateFromTrack(e.GetTouchPoint(TrackHitArea).Position);
        e.Handled = true;
    }

    private void WindowTouchUp(object sender, TouchEventArgs e)
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
