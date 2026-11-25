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

    public static int ReservedBottom(double logicalScreenWidth) =>
        BottomInset(logicalScreenWidth) + ScreenFit.HomeBarHeight(logicalScreenWidth);

    public static int ContentHeight(double screenWidth, double screenHeight) =>
        Math.Max(200, (int)Math.Round(screenHeight, MidpointRounding.AwayFromZero)
            - ReservedBottom(screenWidth));
}
