# Install Snug for Windows from its latest stable GitHub Release.
# For a verification-only run: .\install-windows.ps1 -DownloadOnly
param([switch]$DownloadOnly)
$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    -not [Environment]::Is64BitOperatingSystem) {
    throw 'Snug for Windows requires Windows x64.'
}

$repo = 'rizetngan/snug-for-windows'
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -TimeoutSec 45 -Headers @{ 'User-Agent' = 'Snug-Installer' }
if ($release.draft -or $release.prerelease) { throw 'Only stable releases are supported.' }
$installers = @($release.assets | Where-Object { $_.name -match '^Snug-Setup-[0-9.]+-x64\.exe$' })
$checksums = @($release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' })
if ($installers.Count -ne 1 -or $checksums.Count -ne 1) { throw 'Required release assets were not found.' }

$downloads = Join-Path $env:USERPROFILE 'Downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$installer = Join-Path $downloads $installers[0].name
$sums = Join-Path $downloads 'Snug-SHA256SUMS.txt'
Write-Host "Downloading $($installers[0].name)..."
Invoke-WebRequest -UseBasicParsing -TimeoutSec 360 -Uri $installers[0].browser_download_url -OutFile $installer
Invoke-WebRequest -UseBasicParsing -TimeoutSec 60 -Uri $checksums[0].browser_download_url -OutFile $sums

$expected = @(
    Get-Content -LiteralPath $sums | ForEach-Object {
        if ($_ -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$' -and $Matches[2] -eq $installers[0].name) {
            $Matches[1]
        }
    }
)
if ($expected.Count -ne 1) { throw 'Release checksum missing or ambiguous.' }
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
if ($actual -ne $expected[0]) { throw 'SHA-256 mismatch; installation aborted.' }
Write-Host "Verified $($installers[0].name) (SHA-256: $actual)"

if ($DownloadOnly) {
    Write-Host "Verification complete. Installer remains at $installer"
    return
}

Write-Host 'Snug installs a signed virtual display driver and a SYSTEM-level UU capture-monitor task.'
Write-Host 'The installer itself is unsigned and Windows will request administrator approval.'
Write-Host 'Disconnect active UU Remote sessions before upgrading. This script will not stop UU or disable security.'
$answer = Read-Host 'After disconnecting UU and reviewing the changes, type INSTALL to run the official installer'
if ($answer -cne 'INSTALL') {
    Write-Host "Installation cancelled. The verified installer remains at $installer"
    return
}

$process = Start-Process -FilePath $installer -Verb RunAs -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installer exited with code $($process.ExitCode)." }
Write-Host "Snug $($release.tag_name) installer completed. Verify Snug opens and test UU Remote manually."
