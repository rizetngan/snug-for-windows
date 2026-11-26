namespace Snug.Windows.Core;

/// <summary>
/// Reserves the area obscured by the mobile UU bottom operation tray.
/// Windows only knows the virtual screen; it cannot directly read the height
/// of the iOS native UU bar. Use a proportional inset that remains consistent
/// between managed window placement and the Snug HomeBar positioning.
/// </summary>
public static class UuMobileOverlaySafeArea
{
    public static int BottomInset(double logicalScreenWidth)
    {
        if (logicalScreenWidth <= 0) return 0;
        return (int)Math.Round(
            Math.Clamp(logicalScreenWidth * 0.12, 64, 136),
            MidpointRounding.AwayFromZero);
    }

    // The iPhone 13 Pro notch/status area is 47 pt at 390 pt width.
    // Scale it with the VDD width, not the DPI-virtualized desktop rectangle.
    // The app itself must start below this region; masking an app at Y=0
    // would still leave its navigation/tabs hidden beneath the notch.
    public static int TopInset(double physicalScreenWidth)
    {
        if (physicalScreenWidth <= 0) return 0;
        return (int)Math.Round(
            Math.Clamp(physicalScreenWidth * 47d / 390d, 48, 260),
            MidpointRounding.AwayFromZero);
    }

    // Both sides of the UI use this one phone-app rectangle. Preserve the
    // original bottom HomeBar/UU reserve while adding the upper notch reserve.
    public static WindowBounds AppBounds(WindowBounds screen)
    {
        var top = TopInset(screen.Width);
        var bottom = ReservedBottom(screen.Width);
        return new WindowBounds(screen.X, screen.Y + top, screen.Width,
            Math.Max(1, screen.Height - top - bottom));
    }

    // PhoneSurface uses the upstream fixed 540-wide design canvas. Reserve
    // the UU toolbar *inside the scrollable phone UI*, too: otherwise the
    // last app cards render beneath UU's native controls even though the
    // managed Chrome/editor windows already avoid them.
    public static double PhoneCanvasInset(double physicalWidth, double canvasWidth)
    {
        if (physicalWidth <= 0 || canvasWidth <= 0) return 0;
        return BottomInset(physicalWidth) * canvasWidth / physicalWidth;
    }

    public static int ReservedBottom(double logicalScreenWidth) =>
        BottomInset(logicalScreenWidth) + ScreenFit.HomeBarHeight(logicalScreenWidth);

    public static int ContentHeight(double screenWidth, double screenHeight) =>
        Math.Max(200, (int)Math.Round(screenHeight, MidpointRounding.AwayFromZero)
            - ReservedBottom(screenWidth));
}
