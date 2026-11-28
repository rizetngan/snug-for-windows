using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class RemoteModePolicyTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultAndUnknownSettingsRemainUu()
    {
        Assert.Equal(RemoteMode.Uu, RemoteModePolicy.Normalize(null));
        Assert.Equal(RemoteMode.Uu, RemoteModePolicy.Normalize("future"));
        Assert.Equal(RemoteMode.CrossDesk, RemoteModePolicy.Normalize("CrossDesk"));
    }

    [Fact]
    public void CrossDeskKeepsPreparedScreenWithoutConnectionButNeverWhenDisabled()
    {
        Assert.True(RemoteModePolicy.KeepPreparedScreen(RemoteMode.CrossDesk, true));
        Assert.False(RemoteModePolicy.KeepPreparedScreen(RemoteMode.CrossDesk, false));
        Assert.False(RemoteModePolicy.KeepPreparedScreen(RemoteMode.Uu, true));
    }

    [Fact]
    public void UnknownEvidenceNeverReturnsWindowAndReconnectCancelsPendingDisconnect()
    {
        var session = new CrossDeskEpisode();
        session.Update(RemotePresence.Connected, T);
        Assert.True(session.IsConnected);
        session.Update(RemotePresence.Unknown, T.AddSeconds(1));
        Assert.False(session.IsConnected);
        Assert.False(session.ShouldRestore);
        session.Update(RemotePresence.Disconnected, T.AddSeconds(2));
        Assert.False(session.ShouldRestore);
        session.Update(RemotePresence.Connected, T.AddSeconds(3));
        Assert.True(session.IsConnected);
        Assert.False(session.ShouldRestore);
        session.Update(RemotePresence.Disconnected, T.AddSeconds(4));
        session.Update(RemotePresence.Disconnected, T.AddSeconds(7));
        Assert.True(session.ShouldRestore);
        session.MarkRestored();
        Assert.False(session.ShouldRestore);
    }

    [Fact]
    public void DisconnectWithoutPriorConnectionDoesNotRestore()
    {
        var session = new CrossDeskEpisode();
        session.Update(RemotePresence.Disconnected, T);
        session.Update(RemotePresence.Disconnected, T.AddSeconds(10));
        Assert.False(session.ShouldRestore);
    }

    [Fact]
    public void UnknownInterruptsDisconnectConfirmation()
    {
        var session = new CrossDeskEpisode();
        session.Update(RemotePresence.Connected, T);
        session.Update(RemotePresence.Disconnected, T.AddSeconds(1));
        session.Update(RemotePresence.Unknown, T.AddSeconds(2));
        session.Update(RemotePresence.Disconnected, T.AddSeconds(8));
        Assert.False(session.ShouldRestore);
    }
}
