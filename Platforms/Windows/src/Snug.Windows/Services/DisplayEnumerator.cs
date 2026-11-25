using System.Runtime.InteropServices;
using System.Windows.Forms;
using Snug.Windows.Core;
using Snug.Windows.Interop;

namespace Snug.Windows.Services;

public sealed class DisplayEnumerator
{
    private const string SnugEdidId = "SNG00F0";

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var adapters = GetAdapters();
        var result = new List<DisplayInfo>();
        foreach (var screen in Screen.AllScreens)
        {
            var adapter = adapters.FirstOrDefault(item =>
                string.Equals(item.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
            var monitor = NewDevice();
            NativeMethods.EnumDisplayDevices(screen.DeviceName, 0, ref monitor,
                NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME);

            var friendly = !string.IsNullOrWhiteSpace(monitor.DeviceString)
                ? monitor.DeviceString!
                : !string.IsNullOrWhiteSpace(adapter.DeviceString) ? adapter.DeviceString! : screen.DeviceName;
            var deviceId = string.Join(" | ", new[] { adapter.DeviceID, monitor.DeviceID }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var isVirtual = friendly.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                || deviceId.Contains("ROOT\\", StringComparison.OrdinalIgnoreCase)
                || deviceId.Contains("SWD\\", StringComparison.OrdinalIgnoreCase);
            var virtualizedBounds = new WindowBounds(
                screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
            var native = new NativeMethods.DEVMODE
            {
                Size = checked((ushort)Marshal.SizeOf<NativeMethods.DEVMODE>()),
                DeviceName = string.Empty,
                FormName = string.Empty
            };
            WindowBounds? physicalBounds = null;
            if (NativeMethods.EnumDisplaySettings(screen.DeviceName, -1, ref native)
                && native.PelsWidth > 0 && native.PelsHeight > 0
                && native.PelsWidth <= 16384 && native.PelsHeight <= 16384)
            {
                physicalBounds = new WindowBounds(native.PositionX, native.PositionY,
                    checked((int)native.PelsWidth), checked((int)native.PelsHeight));
            }
            var bounds = PhysicalDisplayBounds.Resolve(virtualizedBounds, physicalBounds);
            var (scaleX, scaleY) = GetDpiScale(bounds);

            result.Add(new DisplayInfo(
                screen.DeviceName,
                friendly,
                deviceId,
                bounds,
                screen.Primary,
                isVirtual,
                scaleX,
                scaleY));
        }
        return result;
    }

    public DisplayInfo? FindSnugDisplay() => GetDisplays().FirstOrDefault(display =>
        display.DeviceId.Contains(SnugEdidId, StringComparison.OrdinalIgnoreCase)
        || display.FriendlyName.Contains("Snug Phone", StringComparison.OrdinalIgnoreCase));

    public DisplayInfo? FindVirtualDisplay() => GetDisplays()
        .Where(display => display.IsVirtual && !display.IsPrimary)
        .OrderByDescending(display => display.Bounds.Height > display.Bounds.Width)
        .ThenByDescending(display => display.Bounds.Height)
        .FirstOrDefault();

    private static (double X, double Y) GetDpiScale(WindowBounds bounds)
    {
        try
        {
            using var dpi = new PhysicalDpiScope();
            if (!dpi.IsValid) return (1.0, 1.0);
            var point = new NativeMethods.POINT
            {
                X = bounds.X + bounds.Width / 2,
                Y = bounds.Y + bounds.Height / 2
            };
            var monitor = NativeMethods.MonitorFromPoint(point, 2);
            if (monitor != IntPtr.Zero &&
                NativeMethods.GetScaleFactorForMonitor(monitor, out var scale) == 0 && scale >= 100)
                return (scale / 100.0, scale / 100.0);
            if (monitor != IntPtr.Zero
                && NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0)
                return (Math.Max(1.0, dpiX / 96.0), Math.Max(1.0, dpiY / 96.0));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return (1.0, 1.0);
    }

    private static List<NativeMethods.DISPLAY_DEVICE> GetAdapters()
    {
        var result = new List<NativeMethods.DISPLAY_DEVICE>();
        for (uint index = 0; index < 32; index++)
        {
            var device = NewDevice();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.StateFlags & 0x1) != 0) result.Add(device);
        }
        return result;
    }

    private static NativeMethods.DISPLAY_DEVICE NewDevice() => new()
    {
        cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
        DeviceName = string.Empty,
        DeviceString = string.Empty,
        DeviceID = string.Empty,
        DeviceKey = string.Empty
    };
}