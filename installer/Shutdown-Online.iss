#define AppName "Shutdown Trey"
#define AppExeName "Shutdown.exe"
#define AppPublisher "sol669"
#define AppURL "https://github.com/sol669/Shutdown"

#ifndef AppVersion
  #define AppVersion "1.1.1"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish-standard"
#endif

[Setup]
AppId={{43A74627-56E1-4F8A-93E9-78F55742E814}
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
OutputBaseFilename=Shutdown-Trey-Setup-v{#AppVersion}
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
AppMutex=sol669.Shutdown.Singleton
MinVersion=10.0.17763
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} online installer
VersionInfoProductName={#AppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "https://aka.ms/dotnet/8.0/dotnet-runtime-win-x64.exe"; DestDir: "{tmp}"; DestName: "dotnet-runtime-8-win-x64.exe"; ExternalSize: 28639808; Flags: external download ignoreversion; Check: not IsDotNet8Installed
Source: "https://aka.ms/windowsappsdk/2.3/latest/windowsappruntimeinstall-x64.exe"; DestDir: "{tmp}"; DestName: "windowsappruntimeinstall-x64.exe"; ExternalSize: 113192760; Flags: external download ignoreversion; Check: not IsWindowsAppRuntimeInstalled

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\dotnet-runtime-8-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Установка Microsoft .NET 8 Runtime..."; Flags: waituntilterminated; Check: not IsDotNet8Installed
Filename: "{tmp}\windowsappruntimeinstall-x64.exe"; Parameters: "--quiet"; StatusMsg: "Установка Windows App Runtime..."; Flags: waituntilterminated; Check: not IsWindowsAppRuntimeInstalled
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet8Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{autopf}\dotnet\shared\Microsoft.NETCore.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function IsWindowsAppRuntimeInstalled: Boolean;
var
  ResultCode: Integer;
  PowerShellPath: String;
  Params: String;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    '"if (Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.2.3'' -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"';
  Result := Exec(PowerShellPath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;
