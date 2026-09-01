#define AppName "Cozy Shutdown"
#define AppExeName "CozyShutdown.exe"
#define AppPublisher "sol669"
#define AppURL "https://github.com/sol669/Cozy-Shutdown"

#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{A3C0F299-C760-4A97-BBF5-54D9A188950B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppPublisher}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\release
OutputBaseFilename=Cozy-Shutdown-Setup-Offline-v{#AppVersion}
SetupIconFile=..\Shutdown-WinUI3-Source\src\Shutdown\Assets\ShutdownTrey.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=sol669.CozyShutdown.Singleton
MinVersion=10.0.17763
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} offline installer
VersionInfoProductName={#AppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "prerequisites\windowsdesktop-runtime-8.0.30-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: not IsDotNetDesktop8Installed
Source: "prerequisites\WindowsAppRuntimeInstall-x64-2.3.1.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: not IsWindowsAppRuntimeInstalled

[Dirs]
Name: "{app}\Data"; Permissions: users-modify

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Cozy Shutdown"; Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\Assets\ShutdownTrey.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\Assets\ShutdownTrey.ico"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Data"

[Run]
Filename: "{tmp}\windowsdesktop-runtime-8.0.30-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Установка Microsoft .NET Desktop Runtime..."; Flags: waituntilterminated; Check: not IsDotNetDesktop8Installed
Filename: "{tmp}\WindowsAppRuntimeInstall-x64-2.3.1.exe"; Parameters: "--quiet"; StatusMsg: "Установка Windows App Runtime..."; Flags: waituntilterminated; Check: not IsWindowsAppRuntimeInstalled
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNetDesktop8Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{autopf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function IsWindowsAppRuntimeInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "if (Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.2'' -ErrorAction SilentlyContinue | Where-Object { [version]$_.Version -ge [version]''2.3.0.0'' } | Select-Object -First 1) { exit 0 } else { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;
