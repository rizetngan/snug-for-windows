namespace Snug.Windows.Core;

/// <summary>
/// Owns only the already-created Snug display. A missing DxgKrnl capture
/// context means video stopped, not necessarily that the UU controller exited.
/// Keep the display for normal phone app switching; never create a new display
/// from the hold state alone. Settings-off and real app exit still release now.
/// </summary>
public sealed class PhoneDisplayHold
{
    public static readonly TimeSpan BackgroundRetention = TimeSpan.FromMinutes(10);
    private DateTimeOffset? _pausedSince;
    private bool _hasLivePhoneDisplay;

    public bool IsPaused => _pausedSince.HasValue;
    public DateTimeOffset? PauseStartedAt => _pausedSince;

    public bool ShouldKeep(bool sessionWantsPhone, bool enabled, bool displayPresent, DateTimeOffset now)
    {
        if (!enabled)
        {
            Reset();
            return false;
        }
        if (sessionWantsPhone)
        {
            _pausedSince = null;
            // The call that starts the display will set the live flag on a later tick.
            _hasLivePhoneDisplay |= displayPresent;
            return true;
        }
        if (!displayPresent || !_hasLivePhoneDisplay)
        {
            Reset();
            return false;
        }
        _pausedSince ??= now;
        return now - _pausedSince.Value <= BackgroundRetention;
    }

    public void Reset()
    {
        _pausedSince = null;
        _hasLivePhoneDisplay = false;
    }
}
