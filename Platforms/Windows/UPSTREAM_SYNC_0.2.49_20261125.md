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
