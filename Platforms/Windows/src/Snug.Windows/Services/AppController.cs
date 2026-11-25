using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Snug.Windows.Core;

namespace Snug.Windows.Services;

public sealed class AppController : INotifyPropertyChanged, IDisposable
{
    private readonly DisplayEnumerator _displayEnumerator = new();
    private readonly WindowManager _windowManager = new();
    private readonly AppCatalogService _appCatalog;
    private readonly PhoneCatalog _phoneCatalog;
    private PhoneChoice _phoneChoice = PhoneChoice.Standard;
    // Desired model and actual live-display model must be separate. Unlike
    // upstream CGVirtualDisplay.reshape, the current Windows VDD cannot
    // change its mode table in-place without invalidating UU's capture.
    private PhoneChoice? _activeDisplayPhoneChoice;
    private int? _activeDisplayInsetPoints;
    private int? _activeDisplayPreferredBaseWidth;
    private int _baseWidthOption;
    private string _uuViewportMode = UuViewportPreset.Classic;
    private PhoneChoice CurrentDisplayChoice => _activeDisplayPhoneChoice ?? _phoneChoice;
    private int CurrentDisplayInsetPoints => _activeDisplayInsetPoints
        ?? UuViewportPreset.VerticalInsetPoints(_uuViewportMode);
    private int CurrentPreferredBaseWidth => _activeDisplayPreferredBaseWidth
        ?? PhoneBaseWidth.AtIndex(_baseWidthOption);
    private ScreenFit CurrentScreenFit => new(CurrentDisplayChoice.Screen,
        CurrentDisplayInsetPoints, CurrentPreferredBaseWidth);
    public ScreenFit PreviewScreenFit => new(_phoneChoice.Screen,
        UuViewportPreset.VerticalInsetPoints(_uuViewportMode),
        PhoneBaseWidth.AtIndex(_baseWidthOption));
    private readonly UuSessionDetector _uuDetector = new();
    private readonly UuConnectionTracker _connection = new();
    private readonly MouseGestureHook _gestureHook = new();
    private readonly PhoneDisplayHold _displayHold = new();
    private int _browserZoomIndex = BrowserZoomLevels.DefaultIndex;
    private bool _browserZoomKnown;
    private readonly Screenshots _screenshots = new(new WindowScreenshotService());
    private ScreenshotPanelWindow? _screenshotPanel;
    private readonly VddDriverClient _driver = new();
    private readonly PhoneDisplayModeSwitcher _modeSwitcher = new();
    private readonly SemaphoreSlim _modeGate = new(1, 1);
    private readonly PhoneActionLane _actionLane = new();
    private readonly ZoomBook _zoomBook;
    private readonly SnugSettingsStore _settingsStore = new();
    private readonly SnugSettings _settings;
    private readonly RestoreLedger _ledger = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snug", "phone-screen.json"));
    // Unlike the active single-window ledger, this book survives a destroyed HWND.
    private readonly LeftBehindBook _leftBehind = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snug", "left-behind.json"));
    private readonly DispatcherTimer _timer;

    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastDisplayScan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastWindowScan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDriverScan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAcceptanceAuditAt = DateTimeOffset.MinValue;
    private DisplayInfo? _availableSnugDisplay;
    private DisplayInfo? _phoneDisplay;
    private bool _endingSession;
    private bool _creatingSession;
    private bool _orphanCleanupStarted;
    private volatile WindowRestoreRecord? _placed;
    private PhoneSurfaceWindow? _phoneSurface;
    private HomeBarWindow? _homeBar;
    private BrowserZoomRailWindow? _browserZoomRail;
    private AppBackBarWindow? _appBackBar;
    private bool _previewOpen;
    private bool _showingPhoneList = true;
    private bool _pullingWindow;
    private WindowKey? _askingDialogKey;
    private bool _recoveringDialog;
    private bool _recoveringLostWindow;
    private int _activeModeWidth = ScreenFit.Standard.BaseWidth;
    private int _placedModeWidth = ScreenFit.Standard.BaseWidth;
    private bool _gesturesEnabled;
    private bool _phoneScreenEnabled;
    private bool _launchAtLogin;
    private bool _welcomed;
    private int _defaultZoomSteps;
    private bool _disposed;
    private string _connectionStatus = "等待 UU 远程";
    private string _displayStatus = "正在检测显示器";
    private string _activityStatus = "准备就绪";
    private string _connectionDiagnostics = "等待 UU 会话日志";
    private string _driverStatusText = "正在检测驱动";
    private bool _isConnected;
    private bool _isDriverReady;

    public ObservableCollection<DisplayInfo> Displays { get; } = [];
    public ObservableCollection<WindowInfo> Windows { get; } = [];
    public ObservableCollection<InstalledApp> InstalledApps { get; } = [];
    public ObservableCollection<RunningApp> RunningApps { get; } = [];

    public PhoneCatalog PhoneCatalog => _phoneCatalog;
    public PhoneChoice CurrentPhoneChoice => _phoneChoice;
    public string PhoneChoiceSummary => _phoneChoice.Title + "  ·  " + _phoneChoice.Screen.SizeText;
    public string UuViewportMode
    {
        get => _uuViewportMode;
        set
        {
            var normalized = UuViewportPreset.Normalize(value);
            if (!Set(ref _uuViewportMode, normalized)) return;
            _settings.UuViewportMode = normalized;
            SaveSettings();
            ActivityStatus = _phoneDisplay is null
                ? "画面比例已更新，下次连接生效"
                : "画面比例已保存；为避免 UU 切屏失效，下次创建手机屏时生效";
        }
    }
    public int BaseWidthOption
    {
        get => _baseWidthOption;
        set
        {
            var next = Math.Clamp(value, 0, PhoneBaseWidth.Options.Count - 1);
            if (!Set(ref _baseWidthOption, next)) return;
            _settings.PhoneBaseWidth = PhoneBaseWidth.AtIndex(next);
            SaveSettings();
            OnPropertyChanged(nameof(PreviewScreenFit));
            ActivityStatus = _phoneDisplay is null
                ? "内容密度已更新，下次连接生效"
                : "内容密度已保存，下次创建手机屏时生效";
        }
    }

    public string ConnectionStatus { get => _connectionStatus; private set => Set(ref _connectionStatus, value); }
    public bool HasPlacedWindow => _placed is not null || _ledger.Load() is not null;
    public WindowKey? PlacedWindowKey => (_placed ?? _ledger.Load())?.Key;
    public string? PlacedAppName => (_placed ?? _ledger.Load())?.AppName;
    public string DisplayStatus { get => _displayStatus; private set => Set(ref _displayStatus, value); }
    public string ActivityStatus { get => _activityStatus; private set => Set(ref _activityStatus, value); }
    public string ConnectionDiagnostics { get => _connectionDiagnostics; private set => Set(ref _connectionDiagnostics, value); }
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }
    public string DriverStatusText { get => _driverStatusText; private set => Set(ref _driverStatusText, value); }
    public bool IsDriverReady { get => _isDriverReady; private set => Set(ref _isDriverReady, value); }
    public bool IsDriverInstalled => _driver.IsInstalled;
    public bool ShouldAutoInstallDriver => !_driver.IsInstalled && !_settings.DriverInstallAttempted;
    public bool CanRepairDriver => !_driver.IsInstalled || _driver.IsManagedBySnug;

    private PhoneScreenSpec CurrentPhoneScreenSpec
    {
        get
        {
            var fit = CurrentScreenFit;
            var size = fit.SizeFor(fit.BaseWidth);
            return new PhoneScreenSpec((int)size.Width, (int)size.Height,
                ScreenFit.HomeBarHeight(fit.BaseWidth), 60, 2);
        }
    }

    public bool PhoneScreenEnabled
    {
        get => _phoneScreenEnabled;
        set
        {
            if (!Set(ref _phoneScreenEnabled, value)) return;
            SaveSettings();
            UuCaptureAcceptanceAudit.Record("SessionCommand", new { command = "phone-screen-enabled-changed", enabled = value });
            if (!value && _phoneDisplay is not null && !_creatingSession)
            {
                UuCaptureAcceptanceAudit.Record("EndPhoneSessionTrigger", new { source = "phone-screen-disabled", connected = _connection.IsConnected, wantsPhoneScreen = _connection.WantsPhoneScreen(DateTimeOffset.UtcNow) });
                _ = EndPhoneSessionAsync();
            }
        }
    }

    public bool GesturesEnabled
    {
        get => _gesturesEnabled;
        set
        {
            if (!Set(ref _gesturesEnabled, value)) return;
            SaveSettings();
            SyncGestures();
        }
    }

    public bool ScreenshotEnabled
    {
        get => _settings.ScreenshotEnabled;
        set {
            if (_settings.ScreenshotEnabled == value) return;
            _settings.ScreenshotEnabled = value;
            OnPropertyChanged();
            SaveSettings();
            if(!value) _screenshots.Dismiss();
            _homeBar?.SetCameraVisible(value);
        }
    }

    public async Task TakeScreenshotAsync()
    {
        if(!ScreenshotEnabled || _showingPhoneList || _phoneDisplay is null || _placed is not { } record || _previewOpen) return;
        await _screenshots.TakeAsync(record.Key, () => !_disposed && ScreenshotEnabled &&
            !_showingPhoneList && _phoneDisplay is not null && _placed?.Key == record.Key &&
            _windowManager.IsAlive(record.Key));
    }
    public void OpenScreenshotCrop() => _screenshots.OpenCrop();
    public void CancelScreenshotCrop() => _screenshots.CancelCrop();
    public void FinishScreenshotCrop(CropRect selection) => _screenshots.FinishCrop(selection);

    private void SyncScreenshotOverlay()
    {
        _screenshotPanel?.Close();
        _screenshotPanel=null;
        if(_disposed) return;
        if(_phoneDisplay is { } display && !_showingPhoneList && _placed is not null && _screenshots.Overlay is { } shot)
        {
            if (_screenshots.IsCropping)
            {
                _homeBar?.Hide();
                _browserZoomRail?.Hide();
            }
            else ShowHomeBar(display);
            _screenshotPanel=new ScreenshotPanelWindow(this,shot);
            _screenshotPanel.PlaceOn(display);
            _screenshotPanel.Show();
        }
        else if(_phoneDisplay is { } current && !_showingPhoneList && _placed is not null && !_endingSession)
            ShowHomeBar(current);
        SyncGestures();
    }

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (!Set(ref _launchAtLogin, value)) return;
            try
            {
                StartupManager.SetEnabled(value);
                SaveSettings();
            }
            catch (Exception error)
            {
                Set(ref _launchAtLogin, !value);
                ActivityStatus = "开机启动设置失败：" + error.Message;
            }
        }
    }

    public bool Welcomed
    {
        get => _welcomed;
        private set
        {
            if (!Set(ref _welcomed, value)) return;
            SaveSettings();
        }
    }

    public int DefaultZoomSteps
    {
        get => _defaultZoomSteps;
        set
        {
            if (!Set(ref _defaultZoomSteps, Math.Clamp(value, 0, 5))) return;
            SaveSettings();
        }
    }

    public bool IsAppCatalogLoaded => _appCatalog.IsLoaded;
    public string WindowCountText => Windows.Count == 0 ? "暂无可用窗口" : $"{Windows.Count} 个窗口";
    public string InstalledAppCountText => $"{InstalledApps.Count} 个 App";
    public string RunningAppCountText => $"{RunningApps.Count} 个 App";
    public string VersionText => $"{typeof(AppController).Assembly.GetName().Version?.ToString(3) ?? "未知"} · Windows";

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppController()
    {
        _settings = _settingsStore.Load();
        _phoneCatalog = LoadPhoneCatalog();
        var savedScreen = new PhoneScreen(_settings.PhoneScreenWidth, _settings.PhoneScreenHeight,
            _settings.PhoneScreenPoints);
        if (savedScreen.IsValid && string.IsNullOrWhiteSpace(_settings.PhoneModel))
        {
            _phoneChoice = new PhoneChoice(null, savedScreen);
        }
        else if (_phoneCatalog.Find(_settings.PhoneModel) is { } savedModel)
        {
            _phoneChoice = new PhoneChoice(savedModel.Id, savedModel.Screen);
        }
        else
        {
            _phoneChoice = PhoneChoice.Standard;
        }
        _uuViewportMode = UuViewportPreset.Normalize(_settings.UuViewportMode);
        _baseWidthOption = PhoneBaseWidth.IndexOf(_settings.PhoneBaseWidth);
        _activeModeWidth = CurrentScreenFit.BaseWidth;
        _placedModeWidth = _activeModeWidth;
        _appCatalog = new AppCatalogService(_windowManager);
        _gestureHook.PlacedWindowKey = () => _placed?.Key;
        _gestureHook.NativeCloseTapped += OnNativeCloseTapped;
        _screenshots.Changed += SyncScreenshotOverlay;
        _zoomBook = new ZoomBook(_settings.AppZoomSteps);
        _phoneScreenEnabled = _settings.PhoneScreenEnabled;
        _gesturesEnabled = _settings.GesturesEnabled;
        _defaultZoomSteps = Math.Clamp(_settings.ZoomSteps, 0, 5);
        _welcomed = _settings.Welcomed;
        _launchAtLogin = StartupManager.IsEnabled();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTick;
    }

    private static PhoneCatalog LoadPhoneCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "phone-models.json");
        using var stream = File.OpenRead(path);
        return PhoneCatalog.Load(stream);
    }

    public void SetPhoneChoice(PhoneChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (!choice.Screen.IsValid) throw new ArgumentException("手机屏幕参数无效。", nameof(choice));

        if (choice.Model is { } modelId)
        {
            var model = _phoneCatalog.Find(modelId)
                ?? throw new ArgumentException("机型不在 Snug 手机型号库中。", nameof(choice));
            choice = new PhoneChoice(model.Id, model.Screen);
        }

        if (_phoneChoice == choice) return;

        _phoneChoice = choice;
        _settings.PhoneModel = choice.Model ?? string.Empty;
        _settings.PhoneScreenWidth = choice.Screen.Width;
        _settings.PhoneScreenHeight = choice.Screen.Height;
        _settings.PhoneScreenPoints = choice.Screen.Points;
        SaveSettings();
        OnPropertyChanged(nameof(CurrentPhoneChoice));
        OnPropertyChanged(nameof(PhoneChoiceSummary));
        OnPropertyChanged(nameof(PreviewScreenFit));
        ActivityStatus = $"已选择：{choice.Title}";

        // Upstream changes the modes of the same CGVirtualDisplay in place.
        // The current Windows VDD cannot do that without destroying the device,
        // invalidating UU's selected display/capture coordinates. Preserve the
        // existing monitor and apply this new model on the next full episode.
        if (_phoneDisplay is not null)
            ActivityStatus = "已保存机型；为保持 UU 当前屏幕不掉线，下次远程会话生效。";
    }

    public void StartCatalogPreview()
    {
        RefreshWindows();
        _ = LoadInstalledAppsAsync();
    }

    public void Start()
    {
        _startedAt = DateTimeOffset.UtcNow;
        RecoverInterruptedMove();
        Refresh();
        _ = LoadInstalledAppsAsync();
        _timer.Start();
        _ = RefreshDriverStatusAsync();
    }

    public void MarkWelcomed() => Welcomed = true;

    public void MarkDriverInstallAttempted()
    {
        _settings.DriverInstallAttempted = true;
        SaveSettings();
    }

    public void Refresh()
    {
        RefreshDisplays();
        RefreshWindows();
        ActivityStatus = "已刷新";
        _ = RefreshDriverStatusAsync();
    }

    public void RefreshDisplays()
    {
        Displays.Clear();
        foreach (var display in _displayEnumerator.GetDisplays()) Displays.Add(display);
        _availableSnugDisplay = _displayEnumerator.FindSnugDisplay();

        if (_phoneDisplay is not null)
            DisplayStatus = $"Snug 手机屏 · {_phoneDisplay.Bounds.Width} × {_phoneDisplay.Bounds.Height}";
        else if (_availableSnugDisplay is not null)
            DisplayStatus = "Snug 手机屏已就绪";
        else if (IsDriverReady)
            DisplayStatus = IsConnected ? "正在创建手机屏" : "Snug 手机屏已就绪";
        else if (_driver.IsInstalled && _driver.IsManagedBySnug)
            DisplayStatus = "等待 UU 远程";
        else
            DisplayStatus = _driver.IsInstalled ? "检测到外部虚拟显示驱动" : "虚拟显示驱动未安装";

        OnPropertyChanged(nameof(Displays));
    }

    public void RefreshWindows()
    {
        var next = _windowManager.ListWindows();
        SyncCollection(Windows, next, window => window.Key);
        SyncCollection(RunningApps, _appCatalog.RefreshRunning(next), app => app.ProcessId);
        OnPropertyChanged(nameof(WindowCountText));
        OnPropertyChanged(nameof(RunningAppCountText));
        if (QuitStalled is { } notice &&
            !notice.CanForce(QuitProcess.Observe(notice.Identity.ProcessId), DateTimeOffset.UtcNow))
            DismissQuitStalled();
    }

    private async Task LoadInstalledAppsAsync()
    {
        try
        {
            await _appCatalog.LoadInstalledAsync();
            SyncCollection(InstalledApps, _appCatalog.Installed, app => app.App.AppId);
            RefreshWindows();
            OnPropertyChanged(nameof(InstalledAppCountText));
            OnPropertyChanged(nameof(IsAppCatalogLoaded));
            ActivityStatus = $"已读取 {InstalledApps.Count} 个 App";
        }
        catch (Exception error)
        {
            ActivityStatus = "读取 App 目录失败：" + error.Message;
        }
    }

    public Task ActivateAppAsync(AppInfo app, bool preview) =>
        _actionLane.RunAsync(() => ActivateAppCoreAsync(app, preview));

    private async Task ActivateAppCoreAsync(AppInfo app, bool preview)
    {
        if (preview)
        {
            ActivityStatus = "预览模式不会启动真实 App";
            return;
        }

        var action = AppList.ActionFor(app, RunningApps.ToArray());
        if (action.Kind == AppActionKind.PullWindow && action.Window is not null)
        {
            await PullWindowAsync(action.Window, preview: false);
            return;
        }

        await OpenAppAsync(app);
    }

    public Task NewWindowAsync(AppInfo app, bool preview) =>
        _actionLane.RunAsync(() => NewWindowCoreAsync(app, preview));

    private async Task NewWindowCoreAsync(AppInfo app, bool preview)
    {
        if (preview)
        {
            ActivityStatus = "预览模式不会启动真实 App";
            return;
        }

        if (HasPlacedWindow)
        {
            await PutBackAsync();
            if (HasPlacedWindow)
            {
                ActivityStatus = "当前窗口尚未恢复，暂不能新建窗口";
                return;
            }
        }

        var running = RunningApps.FirstOrDefault(item => MatchesApp(item, app));
        if (running is null)
        {
            ActivityStatus = $"{app.Name} 尚未运行";
            return;
        }

        var source = running.Windows.FirstOrDefault();
        if (source is null)
        {
            ActivityStatus = $"{app.Name} 没有可用窗口";
            return;
        }

        var known = RunningApps.SelectMany(item => item.Windows)
            .Select(window => window.Key).ToHashSet();
        if (!_windowManager.NewWindow(source.Key, app, out var commandStatus))
        {
            ActivityStatus = commandStatus;
            return;
        }

        ActivityStatus = $"{commandStatus} · {app.Name}";
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(250);
            RefreshWindows();
            var created = RunningApps
                .Where(item => MatchesApp(item, app))
                .SelectMany(item => item.Windows)
                .FirstOrDefault(window => !known.Contains(window.Key));
            if (created is null) continue;

            await PullWindowAsync(created, preview: false);
            return;
        }

        ActivityStatus = $"{app.Name} 未出现新窗口，操作已超时；没有继续发送其他快捷键。";
    }
    private enum ExitObservation { Finished, Asking, TimedOut, FailedToPresent }

    private static bool ProcessHasExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private async Task<ExitObservation> ObserveExitAsync(uint pid, Func<bool> completed,
        TimeSpan duration)
    {
        var until = DateTimeOffset.UtcNow + duration;
        while (!_disposed && DateTimeOffset.UtcNow < until)
        {
            if (completed()) return ExitObservation.Finished;
            if (_windowManager.FindAskingWindow(pid) is { } dialog)
                return await PlaceAskingDialogAsync(dialog)
                    ? ExitObservation.Asking : ExitObservation.FailedToPresent;
            await Task.Delay(250);
        }

        return completed() ? ExitObservation.Finished : ExitObservation.TimedOut;
    }

    private async Task<bool> PlaceAskingDialogAsync(WindowInfo asking)
    {
        if (_phoneDisplay is null || HasPlacedWindow) return false;
        var original = _windowManager.Capture(asking, zoomSteps: 0);
        if (original is null) return false;
        try
        {
            _ledger.Save(original);
            var display = _phoneDisplay;
            if (!await Task.Run(() =>
                    _windowManager.MoveDialogTo(asking.Key, CurrentAppArea(display))))
            {
                if (_windowManager.Restore(original) || !_windowManager.IsAlive(original.Key))
                    _ledger.Clear();
                else
                    _placed = original;
                return false;
            }

            _placed = original;
            _askingDialogKey = original.Key;
            _showingPhoneList = false;
            _phoneSurface?.Hide();
            SyncGestures();
            ShowAppBackBar(display, "应用确认", false, 0);
            ShowHomeBar(display);
            _windowManager.Focus(asking.Key);
            ActivityStatus = "应用正在询问是否保存，请在手机屏上选择。";
            return true;
        }
        catch (IOException error)
        {
            ActivityStatus = "记录保存提示窗口的位置失败：" + error.Message;
            return false;
        }
    }

    private async void OnNativeCloseTapped(CloseButtonHit hit)
    {
        try
        {
            await _actionLane.RunAsync(async () =>
            {
                // A queued gesture must not apply to a replaced window or
                // to a caption that moved after the user lifted their finger.
                if (_disposed || !_connection.IsConnected || _phoneSurface?.IsVisible == true ||
                    _placed is not { } placed || placed.Key != hit.Key ||
                    !NativeCloseHitTest.Revalidate(hit)) return;
                var window = Windows.FirstOrDefault(w => w.Key == hit.Key) ??
                    new WindowInfo(placed.Key, placed.AppName, placed.Title ?? "",
                        placed.ExecutablePath, false, false);
                await CloseWindowCoreAsync(window, false);
                if (!HasPlacedWindow && _phoneSurface?.IsVisible == true)
                    ShowPhoneList();
            });
        }
        catch (Exception error)
        {
            ActivityStatus = "关闭窗口失败：" + error.Message;
        }
    }

    public Task CloseWindowAsync(WindowInfo window, bool preview) =>
        _actionLane.RunAsync(() => CloseWindowCoreAsync(window, preview));

    private async Task CloseWindowCoreAsync(WindowInfo window, bool preview)
    {
        if (preview)
        {
            ActivityStatus = "预览模式不会关闭真实窗口";
            return;
        }

        var placed = _placed ?? _ledger.Load();
        if (placed?.Key == window.Key)
        {
            await PutBackAsync();
            placed = _placed ?? _ledger.Load();
            if (placed?.Key == window.Key)
            {
                ActivityStatus = "窗口尚未恢复，暂不能关闭";
                return;
            }
        }

        if (!_windowManager.IsAlive(window.Key))
        {
            RefreshWindows();
            ActivityStatus = "窗口已经关闭";
            return;
        }

        if (!_windowManager.Close(window.Key))
        {
            ActivityStatus = "关闭请求未发送，窗口可能无响应";
            return;
        }

        ActivityStatus = $"正在关闭：{window.AppName}";
        var observation = await ObserveExitAsync(window.Key.ProcessId,
            () => !_windowManager.IsAlive(window.Key), TimeSpan.FromSeconds(8));
        RefreshWindows();
        ActivityStatus = observation switch
        {
            ExitObservation.Finished => $"已关闭：{window.AppName}",
            ExitObservation.Asking => "请在手机屏处理应用的保存提示。",
            ExitObservation.FailedToPresent => "发现应用确认弹窗，但暂时无法移到手机屏。",
            _ => "窗口仍未关闭；应用可能正在等待内部确认。"
        };
    }
    private QuitStalledNotice? _quitStalled;
    public QuitStalledNotice? QuitStalled
    {
        get => _quitStalled;
        private set => Set(ref _quitStalled, value);
    }

    public void DismissQuitStalled() => QuitStalled = null;

    public Task ForceQuitAsync(QuitStalledNotice notice) => _actionLane.RunAsync(async () =>
    {
        if (_disposed || _previewOpen || QuitStalled != notice) return;
        if (!notice.CanForce(QuitProcess.Observe(notice.Identity.ProcessId), DateTimeOffset.UtcNow))
        {
            DismissQuitStalled();
            return;
        }
        // Do not destroy a placed window whose original state is still owed,
        // nor an app that has subsequently opened its own confirmation dialog.
        if (PlacedWindowKey?.ProcessId == notice.Identity.ProcessId ||
            _windowManager.FindAskingWindow((uint)notice.Identity.ProcessId) is not null)
        {
            DismissQuitStalled();
            ActivityStatus = "App 有待处理的窗口，暂不能强制退出";
            return;
        }

        DismissQuitStalled();
        var success = await Task.Run(() => QuitProcess.TryForce(notice));
        RefreshWindows();
        ActivityStatus = success ? $"已强制退出：{notice.AppName}" : $"无法强制退出：{notice.AppName}";
    });

    public Task QuitAppAsync(RunningApp app, bool preview) =>
        _actionLane.RunAsync(() => QuitAppCoreAsync(app, preview));

    private async Task QuitAppCoreAsync(RunningApp app, bool preview)
    {
        if (preview)
        {
            ActivityStatus = "预览模式不会退出真实 App";
            return;
        }

        DismissQuitStalled();
        if (!AppList.CanQuit(app.App) || AppList.IsExcluded(app.App))
        {
            ActivityStatus = $"{app.App.Name} 不支持从资源库退出";
            return;
        }

        var placed = _placed ?? _ledger.Load();
        if (placed?.Key.ProcessId == app.ProcessId)
        {
            await PutBackAsync();
            placed = _placed ?? _ledger.Load();
            if (placed?.Key.ProcessId == app.ProcessId)
            {
                ActivityStatus = "该 App 的窗口尚未恢复，暂不能退出";
                return;
            }
        }

        // Record identity before sending normal close requests. A stale PID
        // cannot later authorize force-quit of a newly launched app.
        var identity = QuitProcess.Observe(app.ProcessId);
        if (identity is not null && !app.Windows.Any(window =>
            window.Key.ProcessId == app.ProcessId &&
            StringComparer.OrdinalIgnoreCase.Equals(window.ExecutablePath, identity.ExecutablePath)))
            identity = null;

        var requested = 0;
        foreach (var window in app.Windows)
        {
            if (_windowManager.Close(window.Key)) requested++;
        }

        if (requested == 0)
        {
            using var process = AppCatalogService.FindProcess(app);
            if (process is not null)
            {
                try { if (process.CloseMainWindow()) requested++; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        if (requested == 0 && !app.Windows.Any(window =>
            _windowManager.IsAlive(window.Key) && !_windowManager.IsResponsive(window.Key)))
        {
            ActivityStatus = $"无法向 {app.App.Name} 发送退出请求";
            return;
        }

        ActivityStatus = $"正在退出：{app.App.Name}";
        var observation = await ObserveExitAsync((uint)app.ProcessId,
            () => ProcessHasExited(app.ProcessId), TimeSpan.FromSeconds(10));
        RefreshWindows();
        if (observation == ExitObservation.TimedOut && identity is not null &&
            identity.Matches(QuitProcess.Observe(app.ProcessId)) &&
            PlacedWindowKey?.ProcessId != app.ProcessId &&
            _windowManager.FindAskingWindow((uint)app.ProcessId) is null)
        {
            QuitStalled = new(app.App.Name, identity, DateTimeOffset.UtcNow.AddMinutes(2));
        }
        ActivityStatus = observation switch
        {
            ExitObservation.Finished => $"已退出：{app.App.Name}",
            ExitObservation.Asking => "请在手机屏处理应用的保存提示。",
            ExitObservation.FailedToPresent => "发现应用确认弹窗，但暂时无法移到手机屏。",
            _ => $"{app.App.Name} 尚未退出。"
        };
    }
    private async Task OpenAppAsync(AppInfo app)
    {
        if (_phoneDisplay is null)
        {
            ActivityStatus = "没有可用的 Snug 手机屏";
            return;
        }

        if (_placed is not null)
        {
            await PutBackAsync();
            if (_placed is not null) return;
        }

        var knownHandles = Windows.Select(window => window.Key.Handle).ToHashSet();
        try
        {
            AppLauncher.Launch(app);
            ActivityStatus = $"正在打开：{app.Name}";

            for (var attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(250);
                RefreshWindows();

                var matches = RunningApps.Where(running => MatchesApp(running, app)).ToArray();
                var window = matches.SelectMany(running => running.Windows)
                    .FirstOrDefault(candidate => !knownHandles.Contains(candidate.Key.Handle))
                    ?? matches.SelectMany(running => running.Windows).FirstOrDefault();

                if (window is null) continue;
                await PullWindowAsync(window, preview: false);
                return;
            }

            ActivityStatus = $"{app.Name} 已启动，但没有找到可显示的窗口";
        }
        catch (Exception error)
        {
            ActivityStatus = $"打开 {app.Name} 失败：" + error.Message;
        }
    }

    private static bool MatchesApp(RunningApp running, AppInfo app)
    {
        if (string.Equals(running.App.AppId, app.AppId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(app.ExecutablePath)
            && string.Equals(running.App.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(running.App.Name, app.Name, StringComparison.OrdinalIgnoreCase)
            || running.Windows.Any(window => window.Title.Contains(app.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static void SyncCollection<T, TKey>(
        ObservableCollection<T> target,
        IReadOnlyList<T> fresh,
        Func<T, TKey> key)
        where TKey : notnull
    {
        var latest = fresh.ToDictionary(key);
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!latest.ContainsKey(key(target[index]))) target.RemoveAt(index);
        }

        for (var index = 0; index < fresh.Count; index++)
        {
            var item = fresh[index];
            var itemKey = key(item);
            var current = -1;
            for (var candidate = 0; candidate < target.Count; candidate++)
            {
                if (EqualityComparer<TKey>.Default.Equals(key(target[candidate]), itemKey))
                {
                    current = candidate;
                    break;
                }
            }

            if (current < 0) target.Insert(Math.Min(index, target.Count), item);
            else
            {                if (!EqualityComparer<T>.Default.Equals(target[current], item)) target[current] = item;
                if (current != index) target.Move(current, Math.Min(index, target.Count - 1));
            }
        }
    }

    public async Task<DriverInstallResult> InstallDriverAsync()
    {
        ActivityStatus = "正在安装虚拟显示驱动";

        if (_placed is not null)
        {
            await PutBackAsync();
            if (_placed is not null)
                return new(false, "窗口尚未恢复，暂不能修复驱动。");
        }

        if (_driver.IsManagedBySnug && _displayEnumerator.FindSnugDisplay() is not null)
        {
            if (!await ChangeOwnedDisplayAsync(false))
                return new(false, "手机屏尚未关闭，暂不能修复驱动。");
        }

        var result = await _driver.InstallOrRepairAsync();
        ActivityStatus = result.Message;
        await RefreshDriverStatusAsync();
        RefreshDisplays();
        return result;
    }

    public async Task<bool> FinishForQuitAsync()
    {
        _timer.Stop();
        await PutBackAsync();
        if (_placed is not null)
        {
            _timer.Start();
            ActivityStatus = "窗口尚未恢复，Snug 保持运行";
            return false;
        }

        _gestureHook.Stop();
        _gestureHook.SetRegion(null);
        _homeBar?.Close();
        _homeBar = null;
        _browserZoomRail?.Close();
        _browserZoomRail = null;
        _appBackBar?.Close();
        _appBackBar = null;
        _phoneSurface?.Close();
        _phoneSurface = null;
        _phoneDisplay = null;
        if (_driver.IsManagedBySnug && _displayEnumerator.FindSnugDisplay() is not null
            && !await ChangeOwnedDisplayAsync(false))
        {
            _timer.Start();
            ActivityStatus = "手机屏尚未关闭，Snug 保持运行以便重试";
            return false;
        }
        return true;
    }

    public void ClosePreview(PhoneSurfaceWindow window)
    {
        if (!ReferenceEquals(_phoneSurface, window) || !window.IsPreview) return;
        _phoneSurface = null;
        _previewOpen = false;
    }

    public void OpenPhonePreview()
    {
        if (_phoneDisplay is not null)
        {
            ShowPhoneList();
            return;
        }
        if (_phoneSurface is { IsVisible: true } && _previewOpen)
        {
            _phoneSurface.Activate();
            return;
        }

        _previewOpen = true;
        _phoneSurface = new PhoneSurfaceWindow(this, preview: true)
        {
            Width = CurrentPhoneScreenSpec.Width,
            Height = CurrentPhoneScreenSpec.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        _phoneSurface.Show();
        RefreshWindows();
    }

    // DisplayInfo.Bounds is in native physical pixels. Reserve the exact
    // HomeBar and UU footer height; all Win32 placement uses physical scope.
    private WindowBounds CurrentAppArea(DisplayInfo display) => display.Bounds with
    {
        Height = UuMobileOverlaySafeArea.ContentHeight(display.Bounds.Width, display.Bounds.Height)
    };

    /// <summary>Only the Snug-owned monitor may change, and only to a VDD-advertised mode.</summary>
    private async Task<bool> ChangeOwnedDisplayAsync(bool start)
    {
        var before = OtherDisplayGuard.Capture();
        var changed = start ? await _driver.StartDisplayAsync() : await _driver.StopDisplayAsync();
        if (changed && start)
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < until && _displayEnumerator.FindSnugDisplay() is null)
                await Task.Delay(250);
        }
        if (!await OtherDisplayGuard.PreserveAsync(before))
        {
            ActivityStatus = "其他显示器的原始模式未能完全恢复，请检查 Windows 显示设置";
            return false;
        }
        return changed;
    }

    private async Task<bool> ChangePhoneModeAsync(int width)
    {
        var fit = CurrentScreenFit;
        if (!fit.Widths.Contains(width) || _phoneDisplay is null) return false;
        // Windows signed VDD has a validated 20-point mode grid. Round up
        // rather than requesting one of the unsupported intermediate widths.
        width = VddResolutionProfile.SnapWidth(CurrentDisplayChoice, width,
            CurrentDisplayInsetPoints, CurrentPreferredBaseWidth);
        var pixels = fit.PixelSizeFor(width);
        var mode = new VddResolutionMode((int)pixels.Width, (int)pixels.Height, 60);
        await _modeGate.WaitAsync();
        try
        {
            var display = _phoneDisplay;
            if (display is null) return false;
            var previousGeometry = display.Bounds;
            var changingMode = !_modeSwitcher.HasMode(display, mode);
            // WinForms screen bounds are DPI-virtualized (720x1223 on this
            // user's 1080x1834 VDD). Always compare/set the raw Win32 mode.
            // TryChange is a no-op when the existing raw mode already matches.
            var protectedModes = changingMode ? OtherDisplayGuard.Capture() : [];
            var switched = await Task.Run(() =>
            {
                var success = _modeSwitcher.TryChange(display, mode, out var message);
                return (success, message);
            });
            if (changingMode && !await OtherDisplayGuard.PreserveAsync(protectedModes))
            {
                ActivityStatus = "其他显示器的原始模式未能完全恢复，请检查 Windows 显示设置";
                return false;
            }
            if (!switched.success)
            {
                ActivityStatus = switched.message;
                return false;
            }
            // Windows reports the newly switched native mode before it updates
            // the DPI-virtualized desktop rectangle. The former loop exited as
            // soon as EnumDisplaySettings matched, so the old 720x1223 bounds
            // were reused on a new 820x1392 display. That left the browser and
            // HomeBar covering only part of the actual virtual screen.
            DisplayInfo? found = null;
            var geometrySettled = !changingMode;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                found = _displayEnumerator.FindSnugDisplay();
                if (found is not null && _modeSwitcher.HasMode(found, mode))
                {
                    geometrySettled = !changingMode
                        || found.Bounds.Width != previousGeometry.Width
                        || found.Bounds.Height != previousGeometry.Height;
                    if (geometrySettled) break;
                }
                await Task.Delay(150);
            }
            if (found is null || !_modeSwitcher.HasMode(found, mode) || !geometrySettled)
            {
                ActivityStatus = "Windows 尚未确认 Snug 手机屏的新分辨率。";
                return false;
            }
            _phoneDisplay = found;
            _availableSnugDisplay = found;
            _activeModeWidth = width;
            _phoneSurface?.PlaceOn(found);
            _homeBar?.PlaceOn(found);
            _browserZoomRail?.PlaceOn(found);
            _appBackBar?.PlaceOn(found);
            _screenshotPanel?.PlaceOn(found);
            _gestureHook.SetRegion(found, TouchScale());
            SyncGestures();
            RefreshDisplays();
            return true;
        }
        catch (Exception error)
        {
            ActivityStatus = "手机屏模式调整失败：" + error.Message;
            return false;
        }
        finally { _modeGate.Release(); }
    }

    private async Task<bool> FitMovedWindowAsync(WindowInfo window)
    {
        var fit = CurrentScreenFit;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (_phoneDisplay is not { } display) return false;
            var actual = _windowManager.ReadBounds(window.Key);
            if (actual is null) return false;
            var area = CurrentAppArea(display);
            if (Math.Abs(actual.Value.X - area.X) > 3 || Math.Abs(actual.Value.Y - area.Y) > 3)
            {
                ActivityStatus = "应用拒绝移动到手机屏的可用区域，已保留原窗口位置。";
                return false;
            }
            var needed = fit.WidthToFit(
                new PhoneSize(actual.Value.Width, actual.Value.Height),
                new PhoneSize(area.Width, area.Height), _activeModeWidth);
            if (needed <= _activeModeWidth)
            {
                if (actual.Value.Width > area.Width + 2 || actual.Value.Height > area.Height + 2)
                {
                    ActivityStatus = "应用的最小窗口尺寸超过手机屏最大档位，无法完整显示。";
                    return false;
                }
                return true;
            }
            if (!await ChangePhoneModeAsync(needed))
            {
                ActivityStatus = "手机屏无法切到容纳此应用的档位，窗口已返回原位置。";
                return false;
            }
            if (!_windowManager.MoveTo(window.Key, CurrentAppArea(_phoneDisplay!)))
            {
                ActivityStatus = "调整屏幕后 App 未能重新铺放。";
                return false;
            }
        }
        return true;
    }

    private async Task RestoreListModeAsync()
    {
        if (_phoneDisplay is null || !_showingPhoneList) return;
        await ChangePhoneModeAsync(CurrentScreenFit.BaseWidth);
    }

    private async Task RevealPlacedWindowAsync(WindowRestoreRecord placed)
    {
        if (_phoneDisplay is null || !_windowManager.IsResponsive(placed.Key)) return;
        if (!await ChangePhoneModeAsync(_placedModeWidth)) return;
        if (_phoneDisplay is not { } display ||
            !_windowManager.MoveTo(placed.Key, CurrentAppArea(display)))
        {
            ActivityStatus = "无法在当前手机分辨率下重新铺放 App。";
            return;
        }
        _showingPhoneList = false;
        _phoneSurface?.Hide();
        SyncGestures();
        // Upstream PhoneScreenWindows presents the bottom HomeBar only while
        // a placed app is visible; it does not cover the app with a top toolbar.
        _appBackBar?.Hide();
        ShowHomeBar(display);
        _windowManager.Focus(placed.Key);
    }

    public Task PullWindowAsync(WindowInfo window, bool preview) =>
        _actionLane.RunAsync(() => PullWindowCoreAsync(window, preview));

    private async Task PullWindowCoreAsync(WindowInfo window, bool preview)
    {
        if (preview)
        {
            ActivityStatus = "预览模式不会移动真实窗口";
            return;
        }
        if (_phoneDisplay is null)
        {
            ActivityStatus = "没有可用的 Snug 手机屏";
            return;
        }
        if (_pullingWindow) return;
        _pullingWindow = true;

        try
        {
            if ((_placed ?? _ledger.Load()) is { } existing && existing.Key == window.Key)
            {
                await RevealPlacedWindowAsync(existing);
                return;
            }

            if (HasPlacedWindow)
            {
                await PutBackAsync();
                if (HasPlacedWindow) return;
            }

            if (!await ChangePhoneModeAsync(CurrentScreenFit.BaseWidth))
                return;

            _connection.NoteSelfInducedChange(DateTimeOffset.UtcNow);
            var display = _phoneDisplay!;
            var canZoom = ZoomBook.IsZoomable(window.ExecutablePath, window.AppName);
            var zoomSteps = canZoom
                ? _zoomBook.StepsFor(window.ExecutablePath, window.AppName, DefaultZoomSteps)
                : 0;

            var record = await Task.Run(() =>
            {
                if (!_windowManager.IsResponsive(window.Key))
                    throw new InvalidOperationException("App 暂时无响应，未移动窗口。请稍后重试。");

                var saved = _windowManager.Capture(window, zoomSteps)
                    ?? throw new InvalidOperationException("读取窗口原始位置失败，未移动窗口。");
                _ledger.Save(saved);

                if (zoomSteps != 0 && !_windowManager.Zoom(window, zoomSteps))
                {
                    _ledger.Clear();
                    throw new InvalidOperationException("应用没有响应字号快捷键，窗口保持原位。");
                }

                if (!_windowManager.MoveTo(window.Key, CurrentAppArea(display)))
                {
                    var zoomRestored = zoomSteps == 0 || _windowManager.Zoom(window, -zoomSteps);
                    var positionRestored = _windowManager.Restore(saved);
                    if (zoomRestored && positionRestored) _ledger.Clear();
                    else
                    {
                        var pending = saved with { ZoomSteps = zoomRestored ? 0 : zoomSteps };
                        _placed = pending;
                        _ledger.Save(pending);
                    }
                    throw new InvalidOperationException("窗口没有成功移动到手机屏，已尝试恢复原状。");
                }

                return saved;
            });

            _placed = record;
            _browserZoomIndex = BrowserZoomLevels.DefaultIndex;
            _browserZoomKnown = false;
            if (!await FitMovedWindowAsync(window))
            {
                await PutBackAsync();
                ShowPhoneList();
                return;
            }

            // Read the actual post-fit dimensions. This is the only safe
            // signature for matching an app window recreated by Windows.
            var filled = _windowManager.ReadBounds(window.Key);
            if (filled is null)
            {
                await PutBackAsync();
                ShowPhoneList();
                ActivityStatus = "窗口铺屏后无法读取实际大小，已尝试还原";
                return;
            }
            record = record with { FilledBounds = filled };
            _placed = record;
            _ledger.Save(record);

            display = _phoneDisplay!;
            _placedModeWidth = _activeModeWidth;
            _showingPhoneList = false;

            if (canZoom)
            {
                _zoomBook.Remember(window.ExecutablePath, window.AppName, zoomSteps);
                SaveSettings();
            }

            _phoneSurface?.Hide();
            SyncGestures();
            _appBackBar?.Hide();
            ShowHomeBar(display);
            ActivityStatus = $"已打开：{window.AppName}";
        }
        catch (Exception error)
        {
            ActivityStatus = error.Message;
        }
        finally
        {
            _pullingWindow = false;
        }
    }

    public Task PutBackAsync() => _actionLane.RunAsync(PutBackCoreAsync);

    // A user-requested window restore is not a display/session termination.
    // Keep the same monitor, the same catalog, and its current display mode.
    public Task RestoreManagedWindowAsync() => _actionLane.RunAsync(async () =>
    {
        if (_disposed || _endingSession || _phoneDisplay is not { } display || !HasPlacedWindow) return;
        UuCaptureAcceptanceAudit.Record("RestoreManagedWindow", new { stage = "begin", deviceName = display.DeviceName });
        await PutBackCoreAsync();
        var restored = !HasPlacedWindow;
        if (restored && !_endingSession && !_disposed && _phoneDisplay is not null)
            ShowPhoneList(restoreListMode: false);
        UuCaptureAcceptanceAudit.Record("RestoreManagedWindow", new
        {
            stage = "complete", restored, placedWindow = HasPlacedWindow,
            phoneDisplay = _phoneDisplay is not null, deviceName = _phoneDisplay?.DeviceName,
            sameDisplay = _phoneDisplay?.DeviceName == display.DeviceName,
            sameBounds = _phoneDisplay?.Bounds == display.Bounds
        });
    });

    private async Task PutBackCoreAsync()
    {
        _screenshots.Dismiss();
        var record = _placed ?? _ledger.Load();
        if (record is null) return;

        _connection.NoteSelfInducedChange(DateTimeOffset.UtcNow);
        var result = await Task.Run(() =>
        {
            if (!_windowManager.IsAlive(record.Key))
            {
                // Keep the original position when an app destroys the HWND.
                var remembered = _leftBehind.Add(record, DateTimeOffset.UtcNow);
                _ledger.Clear();
                return (Restored: true,
                    Message: remembered
                        ? record.ZoomSteps != 0 && ZoomRecovery.ScopeOf(record.ExecutablePath) == ZoomScope.Workspace
                            ? $"尚未恢复 {record.AppName} 的字号；重新打开原工作区时将检查恢复"
                            : "窗口已消失；已记录原位置，重新出现时将检查是否需要还原"
                        : "原窗口已关闭；没有可匹配的铺屏记录",
                    Remaining: (WindowRestoreRecord?)null);
            }

            if (!_windowManager.IsResponsive(record.Key))
                return (Restored: false, Message: "App 暂时无响应，保留还原记录，恢复后可重试", Remaining: (WindowRestoreRecord?)record);

            var window = new WindowInfo(record.Key, record.AppName, string.Empty,
                record.ExecutablePath, false, false);
            if (record.ZoomSteps != 0)
            {
                if (!_windowManager.Zoom(window, -record.ZoomSteps))
                    return (Restored: false, Message: "字号没有缩回，保留还原记录", Remaining: (WindowRestoreRecord?)record);
                record = record with { ZoomSteps = 0 };
                _ledger.Save(record);
            }

            if (!_windowManager.Restore(record))
                return (Restored: false, Message: "窗口位置恢复失败，保留还原记录", Remaining: (WindowRestoreRecord?)record);

            _ledger.Clear();
            return (Restored: true, Message: "窗口已还原", Remaining: (WindowRestoreRecord?)null);
        });

        _placed = result.Remaining;
        if (result.Restored && _askingDialogKey == record.Key)
            _askingDialogKey = null;
        ActivityStatus = result.Message;
        _homeBar?.Hide();
        _browserZoomRail?.Hide();
        _browserZoomKnown = false;
        _appBackBar?.Hide();
        if (result.Restored) _placedModeWidth = CurrentScreenFit.BaseWidth;
        if (_phoneSurface is { IsVisible: false } && _phoneDisplay is not null) _phoneSurface.Show();
        SyncGestures();
    }

    public Task ReturnToPlacedAsync() => _actionLane.RunAsync(ReturnToPlacedCoreAsync);

    private async Task ReturnToPlacedCoreAsync()
    {
        if ((_placed ?? _ledger.Load()) is { } placed)
            await RevealPlacedWindowAsync(placed);
    }

    public void ShowPhoneList(bool restoreListMode = true)
    {
        _showingPhoneList = true;
        _screenshots.Dismiss();
        OnPropertyChanged(nameof(PlacedWindowKey));
        OnPropertyChanged(nameof(PlacedAppName));
        _homeBar?.Hide();
        _browserZoomRail?.Hide();
        _appBackBar?.Hide();
        if (_phoneSurface is { } surface && _phoneDisplay is { } display)
        {
            if (!surface.IsVisible) surface.Show();
            surface.PlaceOn(display);
        }
        SyncGestures();
        RefreshWindows();
        if (restoreListMode) _ = RestoreListModeAsync();
    }

    private async Task CompleteAskingDialogAsync()
    {
        try
        {
            await PutBackAsync();
            if (!HasPlacedWindow)
            {
                ShowPhoneList();
                ActivityStatus = "已返回 App 资源库";
            }
        }
        finally
        {
            _recoveringDialog = false;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _screenshots.Expire();
        if (_screenshots.Overlay is not null &&
            (_phoneDisplay is null || _placed is null || _showingPhoneList ||
             !_windowManager.IsAlive(_placed.Key)))
            _screenshots.Dismiss();
        // On Windows 10, the secondary monitor taskbar may promote itself after
        // the VDD mode settles. Keep upstream's HomeBar visible without taking
        // keyboard focus; the method is a no-op unless that taskbar overlaps it.
        _homeBar?.EnsureAboveTaskbar();

        if (_askingDialogKey is { } asking
            && !_recoveringDialog && !_windowManager.IsAlive(asking))
        {
            _recoveringDialog = true;
            _ = CompleteAskingDialogAsync();
        }

        var now = DateTimeOffset.UtcNow;
        var signals = _uuDetector.Sample();
        ConnectionDiagnostics = _uuDetector.LastSignalSummary + " · " + _uuDetector.LocalControllerSummary;
        var transitions = _connection.Update(signals, now);
        var wantsPhoneScreen = _connection.WantsPhoneScreen(now);
        var keepPhoneDisplay = _displayHold.ShouldKeep(
            wantsPhoneScreen || (_connection.IsConnected && _phoneDisplay is not null),
            PhoneScreenEnabled, _phoneDisplay is not null, now);
        if (transitions.Count > 0 && _phoneDisplay is not null) SyncGestures();
        if (transitions.Count > 0 || now - _lastAcceptanceAuditAt >= TimeSpan.FromSeconds(1))
        {
            UuCaptureAcceptanceAudit.Record("SessionState", new
            {
                captureActive = signals.CaptureActive,
                captureVerifiedInactive = signals.CaptureVerifiedInactive,
                captureVerifiedActive = signals.CaptureVerifiedActive,
                logFallback = signals.LogFallback,
                isPresent = signals.IsPresent,
                controllerCount = signals.Controllers?.Count ?? (signals.Controller is null ? 0 : 1),
                controllerKinds = signals.Controllers?.Select(c => c.Kind.ToString()).ToArray(),
                connected = _connection.IsConnected,
                wantsPhoneScreen,
                transitions = transitions.Select(t => t.Kind.ToString()).ToArray(),
                phoneScreenEnabled = PhoneScreenEnabled,
                presentationDismissed = false, // Legacy audit field; no local dismissal state.
                endingSession = _endingSession,
                creatingSession = _creatingSession,
                phoneDisplay = _phoneDisplay is not null,
                placedWindow = _placed is not null,
                heldPhoneDisplay = _displayHold.IsPaused,
                snugDisplay = _displayEnumerator.FindSnugDisplay() is not null,
                driverManaged = _driver.IsManagedBySnug
            });
            _lastAcceptanceAuditAt = now;
        }

        IsConnected = _connection.IsConnected;
        ConnectionStatus = !IsConnected ? "等待 UU 远程" : signals.Evidence switch
        {
            UuEvidenceKind.ControllerTable => "UU 已连接 · " +
                (signals.Controllers?.Any(c => c.Kind == ControllerKind.Phone) == true ||
                 signals.Controller?.Kind == ControllerKind.Phone ? "手机或平板" :
                 signals.Controllers is { Count: > 0 } names && names.All(c => c.Kind == ControllerKind.Desktop) ||
                 signals.Controller?.Kind == ControllerKind.Desktop ? "电脑" : "设备未知"),
            UuEvidenceKind.Session => "UU 已连接 · 设备未知",
            _ => "UU 兼容模式 · 设备未知"
        };
        if (_displayHold.IsPaused && keepPhoneDisplay)
            ConnectionStatus = "UU 已暂停 · 手机屏保留";

        if (now - _lastDisplayScan > TimeSpan.FromSeconds(2))
        {
            _lastDisplayScan = now;
            RefreshDisplays();
        }
        if (now - _lastWindowScan > TimeSpan.FromSeconds(2))
        {
            _lastWindowScan = now;
            RefreshWindows();
            CheckLeftBehindReturns(now);
        }

        if (!_recoveringLostWindow && !_pullingWindow && !_recoveringDialog
            && _placed is { } current && !_windowManager.IsAlive(current.Key))
        {
            _recoveringLostWindow = true;
            _ = RecoverLostWindowAsync();
        }
        if (now - _lastDriverScan > TimeSpan.FromSeconds(4))
        {
            _lastDriverScan = now;
            _ = RefreshDriverStatusAsync();
        }

        if (keepPhoneDisplay)
        {
            if (_phoneDisplay is null && !_creatingSession && !_endingSession) _ = StartPhoneSessionAsync();
        }
        else if (_phoneDisplay is not null && !_previewOpen && !_endingSession && !_creatingSession)
        {
            UuCaptureAcceptanceAudit.Record("EndPhoneSessionTrigger", new { source = "on-tick-no-longer-wanted", connected = _connection.IsConnected, wantsPhoneScreen, dismissed = false, phoneScreenEnabled = PhoneScreenEnabled });
            _ = EndPhoneSessionAsync();
        }
        else if (!_creatingSession && !_endingSession && !_orphanCleanupStarted
            && now - _startedAt > TimeSpan.FromSeconds(4)
            && !keepPhoneDisplay
            && _driver.IsManagedBySnug)
        {
            _orphanCleanupStarted = true;
            _ = ClearOrphanedDisplayAsync();
        }
    }

    private async Task RefreshDriverStatusAsync()
    {
        ApplyDriverStatus(await _driver.PingAsync());
    }

    private void ApplyDriverStatus(bool online)
    {
        IsDriverReady = online && _driver.IsManagedBySnug;
        DriverStatusText = _driver.IsRebootRequired
            ? "已安装 · 重启后生效"
            : _driver.IsManagedBySnug && !_driver.IsInstallComplete
                ? "安装未完成 · 可修复"
                : IsDriverReady
                    ? "已就绪"
                    : _driver.IsInstalled
                        ? (_driver.IsManagedBySnug ? "已安装 · 等待连接" : "检测到外部驱动")
                        : "未安装";
        OnPropertyChanged(nameof(CanRepairDriver));
        OnPropertyChanged(nameof(IsDriverInstalled));
        if (_phoneDisplay is null && !IsDriverReady)
            DisplayStatus = _driver.IsInstalled && _driver.IsManagedBySnug
                ? "等待 UU 远程"
                : _driver.IsInstalled ? "检测到外部虚拟显示驱动" : "虚拟显示驱动未安装";
    }

    private async Task RestartPhoneSessionAsync()
    {
        if (_creatingSession || _endingSession || _phoneDisplay is null) return;
        _creatingSession = true;
        try
        {
            await EndPhoneSessionAsync();
            if (_phoneDisplay is not null)
                ActivityStatus = "当前应用窗口尚未恢复，新机型将在下次连接时应用";
        }
        finally
        {
            _creatingSession = false;
        }
    }

    private async Task StartPhoneSessionAsync()
    {
        if (_creatingSession || _endingSession || _disposed) return;
        _creatingSession = true;
        UuCaptureAcceptanceAudit.Record("StartPhoneSession", new { stage = "begin", driverManaged = _driver.IsManagedBySnug });
        _orphanCleanupStarted = false;
        DisplayStatus = "正在创建手机屏";
        ActivityStatus = "正在创建手机屏";

        try
        {
            if (!_driver.IsManagedBySnug)
            {
                await RefreshDriverStatusAsync();
                throw new InvalidOperationException(_driver.IsInstalled
                    ? "检测到其他软件管理的虚拟显示驱动，Snug 未接管它。"
                    : "虚拟显示驱动尚未安装，请在设置中安装驱动。");
            }
            if (_driver.IsRebootRequired)
                throw new InvalidOperationException("驱动安装完成，重启 Windows 后即可使用 Snug 手机屏。");
            if (!_driver.IsInstallComplete)
                throw new InvalidOperationException("虚拟显示驱动安装未完成，请在设置中修复驱动。");
            _connection.NoteSelfInducedChange(DateTimeOffset.UtcNow);
            _activeDisplayPhoneChoice = _phoneChoice;
            _activeDisplayInsetPoints = UuViewportPreset.VerticalInsetPoints(_uuViewportMode);
            _activeDisplayPreferredBaseWidth = PhoneBaseWidth.AtIndex(_baseWidthOption);
            _driver.WritePhoneProfile(_activeDisplayPhoneChoice,
                CurrentDisplayInsetPoints, CurrentPreferredBaseWidth);
            if (!await ChangeOwnedDisplayAsync(true))
                throw new InvalidOperationException("无法启动 Snug 手机屏。请在设置中修复虚拟显示驱动。");
            // Startup already verified driver readiness and the owned display.
            ApplyDriverStatus(true);
            UuCaptureAcceptanceAudit.Record("StartPhoneSession", new { stage = "topology-ready" });

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            DisplayInfo? display = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                display = _displayEnumerator.FindSnugDisplay();
                if (display is not null) break;
                await Task.Delay(250);
            }

            if (display is null)
                throw new InvalidOperationException("驱动已响应，但 Windows 尚未识别 Snug Phone 显示器。");

            ActivatePhoneDisplay(display);
            UuCaptureAcceptanceAudit.Record("StartPhoneSession", new { stage = "mode-start" });
            if (await ChangePhoneModeAsync(CurrentScreenFit.BaseWidth))
            {
                DisplayStatus = $"Snug 手机屏 · {_phoneDisplay!.Bounds.Width} × {_phoneDisplay.Bounds.Height}";
                ActivityStatus = "手机屏已就绪";
                UuCaptureAcceptanceAudit.Record("StartPhoneSession", new { stage = "ready", displayFound = true });
            }
            else
            {
                DisplayStatus = "手机屏分辨率尚未匹配机型";
            }
        }
        catch (Exception error)
        {
            UuCaptureAcceptanceAudit.Record("StartPhoneSession", new { stage = "failed", errorType = error.GetType().Name });
            ActivityStatus = error.Message;
            DisplayStatus = "手机屏创建失败";
            _ = RefreshDriverStatusAsync();
            if (_driver.IsManagedBySnug)
                await ChangeOwnedDisplayAsync(false);
        }
        finally
        {
            _creatingSession = false;
        }
    }

    private void ActivatePhoneDisplay(DisplayInfo display)
    {
        if (_previewOpen)
        {
            _phoneSurface?.Close();
            _phoneSurface = null;
            _previewOpen = false;
        }
        if (_phoneSurface is null || !_phoneSurface.IsVisible || _phoneSurface.IsPreview)
        {
            _phoneSurface?.Close();
            _phoneSurface = new PhoneSurfaceWindow(this, preview: false);
            _phoneSurface.PlaceOn(display);
            _phoneSurface.Show();
        }

        _phoneDisplay = display;
        _activeModeWidth = CurrentScreenFit.BaseWidth;
        _placedModeWidth = _activeModeWidth;
        _showingPhoneList = true;
        _gestureHook.SetRegion(display, TouchScale());
        SyncGestures();
    }

    private void ShowHomeBar(DisplayInfo display)
    {
        if (_screenshots.IsCropping) return;
        _homeBar ??= new HomeBarWindow(this);
        var record = _placed ?? _ledger.Load();
        _homeBar.SetCameraVisible(ScreenshotEnabled);
        _homeBar.PlaceOn(display);
        _homeBar.Show();
        _homeBar.PlaceOn(display);
        var isBrowser = record is not null &&
            BrowserZoomLevels.IsBrowser(record.ExecutablePath, record.AppName);
        if (isBrowser)
        {
            _browserZoomRail ??= new BrowserZoomRailWindow(this);
            _browserZoomRail.PlaceOn(display);
            _browserZoomRail.SetLevel(_browserZoomKnown ? _browserZoomIndex : null);
            _browserZoomRail.Show();
            _browserZoomRail.PlaceOn(display);
        }
        else _browserZoomRail?.Hide();
    }

    private void ShowAppBackBar(DisplayInfo display, string appName, bool canZoom, int zoomSteps)
    {
        _appBackBar ??= new AppBackBarWindow(this);
        _appBackBar.SetApp(appName, canZoom, zoomSteps);
        _appBackBar.PlaceOn(display);
        _appBackBar.Show();
    }

    public void SetCurrentBrowserZoomIndex(int index)
    {
        var record = _placed ?? _ledger.Load();
        if (record is null || _phoneDisplay is null || _showingPhoneList
            || !BrowserZoomLevels.IsBrowser(record.ExecutablePath, record.AppName)) return;
        index = BrowserZoomLevels.ClampIndex(index);
        var browser = new WindowInfo(record.Key, record.AppName, string.Empty,
            record.ExecutablePath, false, false);
        if (!_windowManager.ApplyBrowserZoom(browser, index))
        {
            ActivityStatus = "浏览器未响应缩放操作";
            return;
        }
        _browserZoomIndex = index;
        _browserZoomKnown = true;
        _browserZoomRail?.SetLevel(index);
        ActivityStatus = $"网页缩放 {BrowserZoomLevels.PercentAt(index)}%";
    }

    public void AdjustCurrentAppZoom(int delta)
    {
        var record = _placed ?? _ledger.Load();
        if (record is null || !ZoomBook.IsZoomable(record.ExecutablePath, record.AppName)) return;
        var next = Math.Clamp(record.ZoomSteps + delta, ZoomBook.MinimumSteps, ZoomBook.MaximumSteps);
        if (next == record.ZoomSteps) return;

        var window = new WindowInfo(record.Key, record.AppName, string.Empty,
            record.ExecutablePath, false, false);
        if (!_windowManager.Zoom(window, delta))
        {
            ActivityStatus = "字号调整失败，窗口保持原状";
            return;
        }

        var updated = record with { ZoomSteps = next };
        _placed = updated;
        _ledger.Save(updated);
        _zoomBook.Remember(record.ExecutablePath, record.AppName, next);
        SaveSettings();
        _appBackBar?.SetZoomState(next);
        ActivityStatus = $"{record.AppName} · 字号 {next:+#;-#;0} 级";
    }

    private double TouchScale() => (double)Math.Max(1, _activeModeWidth)
        / Math.Max(1, CurrentScreenFit.BaseWidth);

    private void SyncGestures()
    {
        if (!GesturesEnabled || _phoneDisplay is null || !_connection.IsConnected
            || _phoneSurface?.IsVisible == true || _screenshots.IsCropping)
        {
            _gestureHook.Stop();
            _gestureHook.SetRegion(null);
            return;
        }

        _gestureHook.SetRegion(_phoneDisplay, TouchScale());
        if (!_gestureHook.IsRunning && !_gestureHook.Start())
            ActivityStatus = "手势监听启动失败";
    }

    private async Task EndPhoneSessionAsync()
    {
        if (_endingSession) return;
        _displayHold.Reset();
        _endingSession = true;
        UuCaptureAcceptanceAudit.Record("EndPhoneSession", new { stage = "begin", connected = _connection.IsConnected, wantsPhoneScreen = _connection.WantsPhoneScreen(DateTimeOffset.UtcNow), dismissed = false, phoneScreenEnabled = PhoneScreenEnabled, placedWindow = _placed is not null, phoneDisplay = _phoneDisplay is not null });
        await PutBackAsync();
        UuCaptureAcceptanceAudit.Record("EndPhoneSession", new { stage = "after-putback", placedWindow = _placed is not null, putBackStatus = ActivityStatus, putBackSucceeded = ActivityStatus == "窗口已还原" });
        if (_placed is not null)
        {
            UuCaptureAcceptanceAudit.Record("EndPhoneSession", new { stage = "blocked-window-restore", placedWindow = true });
            ActivityStatus = "窗口尚未恢复，保留手机屏以便重试";
            _endingSession = false;
            return;
        }

        _gestureHook.Stop();
        _gestureHook.SetRegion(null);
        _homeBar?.Close();
        _homeBar = null;
        _browserZoomRail?.Close();
        _browserZoomRail = null;
        _appBackBar?.Close();
        _appBackBar = null;
        _phoneSurface?.Close();
        _phoneSurface = null;
        _phoneDisplay = null;
        _activeDisplayPhoneChoice = null;
        _activeDisplayInsetPoints = null;
        _activeDisplayPreferredBaseWidth = null;
        _previewOpen = false;

        if (_driver.IsManagedBySnug)
        {
            _connection.NoteSelfInducedChange(DateTimeOffset.UtcNow);
            var stopped = await ChangeOwnedDisplayAsync(false);
            UuCaptureAcceptanceAudit.Record("EndPhoneSession", new { stage = "display-stop", stopped, snugDisplay = _displayEnumerator.FindSnugDisplay() is not null });
            if (!stopped)
                ActivityStatus = "手机屏已退出，系统显示器正在关闭；稍后会自动重试";
        }

        DisplayStatus = "等待 UU 远程";
        _endingSession = false;
        RefreshDisplays();
    }

    private async Task ClearOrphanedDisplayAsync()
    {
        if (!_driver.IsManagedBySnug) return;
        if (_displayEnumerator.FindSnugDisplay() is null) return;

        _connection.NoteSelfInducedChange(DateTimeOffset.UtcNow);
        if (await ChangeOwnedDisplayAsync(false))
        {
            await Task.Delay(800);
            RefreshDisplays();
        }
        else
        {
            await Task.Delay(2500);
            _orphanCleanupStarted = false;
        }
    }

    private async Task<bool> WaitForDriverDisplayGoneAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_displayEnumerator.FindSnugDisplay() is null) return true;
            await Task.Delay(250);
        }
        return _displayEnumerator.FindSnugDisplay() is null;
    }

    private async Task RecoverLostWindowAsync()
    {
        try
        {
            await PutBackAsync();
            if (!HasPlacedWindow)
                ShowPhoneList();
        }
        catch (IOException error) { ActivityStatus = "记录关闭窗口状态失败：" + error.Message; }
        catch (UnauthorizedAccessException error) { ActivityStatus = "记录关闭窗口状态失败：" + error.Message; }
        finally { _recoveringLostWindow = false; }
    }

    private void CheckLeftBehindReturns(DateTimeOffset now)
    {
        if (_previewOpen || _placed is not null || _pullingWindow ||
            _recoveringLostWindow || _ledger.Load() is not null)
            return;

        try { _leftBehind.Prune(now); }
        catch (IOException error)
        {
            ActivityStatus = "维护窗口恢复记录失败：" + error.Message;
            return;
        }
        catch (UnauthorizedAccessException error)
        {
            ActivityStatus = "维护窗口恢复记录失败：" + error.Message;
            return;
        }
        if (_leftBehind.Entries.Count == 0) return;

        // Upstream Zed zoom is process-local rather than window-local. Repay
        // only using the original, still-running process (creation time too).
        // If no other window exists, keep the debt until one appears.
        foreach (var entry in _leftBehind.Entries.Where(e =>
            e.Record.ZoomSteps != 0 &&
            ZoomRecovery.ScopeOf(e.Record.ExecutablePath) == ZoomScope.Process &&
            e.Record.ProcessStartedUtcTicks.HasValue))
        {
            var record = entry.Record;
            var start = WindowManager.ProcessStartUtcTicks(record.Key.ProcessId);
            var exited = ProcessHasExited((int)record.Key.ProcessId) ||
                (start.HasValue && start != record.ProcessStartedUtcTicks);
            if (!exited)
            {
                if (start != record.ProcessStartedUtcTicks) continue;
                var other = Windows.FirstOrDefault(w => w.Key.ProcessId == record.Key.ProcessId &&
                    !w.IsMinimized && StringComparer.OrdinalIgnoreCase.Equals(
                        w.ExecutablePath, record.ExecutablePath) && _windowManager.IsResponsive(w.Key));
                if (other is null || !_windowManager.Zoom(other, -record.ZoomSteps)) continue;
            }
            try { _leftBehind.UpdateRecord(entry, record with { ZoomSteps = 0 }); }
            catch (IOException error) { ActivityStatus = "字号恢复账本保存失败：" + error.Message; return; }
            catch (UnauthorizedAccessException error) { ActivityStatus = "字号恢复账本保存失败：" + error.Message; return; }
            // Re-read entries next tick. Only one repayment occurs per scan.
            return;
        }
        foreach (var window in Windows)
        {
            if (window.IsMinimized || !_windowManager.IsResponsive(window.Key)) continue;
            if (_windowManager.ReadBounds(window.Key) is not { } actual) continue;
            if (_leftBehind.Match(window, actual, now,
                WindowManager.ProcessStartUtcTicks(window.Key.ProcessId)) is not { } match) continue;

            var old = match.Record;
            var uniqueDebt = _leftBehind.Entries.Count(entry =>
                entry.Record.ZoomSteps != 0 &&
                StringComparer.OrdinalIgnoreCase.Equals(entry.Record.ExecutablePath, old.ExecutablePath) &&
                StringComparer.Ordinal.Equals(entry.Record.Title, old.Title)) <= 1;
            var uniqueTitle = uniqueDebt && Windows.Count(w =>
                StringComparer.OrdinalIgnoreCase.Equals(w.ExecutablePath, window.ExecutablePath) &&
                StringComparer.Ordinal.Equals(w.Title, window.Title)) == 1;
            var action = ZoomRecovery.Decide(old, window,
                WindowManager.ProcessStartUtcTicks(window.Key.ProcessId), uniqueTitle,
                originalProcessExited: ProcessHasExited((int)old.Key.ProcessId) ||
                    (old.ProcessStartedUtcTicks is { } oldStart &&
                     WindowManager.ProcessStartUtcTicks(old.Key.ProcessId) is { } actualStart &&
                     oldStart != actualStart));
            if (action == ZoomRecoveryAction.KeepForReview)
            {
                ActivityStatus = $"尚未恢复 {old.AppName} 的字号：未能确认返回的是原窗口或工作区";
                continue;
            }
            if (action == ZoomRecoveryAction.Repay &&
                !_windowManager.Zoom(window, -old.ZoomSteps))
                return;

            // Persist the zero balance BEFORE moving the window. A failed
            // geometry restore on the next tick must not shrink text twice.
            if (old.ZoomSteps != 0)
            {
                try { match = _leftBehind.UpdateRecord(match, old with { ZoomSteps = 0 }); }
                catch (IOException error)
                {
                    ActivityStatus = "字号已恢复，暂时无法保存窗口恢复账本：" + error.Message;
                    return;
                }
                catch (UnauthorizedAccessException error)
                {
                    ActivityStatus = "字号已恢复，暂时无法保存窗口恢复账本：" + error.Message;
                    return;
                }
                old = match.Record;
            }
            var restored = old with { Key = window.Key, ZoomSteps = 0 };
            if (!_windowManager.Restore(restored)) return;

            try { _leftBehind.Remove(match); }
            catch (IOException error)
            {
                ActivityStatus = "窗口已恢复，但记录更新失败：" + error.Message;
                return;
            }
            catch (UnauthorizedAccessException error)
            {
                ActivityStatus = "窗口已恢复，但记录更新失败：" + error.Message;
                return;
            }
            ActivityStatus = "已恢复重新出现的窗口：" + window.Title;
            return;
        }
    }
    private void RecoverInterruptedMove()
    {
        var record = _ledger.Load();
        if (record is null) return;
        if (!_windowManager.IsAlive(record.Key))
        {
            try
            {
                _leftBehind.Add(record, DateTimeOffset.UtcNow);
                _ledger.Clear();
            }
            catch (IOException error)
            {
                ActivityStatus = "暂时无法保存窗口恢复记录：" + error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                ActivityStatus = "暂时无法保存窗口恢复记录：" + error.Message;
            }
            return;
        }
        if (!_windowManager.IsResponsive(record.Key))
        {
            ActivityStatus = "发现未完成的窗口恢复；App 暂时无响应，已保留记录";
            return;
        }

        var window = new WindowInfo(record.Key, record.AppName, string.Empty,
            record.ExecutablePath, false, false);
        if (record.ZoomSteps != 0)
        {
            if (!_windowManager.Zoom(window, -record.ZoomSteps))
            {
                ActivityStatus = "发现未完成的字号还原，记录已保留";
                return;
            }
            record = record with { ZoomSteps = 0 };
            _ledger.Save(record);
        }
        if (_windowManager.Restore(record)) _ledger.Clear();
        else ActivityStatus = "发现未完成的窗口还原，记录已保留";
    }

    private void SaveSettings()
    {
        _settings.PhoneScreenEnabled = _phoneScreenEnabled;
        _settings.GesturesEnabled = _gesturesEnabled;
        _settings.ZoomSteps = _defaultZoomSteps;
        _settings.AppZoomSteps = new Dictionary<string, int>(_zoomBook.Remembered, StringComparer.OrdinalIgnoreCase);
        _settings.LaunchAtLogin = _launchAtLogin;
        _settings.Welcomed = _welcomed;
        _settings.PhoneModel = _phoneChoice.Model ?? string.Empty;
        _settings.PhoneScreenWidth = _phoneChoice.Screen.Width;
        _settings.PhoneScreenHeight = _phoneChoice.Screen.Height;
        _settings.PhoneScreenPoints = _phoneChoice.Screen.Points;
        _settings.PhoneBaseWidth = PhoneBaseWidth.AtIndex(_baseWidthOption);
        try { _settingsStore.Save(_settings); }
        catch (Exception error) { ActivityStatus = "设置保存失败：" + error.Message; }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _screenshots.Changed -= SyncScreenshotOverlay;
        _screenshots.Dismiss();
        _screenshotPanel?.Close();
        _screenshotPanel = null;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _uuDetector.Dispose();
        UuCaptureAcceptanceAudit.Close();
        _gestureHook.Dispose();
        _homeBar?.Close();
        _browserZoomRail?.Close();
        _appBackBar?.Close();
        _phoneSurface?.Close();
    }
}
