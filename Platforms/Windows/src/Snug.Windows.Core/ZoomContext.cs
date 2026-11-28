namespace Snug.Windows.Core;

public sealed record BrowserZoomObservation(string TabIdentity);

public sealed record ZoomTarget(WindowKey Window, DateTimeOffset Session, string? TabIdentity,
    bool IsBrowser)
{
    public bool SameScope(ZoomTarget? current) =>
        current is not null && Window == current.Window && Session == current.Session &&
        IsBrowser == current.IsBrowser &&
        (!IsBrowser || (!string.IsNullOrEmpty(TabIdentity) && TabIdentity == current.TabIdentity));
}

public static class ZoomContext
{
    // Only previously classified scopes may receive automatic temporary zoom.
    public static int InitialTemporarySteps(string executablePath, int explicitDefault) =>
        ZoomRecovery.ScopeOf(executablePath) == ZoomScope.Unverified
            ? 0 : Math.Clamp(explicitDefault, ZoomBook.MinimumSteps, ZoomBook.MaximumSteps);
}