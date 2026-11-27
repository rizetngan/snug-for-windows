using System.Globalization;

namespace Snug.Windows.Core;

public sealed record BrowserZoomObservation(string TabIdentity, int? Percent)
{
    public string Label => Percent is int percent ? $"{percent}%" : "缩放";
}

public sealed record ZoomTarget(WindowKey Window, DateTimeOffset Session, string? TabIdentity,
    bool IsBrowser, int? BrowserPercent)
{
    public bool SameScope(ZoomTarget? current) =>
        current is not null && Window == current.Window && Session == current.Session &&
        IsBrowser == current.IsBrowser &&
        (!IsBrowser || (!string.IsNullOrEmpty(TabIdentity) && TabIdentity == current.TabIdentity));
}

/// <summary>A browser tab identity belongs to one connection/profile and window.</summary>
public readonly record struct BridgeTabToken(Guid Connection, int WindowId, int TabId)
{
    public override string ToString() => $"bridge:{Connection:N}:{WindowId}:{TabId}";
    public static bool TryParse(string? text, out BridgeTabToken token)
    {
        token = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(':');
        if (parts.Length != 4 || parts[0] != "bridge" ||
            !Guid.TryParseExact(parts[1], "N", out var connection) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var window) ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var tab) ||
            window < 0 || tab < 0) return false;
        token = new(connection, window, tab);
        return true;
    }
}

public static class ZoomContext
{
    public static int TrackIndex(int percent) => Enumerable.Range(0, BrowserZoomLevels.Percents.Length)
        .MinBy(index => Math.Abs(BrowserZoomLevels.Percents[index] - percent));

    public static int? ActualPercent(double factor) =>
        double.IsFinite(factor) && factor >= 0.1 && factor <= 10
            ? (int)Math.Round(factor * 100, MidpointRounding.AwayFromZero) : null;

    // Only previously classified scopes may receive automatic temporary zoom.
    public static int InitialTemporarySteps(string executablePath, int explicitDefault) =>
        ZoomRecovery.ScopeOf(executablePath) == ZoomScope.Unverified
            ? 0 : Math.Clamp(explicitDefault, ZoomBook.MinimumSteps, ZoomBook.MaximumSteps);
}