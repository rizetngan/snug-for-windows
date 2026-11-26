# Snug for Windows — version status

Latest public release: v0.2.51 (2026-11-26)
Repository: https://github.com/rizetngan/snug-for-windows
Primary source branch: main
Upstream baseline: Snug 2.5.0 — original author zephyr4123, MIT license

## Release contents

- Windows x64 WPF port, phone screen, App Library, virtual display, touch gestures, app window placement/restore and screenshot/crop flows.
- UU capture monitor with independent, bounded session signals. Persistent HTTP(S) connections do not establish a remote session.
- Classic / Expanded / Full phone viewport modes and content density setting.
- One-handed left zoom rail for the supported browser and app set, including touch-anywhere dragging, collapse and reset.
- 0.2.51: app placement below iPhone 13 Pro top notch area, common top/bottom safe bounds, reusable notch guard, and stronger secondary-taskbar/phone HomeBar layering through app switches.

## Checks

- 371 / 371 core regression tests passed; Release solution build 0 warnings, 0 errors.
- Installed 0.2.51 on Windows with installer exit 0 and application startup confirmed.
- The user authorized publication of this candidate to the independently maintained Windows repository.
- Release notes and checksum: [docs/0.2.51/RELEASE_NOTES.md](../../docs/0.2.51/RELEASE_NOTES.md).

## Boundaries

- iPhone 13 Pro top notch geometry is calibrated to this device; other phone models and their cutout areas may differ.
- Some complex UU session, keyboard pan, GPU/protected-window capture and multi-physical-monitor scenarios remain useful regression checks.
- Virtual-display driver is signed, but the Windows installer itself is not code-signed.
- The macOS upstream code and history are preserved; no Windows maintenance request is being sent upstream.

## Build

From Platforms/Windows, run build-snug.cmd or package-snug.cmd with the installed .NET 10 SDK and Inno Setup 7. The packaged installer is artifacts/installer/Snug-Setup-0.2.51-x64.exe.