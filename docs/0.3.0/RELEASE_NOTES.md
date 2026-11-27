# Snug for Windows 0.3.0 — Release notes

**Status: candidate build.** Stable publication is gated on live CrossDesk mobile acceptance and UU regression.

## Highlights

- UU Remote and CrossDesk selectable in one Snug phone workspace.
- CrossDesk Windows host + Android controller support, including absolute-touch translation for taps, move-only slides, long press and scrolling.
- Extension-free Chrome / Edge zoom. No browser extension, Native Messaging host or local relay.
- Optional UU phone model matching and generic phone/tablet aspect presets.
- Clearer provider selector; UU-only settings are hidden in CrossDesk mode.

## Validation

- 426 / 426 core tests passed.
- Release build: 0 warnings, 0 errors.
- Windows 10 Pro 22H2 (build 19045).
- Chrome / Edge audit found no Snug extension or Native Messaging registration.
- Live CrossDesk mobile connection, touch replay, window restoration and UU regression remain pending.

## Packages

- `Snug-Setup-0.3.0-x64.exe`: full setup; administrator approval required for first-time virtual-display driver installation.
- `Snug-Portable-0.3.0-x64.zip`: unpack-and-run; requires the Snug VDD and system tasks to have already been installed.

The installer and launcher are not code-signed. Windows may show a SmartScreen warning. Verify the SHA-256 values before running; use the full installer for a clean setup or the portable package when the driver is already present.

## SHA-256

```text397684e0d64f9cf81a8ae6a8bb6bc74367b9834b14926a3bbbccd713ac226669  Snug-Setup-0.3.0-x64.exe
4ee46016c83253a04ca3fd2736127bfec0ea8a6670dca3df010e49557b160af9  Snug-Portable-0.3.0-x64.zip
```