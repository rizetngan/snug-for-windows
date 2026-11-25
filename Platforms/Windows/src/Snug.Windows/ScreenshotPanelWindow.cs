using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snug.Windows.Core;
using Snug.Windows.Interop;
using Snug.Windows.Services;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Snug.Windows;

internal sealed class ScreenshotPanelWindow : Window
{
    private readonly ScreenshotOverlay _shot;
    private readonly AppController _controller;
    private DisplayInfo? _display;
    public ScreenshotPanelWindow(AppController controller, ScreenshotOverlay shot)
    {
        _controller=controller; _shot=shot;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        AllowsTransparency=shot.Kind!=ShotKind.Cropping;
        Background=shot.Kind==ShotKind.Cropping?Brushes.Black:Brushes.Transparent;
        WindowStartupLocation=WindowStartupLocation.Manual;
        Loaded+=(_,_)=>ApplyPlacement();
    }

    public void PlaceOn(DisplayInfo display)
    {
        _display=display;
        var scale=Math.Max(1, display.Bounds.Width/(double)ScreenFit.DesignWidth);
        var bar=ScreenFit.HomeBarHeight(display.Bounds.Width);
        double w,h,x,y;
        if(_shot.Kind==ShotKind.Cropping)
        {w=display.Bounds.Width;h=display.Bounds.Height;x=display.Bounds.X;y=display.Bounds.Y;}
        else
        {
            w=Math.Round((_shot.Kind==ShotKind.Preview?230:260)*scale);
            h=Math.Round((_shot.Kind==ShotKind.Preview?68:48)*scale);
            x=display.Bounds.X+(_shot.Kind==ShotKind.Preview?12*scale:(display.Bounds.Width-w)/2);
            y=display.Bounds.Y+display.Bounds.Height-UuMobileOverlaySafeArea.BottomInset(display.Bounds.Width)-bar-h-8*scale;
        }
        Left=x/display.DpiScaleX; Top=y/display.DpiScaleY; Width=w/display.DpiScaleX;Height=h/display.DpiScaleY;
        // Reuse a crop view if display mode changes; its normalized selection survives.
        if(Content is null) Content=BuildContent(scale/display.DpiScaleX);
        if(IsLoaded)ApplyPlacement();
    }
    private void ApplyPlacement()
    {
        if(_display is not { } d)return;
        var scale=Math.Max(1,d.Bounds.Width/(double)ScreenFit.DesignWidth);
        int w,h,x,y;
        if(_shot.Kind==ShotKind.Cropping){w=d.Bounds.Width;h=d.Bounds.Height;x=d.Bounds.X;y=d.Bounds.Y;}
        else{
            w=(int)Math.Round((_shot.Kind==ShotKind.Preview?230:260)*scale);
            h=(int)Math.Round((_shot.Kind==ShotKind.Preview?68:48)*scale);
            x=d.Bounds.X+(int)Math.Round(_shot.Kind==ShotKind.Preview?12*scale:(d.Bounds.Width-w)/2);
            y=d.Bounds.Y+d.Bounds.Height-UuMobileOverlaySafeArea.BottomInset(d.Bounds.Width)-ScreenFit.HomeBarHeight(d.Bounds.Width)-h-(int)Math.Round(8*scale);
        }
        NativeMethods.SetPhysicalWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),x,y,w,h,NativeMethods.SWP_NOACTIVATE|(IsVisible?NativeMethods.SWP_SHOWWINDOW:0));
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h=new WindowInteropHelper(this).Handle;
        var style=NativeMethods.GetWindowLong(h,NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(h,NativeMethods.GWL_EXSTYLE,style|NativeMethods.WS_EX_NOACTIVATE|NativeMethods.WS_EX_TOOLWINDOW|(_shot.Kind==ShotKind.Notice?0x20:0));
    }
    private FrameworkElement BuildContent(double s)
    {
        if(_shot.Kind==ShotKind.Cropping && _shot.Image is { } image)
        {
            var grid=new Grid{Background=Brushes.Black};
            var surface=new CropSurface(WindowScreenshotService.Decode(image));
            grid.Children.Add(surface);
            var cancel=ActionButton("取消",s);cancel.HorizontalAlignment=System.Windows.HorizontalAlignment.Left;
            var done=ActionButton("完成",s);done.HorizontalAlignment=System.Windows.HorizontalAlignment.Right;
            cancel.Click+=(_,_)=>_controller.CancelScreenshotCrop();
            done.Click+=(_,_)=>_controller.FinishScreenshotCrop(surface.Selection);
            grid.Children.Add(cancel);grid.Children.Add(done);
            return grid;
        }
        var card=new Border{CornerRadius=new CornerRadius(16*s),Background=new SolidColorBrush(Color.FromArgb(222,0,0,0)),Padding=new Thickness(12*s)};
        if(_shot.Kind==ShotKind.Preview && _shot.Image is { } preview)
        {
            var row=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var thumb=new Border{Width=44*s,Height=44*s,CornerRadius=new CornerRadius(8*s),ClipToBounds=true,Margin=new Thickness(0,0,12*s,0)};
            thumb.Child=new Image{Source=WindowScreenshotService.Decode(preview),Stretch=Stretch.UniformToFill};
            row.Children.Add(thumb);
            var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            words.Children.Add(new TextBlock{Text="已复制截图",Foreground=Brushes.White,FontSize=18*s,FontWeight=FontWeights.SemiBold});
            words.Children.Add(new TextBlock{Text="点按裁剪",Foreground=Brushes.LightGray,FontSize=14*s,Margin=new Thickness(0,2*s,0,0)});
            row.Children.Add(words);
            var button=new Button{Content=row,BorderThickness=new Thickness(0),Padding=new Thickness(0),Background=Brushes.Transparent};
            System.Windows.Automation.AutomationProperties.SetName(button,"截图已复制，点按裁剪");
            button.Click+=(_,_)=>_controller.OpenScreenshotCrop();
            // Default button chrome would cover the dark card.
            var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
            var hit=new FrameworkElementFactory(typeof(Border));hit.SetValue(Border.BackgroundProperty,Brushes.Transparent);hit.AppendChild(presenter);button.Template=new ControlTemplate(typeof(Button)){VisualTree=hit};
            card.Child=button;
        }
        else
        {
            card.IsHitTestVisible=false;
            var message=_shot.Notice switch {ShotNotice.Cropped=>"已复制裁剪截图",ShotNotice.CopyFailed=>"剪贴板写入失败",ShotNotice.CropFailed=>"截图裁剪失败",ShotNotice.WindowGone=>"窗口已关闭",_=>"未能截取当前窗口"};
            card.Child=new TextBlock {Text=message,Foreground=Brushes.White,FontSize=16*s,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        }
        return card;
    }
    private static Button ActionButton(string title,double s)
    {
        var b=new Button{Content=title,Width=74*s,Height=48*s,Margin=new Thickness(22*s,4*s,22*s,0),VerticalAlignment=VerticalAlignment.Top,
            Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),FontSize=18*s,FontWeight=FontWeights.SemiBold};
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,System.Windows.VerticalAlignment.Center);
        var hit=new FrameworkElementFactory(typeof(Border));hit.SetValue(Border.BackgroundProperty,Brushes.Transparent);hit.AppendChild(presenter);b.Template=new ControlTemplate(typeof(Button)){VisualTree=hit};
        return b;
    }
}

internal sealed class CropSurface(BitmapSource bitmap) : FrameworkElement
{
    public CropRect Selection {get;private set;}=CropRect.Full;
    private CropHandle? _handle;
    private Point _start;
    private CropRect _original;
    private CropRect _shown;
    private double Scale=>Math.Max(.6,ActualWidth/ScreenFit.DesignWidth);
    private Rect Selected=>new(_shown.X+Selection.X*_shown.Width,_shown.Y+Selection.Y*_shown.Height,Selection.Width*_shown.Width,Selection.Height*_shown.Height);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var s=Scale;var margin=22*s;var top=64*s;
        var whole=new Rect(0,0,ActualWidth,ActualHeight);
        dc.DrawRectangle(Brushes.Black,null,whole);
        _shown=CropGeometry.Fit(bitmap.PixelWidth,bitmap.PixelHeight,new(margin,top,Math.Max(1,ActualWidth-margin*2),Math.Max(1,ActualHeight-top-margin)));
        if(_shown.Width<=0||_shown.Height<=0)return;
        dc.DrawImage(bitmap,new Rect(_shown.X,_shown.Y,_shown.Width,_shown.Height));
        var sel=Selected;
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(140,0,0,0)),null,new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(whole),new RectangleGeometry(sel)));
        dc.DrawRectangle(null,new Pen(Brushes.White,s),sel);
        var pen=new Pen(Brushes.White,4*s){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        var c=Math.Min(20*s,Math.Min(sel.Width,sel.Height)/3);
        foreach(var x in new[]{sel.Left,sel.Right})foreach(var y in new[]{sel.Top,sel.Bottom})
        {
            dc.DrawLine(pen,new Point(x,y),new Point(x+(x==sel.Left?c:-c),y));
            dc.DrawLine(pen,new Point(x,y),new Point(x,y+(y==sel.Top?c:-c)));
        }
        var tick=c/2;
        foreach(var y in new[]{sel.Top,sel.Bottom})dc.DrawLine(pen,new Point(sel.Left+sel.Width/2-tick,y),new Point(sel.Left+sel.Width/2+tick,y));
        foreach(var x in new[]{sel.Left,sel.Right})dc.DrawLine(pen,new Point(x,sel.Top+sel.Height/2-tick),new Point(x,sel.Top+sel.Height/2+tick));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p=e.GetPosition(this);var r=Selected;var radius=34*Scale;
        if(p.X<r.Left-radius||p.X>r.Right+radius||p.Y<r.Top-radius||p.Y>r.Bottom+radius)return;
        int Side(double x,double l,double h){var a=Math.Abs(x-l);var b=Math.Abs(x-h);return Math.Min(a,b)>radius?0:a<=b?-1:1;}
        _handle=(Side(p.X,r.Left,r.Right),Side(p.Y,r.Top,r.Bottom)) switch {
            (-1,-1)=>CropHandle.TopLeft,(0,-1)=>CropHandle.Top,(1,-1)=>CropHandle.TopRight,
            (-1,0)=>CropHandle.Left,(1,0)=>CropHandle.Right,(-1,1)=>CropHandle.BottomLeft,(0,1)=>CropHandle.Bottom,(1,1)=>CropHandle.BottomRight,_=>null};
        if(_handle is null)return;
        _start=p;_original=Selection;
        if(!CaptureMouse()){_handle=null;return;}
        e.Handled=true;
    }
    private void ApplyDrag(Point p)
    {
        if(_handle is not { } handle||_shown.Width<=0||_shown.Height<=0)return;
        var min=44*Scale;
        Selection=CropGeometry.Resize(_original,handle,(p.X-_start.X)/_shown.Width,(p.Y-_start.Y)/_shown.Height,min/_shown.Width,min/_shown.Height);
        InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(_handle is null||e.LeftButton!=MouseButtonState.Pressed)return;
        ApplyDrag(e.GetPosition(this));e.Handled=true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if(_handle is null)return;
        ApplyDrag(e.GetPosition(this));
        _handle=null;
        if(IsMouseCaptured)ReleaseMouseCapture();
        e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e){_handle=null;base.OnLostMouseCapture(e);}
}