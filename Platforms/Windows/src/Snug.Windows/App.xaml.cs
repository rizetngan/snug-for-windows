using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using Snug.Windows.Services;
using Snug.Windows.Interop;

namespace Snug.Windows;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\SnugForWindows";
    private const string ActivateEventName = @"Local\SnugForWindows.Activate";
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private NotifyIcon? _tray;
    private ToolStripMenuItem? _trayStatus;
    private AppController? _controller;
    private MainWindow? _mainWindow;
    private WelcomeWindow? _welcome;
    private bool _ownsMutex;
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = Environment.GetCommandLineArgs();
        var catalogPreview = args.Any(argument => string.Equals(argument, "--catalog-preview", StringComparison.OrdinalIgnoreCase));
        var settingsPreview = args.Any(argument => string.Equals(argument, "--settings-preview", StringComparison.OrdinalIgnoreCase));
        var mutexName = catalogPreview ? MutexName + ".CatalogPreview" : settingsPreview ? MutexName + ".SettingsPreview" : MutexName;
        var activateEventName = catalogPreview ? ActivateEventName + ".CatalogPreview" : settingsPreview ? ActivateEventName + ".SettingsPreview" : ActivateEventName;
        _mutex = new Mutex(initiallyOwned: true, mutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(activateEventName);
                signal.Set();
            }
            catch { }
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activateEventName);
        if (!catalogPreview && !settingsPreview) _ = Task.Run(WaitForActivation);

        _controller = new AppController();
        if (catalogPreview)
        {
            _controller.StartCatalogPreview();
            _controller.OpenPhonePreview();
            return;
        }
        if (settingsPreview)
        {
            _controller.StartCatalogPreview();
            _mainWindow = new MainWindow(_controller, ExitAsync);
            _mainWindow.ShowSettings();
            return;
        }

        _mainWindow = new MainWindow(_controller, ExitAsync);
        _controller.PropertyChanged += OnControllerPropertyChanged;
        _controller.Start();
        CreateTray();

        var background = args.Any(argument =>
            string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var preview = args.Any(argument =>
            string.Equals(argument, "--preview-phone", StringComparison.OrdinalIgnoreCase));

        if (!_controller.Welcomed)
        {
            if (StartupManager.IsInstalledLocation) _controller.LaunchAtLogin = true;
            _welcome = new WelcomeWindow(_controller, _mainWindow);
            _welcome.Show();
        }
        else if (!background)
        {
            _mainWindow.ShowSettings();
        }

        if (preview) _controller.OpenPhonePreview();
    }

    private void CreateTray()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Snug.ico");
        Icon icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
        _tray = new NotifyIcon
        {
            Icon = icon,
            Text = "Snug · 等待 UU 远程",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        _trayStatus = new ToolStripMenuItem("等待 UU 远程") { Enabled = false };
        var open = new ToolStripMenuItem("打开设置");
        open.Click += (_, _) => ShowSettings();
        var exit = new ToolStripMenuItem("退出 Snug");
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(_trayStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(open);
        menu.Items.Add(exit);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();
    }

    private void OnControllerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppController.ConnectionStatus)) return;
        Dispatcher.BeginInvoke(() =>
        {
            var status = _controller?.ConnectionStatus ?? "Snug";
            if (_trayStatus is not null) _trayStatus.Text = status;
            if (_tray is not null) _tray.Text = ("Snug · " + status)[..Math.Min(63, ("Snug · " + status).Length)];
        });
    }

    private void WaitForActivation()
    {
        while (!_exiting)
        {
            try
            {
                if (_activateEvent?.WaitOne(500) == true)
                    Dispatcher.BeginInvoke(ShowSettings);
            }
            catch (ObjectDisposedException) { return; }
        }
    }

    private void ShowSettings()
    {
        _welcome?.Close();
        _mainWindow?.ShowSettings();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;

        if (_controller is not null && !await _controller.FinishForQuitAsync())
        {
            _exiting = false;
            ShowSettings();
            return;
        }

        _welcome?.Close();
        _mainWindow?.CloseForExit();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _controller?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _tray?.Dispose();
        _activateEvent?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
