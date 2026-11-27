using System.Text;
using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class CrossDeskEvidenceTests
{
    private static CrossDeskLogLine Line(string message) =>
        CrossDeskLogLine.Parse("[2026-11-24 20:20:27.732] [crossdesk] [info] [host.cpp:1] " + message)!;

    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Logger initialized", typeof(CrossDeskLogEvent.Started))]
    [InlineData("Requested display and system idle-sleep inhibition for screen sharing", typeof(CrossDeskLogEvent.SharingBegan))]
    [InlineData("Released screen sharing power inhibitors", typeof(CrossDeskLogEvent.SharingEnded))]
    [InlineData("Last remote controller disconnected; recovering privacy, reason=connection_closed", typeof(CrossDeskLogEvent.LastControllerLeft))]
    [InlineData("Privacy: state=2, overlay=false, input_blocked=false, reason=Enabling privacy on connection", typeof(CrossDeskLogEvent.PrivacyChanged))]
    [InlineData("System audio capture started (attempt 1)", typeof(CrossDeskLogEvent.ControllerConnected))]
    [InlineData("Local display [1:DISPLAY1]", typeof(CrossDeskLogEvent.ControllerConnected))]
    [InlineData("GUI attached to persistent host; no second signaling login", typeof(CrossDeskLogEvent.AttachedToPersistentHost))]
    public void ParsesVerifiedWindowsLogEvents(string message, Type expected) =>
        Assert.IsType(expected, Line(message).Event);

    [Fact]
    public void ParsesDxgiAndWgcPhysicalSlots()
    {
        var dxgi = Assert.IsType<CrossDeskLogEvent.DisplaySlot>(Line(@"DXGI: session display slot=1 name='\\.\DISPLAY2' available=true bounds=(-540,0;540,950), selected=false").Event);
        Assert.Equal("DISPLAY2", dxgi.Name);
        Assert.Equal(new CrossDeskBounds(-540, 0, 540, 950), dxgi.Bounds);
        var wgc = Assert.IsType<CrossDeskLogEvent.DisplaySlot>(Line(@"index: 0, display name: \\.\DISPLAY1, is primary: 1, bounds: (0, 0) - (1920, 1080)").Event);
        Assert.Equal(new CrossDeskBounds(0, 0, 1920, 1080), wgc.Bounds);
    }

    [Fact]
    public void IgnoresForeignOrTruncatedLogs()
    {
        Assert.Null(CrossDeskLogLine.Parse("[2026-11-24 20:20:27.732] [minirtc] [info] Logger initialized"));
        Assert.Null(CrossDeskLogLine.Parse("[2026-11-24 20:20:27] [crossdesk] [info] Logger initialized"));
        Assert.Null(Line("Privacy: state=5, reason=A visible window may overlap privacy coverage"));
    }

    [Fact]
    public void GuiAttachPreservesExistingHostSessionAndSlots()
    {
        var state = new CrossDeskSessionEvidence();
        state.Observe(Line("Logger initialized"), Start);
        Assert.Equal(RemotePresence.Unknown, state.Presence);
        state.Observe(Line("DXGI: session display slot=0 name='DISPLAY1' available=true bounds=(0,0;1920,1080)"), Start.AddSeconds(1));
        state.Observe(Line("Requested display and system idle-sleep inhibition for screen sharing"), Start.AddSeconds(2));
        Assert.Equal(RemotePresence.Connected, state.Presence);
        state.Observe(Line("Logger initialized"), Start.AddSeconds(3));
        state.Observe(Line("DXGI: session display slot=0 name='DISPLAY2' available=true bounds=(1920,0;540,950)"), Start.AddSeconds(4));
        state.Observe(Line("GUI attached to persistent host; no second signaling login"), Start.AddSeconds(5));
        Assert.Equal(RemotePresence.Connected, state.Presence);
        Assert.True(state.CanSeeDisplay(@"\\.\DISPLAY1"));
        Assert.False(state.CanSeeDisplay("DISPLAY2"));
        state.Observe(Line("Last remote controller disconnected; recovering privacy, reason=connection_closed"), Start.AddSeconds(6));
        Assert.Equal(RemotePresence.Disconnected, state.Presence);
        state.Observe(Line("Requested display and system idle-sleep inhibition for screen sharing"), Start.AddSeconds(2));
        Assert.Equal(RemotePresence.Disconnected, state.Presence);
        state.Reset();
        Assert.Equal(RemotePresence.Unknown, state.Presence);
        Assert.Null(state.CanSeeDisplay("DISPLAY1"));
    }

    [Fact]
    public void PrivacyTransitionsConservativelyProtectNativeRemoteInput()
    {
        var state = new CrossDeskSessionEvidence();
        state.Observe(Line("Logger initialized"), Start);
        Assert.Null(state.PrivacyBlocksInput);
        state.Observe(Line("Privacy: state=2, overlay=false, input_blocked=false, reason=Enabling privacy on connection"), Start.AddSeconds(1));
        Assert.True(state.PrivacyBlocksInput);
        Assert.Equal(RemotePresence.Connected, state.Presence);
        state.Observe(Line("Privacy: state=3, overlay=true, input_blocked=true, reason=Privacy screen is on"), Start.AddSeconds(2));
        Assert.True(state.PrivacyBlocksInput);
        state.Observe(Line("Privacy: state=4, overlay=true, input_blocked=true, reason=Restoring local desktop and input"), Start.AddSeconds(3));
        Assert.True(state.PrivacyBlocksInput);
        state.Observe(Line("Privacy: state=1, overlay=false, input_blocked=false, reason=Privacy screen is off"), Start.AddSeconds(4));
        Assert.False(state.PrivacyBlocksInput);
        state.Observe(Line("Privacy: state=3, overlay=true, input_blocked=true, reason=Privacy screen is on"), Start.AddSeconds(2));
        Assert.False(state.PrivacyBlocksInput);
        state.Reset();
        Assert.Null(state.PrivacyBlocksInput);
    }

    [Fact]
    public void FollowerBuffersPartialLinesAndResetsOnRotation()
    {
        var f = new CrossDeskLogFollower();
        var a = Encoding.UTF8.GetBytes("one\r\nhalf");
        var b = Encoding.UTF8.GetBytes("-line\n");
        Assert.Equal((0L, a.Length), f.NextRange(1, a.Length));
        Assert.Equal(new[] { "one" }, f.Feed(a));
        Assert.Equal(((long)a.Length, b.Length), f.NextRange(1, a.Length + b.Length));
        Assert.Equal(new[] { "half-line" }, f.Feed(b));
        Assert.Equal((0L, 4), f.NextRange(2, 4));
        Assert.Equal(new[] { "new" }, f.Feed(Encoding.UTF8.GetBytes("new\n")));
        Assert.Equal((4L, (int)CrossDeskLogFollower.InitialWindow), f.NextRange(2, 4 + 3 * CrossDeskLogFollower.InitialWindow));
    }
}
