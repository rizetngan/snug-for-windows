using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Snug.Windows.Core;
using Snug.Windows.Interop;

namespace Snug.Windows.Services;

public sealed class WindowManager
{
    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Snug.Windows", "GameViewer", "GameViewerServer", "GameViewerService",
        "dwm", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchApp", "TextInputHost", "RuntimeBroker"
    };

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            var className = ReadClassName(handle);
            var ownedDialog = className.Equals("#32770", StringComparison.OrdinalIgnoreCase);
            if (!NativeMethods.IsWindowVisible(handle)
                || handle == NativeMethods.GetShellWindow()
                || (NativeMethods.GetWindow(handle, NativeMethods.GW_OWNER) != IntPtr.Zero && !ownedDialog)
                || (NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;

            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            if (pid == 0 || pid == _ownProcessId) return true;

            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (ExcludedProcesses.Contains(process.ProcessName)) return true;
                var title = ReadTitle(handle);
                                if (string.IsNullOrWhiteSpace(title)) return true;
                var executable = ReadExecutable(process);
                if (string.IsNullOrWhiteSpace(process.ProcessName)) return true;

                windows.Add(new WindowInfo(
                    new WindowKey(pid, handle.ToInt64()),
                    process.ProcessName,
                    title,
                    executable,
                    NativeMethods.IsIconic(handle),
                    NativeMethods.IsZoomed(handle)));
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);

        var foreground = NativeMethods.GetForegroundWindow().ToInt64();
        // EnumWindows returns top-level windows in Z order; stable OrderBy keeps that order.
        return windows
            .OrderByDescending(window => window.Key.Handle == foreground)
            .ToList();
    }

    public WindowRestoreRecord? Capture(WindowInfo window, int zoomSteps)
    {
        var handle = new IntPtr(window.Key.Handle);
        if (!IsSameWindow(handle, window.Key.ProcessId)) return null;
        var placement = NewPlacement();
        if (!NativeMethods.GetWindowPlacement(handle, ref placement)) return null;

        var data = new WindowPlacementData(
            placement.Flags,
            placement.ShowCmd,
            placement.MinPosition.X,
            placement.MinPosition.Y,
            placement.MaxPosition.X,
            placement.MaxPosition.Y,
            new WindowBounds(
                placement.NormalPosition.Left,
                placement.NormalPosition.Top,
                placement.NormalPosition.Right - placement.NormalPosition.Left,
                placement.NormalPosition.Bottom - placement.NormalPosition.Top));

        return new WindowRestoreRecord(1, window.Key, window.AppName, window.ExecutablePath,
            data, zoomSteps, DateTimeOffset.UtcNow, window.Title,
            ProcessStartedUtcTicks: ProcessStartUtcTicks(window.Key.ProcessId),
            OriginalPhysicalBounds: placement.ShowCmd == 1 ? ReadPhysicalBounds(window.Key) : null);
    }

    /// <summary>Finds a visible, owned, modal dialog belonging to the given app.</summary>
    public WindowInfo? FindAskingWindow(uint processId)
    {
        WindowInfo? result = null;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle)) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            if (pid != processId || pid == _ownProcessId) return true;

            var className = ReadClassName(handle);
            var owner = NativeMethods.GetWindow(handle, NativeMethods.GW_OWNER);
            var title = ReadTitle(handle);
            if (!DialogClassifier.IsAskingWindow(className, title, owner != IntPtr.Zero))
                return true;

            result = new WindowInfo(new WindowKey(pid, handle.ToInt64()),
                string.Empty, title.Length == 0 ? "应用确认" : title,
                string.Empty, false, false);
            return false;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>Moves an existing modal popup into the visible phone region without resizing it.</summary>
    public bool MoveDialogTo(WindowKey key, WindowBounds phoneArea)
    {
        var handle = new IntPtr(key.Handle);
        if (!IsSameWindow(handle, key.ProcessId) || NativeMethods.IsHungAppWindow(handle)
            || !NativeMethods.GetWindowRect(handle, out var rect))
            return false;

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var x = phoneArea.X + Math.Max(0, (phoneArea.Width - width) / 2);
        var y = phoneArea.Y + Math.Max(0, (phoneArea.Height - height) / 2);
        if (!NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, width, height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW))
            return false;

        return NativeMethods.GetWindowRect(handle, out var actual)
            && Math.Abs(actual.Left - x) <= 5
            && Math.Abs(actual.Top - y) <= 5;
    }

    public bool IsResponsive(WindowKey key)
    {
        var handle = new IntPtr(key.Handle);
        return IsSameWindow(handle, key.ProcessId) && !NativeMethods.IsHungAppWindow(handle);
    }

    public bool MoveTo(WindowKey key, WindowBounds target)
    {
        var handle = new IntPtr(key.Handle);
        if (!IsSameWindow(handle, key.ProcessId) || NativeMethods.IsHungAppWindow(handle)) return false;
        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        if (!NativeMethods.SetWindowPos(handle, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
            return false;

        Thread.Sleep(180);
        return NativeMethods.GetWindowRect(handle, out var actual)
            && Math.Abs(actual.Left - target.X) <= 3
            && Math.Abs(actual.Top - target.Y) <= 3
            && actual.Right >= target.X + Math.Min(target.Width, 540)
            && actual.Bottom >= target.Y + Math.Min(target.Height, 600);
    }

    public WindowBounds? ReadBounds(WindowKey key)
    {
        var handle = new IntPtr(key.Handle);
        if (!IsSameWindow(handle, key.ProcessId) ||
            !NativeMethods.GetWindowRect(handle, out var rect)) return null;
        return new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left,
            rect.Bottom - rect.Top);
    }

    private static WindowBounds? ReadPhysicalBounds(WindowKey key)
    {
        using var dpi = new PhysicalDpiScope();
        var handle = new IntPtr(key.Handle);
        if (!dpi.IsValid || !IsSameWindow(handle, key.ProcessId) ||
            !NativeMethods.GetWindowRect(handle, out var rect)) return null;
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public bool Restore(WindowRestoreRecord record)
    {
        var handle = new IntPtr(record.Key.Handle);
        if (!IsSameWindow(handle, record.Key.ProcessId)) return false;
        var saved = record.OriginalPlacement;
        var placement = new NativeMethods.WINDOWPLACEMENT
        {
            Length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
            Flags = saved.Flags,
            ShowCmd = saved.ShowCommand,
            MinPosition = new NativeMethods.POINT { X = saved.MinX, Y = saved.MinY },
            MaxPosition = new NativeMethods.POINT { X = saved.MaxX, Y = saved.MaxY },
            NormalPosition = new NativeMethods.RECT
            {
                Left = saved.NormalPosition.X,
                Top = saved.NormalPosition.Y,
                Right = saved.NormalPosition.X + saved.NormalPosition.Width,
                Bottom = saved.NormalPosition.Y + saved.NormalPosition.Height
            }
        };
        if (!NativeMethods.SetWindowPlacement(handle, ref placement)) return false;
        if (saved.ShowCommand != 1 || record.OriginalPhysicalBounds is not { } original)
            return true; // Keep legacy/native minimized and maximized semantics.

        // Native WindowPlacement can apply the *current* monitor DPI to a
        // pre-move rectangle. Settle on the destination, then correct in
        // physical coordinates. Never place off-screen if a monitor was removed.
        using var dpi = new PhysicalDpiScope();
        var center = new NativeMethods.POINT { X = original.X + original.Width / 2,
            Y = original.Y + original.Height / 2 };
        if (!dpi.IsValid || NativeMethods.MonitorFromPoint(center, 0) == IntPtr.Zero)
            return true;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Thread.Sleep(180);
            if (!IsSameWindow(handle, record.Key.ProcessId) ||
                !NativeMethods.GetWindowRect(handle, out var current)) return false;
            if (Math.Abs(current.Left - original.X) <= 3 &&
                Math.Abs(current.Top - original.Y) <= 3 &&
                Math.Abs(current.Right - current.Left - original.Width) <= 3 &&
                Math.Abs(current.Bottom - current.Top - original.Height) <= 3)
                return true;
            if (!NativeMethods.SetWindowPos(handle, IntPtr.Zero, original.X, original.Y,
                original.Width, original.Height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE))
                return false;
        }
        Thread.Sleep(180);
        return NativeMethods.GetWindowRect(handle, out var final)
            && Math.Abs(final.Left - original.X) <= 3 && Math.Abs(final.Top - original.Y) <= 3
            && Math.Abs(final.Right - final.Left - original.Width) <= 3
            && Math.Abs(final.Bottom - final.Top - original.Height) <= 3;
    }

    public bool Focus(WindowKey key)
    {
        var handle = new IntPtr(key.Handle);
        if (!IsSameWindow(handle, key.ProcessId)) return false;
        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        return NativeMethods.SetForegroundWindow(handle);
    }

    public bool IsAlive(WindowKey key) => IsSameWindow(new IntPtr(key.Handle), key.ProcessId);

    public static long? ProcessStartUtcTicks(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or OverflowException) { return null; }
    }

    /// <summary>
    /// Prefer the app's actual "New Window" menu command, following original Snug's
    /// NewWindowMenu ranking. Only a small verified set of apps uses shortcuts.
    /// This prevents Ctrl+N from unexpectedly creating a file or document.
    /// </summary>
    public bool NewWindow(WindowKey key, AppInfo app, out string status)
    {
        status = string.Empty;
        if (!IsResponsive(key) || !Focus(key))
        {
            status = "应用窗口没有响应或无法获得焦点。";
            return false;
        }

        var handle = new IntPtr(key.Handle);
        var items = new List<NewWindowMenu.Item>();
        var rootMenu = NativeMethods.GetMenu(handle);
        if (rootMenu != IntPtr.Zero)
            CollectMenuCommands(rootMenu, null, items, 0);
        var chosen = NewWindowMenu.Pick(items);
        if (chosen is not null)
        {
            if (!NativeMethods.PostMessage(handle, NativeMethods.WM_COMMAND,
                    new IntPtr((long)chosen.CommandId), IntPtr.Zero))
            {
                status = "应用的新建窗口菜单命令未能发送。";
                return false;
            }

            status = $"已调用应用菜单：{chosen.Title}";
            return true;
        }

        var shortcut = NewWindowMenu.KnownShortcut(app);
        if (shortcut == NewWindowMenu.Shortcut.None)
        {
            status = $"未识别到 {app.Name} 的新建窗口菜单，已取消，避免误建文档。";
            return false;
        }

        const byte vkControl = 0x11, vkShift = 0x10, vkN = 0x4E;
        const uint keyUp = 0x0002;
        KeyboardNativeMethods.keybd_event(vkControl, 0, 0, UIntPtr.Zero);
        if (shortcut == NewWindowMenu.Shortcut.CtrlShiftN)
            KeyboardNativeMethods.keybd_event(vkShift, 0, 0, UIntPtr.Zero);
        KeyboardNativeMethods.keybd_event(vkN, 0, 0, UIntPtr.Zero);
        KeyboardNativeMethods.keybd_event(vkN, 0, keyUp, UIntPtr.Zero);
        if (shortcut == NewWindowMenu.Shortcut.CtrlShiftN)
            KeyboardNativeMethods.keybd_event(vkShift, 0, keyUp, UIntPtr.Zero);
        KeyboardNativeMethods.keybd_event(vkControl, 0, keyUp, UIntPtr.Zero);
        status = shortcut == NewWindowMenu.Shortcut.CtrlShiftN
            ? "已请求 Ctrl+Shift+N 新建窗口" : "已请求 Ctrl+N 新建窗口";
        return true;
    }

    private static void CollectMenuCommands(IntPtr menu, string? parent,
        List<NewWindowMenu.Item> items, int depth)
    {
        if (depth > 4 || items.Count > 256) return;
        var count = Math.Clamp(NativeMethods.GetMenuItemCount(menu), 0, 256);
        for (var index = 0; index < count && items.Count <= 256; index++)
        {
            var titleBuffer = new StringBuilder(512);
            NativeMethods.GetMenuString(menu, (uint)index, titleBuffer,
                titleBuffer.Capacity, NativeMethods.MF_BYPOSITION);
            var title = titleBuffer.ToString().Trim();
            var sub = NativeMethods.GetSubMenu(menu, index);
            var state = NativeMethods.GetMenuState(menu, (uint)index,
                NativeMethods.MF_BYPOSITION);
            var enabled = (state & (NativeMethods.MF_GRAYED | NativeMethods.MF_DISABLED)) == 0;
            if (sub != IntPtr.Zero)
            {
                // Inspect only menu branches relevant to file/window commands.
                if (depth == 0 || NewWindowMenu.WorthOpening(title))
                    CollectMenuCommands(sub, title, items, depth + 1);
            }
            else if (title.Length != 0)
            {
                var id = NativeMethods.GetMenuItemID(menu, index);
                items.Add(new NewWindowMenu.Item(title, id, parent, enabled));
            }
        }
    }
    public bool Close(WindowKey key)
    {
        var handle = new IntPtr(key.Handle);
        return IsSameWindow(handle, key.ProcessId)
            && !NativeMethods.IsHungAppWindow(handle)
            && NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
    // Browser zoom is per site in Chromium. Only apply on an explicit user
    // command, never when moving/restoring a browser window. Ctrl+0 resets
    // the current page, then real native zoom stops yield a known percentage.
    public bool ApplyBrowserZoom(WindowInfo window, int levelIndex)
    {
        if (!BrowserZoomLevels.IsBrowser(window.ExecutablePath, window.AppName)
            || !IsResponsive(window.Key) || !Focus(window.Key)) return false;

        const byte control = 0x11;
        const uint keyUp = 0x0002;
        void Key(byte key)
        {
            KeyboardNativeMethods.keybd_event(control, 0, 0, UIntPtr.Zero);
            KeyboardNativeMethods.keybd_event(key, 0, 0, UIntPtr.Zero);
            KeyboardNativeMethods.keybd_event(key, 0, keyUp, UIntPtr.Zero);
            KeyboardNativeMethods.keybd_event(control, 0, keyUp, UIntPtr.Zero);
        }
        Key(0x30); // Ctrl+0
        Thread.Sleep(65);
        var steps = BrowserZoomLevels.StepsFromDefault(levelIndex);
        var key = steps > 0 ? (byte)0xBB : (byte)0xBD;
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            Key(key);
            Thread.Sleep(65);
        }
        return true;
    }

    public bool Zoom(WindowInfo window, int steps)
    {
        if (steps == 0) return true;
        if (!ZoomBook.IsZoomable(window.ExecutablePath, window.AppName) || !IsResponsive(window.Key) || !Focus(window.Key)) return false;
        const byte vkControl = 0x11;
        const byte vkPlus = 0xBB;
        const byte vkMinus = 0xBD;
        const uint keyUp = 0x0002;
        for (var index = 0; index < Math.Abs(steps); index++)
        {
            KeyboardNativeMethods.keybd_event(vkControl, 0, 0, UIntPtr.Zero);
            var key = steps > 0 ? vkPlus : vkMinus;
            KeyboardNativeMethods.keybd_event(key, 0, 0, UIntPtr.Zero);
            KeyboardNativeMethods.keybd_event(key, 0, keyUp, UIntPtr.Zero);
            KeyboardNativeMethods.keybd_event(vkControl, 0, keyUp, UIntPtr.Zero);
            Thread.Sleep(90);
        }
        return true;
    }

    private static bool IsSameWindow(IntPtr handle, uint expectedPid)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle)) return false;
        NativeMethods.GetWindowThreadProcessId(handle, out var actualPid);
        return actualPid == expectedPid;
    }

    private static NativeMethods.WINDOWPLACEMENT NewPlacement() => new()
    {
        Length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
    };

    private static string ReadClassName(IntPtr handle)
    {
        var name = new StringBuilder(256);
        NativeMethods.GetClassName(handle, name, name.Capacity);
        return name.ToString();
    }

    private static string ReadTitle(IntPtr handle)
    {
        var length = Math.Clamp(NativeMethods.GetWindowTextLength(handle), 0, 2048);
        var text = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(handle, text, text.Capacity);
        return text.ToString().Trim();
    }

    private static string ReadExecutable(Process process)
    {
        try { return process.MainModule?.FileName ?? string.Empty; }
        catch (System.ComponentModel.Win32Exception) { return string.Empty; }
        catch (InvalidOperationException) { return string.Empty; }
    }
}
