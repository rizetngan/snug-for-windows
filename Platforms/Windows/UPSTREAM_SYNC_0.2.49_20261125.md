# 0.2.49 unified acceptance: upstream 2.9.1 + Windows 0.2.48

## Upstream sources
- https://github.com/zephyr4123/snug/releases/tag/v2.9.1
- https://github.com/zephyr4123/snug/releases/tag/v2.8.2
- https://github.com/zephyr4123/snug/issues/63
- https://github.com/zephyr4123/snug/pull/57
- https://github.com/zephyr4123/snug/issues/54

## Implementation
- Same original WindowKey (PID + HWND) and compatible creation timestamp matches regardless of geometry. Re-created windows still require executable and filled-window-size matching. Current Win32 IsWindow already checks window existence independently of visibility.
- Restore path first exits maximization if the original saved placement is normal, then applies saved placement and DPI-safe physical correction.
- Content density: default auto, 600 / 720 / 840 logical width. Same pin applied to ScreenFit, VDD mode list, mode snapping and touch baseline; only effective at next display creation.
- The Windows capture observer already tracks independent DxgKrnl contexts, not macOS SCStream. Overlap of old/new capture contexts stays positive; additional tests cover both switching orders and restart. Audio vs video stream metadata and active monitor identity are not currently available: no unverified Mac port claims.
- HomeBar's screenshot hit region previously overlapped the full-width return/menu button at the left edge. Screenshot now requires a stationary 500 ms hold; a normal tap cannot trigger it, and an upward swipe still opens the app list. Screenshot tooltip is updated to 长按截图.

## Combined real-device acceptance
1. Preserve installed 0.2.48 for rollback. Start 0.2.49 candidate only when UU phone display and managed-window ledger are idle.
2. UU iPhone: connect, use Chrome / Edge. Verify left zoom rail: slider, plus/minus, reset, clicking outside, two-second auto-collapse, normal webpage scrolling and focus.
3. Keyboard not open: Snug bottom navigation and UU's own bottom menu both operable. Check webpage input box, keyboard opening and closing.
4. Switch to another iPhone App for one minute then back to UU: original virtual monitor and application remain usable.
5. Compare Classic/Expanded/Full screen insets on iPhone; each applies only to next created display. Check top/bottom clipping and accurate touch.
6. Compare content density Default vs 720 via phone-display recreation, aspect ratio unchanged. Restore default if fonts become too small.
7. Place browser and an application with high minimum window width, then minimize, maximize and return. On Snug exit ensure original window bounds and physical desktop geometry are restored.
8. Repeat UU remote switch between primary and virtual display; never interpret video stream overlap as a disconnection. No primary monitor resolution or refresh changes.
9. Publish only after mobile checks pass.


## Screenshot mis-touch acceptance
- Tap the centered HomeBar chevron repeatedly: only the App Library opens; no screenshot is captured.
- Tap the HomeBar camera area briefly: no screenshot is captured.
- Hold the camera area stationary for at least 500 ms: exactly one screenshot is captured.
- Start on the camera area and swipe upward: App Library opens; no screenshot is captured.
- Move off the camera button before release: no screenshot is captured.
- Verify mouse and UU touch-forwarded input behave identically.

## 2026-11-25 mobile iPhone screenshot: partial PhoneSurface regression
Real VDD current mode was 1080x2088 (EnumDisplaySettings) while Screen.AllScreens
reported 720x1392 DPI-virtualized. All WPF overlays, managed app bounds and HomeBar
previously shared the wrong 720x1392 rectangle, leaving 1/3 of the remote canvas
as wallpaper and exposing the secondary Windows taskbar. DisplayEnumerator
now uses raw DEVMODE physical position and dimensions for each monitor, falling
back only when unavailable. PhoneSurfaceWindow is elevated over the secondary
taskbar at its native full-monitor bounds. Follow-up real UU screenshot must
confirm browser viewport, touch mapping, screenshot button and UU native toolbar.

## 2026-11-25 follow-up iPhone screenshot: UU-native bottom overlap
The full-width App library now renders correctly on the raw VDD. However
the last App row was still being shown behind the floating UU iPhone control
tray. PhoneSurface now gives its app/search/model canvas an additional
proportional bottom row, translated from the same physical pixel inset
already used by AppController's managed-window bounds and HomeBar positioning.
The WPF top-level window remains full VDD size and topmost so that Windows
secondary taskbar cannot bleed through. Native UU phone overlay itself
remains controlled by the UU app.
