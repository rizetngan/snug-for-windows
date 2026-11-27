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
- Extension-free audit: no Snug browser extension manifest found in Chrome or Edge profiles, no Snug Native Messaging registration found in HKCU/HKLM, and no BrowserZoomExtension or SnugZoomHost binary remains in the installed Snug folder.
- Release packaging note: the 0.3.0 installer and launcher are currently unsigned. Include SHA-256 in the release and keep the SmartScreen note in the installation guide unless a trusted code-signing certificate is added.

## How to test
1. Keep existing UU 0.2.54 installation until the candidate is accepted. Install the 0.3.0 candidate only with the previous screen exited.
2. For UU: select UU, enable phone screen, check model selection, switch apps/tabs, drag the native zoom rail, background/return and disconnect.
3. For CrossDesk: disconnect other remotes, turn off phone screen, choose CrossDesk, select model, enable phone screen, wait for prepared screen, then start the CrossDesk host. If it cannot see the screen, manually exit and restart its host while idle. Do not restart an active host or force-bind a different VDD.
4. Connect a mobile CrossDesk client and select the Snug monitor. Check open/return, clicks, drag/scroll, long press, browser tabs, zoom, app layout and disconnect/restore. Keep screen prepared across reconnect. For a persistent service or privacy mode, validate replay-marker compatibility with real host logs.
5. Turn off phone screen to remove the prepared monitor after restoring the moved window. Switch to UU only after the screen is released and remotes disconnected.

## Gates
- 428 unit/core tests passed and Release build reported zero warnings and errors before candidate packaging.
- CrossDesk Windows and Android 1.7.7 are now installed in per-user / device app locations. Android first-run privacy consent was confirmed by the user; the app is connected to the signaling server.
- Direct IP host listens on TCP 52888. The tablet can ping the Windows PC, but inbound TCP is blocked by Windows Firewall. A temporary ADB reverse tunnel to 127.0.0.1:52888 succeeds and is reserved for test only; a narrowly scoped Private-profile firewall rule is still needed for normal LAN use.
- After a manual retry, CrossDesk Windows reports `已连接服务器` in device-ID mode. ID mode is available for acceptance and needs no inbound firewall rule. Direct IP remains optional and requires Private-profile TCP 52888. The CrossDesk Service was not installed; it is optional for normal desktop control and only required for lock-screen/sign-in-screen control.
- Authenticated mobile session and selection of Snug DISPLAY40 passed. Enhanced gestures, full-screen layout, zoom, moved-window restore and UU regression still require live validation. Do not publish as a stable release yet.
- Manual host restart guide included; no automatic restart, driver force-binding, or third-party host installation. Electron launcher handoff and broader UWP enumeration remain separate investigations; existing normal/minimized restore and mode protection are not rewritten.

## Live acceptance update
- Generic presets now have stable model identities; restoring an unknown preset with valid saved dimensions preserves those dimensions as a custom choice. Actual 16:10 VDD mode 1080 x 1728 created and released correctly; physical display unchanged.
- CrossDesk portable logs are resolved relative to the running executable. Host DXGI registered the actual Snug display after startup.
- Android authentication succeeded; host logs and Android UI confirmed the real session and selection of DISPLAY40. Normal disconnect emitted Last remote controller disconnected, privacy restoration and released screen-sharing power inhibitors.

- Actual capture was 1080 x 1728, but old WPF surface measured 864 x 1382 at 96 DPI. The DLL launch entry point now sets PerMonitorV2 before WPF initialization. The new preview process verified PerMonitorV2 and 120 DPI at the 125% virtual display. Full phone-surface screenshot remains to be retested.
- CrossDesk upstream privacy input guard admits only its own injected marker. Snug now leaves native input alone while privacy is enabled, transitioning, or unknown, suppresses enhanced gestures and zoom, and resumes only after explicit off evidence. No marker spoofing or privacy-setting change. 428/428 tests pass; Release build 0 warnings/errors.
- Disconnect left Windows on the locked desktop; subsequent real interaction needs the user to unlock Windows. Do not treat the preview DPI check or normal session disconnect as proof of app-window restoration.

### Live DPI and native-input follow-up
- Live WPF catalog HWND matched the physical VDD at 1080 x 1728, 120 DPI, PerMonitorV2. Screenshot confirmed full-screen catalog; CrossDesk privacy-on native tap placed a Notepad window successfully and the status indicated native operation.
- Switching to 1640 x 2624 triggered 216 DPI; Notepad fit the intended app area, but the home-bar HWND expanded to 3580 x 497 instead of 1640 x 221. Prevent loaded windows from moving to the primary display during placement; reconcile the home bar's physical rectangle after WPF layout settles. Build and 428 core tests pass; live navigation retest pending.
- CrossDesk disconnect logged privacy restoration, screen-sharing resource release, and a requested Windows lock. Snug retained its restore ledger while the desktop was locked. Verify original window placement after unlock; do not claim window restore passed merely from the disconnect log.
