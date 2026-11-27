namespace Snug.Windows.Core;

public enum RemoteMode { Uu, CrossDesk }

public static class RemoteModePolicy
{
    public static RemoteMode Normalize(string? value) =>
        string.Equals(value, "CrossDesk", StringComparison.OrdinalIgnoreCase)
            ? RemoteMode.CrossDesk : RemoteMode.Uu;

    public static bool KeepPreparedScreen(RemoteMode mode, bool enabled) =>
        mode == RemoteMode.CrossDesk && enabled;
}

/// <summary>Keep the CrossDesk monitor registered while armed. Restore its
/// managed window only after an observed connection followed by sustained
/// explicit negative evidence. Missing logs never mean disconnection.</summary>
public sealed class CrossDeskEpisode
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);
    private bool _seenConnected;
    private DateTimeOffset? _disconnectedSince;
    public bool IsConnected { get; private set; }
    public bool ShouldRestore { get; private set; }

    public void Update(RemotePresence presence, DateTimeOffset now)
    {
        IsConnected = presence == RemotePresence.Connected;
        if (IsConnected)
        {
            _seenConnected = true;
            _disconnectedSince = null;
            ShouldRestore = false;
        }
        else if (presence == RemotePresence.Disconnected && _seenConnected)
        {
            _disconnectedSince ??= now;
            ShouldRestore = now - _disconnectedSince.Value >= Grace;
        }
        else
        {
            _disconnectedSince = null;
            ShouldRestore = false;
        }
    }

    public void MarkRestored()
    {
        _seenConnected = false;
        _disconnectedSince = null;
        ShouldRestore = false;
    }

    public void Reset()
    {
        MarkRestored();
        IsConnected = false;
    }
}
