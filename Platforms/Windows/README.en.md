# Snug for Windows

[简体中文](README.md) · [English](README.en.md) · [Project home](../../README.en.md)

Snug enhances display and touch interaction for UU Remote. This community contribution adds Windows support to [Snug](https://github.com/zephyr4123/snug) 2.5.0, preserving the original App Library, phone workspace, window restoration, gestures and screenshot workflow alongside macOS. Windows 0.2.51 is an independently maintained release; upstream has explicitly declined to merge the Windows port, so Windows support will continue on a separate maintenance track.

Version 0.2.48 adds a bottom UU phone-safe area, a Chrome/Edge left-side zoom rail, up to ten minutes of background phone-display retention and Classic/Expanded/Full viewport calibration. The same inset applies to Snug's navigation and managed app content. See the [illustrated 0.2.48 guide](../../docs/0.2.48/README.md).

**0.2.51:** Adds an iPhone 13 Pro notch top safe area, repositions windows and the bottom HomeBar after app switches, and extends the left-edge zoom rail to more supported apps. For immediate release after UU disconnect, turn Phone Screen OFF.


## 0.3.0 candidate · CrossDesk and extension-free zoom

- One phone workspace supports UU Remote and CrossDesk. In CrossDesk mode, Windows runs the host and Android runs the controller.
- CrossDesk absolute touch input enters the shared gesture core through its dedicated input marker for taps, swipes, long press and scrolling.
- Chrome / Edge use native Windows tab identification and relative zoom; no browser extension or Native Messaging is required.
- Optional single-phone UU model matching (off by default) and five generic aspect presets.

[0.3.0 illustrated guide and architecture](../../docs/0.3.0/README.en.md). Candidate only: 426 core tests pass; real CrossDesk mobile connection and UU regression are still in acceptance.
## Installation and usage

1. Install UU Remote and the Snug Windows x64 installer on the controlled computer. First installation requests administrator approval to deploy the signed virtual display driver and register the SYSTEM-level UU capture monitor startup task.
2. Connect from your phone in UU Remote, select Snug Phone in the display list and use screen-touch mode.
3. Open an app or window in the App Library; use the bottom bar to return. Snug manages window fitting, scrolling, long press and per-app text enlargement.
4. Enable Screenshot in desktop Settings (off by default). Tap the camera when a window is placed; tap the thumbnail to crop and copy the result to the host clipboard.
5. The SYSTEM monitor observes UU's display-capture lifecycle and sends only fixed state metadata to Snug through a local one-way pipe. It can confirm capture activity, not the controller's device type or physical monitor identity. If the monitor starts late or loses ETW events, Snug deliberately falls back to unknown/compatibility mode instead of inventing a disconnected state.

## PowerShell / AI Agent installation

Paste the following into **Windows PowerShell** to install the latest stable release:

```powershell
$ErrorActionPreference = 'Stop'
$script = Join-Path $env:TEMP 'install-snug.ps1'
Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/rizetngan/snug-for-windows/main/scripts/install-windows.ps1' -OutFile $script
& $script
```

The script verifies the release's SHA-256, asks you to disconnect UU Remote before installation, and prompts for administrator approval when running the official installer. **Prefer an AI Agent?** [Copy the full Agent installation prompt](../../README.en.md#install-with-an-ai-agent).

Installer source: [scripts/install-windows.ps1](../../scripts/install-windows.ps1). To verify without installing, run `& $script -DownloadOnly`.

## Build and package

Requirements: Windows x64 (Windows 10 22H2 tested), .NET SDK 10.0.404 (latest patch in its feature band allowed), NuGet connectivity. Packaging additionally requires Windows Desktop Runtime 10.0.15 and Inno Setup 7. Build and unit tests do not need administrator rights.

    cd Platforms\Windows
    build-snug.cmd
    run-snug.cmd

SDK resolution: SNUG_DOTNET_ROOT, .tools/dotnet, DOTNET_ROOT, standard Program Files/dotnet. For custom tools set the environment variables in the current terminal:

    $env:SNUG_DOTNET_ROOT = 'C:\Program Files\dotnet'
    $env:SNUG_ISCC = 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
    .\build-snug.cmd
    .\package-snug.cmd

The output is artifacts/installer/Snug-Setup-0.2.51-x64.exe. The installer bundles the pinned runtime and signed VDD; the installer itself is not signed. The app-scoped legacy CET compiler bridge preserves the SDK binaries and system security settings. On supported systems, -p:SnugUseLegacyWindowsCETWorkaround=false uses the native SDK compiler.

## Architecture

- Snug.Windows.Core: platform-independent window and restoration state, screen fit, gesture and momentum logic, UU session evidence and screenshot geometry.
- Snug.Windows: WPF interface and Windows system adapters.
- installer: signed VDD 25.7.23 and NefCon 1.21.1, plus a SYSTEM startup task for the UU capture monitor. The monitor uses a user-scoped named pipe; only Snug-managed tasks and device resources are removed on uninstall.

## Validation and limitations

268 core tests pass. Local Windows tests cover window placement/restoration, VS Code restart zoom recovery, Chrome tab input and two-axis scrolling, menu drag, force quit, native window capture and crop. Actual iPhone phone-screen re-entry and UU-disconnect cleanup have also passed.
Menu momentum passed WPF timer tests. Native clipboard write after mobile screenshot/crop and the complete iPhone gesture matrix remain pending. VDD mode steps differ from macOS. Mixed physical-display configurations, some apps zoom behavior, cross-virtual-desktop handling and protected/GPU window capture need wider testing.
The Windows UU detector combines visible server overlays, the local controlled-device UIA table, bounded streamer-log growth and the SYSTEM ETW capture inventory. Remote peer TCP is diagnostic context only and is not session-presence evidence; persistent 80/443 connections cannot extend a session. Controller type and streamed-display identity remain unknown.

## Credits and license

Original Snug author: zephyr4123. Project predecessor: UUyc-companion by AoyamaShou. Original copyright, history and community acknowledgments are preserved. Windows support additionally thanks Virtual Display Driver and NefCon. See the [MIT license](../../LICENSE) and [third-party notices](installer/THIRD_PARTY_NOTICES.md). Snug is not an official UU Remote product.
Starting in 0.2.42, switch between the primary display and Snug Phone directly in UU without destroying or recreating the virtual display. Restore Window in the App Library returns the managed window while keeping the phone display available. Ending the episode or disabling Phone Screen restores windows and releases the display.
