using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Snug.Windows.Core;

namespace Snug.Windows.Services;

public sealed record DriverInstallResult(bool Success, string Message, int? ExitCode = null);

/// <summary>
/// Manages the official VDD package through Snug's fixed scheduled tasks.
/// The display device exists only while a UU phone-screen session is active.
/// </summary>
public sealed class VddDriverClient
{
    private const string PipeName = "MTTVirtualDisplayPipe";
    private const string DriverRoot = @"C:\VirtualDisplayDriver";
    private const string StartTask = @"\Snug\StartPhoneDisplay";
    private const string StopTask = @"\Snug\StopPhoneDisplay";

    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Snug", "driver-ownership.json");

    private static string PhoneProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Snug", "phone-profile.json");

    public bool IsManagedBySnug
    {
        get
        {
            try
            {
                if (!File.Exists(MarkerPath)) return false;
                using var document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
                return document.RootElement.TryGetProperty("managedBySnug", out var value)
                    && value.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }
    }

    public bool IsInstalled =>
        File.Exists(Path.Combine(DriverRoot, "MttVDD.inf"))
        || File.Exists(@"C:\Windows\System32\drivers\UMDF\MttVDD.dll");

    public bool IsInstallComplete
    {
        get
        {
            try
            {
                if (!File.Exists(MarkerPath)) return false;
                using var document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
                return document.RootElement.TryGetProperty("installState", out var state)
                    && state.GetString() == "installed";
            }
            catch { return false; }
        }
    }

    public bool IsRebootRequired
    {
        get
        {
            try
            {
                if (!File.Exists(MarkerPath)) return false;
                using var document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
                return document.RootElement.TryGetProperty("rebootRequired", out var value)
                    && value.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }
    }

    public void WritePhoneProfile(PhoneChoice choice, int verticalInsetPoints = (int)ScreenFit.UuInset)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var modes = VddResolutionProfile.Create(choice, verticalInsetPoints: verticalInsetPoints);
        var profile = new
        {
            schemaVersion = 1,
            model = choice.Model ?? "Custom",
            screenWidth = choice.Screen.Width,
            screenHeight = choice.Screen.Height,
            points = choice.Screen.Points,
            verticalInsetPoints,
            refreshRate = 60,
            modes = modes.Select(mode => new
            {
                width = mode.Width,
                height = mode.Height,
                refreshRate = mode.RefreshRate
            }).ToArray()
        };

        if (!File.Exists(PhoneProfilePath))
            throw new InvalidOperationException("手机配置文件尚未初始化，请先修复 Snug 虚拟显示驱动。");
        File.WriteAllText(PhoneProfilePath, JsonSerializer.Serialize(profile));
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var connected = false;

        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1200, timeout.Token);
            connected = true;

            var command = Encoding.Unicode.GetBytes("PING");
            await pipe.WriteAsync(command, timeout.Token);
            await pipe.FlushAsync(timeout.Token);

            var response = new byte[16];
            var read = await pipe.ReadAsync(response.AsMemory(), timeout.Token);
            if (read >= 4 && Encoding.ASCII.GetString(response, 0, read).StartsWith("PONG", StringComparison.Ordinal))
                return true;
        }
        catch (IOException) { }
        catch (TimeoutException) { }
        catch (OperationCanceledException) { }

        // VDD 25.7.23 on this Windows 10 machine accepts the pipe command but
        // closes the message pipe without delivering PONG. A connected named
        // pipe PLUS our own SNG00F0 EDID monitor proves the device is active.
        return connected && new DisplayEnumerator().FindSnugDisplay() is not null;
    }

    public async Task<bool> StartDisplayAsync(CancellationToken cancellationToken = default)
    {
        if (!IsManagedBySnug || !IsInstalled || !IsInstallComplete || IsRebootRequired) return false;
        // When our monitor is absent, the driver pipe normally does not exist.
        // Avoid spending the initial connection timeout probing a cold device.
        // The existing SYSTEM task validates ownership and installs with
        // --no-duplicates; the post-start readiness check still gates success.
        var existingDisplay = new DisplayEnumerator().FindSnugDisplay() is not null;
        var timing = Stopwatch.StartNew();
        if (existingDisplay && await PingAsync(cancellationToken)) return true;
        cancellationToken.ThrowIfCancellationRequested();
        UuCaptureAcceptanceAudit.Record("VddStartTiming", new { stage = "request", existingDisplay, elapsedMs = timing.ElapsedMilliseconds });
        if (!await RunScheduledTaskAsync(StartTask, cancellationToken)) return false;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await PingAsync(cancellationToken))
            {
                UuCaptureAcceptanceAudit.Record("VddStartTiming", new { stage = "ready", elapsedMs = timing.ElapsedMilliseconds });
                return true;
            }
            await Task.Delay(400, cancellationToken);
        }
        return false;
    }

    public async Task<bool> StopDisplayAsync(CancellationToken cancellationToken = default)
    {
        if (!IsManagedBySnug) return true;
        if (!await RunScheduledTaskAsync(StopTask, cancellationToken)) return false;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new DisplayEnumerator().FindSnugDisplay() is null) return true;
            await Task.Delay(350, cancellationToken);
        }
        return new DisplayEnumerator().FindSnugDisplay() is null;
    }

    public async Task<DriverInstallResult> InstallOrRepairAsync()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Driver", "Install-SnugDriver.ps1");
        if (!File.Exists(script))
            return new(false, "安装脚本未找到，请重新安装 Snug。");

        try
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(script)} -Action Install"
            };
            using var process = Process.Start(start);
            if (process is null) return new(false, "无法启动驱动安装程序。");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                return new(false, "驱动安装未完成。可查看 %ProgramData%\\Snug\\Logs。", process.ExitCode);
            return IsRebootRequired
                ? new(true, "驱动已安装，需要重启 Windows 后生效。", process.ExitCode)
                : new(true, "虚拟显示驱动已安装；连接 UU 时会自动创建手机屏。", process.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            return new(false, "已取消管理员授权。");
        }
        catch (Exception error)
        {
            return new(false, error.Message);
        }
    }

    private static async Task<bool> RunScheduledTaskAsync(string taskName, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = $"/Run /TN \"{taskName}\""
            };
            using var process = Process.Start(start);
            if (process is null) return false;

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await stdout;
            var error = await stderr;
            return process.ExitCode == 0
                && (output.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("成功", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(error));
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception) { return false; }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
