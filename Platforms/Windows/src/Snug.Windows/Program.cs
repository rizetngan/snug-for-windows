using System.Diagnostics;
using System.Windows.Forms;

namespace Snug.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // The portable launcher runs this DLL through dotnet.exe, so the apphost
        // manifest is not applied. Set awareness before WPF initializes USER32.
        if (!System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2))
            Trace.TraceWarning("Snug: process DPI awareness was already set or could not be changed.");
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}