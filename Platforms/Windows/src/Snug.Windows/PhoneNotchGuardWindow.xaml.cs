using System.Windows;
using Snug.Windows.Core;
using Snug.Windows.Interop;
using Snug.Windows.Services;

namespace Snug.Windows;

/// <summary>
/// Opaque, non-activating phone-notch guard. Shown only while a managed
/// application occupies the VDD; the catalog already has its own top padding.
/// </summary>
public partial class PhoneNotchGuardWindow : Window
{
    private DisplayInfo? _display;

    public PhoneNotchGuardWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyPlacement();
    }

    public void PlaceOn(DisplayInfo display)
    {
        _display = display;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = display.Bounds.X / display.DpiScaleX;
        Top = display.Bounds.Y / display.DpiScaleY;
        Width = display.Bounds.Width / display.DpiScaleX;
        Height = UuMobileOverlaySafeArea.TopInset(display.Bounds.Width) / display.DpiScaleY;
        if (IsLoaded) ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        if (_display is not { } display) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var moved = NativeMethods.SetPhysicalWindowPos(hwnd, new IntPtr(-1),
            display.Bounds.X, display.Bounds.Y,
            display.Bounds.Width, UuMobileOverlaySafeArea.TopInset(display.Bounds.Width),
            NativeMethods.SWP_NOACTIVATE | (IsVisible ? NativeMethods.SWP_SHOWWINDOW : 0));
        if (!moved)
            Top = display.Bounds.Y / display.DpiScaleY;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }
}