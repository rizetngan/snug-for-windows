using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Snug.Windows.Core;
using Snug.Windows.Interop;
using Snug.Windows.Services;

namespace Snug.Windows;

public partial class HomeBarWindow : Window
{
    private readonly AppController _controller;
    private readonly HomeBarCameraGesture _cameraGesture = new();
    private DisplayInfo? _targetDisplay;
    private bool _cameraLongPressAccepted;
    private bool _cameraKeyboardArmed;
    private bool _placementQueued;

    public HomeBarWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Opacity = 0;
        CameraButton.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) _cameraKeyboardArmed = true;
        };
        CameraButton.PreviewKeyUp += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                _cameraLongPressAccepted = _cameraKeyboardArmed;
                _cameraKeyboardArmed = false;
            }
        };
        Loaded += (_, _) => ApplyPlacement();
    }

    public void PlaceOn(DisplayInfo display)
    {
        _targetDisplay = display;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (!IsLoaded) { Left = 0; Top = 0; }
        Width = display.Bounds.Width / display.DpiScaleX;
        var barHeight = ScreenFit.HomeBarHeight(display.Bounds.Width);
        var safeFooter = UuMobileOverlaySafeArea.BottomInset(display.Bounds.Width);
        Height = (barHeight + safeFooter) / display.DpiScaleY;
        NavigationRow.Height = new GridLength(barHeight, GridUnitType.Star);
        SafeFooterRow.Height = new GridLength(safeFooter, GridUnitType.Star);
        CameraButton.Width = 54 * Math.Max(1.0, display.Bounds.Width / (double)ScreenFit.DesignWidth) / display.DpiScaleX;
        CameraGlyph.Width = CameraGlyph.Height = 20 * Math.Max(1.0, display.Bounds.Width / (double)ScreenFit.DesignWidth) / display.DpiScaleX;
        if (IsLoaded) ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        if (_targetDisplay is { } display)
        {
            var barHeight = ScreenFit.HomeBarHeight(display.Bounds.Width);
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            // The secondary Windows taskbar is itself topmost. The upstream
            // PhonePanel lives above the menu bar; do the same on Windows.
            // NOZORDER was keeping HomeBar *behind* Shell_SecondaryTrayWnd.
            var placed = NativeMethods.SetPhysicalWindowPos(handle, new IntPtr(-1),
                display.Bounds.X,
                display.Bounds.Y + display.Bounds.Height - barHeight
                    - UuMobileOverlaySafeArea.BottomInset(display.Bounds.Width),
                display.Bounds.Width,
                UuMobileOverlaySafeArea.ReservedBottom(display.Bounds.Width),
                NativeMethods.SWP_NOACTIVATE | (IsVisible ? NativeMethods.SWP_SHOWWINDOW : 0));
            if (!placed)
                Top = (display.Bounds.Y + display.Bounds.Height - barHeight
                    - UuMobileOverlaySafeArea.BottomInset(display.Bounds.Width)) / display.DpiScaleY;
        }

        // Moving a WPF HWND across DPI boundaries can enqueue a second size
        // change after SetWindowPos. Reconcile once after layout, using the
        // latest target (never an obsolete monitor/mode).
        if (!_placementQueued)
        {
            _placementQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                _placementQueued = false;
                if (!IsVisible || _targetDisplay is not { } target) return;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var height = UuMobileOverlaySafeArea.ReservedBottom(target.Bounds.Width);
                var y = target.Bounds.Y + target.Bounds.Height - height;
                if (!NativeMethods.GetPhysicalWindowRect(hwnd, out var rect)) return;
                if (rect.Left == target.Bounds.X && rect.Top == y &&
                    rect.Right - rect.Left == target.Bounds.Width && rect.Bottom - rect.Top == height) return;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                Width = target.Bounds.Width / dpi.DpiScaleX;
                Height = height / dpi.DpiScaleY;
                NativeMethods.SetPhysicalWindowPos(hwnd, new IntPtr(-1), target.Bounds.X, y,
                    target.Bounds.Width, height, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }));
        }
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    /// <summary>Repairs the topmost order only if Windows raised the secondary taskbar above us.</summary>
    public void EnsureAboveTaskbar()
    {
        if (!IsLoaded || !IsVisible || _targetDisplay is null) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetPhysicalWindowRect(handle, out var bar)) return;

        // Iterate only the windows above this bar, not every desktop window.
        // Never raise over another application dialog or affect the primary taskbar.
        var node = NativeMethods.GetWindow(handle, 3); // GW_HWNDPREV
        for (var inspected = 0; inspected < 512 && node != IntPtr.Zero; inspected++)
        {
            if (NativeMethods.IsWindowVisible(node))
            {
                var className = new StringBuilder(64);
                NativeMethods.GetClassName(node, className, className.Capacity);
                if (className.ToString() == "Shell_SecondaryTrayWnd"
                    && NativeMethods.GetPhysicalWindowRect(node, out var tray)
                    && tray.Right > bar.Left && tray.Left < bar.Right
                    && tray.Bottom > bar.Top && tray.Top < bar.Bottom)
                {
                    const uint noMove = 0x0002;
                    const uint noSize = 0x0001;
                    NativeMethods.SetPhysicalWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0,
                        noMove | noSize | NativeMethods.SWP_NOACTIVATE);
                    return;
                }
            }
            node = NativeMethods.GetWindow(node, 3);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    // The original HomeBarView handles a short upward swipe as an alternative
    // to tapping the chevron. Windows UU may forward either mouse or touch input.
    public void SetCameraVisible(bool visible) =>
        CameraButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        // A normal tap on the bottom navigation must never take a screenshot.
        // Touch/mouse activation requires a deliberate 500 ms stationary hold.
        if (!_cameraLongPressAccepted) return;
        _cameraLongPressAccepted = false;
        if (!_swipeActivated) await _controller.TakeScreenshotAsync();
        _swipeActivated = false;
    }

    private void BeginCameraGesture(System.Windows.Point point)
    {
        _cameraLongPressAccepted = false;
        _cameraGesture.Begin(point.X, point.Y, Environment.TickCount64);
    }

    private void MoveCameraGesture(System.Windows.Point point) =>
        _cameraGesture.Move(point.X, point.Y);

    private void EndCameraGesture(System.Windows.Point point)
    {
        var inside = point.X >= 0 && point.X <= CameraButton.ActualWidth &&
            point.Y >= 0 && point.Y <= NavigationBody.ActualHeight;
        _cameraLongPressAccepted = _cameraGesture.End(
            point.X, point.Y, Environment.TickCount64, inside);
    }

    private double? _swipeStart;
    private double _wheelDistance;
    private bool _swipeActivated;

    private double SwipeThreshold => 12.0 * Math.Max(1.0, ActualWidth / ScreenFit.DesignWidth);

    private void BeginSwipe(double y)
    {
        _swipeStart = y;
        _swipeActivated = false;
        _wheelDistance = 0;
    }

    private void ContinueSwipe(double y)
    {
        if (_swipeStart is not { } start || _swipeActivated) return;
        if (start - y < SwipeThreshold) return;
        _swipeActivated = true;
        _controller.ShowPhoneList();
    }

    private void EndSwipe() => _swipeStart = null;

    private void OnSwipeDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(NavigationBody);
        _cameraLongPressAccepted = false;
        if (CameraButton.IsVisible && p.X < CameraButton.ActualWidth)
            BeginCameraGesture(p);
        else
            _cameraGesture.Cancel();
        BeginSwipe(p.Y);
    }
    private void OnSwipeMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(NavigationBody);
        if (_cameraGesture.IsActive) MoveCameraGesture(p);
        ContinueSwipe(p.Y);
    }
    private void OnSwipeUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(NavigationBody);
        if (_cameraGesture.IsActive) EndCameraGesture(p);
        ContinueSwipe(p.Y);
        EndSwipe();
    }

    private void OnSwipeWheel(object sender, MouseWheelEventArgs e)
    {
        // A finger swipe upward is normally translated to scrolling down.
        if (e.Delta >= 0) { _wheelDistance = 0; return; }
        _wheelDistance += -e.Delta / 120.0 * 24;
        if (_wheelDistance >= SwipeThreshold)
        {
            _wheelDistance = 0;
            _controller.ShowPhoneList();
            e.Handled = true;
        }
    }

    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        var p = e.GetTouchPoint(NavigationBody).Position;
        _cameraLongPressAccepted = false;
        if (CameraButton.IsVisible && p.X < CameraButton.ActualWidth)
            BeginCameraGesture(p);
        else
            _cameraGesture.Cancel();
        BeginSwipe(p.Y);
    }
    private void OnTouchMove(object sender, TouchEventArgs e)
    {
        var p = e.GetTouchPoint(NavigationBody).Position;
        if (_cameraGesture.IsActive) MoveCameraGesture(p);
        ContinueSwipe(p.Y);
    }
    private void OnTouchUp(object sender, TouchEventArgs e)
    {
        var p = e.GetTouchPoint(NavigationBody).Position;
        if (_cameraGesture.IsActive) EndCameraGesture(p);
        ContinueSwipe(p.Y);
        EndSwipe();
    }

    private void ShowList_Click(object sender, RoutedEventArgs e)
    {
        if (!_swipeActivated) _controller.ShowPhoneList();
        _swipeActivated = false;
    }
}
