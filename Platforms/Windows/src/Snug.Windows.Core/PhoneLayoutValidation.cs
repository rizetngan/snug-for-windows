namespace Snug.Windows.Core;

/// <summary>All values are in physical desktop pixels, never WPF DIPs.</summary>
public static class PhoneLayoutValidation
{
    public static bool MatchesMode(WindowBounds bounds, int width, int height) =>
        width > 0 && height > 0 && bounds.Width == width && bounds.Height == height;

    public static bool GeometryChanged(DisplayInfo previous, DisplayInfo current) =>
        previous.DeviceName != current.DeviceName || previous.Bounds != current.Bounds ||
        Math.Abs(previous.DpiScaleX - current.DpiScaleX) > 0.001 ||
        Math.Abs(previous.DpiScaleY - current.DpiScaleY) > 0.001;

    public static int? MatchingWidth(ScreenFit fit, WindowBounds bounds)
    {
        foreach (var width in fit.Widths)
        {
            var size = fit.PixelSizeFor(width);
            if (MatchesMode(bounds, (int)size.Width, (int)size.Height)) return width;
        }
        return null;
    }

    // Native applications can choose a smaller frame. Never accept an invalid
    // frame, wrong origin, or a window extending beyond the safe app area.
    public static bool Fits(WindowBounds actual, WindowBounds area) =>
        actual.Width > 0 && actual.Height > 0 && area.Width > 0 && area.Height > 0 &&
        Math.Abs((long)actual.X - area.X) <= 3 &&
        Math.Abs((long)actual.Y - area.Y) <= 3 &&
        actual.Width <= (long)area.Width + 2 &&
        actual.Height <= (long)area.Height + 2;
}