## 0.3.0 — CrossDesk adaptation candidate

- Add experimental CrossDesk remote mode with pre-created phone display, independent process/log detection, registration status and confirmed disconnect window restoration.
- Route CrossDesk CSDM absolute touch, taps and move-only slides through the existing gesture core; keep native controls and UU input separate.
- Add optional UU model matching and five generic portrait presets. Preserve the extension-free zoom, existing driver ownership and window layout safeguards.
- Candidate only: live CrossDesk host/client, persistent-host and privacy replay, real mobile gestures, and UU regression acceptance still required.

## 0.2.54 — native-only context zoom (candidate, 2026-11-27)

- Remove the browser extension, Native Messaging relay and packaging dependencies. Chrome/Edge use Windows UI Automation only to identify the selected tab, never to infer its percentage.
- Use neutral-centred relative zoom gestures with native shortcuts; reset uses the browser's configured default. Recheck selected tab and foreground window before each step. Never replay or display a previous tab's absolute percentage.
- Preserve the phone safe areas, layout reconciliation and full-track touch controls. Upgrading from 0.2.53 removes its obsolete bundled companion files.

- Verification: 398 core tests passed; Chrome and Edge with extensions disabled independently verified A=200%, B=100%, rejected stale A gestures, adjusted B=110%, preserved A, and restored A to its native default. Both browsers passed after dismissing an isolated Edge update dialog. Installer built without the companion. iPhone UU acceptance remains pending.

## 0.2.53 — context-aware zoom (candidate, 2026-11-27)

- Real Chrome 158 test reproduced stale native UIA zoom tooltip: tab A had 200%, tab B's independently measured page pixel ratio was 100%, but UIA reported 200% on B. UIA is no longer used as a zoom-value source.
- Add optional Chrome/Edge companion (BrowserZoomExtension) using native tabs.getZoom/setZoom APIs and exact active tab/window IDs. A same-user Windows named pipe and small Native Messaging host relay messages; no web-content permission, URL/title collection, profile access or network listener.
- Track the current browser window/profile/tab and placement session; reject stale drag targets, read external shortcut changes, and display only actual API-confirmed percentages. Reset preserves the browser's configured default and native same-site zoom behavior.
- Without the companion, use native selected-tab identity only for relative zoom. Unobservable desktop apps use relative gestures, not an inherited absolute value. Manual gestures remain native app changes and are not recorded/replayed across other windows or unknown panes. Explicit temporary default zoom remains for existing classified recovery scopes only.
- Preserve full-track touch dragging, auto-collapse and all 0.2.52 phone geometry/recovery changes.
- Candidate verification: 407 core tests, 7 extension API mock tests, 7 real host/pipe transport checks. Real Chrome/Edge companion installation and iPhone UU acceptance are still pending. Main/public release remain 0.2.51.
## 0.2.52 — physical layout reconciliation (candidate, 2026-11-27)

- Fix stale phone display geometry after a native mode reset or DPI change. Accept a mode only when the physical rectangle matches the requested native resolution exactly.
- Reconcile the existing display, managed application, phone catalog, notch guard, bottom bar, zoom rail, screenshot controls and gestures together. Serialize and deduplicate automatic reconciliation.
- Validate the actual window rectangle after fitting and same-session re-entry; attempt normal restoration and return to the catalog when the fit fails.
- Retry outstanding startup restore records through the existing restoration path before starting another phone session, preserving the ledger on failures. Ensure end-session failures release their transition guard.
- Preserve existing content zoom interactions, preferences, virtual-display driver and UU session policy.
- 15 new core regression cases for stale 1640x3170 layout on 1080x2088 mode, logical versus physical dimensions, DPI/origin changes, fit boundaries, same-mode app drift, and reuse of a valid native mode. Real iPhone/UU acceptance remains pending; 0.2.51 remains the public release.
## 0.2.51 — iPhone notch safe area and app-switch taskbar recovery (release, 2026-11-26)

- Real iPhone 13 Pro reproduction: Chrome was placed at raw Y=0 on a 1640x3170 Snug VDD, covering the notch region. The secondary Windows taskbar can also overtake the HomeBar after switching applications.
- Apply a single physically measured, DPI-safe app rectangle on every window pull, resize and same-session re-entry. Its top inset follows the iPhone 13 Pro 47pt / 390pt notch ratio; existing UU/HomeBar bottom reserves are retained.
- Add one non-activating, reusable top-safe overlay only while an app is placed; hide it in the Snug app catalog and close it when the phone display session ends. No extra labels or controls.
- Repair bottom HomeBar z-order again after the new app receives focus and after the WPF presentation frame, alongside the existing periodic secondary-taskbar guard.
- Baseline 1640x3170 virtual display: Chrome changes from (1920,0,1640,2949) to (1920,198,1640,2751), preserving its existing bottom boundary at Y=2949 while moving its tabs out of the notch. HomeBar covers the remaining bottom through Y=3170.
- Tests: 371/371 core tests pass; Release solution build succeeds with 0 warnings and 0 errors.
- Approved for public release on 2026-11-26. Real-device switching, zoom gestures and protected-window combinations remain useful regression scenarios for future updates.
## 0.2.50 — universal touch zoom rail (candidate, 2026-11-25)

- Replace the browser-only zoom rail with one shared left-edge controller for Chrome/Edge and the existing supported app set: Cursor, VS Code, Zed, Antigravity, Windows Terminal, WezTerm and Claude CLI.
- A dedicated transparent hit surface now covers the entire track area; pointer/touch down anywhere in that area immediately selects the nearest native step and captures the gesture. Dragging no longer depends on grabbing the visual thumb; the thumb is only a position indicator. Apply once on release. Browser levels remain 50–200%; supported apps retain their own relative -4 to +8 levels.
- Expand the rail to 64% of the usable phone-screen height, keep the narrow collapsed handle, retain two-second auto-collapse, outside-click dismissal and left-swipe dismissal, and add a mode-aware reset (100% for browsers, default level for apps).
- Keep unsupported document/design applications out of the shortcut allowlist until their zoom commands are verified; WPS, Word and PDF/Adobe apps are not claimed as supported by this change.
- Core tests: 364/364 passed. Windows Release build: 0 warnings, 0 errors. Real UU phone-side touch, drag and overlay-occlusion acceptance remains pending.

## 0.2.49 — window recovery and content density (candidate, 2026-11-25)

- Window identity now wins over changed geometry when reconciling a still-live, original HWND; an incompatible process generation is rejected. A newly recreated HWND still requires matching original application path and last Snug-filled size before restoring. This follows the recovery invariant established in upstream Snug 2.9.1 (#63).
- Windows maximized windows with a saved normal placement explicitly exit maximization before original placement is restored. Previously maximized windows preserve their saved state.
- New 内容密度 setting: original automatic width (default), 600, 720 and 840 logical units. It changes the initial phone workspace density without changing aspect ratio. The exact VDD mode list, mode snapping, window layout and touch baseline use the same pinned setting, effective when the phone screen is next created. Active UU capture is never rebuilt just because a setting changes.
- Windows DxgKrnl capture already tracks independent contexts; add explicit regression tests for upstream 2.8.2's old/new video capture overlap in both event orders and temporary stop/restart. Audio-only stream identity and which physical monitor UU currently streams are not exposed by the current verified Windows capture monitor; do not claim them as ported.
- Carries forward 0.2.48 left-edge WPF browser zoom, two-second collapse, shared UU bottom safe area, Classic/Expanded/Full viewport profiles and ten-minute background hold.
- Bottom HomeBar screenshot is now a 500 ms stationary long-press action. Short taps on the return/menu bar do not capture; movement cancels the camera gesture but preserves upward-swipe navigation. Mouse, touch, release bounds and keyboard accessibility paths are covered.
- Candidate for end-to-end UU iPhone validation; do not publish as stable until all real-device checks pass.

# Windows changelog

## 0.2.48 — background continuity, mobile safe area and one-handed zoom (release)

- Keep an existing owned Snug display and managed window through mobile UU temporary capture suspension for up to ten minutes; returning to UU reuses the same display instead of recreating it. A background hold never creates a new display. True explicit setting-off or Snug Quit still releases the screen.
- A capture-inactive event is not a verified UU controller exit. The fast 0.2.47 disconnect end is intentionally replaced by a background-retention timeout for the existing screen, until a trustworthy controller-level quit signal is found. Manual release via Phone Screen OFF is available.
- Chrome and Edge: replaced the bottom popup with a native WPF left-edge vertical zoom rail, 50–200% native steps, +/- and reset. The opaque ClearType control supports two-second auto-collapse, outside-click collapse, and a leftward dismiss gesture. Only the current webpage is zoomed, without changing display resolution.
- UU mobile menu avoidance: use one proportional bottom safe zone for both the managed application bounds and Snug HomeBar placement, rather than letting the UU operation menu cover the virtual-display navigation.
- WPF rail rendering checked in an isolated real-control harness; collapsed 69×71, expanded 86×427, auto-collapsed again after about two seconds. 325/325 core tests and the Windows Release build passed. The mobile UU touch/occlusion regression remains subject to phone-side testing.
- Latest UU viewport compatibility: selectable classic / expanded / fullscreen presets. Recomputes phone aspect ratio, VDD mode table, window geometry and touch scaling consistently. Presets take effect on next display creation, never destructively while connected. Classic preserves 0.2.47 geometry.
- Preserve upstream-inspired app library, window restoration, input replay, driver safeguards, quick connect and monitor switching. iPhone acceptance pending.

## 0.2.47 — overlapping verified response windows (release)

- One second of continuously verified complete active capture can expedite the unknown-device session path; retain two seconds for incomplete, rebuilding or log-only evidence. Known desktop classification still vetoes phone display; faster capture detection is not phone identification.
- Overlap the two-second continuous verified inactivity window with disconnect debounce; retain at least a half-second after Interrupted and the topology protection. Six-second fallback and ten-second departed-controller grace unchanged.
- Preserve the existing three-second capture rebuild guard, driver, restoration and display protection. 300/300 tests passed; one measured iPhone episode completed normally (4.43s Connected-to-ready; 2.82s Interrupted-to-display-stop; 3.82s first Inactive-to-display-stop). Maintainer approved this release; no claim of comprehensive multi-device or long-term validation.

## 0.2.46 — two-second verified inactivity trial (candidate)

- Reduce only the verified-inactive grace after interruption from 4s to 2s. Keep 6s fallback, 10s departed-controller grace, 3s capture-rebuild protection, 1.5s debounce and 5s topology quiet window.
- Keep driver readiness, physical monitor protection, restoration and connection behavior from 0.2.45.
- Two real phone episodes recorded 5.07/4.84s Connected-to-ready and 3.76/3.82s Interrupted-to-display-stop. Both complete inactive capture paths used about 2s grace.

## 0.2.45 — evidence-based session response (candidate)

- Use a four-second early expiry only after interruption and continuously verified inactive capture inventory; any positive signal or incomplete capture evidence prevents the fast path. Normal six-second reconnect, ten-second departed-controller grace and topology/rebuild guards unchanged.
- Reuse successful driver readiness instead of a redundant blocking PING. Keep owned-display and physical monitor checks, mode settling and restore semantics intact.
- After a normal UU service restart, real-phone trace confirmed complete inventory and 4.04s early expiry, 5.79s interruption-to-display-stop. Connection-to-ready 4.73s.

## 0.2.44 — six-second full-session reconnect trial (candidate)

- Reduce the full-session interruption grace from 10s to 6s. Keep the existing 10s grace for a departed controller while another controller remains connected.
- Retain the 500ms polling, 1.5s debounce, 5s topology quiet window, 2s unknown-controller grace and 3s capture rebuild protection. No further change to driver readiness or input processing.
- One recorded iPhone episode completed normally: Connected-to-ready 5.32s, Interrupted-to-display-stop 7.95s. No unintended Interrupted/Ended. A separate interruption/resume was not observed in this trace.

## 0.2.43 — shorter session response (candidate)

- Reduce the Windows reconnect grace after interruption from 15s to 10s. Keep the 1.5s disconnect debounce, 5s self-induced topology guard, 2s unknown-controller grace, and capture rebuild protection.
- Skip the initial cold-driver ping only when no Snug monitor exists. Keep SYSTEM task ownership checks, no-duplicates behavior, post-start ping and display geometry readiness checks.
- Maintainer confirmed stable mobile behavior; one real episode recorded 4.82s Connected-to-ready and 11.97s Interrupted-to-display-stop, no unintended interruption before disconnect. Broader reconnect matrix remains pending.

## 0.2.42 — retain the phone display for the UU session (candidate)

- Keep the existing phone display and app state when switching between monitors in UU. Remove the episode-dismiss/re-entry flow and primary-desktop floating button.
- Use the existing App Library restore-window action to return a managed window without destroying or changing the phone display; keep the same catalog available.
- Preserve existing UU lifetime detection, disable/quit/disconnect cleanup, and 0.2.41 input replay implementation.
- Maintainer confirmed same-session monitor switching and window restoration on iPhone. Audit verified RestoreManagedWindow completed with sameDisplay and sameBounds true; physical display cleanup completed after disconnect.

## 0.2.41 — deferred mouse replay

- Fix a measured ~1-second low-level mouse-hook stall when replaying taps.
- Deliver replayed mouse input on a separate serial worker, with each click submitted as a single SendInput batch. Keep gesture classification on the existing input thread.
- Preserve double-click, long-press right-click, selection, scrolling, and release a synthetic held button on session changes.
- 283 core tests pass; native Windows replay, double-click, long-press, selection, scroll and cancellation smoke tests pass. The maintainer verified the phone tap-latency fix and disconnect restoration on a real iPhone. Repeated keyboard opening can still fail to pan in UU itself, including on a normal Windows screen; no Snug-side workaround is introduced.

## 0.2.40 — input latency candidate

- Move the global mouse hook and inertial scrolling to a dedicated input dispatcher, independent of UI work.
- Keep native Snug controls and browser chrome on the original event path; avoid process-name lookup for content taps.
- Coalesce redundant phone-library refreshes. Match upstream's nonactivating phone panel outside search and model selection.
- Candidate: actual iPhone tap latency, drag, native close, search focus, app switching, keyboard occlusion and disconnect regression still require verification. iOS/UU keyboard viewport fitting is not implemented by this patch.

## 0.2.39 — capture monitor recovery

- Fix persistent ETW monitor session ownership: use a stable service-specific trace name instead of allocating a fresh random name every launch.
- Reclaim orphaned GUID-named legacy Snug monitor sessions on startup; never stop temporary pilot/probe or unrelated ETW sessions.
- Prevent concurrent monitor service launches and record provider initialization in the monitor log.
- Passed startup and active iPhone + UU session regression (276 automated tests). Reclaimed six legacy traces; verified browser fitting, exit/re-entry and disconnect restore. Restart UU service or Windows once if the server predates the upgraded monitor and capture detection is unavailable. No changes to disconnect evidence or existing restoration policy.


## 0.2.38 — final UU session evidence and acceptance

- Align Windows UU session presence with the upstream model: visible overlay, controller-table evidence, active streamer/capture evidence and SYSTEM ETW capture inventory are authoritative signals.
- Treat remote peer TCP as diagnostic context only. Persistent 80/443 connections do not establish or extend a UU session.
- Preserve controller identity as Unknown when the local UIA table cannot identify a device; do not infer phone/device identity from network state.
- 268 core tests pass; Release solution build passes with 0 warnings and 0 errors.
- Real iPhone + UU Remote acceptance passed: phone-screen creation, >60-second stability, same-session phone-screen re-entry after Exit Phone Screen, and UU-disconnect window restoration / virtual-display cleanup.
- Installer baseline: Snug-Setup-0.2.38-x64.exe; SHA-256 5E803AD6E11676D609B5B4A9A1B8F5E391E536C254564408B115F86A7C132D96.
## 0.2.37 — activity-based UU log fallback

- Replace lifetime file-lock detection with PID-scoped streamer-log growth tracking.
- Require two append-growth observations within 3 seconds; expire activity 2 seconds after the last growth.
- Reset on log rotation, truncation, server ambiguity, unreadable log state and clock regression.
- Keep complete SYSTEM ETW capture inventory authoritative; log growth remains a low-confidence compatibility hint.
- Preserve the existing phone-screen re-entry flow and all upstream 2.5.0 features.
- Validation: 259 core tests pass; full Release solution build passes with 0 warnings and 0 errors.

## 0.2.36

- Add an explicit same-session phone-screen re-entry control after a user dismisses the phone screen.