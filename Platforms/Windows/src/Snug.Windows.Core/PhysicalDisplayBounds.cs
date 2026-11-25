namespace Snug.Windows.Core;

/// <summary>
/// Windows Forms Screen.Bounds may be DPI-virtualized (720x1392) while the
/// signed VDD advertises a raw 1080x2088 mode. Every WPF window, managed
/// application and touch rectangle must use the same physical desktop space.
/// </summary>
public static class PhysicalDisplayBounds
{
    public static WindowBounds Resolve(WindowBounds virtualized, WindowBounds? raw)
    {
        if (raw is not { } native ||
            native.Width < 200 || native.Height < 200 ||
            native.Width > 16384 || native.Height > 16384)
            return virtualized;
        return native;
    }
}
