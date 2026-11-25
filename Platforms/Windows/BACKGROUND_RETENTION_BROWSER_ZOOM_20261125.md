# 0.2.48 acceptance

## Finding
DxgKrnl output duplication stops both when UU video pauses in the iPhone background and when the controller disconnects. The current UU connection_log_controlled binary .slog files do not expose a verified exit to Snug. Peer TCP alone is sticky and cannot certify a real controller exit. Do not interpret capture inactivity as exit.

## Candidate
- A previously created phone screen remains available across mobile backgrounding for up to 10 minutes, retaining the display identity and window state. Returning to UU resets the timer without creating a second display. No new screen from hold state.
- Phone Screen OFF, Quit and retention expiry restore windows and release the Snug-owned display. A real intentional UU disconnect without a separate verified exit will leave the screen for the retention window; this is a deliberate correctness-over-latency tradeoff.
- While suspended, connection label says UU 已暂停 · 手机屏保留. The input hook stops and resumes with session presence.

## Browser
- Bottom HomeBar shows 缩放 when a Chrome or Edge window is placed. Popup has slider with native 50 / 67 / 75 / 80 / 90 / 100 / 110 / 125 / 150 / 175 / 200 percent stops, plus/minus and reset.
- Native Ctrl+0 then browser zoom shortcuts only on user action. Zoom is per-site according to browser behavior. No automatic browser zoom changes on placement/restoration, no virtual display resolution changes. Initial slider shows a target only, as browser may have an existing per-site zoom.

## Acceptance checklist
- Real iPhone: leave UU for 10 seconds, one minute and five minutes; return each time and verify same screen identity, app window bounds, no display-start/stop.
- UU switch primary and phone monitors three times; return to phone app and use window restoration.
- Explicit Phone Screen OFF immediately releases; timeout after a real disconnect releases with original window restored. Disconnect while waiting may now take up to ten minutes unless explicitly released.
- Chrome and Edge each: place window, drag slider, +/- and reset; verify actual webpage zoom, no VDD mode changes, no input interference or unexpected focus/tab jumps.
- Reconnect during pause; reconnect after timeout; controller process stops; incomplete ETW; physical desktop monitor remains stable.
- Review iPhone screenshots of browser popup before release. 0.2.47 installed stable remains intact.


## Mobile viewport adaptation
The connected Windows UU server reports 4.48.4.6020; iPhone UU app version cannot be read from Windows. Recent iOS app notes describe adaptive occlusion avoidance, but exact UI dimensions require phone-side validation.

0.2.47 uses fixed 91-point vertical insets each side. Configured iPhone 13 Pro (1170x2532 pixels, 390 logical points wide) should have native aspect 2.164; the old usable aspect is about 1.697. The currently active 820x1392 logical Snug display matches that old ratio.

0.2.48 adds UU 画面比例 settings: 标准（旧版） uses 91 points each edge, 扩展画面 uses 45, 全面屏 uses 0. Identical inset controls VDD advertised modes, Window screen fit and touch scaling. A new setting applies only when a new Snug display is created, not while the existing UU-selected virtual monitor is live. Default Classic remains until real-device calibration. Use the actual iPhone viewport to choose best preset; never assume full screen is always correct.

Acceptance: safely end existing managed window; disconnect UU; launch candidate, compare presets on iPhone; check fit without top/bottom cropping or black borders, correct touch targets, taskbar and HomeBar, switch primary/phone screens, browser zoom, 10s/1m/5m UU backgrounding, and Phone Screen OFF cleanup. No publish before user acceptance.
