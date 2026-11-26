# Snug for Windows 0.2.51

Windows x64 · 2026-11-26

## What's new

- **Room for the iPhone notch:** App content starts below the iPhone 13 Pro top safe area. A slim dark guard protects that space without adding controls.
- **Safer app switching:** Reapply the same physical-pixel bounds when opening, switching, resizing or re-entering a phone app, so the top and bottom reserved areas stay consistent.
- **Keep Snug controls visible:** Strengthen the bottom HomeBar z-order after app focus changes, avoiding collisions with Windows' secondary-display taskbar.
- **One-hand zoom:** Carry forward the 0.2.50 full-height touch-friendly left zoom rail, supporting Chrome/Edge and other verified apps including Cursor, VS Code and Antigravity. Touch anywhere on the track or drag to adjust.

Includes the earlier UU bottom control area, three viewport presets and phone-display background retention.

## Download and installation

**Windows x64:** [Snug-Setup-0.2.51-x64.exe](https://github.com/rizetngan/snug-for-windows/releases/download/v0.2.51/Snug-Setup-0.2.51-x64.exe)

SHA-256:

    57058F46FFB94CD5C24B4D16965391D9709EE0F15099D8B3443241F0DED8DB98

The installer deploys Snug, the bundled .NET runtime and a signed virtual-display driver. The installer itself is unsigned; standard Windows administrator approval is required. Disconnect UU Remote and close the phone screen before upgrading; existing Snug settings are retained.

The Windows PowerShell installer can download the latest stable GitHub Release and verify its SHA-256 before installation: [installation instructions](../../README.md#powershell-安装).

## Validation

- 371 / 371 core automated tests passed
- Release solution build: 0 errors, 0 warnings
- Windows 0.2.51 installed and launched on the target Windows machine; the notch-guard component is present in the installed binary
- User approved this version for public release following the iPhone app-switching review

The phone-specific notch inset is calibrated for iPhone 13 Pro. Device-specific cutout positions and remaining keyboard behavior should be tested separately for other phones.

**Maintained independently by [rizetngan](https://github.com/rizetngan), based on the original [Snug](https://github.com/zephyr4123/snug).**