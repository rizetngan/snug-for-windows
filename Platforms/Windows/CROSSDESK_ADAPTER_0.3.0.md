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

### Provider-specific restore live acceptance
- Normal exit restored the test window exactly to physical (145,237), 406x335, and released the VDD. The previous automatic disconnect restore had been blocked by independently reported UU capture activity.
- Remove the UU connection condition only from the CrossDesk-owned managed-window return path; keep provider-switch and display-ownership rules unchanged. Add provider state to the optional acceptance audit. 428 core tests passed; Release build 0 warnings/errors.
- Real CrossDesk session on DISPLAY43 opened Notepad and switched to 1640x2624. Bottom bar/chevron rendered in the correct position after the DPI reconciliation. Native remote taps did not reliably reach the bottom bar after the mode change; input mapping remains unverified (do not attribute the cause solely to DPI or CrossDesk without further testing).
- Explicit CrossDesk disconnect triggered automatic return. Audit changed placedWindow=true to false, ledger removed, test HWND physical rectangle exactly (145,237,406,335), and phone display remained prepared. Windows locked automatically following the CrossDesk disconnect. Enhanced gestures, browser zoom, navigation click and UU live regression remain outstanding.

### Absolute-pointer acceptance correction
- CrossDesk 1.7.7 host logged mouse-display geometry updates after each DXGI mode change. The Android menu was displaying Relative Mouse, which denotes the current input mode, not a target action. Upstream [SessionControls](https://github.com/kunkundi/crossdesk/blob/v1.7.7/apps/android/app/src/main/java/cn/crossdesk/mobile/SessionControls.java) and [RemoteVideoView](https://github.com/kunkundi/crossdesk/blob/v1.7.7/apps/android/app/src/main/java/cn/crossdesk/mobile/RemoteVideoView.java) confirm relative mode moves only on drag and ordinary taps act at the current pointer.
- Do not attribute the failed direct taps to a Snug coordinate-mapping defect on that evidence. The Android client has now been switched to Absolute Mouse and its menu confirms the mode. The preceding repeat disconnect again cleared the restore ledger. A new connected session is ready; Windows is locked following disconnect. Direct absolute navigation and zoom still require retest after unlocking. Privacy configuration remains unchanged.
### Absolute-pointer live navigation acceptance
- After the user unlocked Windows, confirmed Android was in Absolute Mouse mode, selected DISPLAY43, and tapped the dedicated Notepad acceptance document from the catalog. The app fit the 1640x2624 display. A tap on the centered bottom chevron returned to the app catalog, with the same document marked as the current app on the phone screen. Native absolute tap and bottom navigation now pass across the mode change, with privacy settings unchanged.
- The attempted return to the current app could not be verified because the Android foreground changed to an unrelated application; further tablet clicks were stopped. Enhanced gestures and browser zoom with privacy off, and UU live regression, remain outstanding.

### Privacy-off live gesture and native zoom acceptance
- User resumed Android CrossDesk. Confirmed Absolute Mouse and selected DISPLAY43. Temporarily turned off privacy through the client's normal session control; host logged `state=1, overlay=false, input_blocked=false`. No persistent settings changed.
- Real Notepad long press produced the native right-click context menu. Quick swipe selected text instead of scrolling. A scoped 6.5-second read-only mouse probe captured only injected mouse events in the virtual-screen region: marker CSDM (0x4353444D), left-down followed by the first 185-pixel drag after 16 ms, then move events and left-up. Android 1.7.7 emits button-drag swipes as well as the previously supported move-only form. The normalizer now recognizes a fast first drag as Slide and a delayed drag as Hold. Added two regression tests: fast drag scroll, delayed drag selection. 430/430 core tests passed; Release build 0 warnings/errors. New binary still needs live swipe retest.
- In a dedicated Chrome local test window, expanded Snug's side zoom rail and tapped the track: Chrome native zoom changed from 100% to 175%; independent test page devicePixelRatio changed from 2.250 to 3.938 (viewport 717x797 to 410x455). Snug Default reset returned Chrome to 100%, devicePixelRatio 2.250 and viewport 717x797. No Snug browser extension or Native Messaging host used. Drag of expanded rail was not reliably verified; tab-switch isolation and UU live regression remain pending.
- Returned dedicated test browser window through Snug's Restore action; ledger removed. Re-enabled privacy through normal client UI; host logged `state=3, overlay=true, input_blocked=true` and Android confirmed privacy on. Existing session retained; no disconnect or new Windows lock requested.
