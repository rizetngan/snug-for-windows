using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Snug.Windows.Core;

namespace Snug.Windows.Services;

/// <summary>Read-only CrossDesk observer. Does not start, stop or modify a
/// remote host, claim a session, or change display topology.</summary>
internal sealed class CrossDeskSessionDetector
{
    private sealed class Source(string path)
    {
        public string Path { get; } = path;
        public CrossDeskLogFollower Follower { get; } = new();
        public CrossDeskSessionEvidence Evidence { get; } = new();
        public ulong? Identity { get; set; }
        public long Offset { get; set; }

        public void Reset()
        {
            Follower.Reset();
            Evidence.Reset();
            Identity = null;
            Offset = 0;
        }
    }

    internal sealed record Reading(bool HostRunning, bool LogReadable,
        RemotePresence Presence, int Slots, bool? DisplayReachable)
    {
        public string Summary => !HostRunning ? "CrossDesk: 未运行"
            : !LogReadable ? "CrossDesk: 日志未确认"
            : $"CrossDesk: {Presence switch { RemotePresence.Connected => "检测到连接",
                RemotePresence.Disconnected => "检测到断开", _ => "状态未知" }}"
                + (Slots > 0 ? $" · {Slots} 个已登记显示器" : "")
                + (DisplayReachable == false ? " · 手机屏未登记" : "");
    }

    private readonly object _gate = new();
    private readonly Source[] _sources = CandidatePaths().Select(path => new Source(path)).ToArray();
    private DateTimeOffset _lastPoll;
    private bool _polling;
    private (int Id, DateTimeOffset Started)? _generation;
    private Reading _cached = new(false, false, RemotePresence.Unknown, 0, null);

    public Reading Sample(string? deviceName)
    {
        lock (_gate)
        {
            if (!_polling && DateTimeOffset.UtcNow - _lastPoll >= TimeSpan.FromSeconds(2))
            {
                _polling = true;
                _ = Task.Run(() => Poll(deviceName));
            }
            return _cached;
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrossDesk", "logs", "crossdesk.log");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var folder in new[] { "System32", "SysWOW64" })
            yield return System.IO.Path.Combine(windows, folder, "config", "systemprofile",
                "AppData", "Local", "CrossDesk", "logs", "crossdesk.log");
    }

    private void Poll(string? deviceName)
    {
        Reading reading = new(false, false, RemotePresence.Unknown, 0, null);
        try
        {
            var processes = Process.GetProcessesByName("CrossDesk");
            try
            {
                var generations = new List<(int Id, DateTimeOffset Started)>();
                foreach (var process in processes)
                {
                    try { generations.Add((process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()))); }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }

                if (generations.Count == 0)
                {
                    ResetSources();
                    _generation = null;
                    reading = new(processes.Length > 0, false, RemotePresence.Unknown, 0, null);
                }
                else
                {
                    var generation = generations.OrderBy(g => g.Started).ThenBy(g => g.Id).First();
                    if (_generation != generation)
                    {
                        ResetSources();
                        _generation = generation;
                    }

                    var readable = new List<CrossDeskSessionEvidence>();
                    foreach (var source in _sources)
                    {
                        try
                        {
                            using var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);
                            var identity = FileIdentity(stream.SafeFileHandle);
                            if (identity is null) { source.Reset(); continue; }
                            if (source.Identity != identity || stream.Length < source.Offset) source.Reset();
                            source.Identity = identity;
                            if (source.Follower.NextRange(identity, stream.Length) is { } range)
                            {
                                stream.Seek(range.Offset, SeekOrigin.Begin);
                                var bytes = new byte[range.Count];
                                var count = 0;
                                while (count < bytes.Length)
                                {
                                    int n = stream.Read(bytes, count, bytes.Length - count);
                                    if (n == 0) break;
                                    count += n;
                                }
                                source.Offset = range.Offset + count;
                                foreach (var raw in source.Follower.Feed(bytes.AsSpan(0, count)))
                                {
                                    var parsed = CrossDeskLogLine.Parse(raw);
                                    if (parsed?.GetTimestamp() is { } stamp &&
                                        stamp >= generation.Started.AddSeconds(-2) &&
                                        stamp <= DateTimeOffset.UtcNow.AddSeconds(5))
                                        source.Evidence.Observe(parsed, stamp);
                                }
                            }
                            readable.Add(source.Evidence);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                            FormatException or OverflowException)
                        {
                            source.Reset();
                        }
                    }

                    var known = readable.Where(s => s.Presence != RemotePresence.Unknown)
                        .OrderByDescending(s => s.LastEvent).ToArray();
                    var presence = known.Length == 0 ? RemotePresence.Unknown : known[0].Presence;
                    if (known.Any(s => s.Presence != presence)) presence = RemotePresence.Unknown;
                    var slotSource = readable.Where(s => s.Slots.Count > 0)
                        .OrderByDescending(s => s.LastEvent).FirstOrDefault();
                    var reachable = deviceName is null ? null : slotSource?.CanSeeDisplay(deviceName);
                    reading = new(true, readable.Count > 0, presence, slotSource?.Slots.Count ?? 0, reachable);
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ResetSources();
            _generation = null;
            reading = new(true, false, RemotePresence.Unknown, 0, null);
        }
        finally
        {
            lock (_gate)
            {
                _cached = reading;
                _lastPoll = DateTimeOffset.UtcNow;
                _polling = false;
            }
        }
    }

    private void ResetSources()
    {
        foreach (var source in _sources) source.Reset();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);

    private static ulong? FileIdentity(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var info)
            ? ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow : null;
}
