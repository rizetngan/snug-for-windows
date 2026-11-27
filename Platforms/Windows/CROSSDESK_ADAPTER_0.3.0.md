# Windows 0.3.0 - CrossDesk adaptation candidate

## Baselines and credits
- Own candidate 0.2.54 / 100122f: extension-free native context zoom.
- zephyr4123/snug v2.9.1; main 9c7918d: no new core changes after b7a01d2.
- nicocart/snug-win v1.5.1 / 03c4491: Windows CrossDesk log parsing, multi-profile diagnostics and touch normalizer adapted under MIT; notices included.
- v1.5.0 controller matching and portrait ratio presets inspired the additional model-picker improvements.

## Implemented
- Explicit persisted remote choice; existing settings migrate to UU by default.
- CrossDesk mode prepares a Snug-owned portrait virtual display before host startup. The existing owned-driver and window-restore paths are reused; native input uses full screen ratio, without UU viewport subtraction.
- Bounded incremental log polling across user and SYSTEM profiles, process generation, stale-reading expiry, and display registration diagnosis. Contradictory or absent evidence remains unknown, except confirmed process absence.
- Two-second confirmed-disconnect grace; restore moved windows before returning to the App Library; keep the prepared display online for the next connection. Unknown evidence does not restore. Concurrent UU prevents automatic restoration. Provider switching requires the prior screen released and remotes disconnected.
- CSDM marker routes CrossDesk absolute touch, click, long press and move-only slide through shared gesture translator, excluding native controls and browser chrome. Relative cursor input is passed through. Native Windows input and existing UU behavior stay separate.
- Optional UU single-controller exact model matching applies to the next new screen, never changes current display or saved manual model. Five generic portrait presets appear in the existing picker.
- CrossDesk helper processes excluded from app library. Existing minimized-window placement and physical display-mode guards retained; no wholesale replacement.
- CrossDesk settings UI uses readable selection colors, hides UU-only settings when CrossDesk is selected, and updates the status badge and setup hint immediately.

## How to test
1. Keep existing UU 0.2.54 installation until the candidate is accepted. Install the 0.3.0 candidate only with the previous screen exited.
2. For UU: select UU, enable phone screen, check model selection, switch apps/tabs, drag the native zoom rail, background/return and disconnect.
3. For CrossDesk: disconnect other remotes, turn off phone screen, choose CrossDesk, select model, enable phone screen, wait for prepared screen, then start the CrossDesk host. If it cannot see the screen, manually exit and restart its host while idle. Do not restart an active host or force-bind a different VDD.
4. Connect a mobile CrossDesk client and select the Snug monitor. Check open/return, clicks, drag/scroll, long press, browser tabs, zoom, app layout and disconnect/restore. Keep screen prepared across reconnect. For a persistent service or privacy mode, validate replay-marker compatibility with real host logs.
5. Turn off phone screen to remove the prepared monitor after restoring the moved window. Switch to UU only after the screen is released and remotes disconnected.

## Gates
- 426 unit/core tests passed and Release build reported zero warnings and errors before candidate packaging.
- CrossDesk Windows and Android 1.7.7 are now installed in per-user / device app locations. Android is still at the first-run privacy-consent gate; no consent was accepted on the user's behalf.
- Direct IP host listens on TCP 52888. The tablet can ping the Windows PC, but inbound TCP is blocked by Windows Firewall. A temporary ADB reverse tunnel to 127.0.0.1:52888 succeeds and is reserved for test only; a narrowly scoped Private-profile firewall rule is still needed for normal LAN use.
- ID/signaling mode currently remains disconnected on this network (TLS handshake to api.crossdesk.cn:9099 timed out). The CrossDesk Service was not installed; it is optional for normal desktop control and only required for lock-screen/sign-in-screen control.
- Real mobile session, privacy input replay, safe-area/rail interaction, screen hotplug and UU regression remain unverified. Do not publish as a stable release yet.
- Manual host restart guide included; no automatic restart, driver force-binding, or third-party host installation. Electron launcher handoff and broader UWP enumeration remain separate investigations; existing normal/minimized restore and mode protection are not rewritten.
