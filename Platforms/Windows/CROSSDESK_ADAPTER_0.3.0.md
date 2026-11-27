# Windows 0.3.0 - CrossDesk adaptation

## Reference baseline
- Own candidate: 0.2.54 / 100122f (extension-free native zoom).
- Original Snug: v2.9.1; main 9c7918d. Delta b7a01d2..9c7918d is documentation only.
- nicocart/snug-win: v1.5.1 / 03c4491, MIT. CrossDesk Windows log lifecycle and display slot reference; attribution included in installer/THIRD_PARTY_NOTICES.md.

## Phase 1 implemented
- CrossDeskLogLine, CrossDeskLogFollower, CrossDeskSessionEvidence: Windows host log parsing, bounded incremental reads, rotation and partial lines, explicit session evidence, GUI reattachment and slot tracking.
- CrossDeskSessionDetector: read-only process generation and independent user/SYSTEM log observations; conflicting session evidence stays unknown. Main connection status tooltip exposes diagnostic summary.
- Existing UU connection tracker, capture observer, virtual display lifecycle, window restoration, input hook, browser native zoom and geometry unchanged. No automatic CrossDesk activation, restart or installation.
- Validation: 410 core tests passed (12 added). Release solution build: 0 warnings, 0 errors. Real CrossDesk host/client, live session lifecycle and touch routing not yet tested.

## Integration order
1. Verify installed CrossDesk host and actual mobile client logs; verify display registration/hotplug and persistent host behavior.
2. Separate remote ownership/session routing from existing UU detector, preserving UU behavior. Pre-create and verify the virtual display before a host that caches display slots starts; never tear down another provider's live screen.
3. Adapt CrossDesk absolute/relative input and mouse markers, then reuse current gesture and replay infrastructure. Explicit disconnect restores managed windows before removing owned displays.
4. Assess manual restart only while idle, after proving screen registration failure. Automatic restart requires bounded retries, cooldown and real idle evidence.
5. Evaluate controller model matching, generic ratio presets, minimized-window bounds, display recovery and Electron launcher handoff independently; integrate only regression-tested changes. Computer library and tray enhancements are separate.
6. Acceptance: UU unchanged, CrossDesk iPhone/Android connection and disconnection, host restart, display switching, touch, zoom without extension, safe areas, and recovery. No release until live acceptance.
