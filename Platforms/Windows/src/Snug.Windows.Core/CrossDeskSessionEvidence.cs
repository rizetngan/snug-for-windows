namespace Snug.Windows.Core;

public readonly record struct CrossDeskBounds(double X, double Y, double Width, double Height);
public enum RemotePresence { Unknown, Disconnected, Connected }

/// <summary>Only explicit host events establish presence. A GUI attaching to
/// an existing host must not be mistaken for a disconnection.</summary>
public sealed class CrossDeskSessionEvidence
{
    private readonly Dictionary<int, CrossDeskLogEvent.DisplaySlot> _slots = [];
    private Dictionary<int, CrossDeskLogEvent.DisplaySlot>? _previousSlots;
    private RemotePresence _presence;
    private RemotePresence _previousPresence;
    private bool? _previousPrivacy;
    public bool? PrivacyBlocksInput { get; private set; }
    private bool _attachedGui;
    private DateTimeOffset? _lastEvent;
    public RemotePresence Presence => _presence;
    public DateTimeOffset? LastEvent => _lastEvent;
    public IReadOnlyCollection<CrossDeskLogEvent.DisplaySlot> Slots => _slots.Values.ToArray();

    public void Observe(CrossDeskLogLine line, DateTimeOffset timestamp)
    {
        if (_lastEvent is { } last && timestamp < last) return;
        _lastEvent = timestamp;
        switch (line.Event)
        {
            case CrossDeskLogEvent.Started:
                if (!_attachedGui)
                {
                    _previousSlots = new(_slots);
                    _previousPresence = _presence;
                    _previousPrivacy = PrivacyBlocksInput;
                }
                _slots.Clear();
                _attachedGui = false;
                _presence = RemotePresence.Unknown;
                PrivacyBlocksInput = null;
                break;
            case CrossDeskLogEvent.AttachedToPersistentHost:
                _attachedGui = true;
                _slots.Clear();
                if (_previousSlots is not null)
                    foreach (var slot in _previousSlots) _slots[slot.Key] = slot.Value;
                _presence = _previousPresence;
                PrivacyBlocksInput = _previousPrivacy;
                break;
            case CrossDeskLogEvent.DisplaySlot slot when !_attachedGui:
                _slots[slot.Slot] = slot;
                break;
            case CrossDeskLogEvent.PrivacyChanged privacy:
                PrivacyBlocksInput = privacy.BlocksInput;
                if (privacy.EnablingConnection) _presence = RemotePresence.Connected;
                break;
            case CrossDeskLogEvent.SharingBegan:
            case CrossDeskLogEvent.ControllerConnected:
                _presence = RemotePresence.Connected;
                break;
            case CrossDeskLogEvent.SharingEnded:
            case CrossDeskLogEvent.LastControllerLeft:
                _presence = RemotePresence.Disconnected;
                break;
        }
    }

    public bool? CanSeeDisplay(string deviceName) => _slots.Count == 0 ? null :
        _slots.Values.Any(slot => slot.Available &&
            string.Equals(slot.Name, CrossDeskLogLine.NormalizeDisplayName(deviceName),
                StringComparison.OrdinalIgnoreCase));

    public void Reset()
    {
        _slots.Clear();
        _previousSlots = null;
        _presence = _previousPresence = RemotePresence.Unknown;
        _attachedGui = false;
        _lastEvent = null;
        PrivacyBlocksInput = _previousPrivacy = null;
    }
}
