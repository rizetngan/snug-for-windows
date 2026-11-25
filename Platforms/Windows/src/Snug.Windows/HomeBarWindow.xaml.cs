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
    private DisplayInfo? _targetDisplay;

    public HomeBarWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Opacity = 0;
        Loaded += (_, _) => ApplyPlacement();
    }

    public void PlaceOn(DisplayInfo display)
    {
        _targetDisplay = display;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = display.Bounds.Width / display.DpiScaleX;
        Height = ScreenFit.HomeBarHeight(display.Bounds.Width) / display.DpiScaleY;
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
            var placed = NativeMethods.SetWindowPos(handle, new IntPtr(-1),
                display.Bounds.X,
                display.Bounds.Y + display.Bounds.Height - barHeight
                    - UuMobileOverlaySafeArea.BottomInset(display.Bounds.Width),
                display.Bounds.Width,
                barHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            if (!placed)
                Top = (display.Bounds.Y + display.Bounds.Height - barHeight
                    - UuMobileOverlaySafeArea.BottomInset(display.Bounds.Width)) / display.DpiScaleY;
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    /// <summary>Repairs the topmost order only if Windows raised the secondary taskbar above us.</summary>
    public void EnsureAboveTaskbar()
    {
        if (!IsLoaded || !IsVisible || _targetDisplay is null) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var bar)) return;

        // Iterate only the windows above this bar, not every desktop window.
        // Never raise over another application dialog or affect the primary taskbar.
        var node = NativeMethods.GetWindow(handle, 3); // GW_HWNDPREV
        for (var inspected = 0; inspected < 80 && node != IntPtr.Zero; inspected++)
        {
            if (NativeMethods.IsWindowVisible(node))
            {
                var className = new StringBuilder(64);
                NativeMethods.GetClassName(node, className, className.Capacity);
                if (className.ToString() == "Shell_SecondaryTrayWnd"
                    && NativeMethods.GetWindowRect(node, out var tray)
                    && tray.Right > bar.Left && tray.Left < bar.Right
                    && tray.Bottom > bar.Top && tray.Top < bar.Bottom)
                {
                    const uint noMove = 0x0002;
                    const uint noSize = 0x0001;
                    NativeMethods.SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0,
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
        if (!_swipeActivated) await _controller.TakeScreenshotAsync();
        _swipeActivated = false;
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
        var p=e.GetPosition(this);
        if (CameraButton.IsVisible && p.X < CameraButton.ActualWidth) { EndSwipe(); _swipeActivated=false; return; }
        BeginSwipe(p.Y);
    }
    private void OnSwipeMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) ContinueSwipe(e.GetPosition(this).Y);
    }
    private void OnSwipeUp(object sender, MouseButtonEventArgs e)
    {
        ContinueSwipe(e.GetPosition(this).Y);
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
        var p=e.GetTouchPoint(this).Position;
        if (CameraButton.IsVisible && p.X < CameraButton.ActualWidth) { EndSwipe(); _swipeActivated=false; return; }
        BeginSwipe(p.Y);
    }
    private void OnTouchMove(object sender, TouchEventArgs e) => ContinueSwipe(e.GetTouchPoint(this).Position.Y);
    private void OnTouchUp(object sender, TouchEventArgs e)
    {
        ContinueSwipe(e.GetTouchPoint(this).Position.Y);
        EndSwipe();
    }

    private void ShowList_Click(object sender, RoutedEventArgs e)
    {
        if (!_swipeActivated) _controller.ShowPhoneList();
        _swipeActivated = false;
    }
}
