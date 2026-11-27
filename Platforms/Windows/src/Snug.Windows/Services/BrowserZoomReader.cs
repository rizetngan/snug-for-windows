using System.Diagnostics;
using System.Windows.Automation;
using Snug.Windows.Core;
using Snug.Windows.Interop;

namespace Snug.Windows.Services;

/// <summary>
/// Native fallback reads only selected tab identity, never the Chromium zoom
/// tooltip: real-browser tests proved that tooltip may retain another tab's value.
/// </summary>
internal static class BrowserZoomReader
{
    private readonly record struct Node(AutomationElement Element, int Depth, bool InTabStrip);

    public static BrowserZoomObservation? Read(WindowKey key)
    {
        var handle = new IntPtr(key.Handle);
        if (!NativeMethods.IsWindow(handle) || NativeMethods.IsHungAppWindow(handle)) return null;
        NativeMethods.GetWindowThreadProcessId(handle, out var pid);
        if (pid != key.ProcessId) return null;
        var watch = Stopwatch.StartNew();
        try
        {
            var raw = AutomationElement.FromHandle(handle);
            if (raw is null) return null;
            var cache = new CacheRequest
            {
                TreeScope = TreeScope.Element,
                TreeFilter = Automation.RawViewCondition,
                AutomationElementMode = AutomationElementMode.Full
            };
            cache.Add(AutomationElement.ControlTypeProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<Node>();
            queue.Enqueue(new(raw.GetUpdatedCache(cache), 0, false));
            var count = 0;
            while (queue.Count > 0 && ++count <= 640 && watch.ElapsedMilliseconds < 1200)
            {
                var node = queue.Dequeue();
                var type = node.Element.Cached.ControlType;
                if (type == ControlType.TabItem)
                {
                    if (node.InTabStrip && !node.Element.Cached.IsOffscreen &&
                        node.Element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) &&
                        pattern is SelectionItemPattern item && item.Current.IsSelected &&
                        node.Element.GetRuntimeId() is { Length: > 0 } id)
                        return new("native:" + string.Join(":", id));
                    continue;
                }
                if (node.Depth >= 16 || type == ControlType.Document ||
                    type == ControlType.ToolBar || type == ControlType.Button ||
                    type == ControlType.Edit || type == ControlType.Text ||
                    type == ControlType.Image || type == ControlType.Hyperlink ||
                    type == ControlType.MenuItem || type == ControlType.ListItem) continue;
                for (var child = walker.GetFirstChild(node.Element, cache); child is not null;
                    child = walker.GetNextSibling(child, cache))
                {
                    if (count + queue.Count >= 640 || watch.ElapsedMilliseconds >= 1200) break;
                    queue.Enqueue(new(child, node.Depth + 1, node.InTabStrip || type == ControlType.Tab));
                }
            }
            return null;
        }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or ArgumentException
            or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }
}