@echo off
setlocal
set "SNUG_ROOT=%~dp0"
cd /d "%SNUG_ROOT%"
call "%SNUG_ROOT%tools\Resolve-Dotnet.cmd"
if errorlevel 1 exit /b %errorlevel%
set "PACKAGE=%SNUG_ROOT%artifacts\package"
set "BUILD_OUTPUT=%SNUG_ROOT%artifacts\release-build"
set "CAPTURE_BUILD_OUTPUT=%SNUG_ROOT%artifacts\capture-monitor-build"
set "RUNTIME_VERSION=10.0.15"
if not exist "%DOTNET_ROOT%\shared\Microsoft.WindowsDesktop.App\%RUNTIME_VERSION%\PresentationFramework.dll" (
  echo Packaging requires Windows Desktop Runtime %RUNTIME_VERSION% in the selected SDK directory.
  exit /b 1
)
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

rem Use a dedicated build directory so an open Snug preview cannot lock packaging DLLs.
"%DOTNET_ROOT%\dotnet.exe" restore "%SNUG_ROOT%Snug.Windows.sln"
if errorlevel 1 exit /b %errorlevel%
"%DOTNET_ROOT%\dotnet.exe" restore "%SNUG_ROOT%tools\UuCaptureProbe\UuCaptureProbe.csproj"
if errorlevel 1 exit /b %errorlevel%
"%DOTNET_ROOT%\dotnet.exe" test "%SNUG_ROOT%Snug.Windows.sln" -c Release --no-restore
if errorlevel 1 exit /b %errorlevel%
rem Package only production binaries, not xUnit/testhost dependencies.
if exist "%BUILD_OUTPUT%" rmdir /s /q "%BUILD_OUTPUT%"
if exist "%BUILD_OUTPUT%" (
  echo Cannot clean the release staging directory. Close its preview process and retry.
  exit /b 1
)
"%DOTNET_ROOT%\dotnet.exe" build "%SNUG_ROOT%src\Snug.Windows\Snug.Windows.csproj" -c Release --no-restore "-p:OutDir=%BUILD_OUTPUT%/"
if errorlevel 1 exit /b %errorlevel%

rem Build the privileged UU capture monitor separately from the interactive WPF app.
if exist "%CAPTURE_BUILD_OUTPUT%" rmdir /s /q "%CAPTURE_BUILD_OUTPUT%"
if exist "%CAPTURE_BUILD_OUTPUT%" (
  echo Cannot clean the capture monitor staging directory.
  exit /b 1
)
"%DOTNET_ROOT%\dotnet.exe" build "%SNUG_ROOT%tools\UuCaptureProbe\UuCaptureProbe.csproj" -c Release --no-restore "-p:OutDir=%CAPTURE_BUILD_OUTPUT%/"
if errorlevel 1 exit /b %errorlevel%

if exist "%PACKAGE%" rmdir /s /q "%PACKAGE%"
mkdir "%PACKAGE%"
if errorlevel 1 exit /b %errorlevel%

robocopy "%BUILD_OUTPUT%" "%PACKAGE%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP
if errorlevel 8 exit /b %errorlevel%

mkdir "%PACKAGE%\CaptureMonitor"
robocopy "%CAPTURE_BUILD_OUTPUT%" "%PACKAGE%\CaptureMonitor" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP
if errorlevel 8 exit /b %errorlevel%
copy /y "%SNUG_ROOT%installer\payload\CaptureMonitor\Install-SnugCaptureMonitor.ps1" "%PACKAGE%\CaptureMonitor\Install-SnugCaptureMonitor.ps1" >nul
if errorlevel 1 exit /b %errorlevel%

mkdir "%PACKAGE%\Runtime\host\fxr\%RUNTIME_VERSION%" 2>nul
mkdir "%PACKAGE%\Runtime\shared\Microsoft.NETCore.App\%RUNTIME_VERSION%" 2>nul
mkdir "%PACKAGE%\Runtime\shared\Microsoft.WindowsDesktop.App\%RUNTIME_VERSION%" 2>nul

copy /y "%DOTNET_ROOT%\dotnet.exe" "%PACKAGE%\Runtime\dotnet.exe" >nul
if errorlevel 1 exit /b %errorlevel%

robocopy "%DOTNET_ROOT%\host\fxr\%RUNTIME_VERSION%" "%PACKAGE%\Runtime\host\fxr\%RUNTIME_VERSION%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP
if errorlevel 8 exit /b %errorlevel%
robocopy "%DOTNET_ROOT%\shared\Microsoft.NETCore.App\%RUNTIME_VERSION%" "%PACKAGE%\Runtime\shared\Microsoft.NETCore.App\%RUNTIME_VERSION%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP
if errorlevel 8 exit /b %errorlevel%
robocopy "%DOTNET_ROOT%\shared\Microsoft.WindowsDesktop.App\%RUNTIME_VERSION%" "%PACKAGE%\Runtime\shared\Microsoft.WindowsDesktop.App\%RUNTIME_VERSION%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP
if errorlevel 8 exit /b %errorlevel%

copy /y "%DOTNET_ROOT%\LICENSE.txt" "%PACKAGE%\Runtime\LICENSE.txt" >nul
copy /y "%DOTNET_ROOT%\ThirdPartyNotices.txt" "%PACKAGE%\Runtime\ThirdPartyNotices.txt" >nul
copy /y "%SNUG_ROOT%..\..\LICENSE" "%PACKAGE%\LICENSE" >nul
copy /y "%SNUG_ROOT%installer\THIRD_PARTY_NOTICES.md" "%PACKAGE%\THIRD_PARTY_NOTICES.md" >nul

if not exist "%CSC%" (
  echo .NET Framework C# compiler was not found: %CSC%
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" /out:"%PACKAGE%\SnugLauncher.exe" "%SNUG_ROOT%tools\SnugLauncher.cs"
if errorlevel 1 exit /b %errorlevel%

if not exist "%SNUG_ROOT%artifacts\installer" mkdir "%SNUG_ROOT%artifacts\installer"
set "ISCC=%SNUG_ISCC%"
if not defined ISCC set "ISCC=%SNUG_ROOT%.tools\innosetup7\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%SNUG_ROOT%..\..\..\..\.tools\innosetup7\ISCC.exe"
if not exist "%ISCC%" (
  echo Inno Setup compiler not found. Install the official Inno Setup compiler before packaging.
  exit /b 1
)
"%ISCC%" "%SNUG_ROOT%installer\Snug.iss"
if errorlevel 1 exit /b %errorlevel%

echo.
echo Snug installer created in:
echo %SNUG_ROOT%artifacts\installer
exit /b 0