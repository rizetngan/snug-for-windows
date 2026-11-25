namespace Snug.Windows.Core;

/// <summary>Chrome/Edge native zoom levels; each + or - key advances one stop.
/// This does not represent Win32 display scaling or a custom webview zoom.
/// </summary>
public static class BrowserZoomLevels
{
    public static readonly int[] Percents = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200];
    public const int DefaultIndex = 5;
    public static int ClampIndex(int index) => Math.Clamp(index, 0, Percents.Length - 1);
    public static int PercentAt(int index) => Percents[ClampIndex(index)];
    public static int StepsFromDefault(int index) => ClampIndex(index) - DefaultIndex;
    public static bool IsBrowser(string executablePath, string appName = "")
    {
        var name = Path.GetFileName(executablePath);
        if (string.IsNullOrWhiteSpace(name)) name = appName;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        return name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase);
    }
}
