using System.Windows;
using System.Windows.Media.Animation;
using Snug.Windows.Core;
using Snug.Windows.Interop;
using Snug.Windows.Services;

namespace Snug.Windows;

public partial class AppBackBarWindow : Window
{
    private readonly AppController _controller;
    private DisplayInfo? _targetDisplay;

    public AppBackBarWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Opacity = 0;
        Loaded += (_, _) =>
        {
            ApplyPlacement();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        };
    }

    public void SetApp(string appName, bool canZoom, int zoomSteps)
    {
        AppNameText.Text = appName;
        ZoomControls.Visibility = canZoom ? Visibility.Visible : Visibility.Collapsed;
        Width = canZoom ? 316 : 190;
        SetZoomState(zoomSteps);
    }

    public void SetZoomState(int zoomSteps)
    {
        var label = zoomSteps == 0 ? "默认" : $"{zoomSteps:+#;-#;0}";
        ZoomControls.ToolTip = $"当前字号：{label}";
    }

    public void PlaceOn(DisplayInfo display)
    {
        _targetDisplay = display;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (display.Bounds.X + 14) / display.DpiScaleX;
        Top = (display.Bounds.Y + 14) / display.DpiScaleY;
        Width = (ZoomControls.Visibility == Visibility.Visible ? 316 : 190) / display.DpiScaleX;
        Height = 52 / display.DpiScaleY;
        if (IsLoaded) ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        if (_targetDisplay is not { } display) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        NativeMethods.SetPhysicalWindowPos(handle, IntPtr.Zero,
            display.Bounds.X + 14, display.Bounds.Y + 14,
            ZoomControls.Visibility == Visibility.Visible ? 316 : 190, 52,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | (IsVisible ? NativeMethods.SWP_SHOWWINDOW : 0));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _controller.ShowPhoneList();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => _controller.AdjustCurrentAppZoom(-1);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => _controller.AdjustCurrentAppZoom(1);
}
