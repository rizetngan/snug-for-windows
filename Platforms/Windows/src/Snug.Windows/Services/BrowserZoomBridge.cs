using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Snug.Windows.Core;
using Snug.Windows.Interop;

namespace Snug.Windows.Services;

/// <summary>Same-user, local-only native-messaging transport. No TCP port, page data,
/// remote debugging, global browser preference changes, or simulated zoom estimates.</summary>
internal sealed class BrowserZoomBridge : IDisposable
{
    internal const string HostName = "com.rizetngan.snug.zoom";
    internal const string ExtensionId = "pcefdmjkmhlpdlgamnhajbpjkpffagbk";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private volatile Binding? _binding;
    private bool _started;

    private sealed record Hello(string Kind, int Version, string Browser);
    private sealed record Request(int Id, string Op, int? WindowId = null, int? TabId = null, double? Factor = null);
    private sealed record Reply(int Id, bool Ok, int WindowId, int TabId, double Factor, bool Focused);
    private sealed record Binding(WindowKey Key, DateTimeOffset Session, Guid Client, int WindowId);

    private static string PipeName => "Snug.Windows.ZoomBridge.v1." +
        WindowsIdentity.GetCurrent().User!.Value;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(ListenAsync);
    }

    public static void RegisterInstalledHost()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "SnugZoomHost.exe");
        if (!File.Exists(host)) return;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snug");
        Directory.CreateDirectory(folder);
        var manifest = Path.Combine(folder, "browser-zoom-host.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "Snug Browser Zoom local relay",
            path = host,
            type = "stdio",
            allowed_origins = new[] { $"chrome-extension://{ExtensionId}/" }
        }));
        foreach (var browser in new[] { @"Google\Chrome", @"Microsoft\Edge" })
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\{browser}\NativeMessagingHosts\{HostName}");
            key.SetValue("", manifest, RegistryValueKind.String);
        }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 16,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                _ = HandleAsync(pipe);
                pipe = null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                try { await Task.Delay(500, _stop.Token); } catch (OperationCanceledException) { break; }
            }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        Client? client = null;
        try
        {
            using var greeting = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            greeting.CancelAfter(TimeSpan.FromSeconds(5));
            var bytes = await ReadFrameAsync(pipe, greeting.Token);
            var hello = bytes is null ? null : JsonSerializer.Deserialize<Hello>(bytes, Json);
            if (hello is not { Kind: "hello", Version: 1 } || hello.Browser is not ("chrome" or "edge")) return;
            var ready = JsonSerializer.SerializeToUtf8Bytes(new { kind = "ready", version = 1 }, Json);
            await pipe.WriteAsync(BitConverter.GetBytes(ready.Length), greeting.Token);
            await pipe.WriteAsync(ready, greeting.Token);
            await pipe.FlushAsync(greeting.Token);
            client = new Client(pipe, hello.Browser);
            _clients[client.Id] = client;
            await client.ReceiveAsync(_stop.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or
            JsonException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            if (client is not null)
            {
                _clients.TryRemove(client.Id, out _);
                client.Dispose();
            }
            else pipe.Dispose();
        }
    }

    public async Task<BrowserZoomObservation?> ReadAsync(WindowKey key, DateTimeOffset session, string executable)
    {
        if (_stop.IsCancellationRequested || !Alive(key)) return null;
        var browser = Path.GetFileName(executable).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
            ? "edge" : "chrome";
        var binding = _binding;
        if (binding is not null && binding.Key == key && binding.Session == session &&
            _clients.TryGetValue(binding.Client, out var bound) && bound.Browser == browser)
        {
            var reply = await bound.AskAsync("read", binding.WindowId, null, null, _stop.Token);
            if (reply is { Ok: true } && reply.WindowId == binding.WindowId && Alive(key))
                return Observation(bound, reply);
            if (ReferenceEquals(_binding, binding)) _binding = null;
        }

        // Chrome's window IDs are not HWNDs. Associate only a uniquely focused
        // native browser window and profile; never match by title or URL.
        if (!Foreground(key)) return null;
        var clients = _clients.Values.Where(c => c.Browser == browser).ToArray();
        if (clients.Length == 0) return null;
        var replies = await Task.WhenAll(clients.Select(async client =>
            (Client: client, Reply: await client.AskAsync("focus", null, null, null, _stop.Token))));
        if (!Foreground(key)) return null;
        var focused = replies.Where(r => r.Reply is { Ok: true, Focused: true } reply &&
            Observation(r.Client, reply) is not null).ToArray();
        if (focused.Length != 1) return null;
        var match = focused[0];
        _binding = new(key, session, match.Client.Id, match.Reply!.WindowId);
        return Observation(match.Client, match.Reply);
    }

    public async Task<BrowserZoomObservation?> ApplyAsync(ZoomTarget target, int selectedValue, bool reset)
    {
        if (!BridgeTabToken.TryParse(target.TabIdentity, out var token) ||
            _binding is not { } binding || binding.Key != target.Window ||
            binding.Session != target.Session || binding.Client != token.Connection ||
            binding.WindowId != token.WindowId || !_clients.TryGetValue(token.Connection, out var client) ||
            !Foreground(target.Window)) return null;
        var factor = BrowserZoomLevels.PercentAt(selectedValue) / 100.0;
        var reply = await client.AskAsync(reset ? "reset" : "set", token.WindowId,
            token.TabId, reset ? null : factor, _stop.Token);
        return reply is { Ok: true } && reply.WindowId == token.WindowId &&
            reply.TabId == token.TabId ? Observation(client, reply) : null;
    }

    private static BrowserZoomObservation? Observation(Client client, Reply reply) =>
        reply.WindowId >= 0 && reply.TabId >= 0 && ZoomContext.ActualPercent(reply.Factor) is int percent
            ? new(new BridgeTabToken(client.Id, reply.WindowId, reply.TabId).ToString(), percent) : null;

    private static bool Alive(WindowKey key)
    {
        var hwnd = new IntPtr(key.Handle);
        if (!NativeMethods.IsWindow(hwnd) || NativeMethods.IsHungAppWindow(hwnd)) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == key.ProcessId;
    }
    private static bool Foreground(WindowKey key) => Alive(key) &&
        NativeMethods.GetForegroundWindow() == new IntPtr(key.Handle);

    private static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var count = await stream.ReadAsync(header.AsMemory(), ct);
        if (count == 0) return null;
        if (count < 4) await stream.ReadExactlyAsync(header.AsMemory(count), ct);
        var length = BitConverter.ToInt32(header);
        if (length is <= 0 or > 32768) throw new IOException("Invalid zoom-bridge frame");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes.AsMemory(), ct);
        return bytes;
    }

    private sealed class Client(NamedPipeServerStream pipe, string browser) : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Browser { get; } = browser;
        private int _sequence;
        private readonly SemaphoreSlim _write = new(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Reply?>> _pending = new();

        public async Task<Reply?> AskAsync(string op, int? window, int? tab, double? factor, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _sequence);
            var response = new TaskCompletionSource<Reply?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = response;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(1500);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new Request(id, op, window, tab, factor), Json);
                await _write.WaitAsync(deadline.Token);
                try
                {
                    await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), deadline.Token);
                    await pipe.WriteAsync(bytes, deadline.Token);
                    await pipe.FlushAsync(deadline.Token);
                }
                finally { _write.Release(); }
                return await response.Task.WaitAsync(deadline.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or
                ObjectDisposedException or InvalidOperationException) { return null; }
            finally { _pending.TryRemove(id, out _); }
        }

        public async Task ReceiveAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(pipe, ct);
                if (frame is null) break;
                var reply = JsonSerializer.Deserialize<Reply>(frame, Json);
                if (reply is not null && _pending.TryGetValue(reply.Id, out var wait))
                    wait.TrySetResult(reply);
            }
        }

        public void Dispose()
        {
            foreach (var pending in _pending.Values) pending.TrySetResult(null);
            pipe.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var client in _clients.Values) client.Dispose();
        _clients.Clear();
    }
}