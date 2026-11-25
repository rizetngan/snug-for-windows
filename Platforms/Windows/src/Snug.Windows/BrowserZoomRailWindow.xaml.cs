using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Snug.Windows.Core;
using Snug.Windows.Interop;
using Snug.Windows.Services;

namespace Snug.Windows;

/// <summary>
/// Small non-activating mobile zoom rail, confined to the Snug-owned display.
/// The rail observes outside mouse clicks but does not consume browser input.
/// Phone UU may synthesize both mouse and touch events; native zoom is only
/// committed on slider release, preventing repetitive Ctrl+0 during a drag.
/// </summary>
public partial class BrowserZoomRailWindow : Window
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    private readonly AppController _controller;
    private readonly DispatcherTimer _autoHide;
    private DisplayInfo? _targetDisplay;
    private DateTimeOffset _lastInteraction;
    private bool _expanded;
    private bool _dragging;
    private bool _lastButtonDown;
    private double? _swipeStartX;
    private int _index = BrowserZoomLevels.DefaultIndex;
    private bool _known;

    public BrowserZoomRailWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        _autoHide = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(90) };
        _autoHide.Tick += AutoHideTick;
        _autoHide.Start();
        Loaded += (_, _) => ApplyPlacement();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Collapse(); };
        ZoomSlider.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, _) => { _dragging = true; Touch(); }), true);
        ZoomSlider.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler((_, _) => CommitDrag()), true);
        ZoomSlider.AddHandler(PreviewTouchDownEvent,
            new EventHandler<TouchEventArgs>((_, _) => { _dragging = true; Touch(); }), true);
        ZoomSlider.AddHandler(PreviewTouchUpEvent,
            new EventHandler<TouchEventArgs>((_, _) => CommitDrag()), true);
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

    public void SetLevel(int? level)
    {
        _known = level.HasValue;
        _index = BrowserZoomLevels.ClampIndex(level ?? BrowserZoomLevels.DefaultIndex);
        ZoomSlider.Value = _index;
        RefreshText(_index);
    }

    private void RefreshText(int index)
    {
        LargePercent.Text = $"{BrowserZoomLevels.PercentAt(index)}%";
        CompactPercent.Text = _known
            ? $"{BrowserZoomLevels.PercentAt(index)}%" : "缩放";
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
        _dragging = false;
        _swipeStartX = null;
        ExpandedBody.Visibility = Visibility.Collapsed;
        CompactBody.Visibility = Visibility.Visible;
        ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        if (_targetDisplay is not { } display) return;
        const double compactWidthDip = 69;
        const double expandedWidthDip = 86;
        const double compactHeightDip = 71;
        const double expandedHeightDip = 427;
        var wDip = _expanded ? expandedWidthDip : compactWidthDip;
        var hDip = _expanded ? expandedHeightDip : compactHeightDip;
        Width = wDip;
        Height = hDip;
        var w = Math.Max(1, (int)Math.Round(wDip * display.DpiScaleX));
        var h = Math.Max(1, (int)Math.Round(hDip * display.DpiScaleY));
        var x = display.Bounds.X + (int)Math.Round(display.Bounds.Width * 0.016);
        var topGuard = display.Bounds.Y + 45;
        var bottomGuard = display.Bounds.Y + display.Bounds.Height
            - UuMobileOverlaySafeArea.ReservedBottom(display.Bounds.Width) - 15;
        var y = Math.Clamp(
            display.Bounds.Y + (int)Math.Round(display.Bounds.Height * .48) - h / 2,
            topGuard, Math.Max(topGuard, bottomGuard - h));
        if (!IsLoaded) { Left = x / display.DpiScaleX; Top = y / display.DpiScaleY; return; }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            NativeMethods.SetPhysicalWindowPos(hwnd, new IntPtr(-1), x, y, w, h,
                NativeMethods.SWP_NOACTIVATE |
                (IsVisible ? NativeMethods.SWP_SHOWWINDOW : 0));
    }

    private void Commit(int index)
    {
        if (_targetDisplay is null || !IsVisible) return;
        _index = BrowserZoomLevels.ClampIndex(index);
        _controller.SetCurrentBrowserZoomIndex(_index);
        _known = true;
        RefreshText(_index);
        Touch();
    }

    private void CommitDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        Commit((int)Math.Round(ZoomSlider.Value));
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => Expand();
    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LargePercent is not null)
            LargePercent.Text = $"{BrowserZoomLevels.PercentAt((int)Math.Round(e.NewValue))}%";
        if (_dragging) Touch();
    }

    private void Plus_Click(object sender, RoutedEventArgs e) =>
        UpdateAndCommit(_index + 1);
    private void Minus_Click(object sender, RoutedEventArgs e) =>
        UpdateAndCommit(_index - 1);
    private void Reset_Click(object sender, RoutedEventArgs e) =>
        UpdateAndCommit(BrowserZoomLevels.DefaultIndex);
    private void UpdateAndCommit(int index)
    {
        index = BrowserZoomLevels.ClampIndex(index);
        ZoomSlider.Value = index;
        Commit(index);
    }

    private void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        _swipeStartX = e.GetPosition(this).X;
        Touch();
    }
    private void WindowMouseUp(object sender, MouseButtonEventArgs e) =>
        CheckSwipe(e.GetPosition(this).X);
    private void WindowTouchDown(object sender, TouchEventArgs e)
    {
        _swipeStartX = e.GetTouchPoint(this).Position.X;
        Touch();
    }
    private void WindowTouchUp(object sender, TouchEventArgs e) =>
        CheckSwipe(e.GetTouchPoint(this).Position.X);

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
        else if (!_dragging && DateTimeOffset.UtcNow - _lastInteraction >= TimeSpan.FromSeconds(2))
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
        base.OnClosed(e);
    }
}
