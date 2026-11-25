using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using Snug.Windows;
using Snug.Windows.Core;
using Snug.Windows.Services;
internal static class GeometryCheck
{
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int L,T,R,B;}
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    static int failures;
    static Rect Physical(Window w) {
        var old=SetThreadDpiAwarenessContext(new IntPtr(-4));
        if(old==IntPtr.Zero)throw new Exception("DPI scope unavailable");
        try{if(!GetWindowRect(new WindowInteropHelper(w).Handle,out var r))throw new Exception("GetWindowRect failed");return r;}
        finally{SetThreadDpiAwarenessContext(old);}
    }
    static void Check(string name,Window w,WindowBounds target) {
        var r=Physical(w); var h=new WindowInteropHelper(w).Handle;
        var ok=Math.Abs(r.L-target.X)<=3&&Math.Abs(r.T-target.Y)<=3&&Math.Abs(r.R-r.L-target.Width)<=3&&Math.Abs(r.B-r.T-target.Height)<=3;
        Console.WriteLine($"{name}: actual={r.L},{r.T} {r.R-r.L}x{r.B-r.T}; target={target.X},{target.Y} {target.Width}x{target.Height}; awareness={GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(h))}; PASS={ok}");
        if(!ok)failures++;
    }
    [STAThread] static void Main(){
        SetProcessDpiAwarenessContext(new IntPtr(-2)); // intentionally match system-aware packaged host
        var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var d=new DisplayEnumerator().FindSnugDisplay();
        if(d==null){Console.WriteLine("BLOCKED: no existing Snug monitor");Environment.ExitCode=2;return;}
        var b=d.Bounds;Console.WriteLine($"VDD={b.X},{b.Y} {b.Width}x{b.Height}; SCALE={d.DpiScaleX}/{d.DpiScaleY}");
        var controller=new AppController();
        var phone=new PhoneSurfaceWindow(controller,false);
        phone.PlaceOn(d);phone.Show();
        var bar=new HomeBarWindow(controller);bar.SetCameraVisible(true);bar.PlaceOn(d);bar.Show();bar.PlaceOn(d);
        var rail=new BrowserZoomRailWindow(controller);rail.PlaceOn(d);rail.Show();rail.PlaceOn(d);
        int stage=0;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};
        timer.Tick+=(_,_)=>{
            try {
                stage++;
                if(stage==1){
                    Check("PhoneSurface",phone,b);
                    var height=ScreenFit.HomeBarHeight(b.Width);
                    Check("HomeBar",bar,new WindowBounds(b.X,b.Y+b.Height-height-UuMobileOverlaySafeArea.BottomInset(b.Width),b.Width,UuMobileOverlaySafeArea.ReservedBottom(b.Width)));
                    var nav=(System.Windows.Controls.Grid)bar.FindName("NavigationBody"); var navExpected=height/d.DpiScaleY; var navOk=Math.Abs(nav.ActualHeight-navExpected)<=2; Console.WriteLine($"Navigation height={nav.ActualHeight}, expected={navExpected}, PASS={navOk}"); if(!navOk)failures++; var rr=Physical(rail);
                    var inside=rr.L>=b.X&&rr.R<=b.X+b.Width&&rr.T>=b.Y&&rr.B<=b.Y+b.Height&&rr.R-rr.L<=Math.Ceiling(70*d.DpiScaleX)+3;
                    Console.WriteLine($"Rail within monitor: {inside}; {rr.L},{rr.T} {rr.R-rr.L}x{rr.B-rr.T}");
                    if(!inside)failures++;
                    phone.Hide();phone.PlaceOn(d);
                    bool hidden=!IsWindowVisible(new WindowInteropHelper(phone).Handle);
                    Console.WriteLine($"Hidden catalog remains hidden: {hidden}");if(!hidden)failures++;
                    phone.Show();phone.PlaceOn(d);
                }
                if(stage==2){
                    Check("Catalog re-show",phone,b);
                    var dummy=new System.Windows.Window{Width=320,Height=360,WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false,Title="Snug geometry fixture"};
                    dummy.Show();
                    var target=b with {Height=UuMobileOverlaySafeArea.ContentHeight(b.Width,b.Height)};
                    var key=new WindowKey((uint)Environment.ProcessId,new WindowInteropHelper(dummy).Handle.ToInt64());
                    var manager=new WindowManager();
                    bool moved=manager.MoveTo(key,target);
                    Console.WriteLine($"WindowManager.MoveTo={moved}");
                    if(!moved)failures++;
                    Check("Managed fixture",dummy,target);
                    dummy.Close();
                    timer.Stop();rail.Close();bar.Close();phone.Close();
                    // No controller startup or disposal: never modify real session/driver.
                    Console.WriteLine($"FAILURES={failures}");Environment.ExitCode=failures==0?0:1;app.Shutdown(Environment.ExitCode);
                }
            }catch(Exception e){Console.WriteLine(e);timer.Stop();app.Shutdown(3);}
        };
        timer.Start();app.Run();
    }
}