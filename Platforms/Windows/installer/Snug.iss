#define AppName "Snug for Windows"
#define AppVersion "0.2.49"
#define AppExeName "SnugLauncher.exe"

[Setup]
AppId={{6A9A3C8D-9C6F-4F8B-AF5E-21B6A5B5F4E1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Snug contributors
AppPublisherURL=https://github.com/rizetngan/snug-for-windows
AppSupportURL=https://github.com/rizetngan/snug-for-windows/issues
VersionInfoProductName=Snug for Windows
VersionInfoDescription=Snug for Windows (community preview)
VersionInfoCopyright=Copyright (c) 2026 AoyamaShou; Copyright (c) 2026 zephyr4123
DefaultDirName={autopf}\Snug
DefaultGroupName=Snug
UninstallDisplayIcon={app}\Snug.ico
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.18362
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=Snug-Setup-{#AppVersion}-x64
SetupIconFile=assets\Snug.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式"; Flags: unchecked

[Files]
Source: "..\artifacts\package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Snug\Snug"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Snug.ico"
Name: "{autodesktop}\Snug"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Snug.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 Snug"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\CaptureMonitor\Install-SnugCaptureMonitor.ps1"" -Action Uninstall"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "SnugCaptureMonitorUninstall"; Check: HasCaptureMonitorConfig
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Driver\Install-SnugDriver.ps1"" -Action Uninstall"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "SnugDriverUninstall"; Check: HasSnugDriverMarker

[Code]
var
  CaptureMonitorStopped: Boolean;
  CaptureMonitorReady: Boolean;

function IsSnugRunning: Boolean;
begin
  Result := CheckForMutexes('Local\SnugForWindows');
end;

function HasSnugDriverMarker: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonappdata}\Snug\driver-ownership.json'));
end;

function HasCaptureMonitorConfig: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonappdata}\Snug\uu-capture-monitor.json'));
end;

function InitializeSetup: Boolean;
begin
  Result := not IsSnugRunning;
  if not Result then
    MsgBox('请先退出 Snug，再安装或更新。', mbError, MB_OK);
end;

function InitializeUninstall: Boolean;
begin
  Result := not IsSnugRunning;
  if not Result then
    MsgBox('请先退出 Snug，再卸载。', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ScriptPath: String;
  PowerShellPath: String;
  Parameters: String;
begin
  Result := '';
  ScriptPath := ExpandConstant('{app}\CaptureMonitor\Install-SnugCaptureMonitor.ps1');
  if not FileExists(ScriptPath) then exit;

  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -Action Stop';
  if not Exec(PowerShellPath, Parameters, ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := '无法停止 Snug UU 后台监视器，更新已暂停。'
  else if ResultCode <> 0 then
    Result := 'Snug UU 后台监视器未能正常停止，更新已暂停。'
  else
    CaptureMonitorStopped := True;
end;

procedure DeinitializeSetup();
var
  ResultCode: Integer;
  ScriptPath: String;
  PowerShellPath: String;
  Parameters: String;
begin
  if CaptureMonitorStopped and not CaptureMonitorReady then
  begin
    ScriptPath := ExpandConstant('{app}\CaptureMonitor\Install-SnugCaptureMonitor.ps1');
    if FileExists(ScriptPath) then
    begin
      PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
      Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -Action Resume';
      if not Exec(PowerShellPath, Parameters, ExpandConstant('{app}'),
        SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        MsgBox('更新未完成，Snug UU 后台监视器也未能自动恢复。请重新运行 Snug 安装程序修复。', mbError, MB_OK);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  PowerShellPath: String;
  Parameters: String;
  MonitorScriptPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
    Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{app}\Driver\Install-SnugDriver.ps1') + '" -Action Install';

    if not Exec(PowerShellPath, Parameters, ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('无法启动 Snug 虚拟显示驱动安装程序。');

    if ResultCode <> 0 then
      RaiseException('Snug 虚拟显示驱动安装失败。请查看 %ProgramData%\Snug\Logs\driver-install.log。');

    MonitorScriptPath := ExpandConstant('{app}\CaptureMonitor\Install-SnugCaptureMonitor.ps1');
    Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' + MonitorScriptPath + '" -Action Install';
    if not Exec(PowerShellPath, Parameters, ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('无法启动 Snug UU 后台监视器安装程序。');

    if ResultCode <> 0 then
      RaiseException('Snug UU 后台监视器安装失败。请查看 %ProgramData%\Snug\Logs\uu-capture-monitor-install.log。');

    CaptureMonitorReady := True;
  end;
end;