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
