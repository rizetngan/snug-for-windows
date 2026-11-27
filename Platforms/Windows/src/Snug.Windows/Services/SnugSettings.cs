using System.IO;
using System.Reflection;
using System.Text.Json;
using Snug.Windows.Core;
using Microsoft.Win32;

namespace Snug.Windows.Services;

public sealed class SnugSettings
{
    public bool PhoneScreenEnabled { get; set; } = true;
    public string RemoteMode { get; set; } = "Uu";
    public bool AutoMatchPhoneModel { get; set; }
    public bool GesturesEnabled { get; set; } = true;
    public bool ScreenshotEnabled { get; set; }
    public int ZoomSteps { get; set; } = 2;
    public string UuViewportMode { get; set; } = UuViewportPreset.Classic;
    public int PhoneBaseWidth { get; set; }
    public string PhoneModel { get; set; } = "Apple iPhone 17 Pro Max";
    public int PhoneScreenWidth { get; set; } = 1320;
    public int PhoneScreenHeight { get; set; } = 2868;
    public int? PhoneScreenPoints { get; set; } = 440;
    public Dictionary<string, int> AppZoomSteps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LaunchAtLogin { get; set; } = true;
    public bool Welcomed { get; set; }
    public bool DriverInstallAttempted { get; set; }
}

public sealed class SnugSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snug", "settings.json");

    public SnugSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new SnugSettings();
            return JsonSerializer.Deserialize<SnugSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new SnugSettings();
        }
        catch
        {
            return new SnugSettings();
        }
    }

    public void Save(SnugSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _path, true);
    }
}

public static class StartupManager
{
    public static bool IsInstalledLocation
    {
        get
        {
            var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            return appDirectory.StartsWith(programFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Snug";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value
            && value.Contains("--background", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var launcherPath = Path.Combine(AppContext.BaseDirectory, "SnugLauncher.exe");
        string command;
        if (File.Exists(launcherPath))
        {
            command = $"{Quote(launcherPath)} --background";
        }
        else
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定 Snug 启动程序路径。");
            var assemblyPath = Assembly.GetEntryAssembly()?.Location;
            var isDotnetHost = string.Equals(Path.GetFileName(processPath), "dotnet.exe",
                StringComparison.OrdinalIgnoreCase);
            command = isDotnetHost && !string.IsNullOrWhiteSpace(assemblyPath)
                ? $"{Quote(processPath)} {Quote(assemblyPath)} --background"
                : $"{Quote(processPath)} --background";
        }
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
