; Inno Setup script for EcoTrack Hardware Bridge
; Builds: EcoTrackHardwareBridgeSetup.exe
;
; Compile from the "installer" folder with:
;   iscc EcoTrackHardwareBridge.iss
;
; Notes:
;  - Logs in C:\ProgramData\EcoTrack\Logs are intentionally left untouched on uninstall.

#define AppName "EcoTrack Hardware Bridge"
#define AppVersion "1.0.0"
#define AppPublisher "EcoTrack"
#define AppExeName "EcoTrack.HardwareBridge.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{7E4C2B1A-5D3F-4A9E-8C21-9F0B6E7A1C34}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={commonpf}\EcoTrack\HardwareBridge
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=Output
OutputBaseFilename=EcoTrackHardwareBridgeSetup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu shortcut
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
; Startup shortcut (launches the bridge on Windows sign-in for all users)
Name: "{commonstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
