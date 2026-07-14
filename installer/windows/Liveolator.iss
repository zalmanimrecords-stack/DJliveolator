; Liveolator — Windows installer (Inno Setup 6.5+)
;
; Compiled by scripts/build-installer.ps1, which publishes the app self-contained and
; passes the two defines below. The publish folder already contains everything the app
; needs at runtime (the .NET runtime, Avalonia, and the BASS native libraries — see the
; CopyBassNativeToPublish target in Liveolator.App.csproj).
;
;   AppVersion  — release version, read from Liveolator.App.csproj <Version>
;   PublishDir  — absolute path to the self-contained publish output

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z (use scripts/build-installer.ps1)
#endif
#ifndef PublishDir
  #error Pass /DPublishDir=<publish folder> (use scripts/build-installer.ps1)
#endif

#define AppName "Liveolator"
#define AppPublisher "Zalman Im Records"
#define AppExeName "Liveolator.App.exe"

[Setup]
; Never change this AppId — it is how upgrades find the existing install.
AppId={{9C4F1B62-7D38-4A0E-B5C9-2E6F8A31D7B4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default (no admin prompt — a performance laptop is often locked
; down); the dialog still lets the user elevate for an all-users install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=LiveolatorSetup-{#AppVersion}
SetupIconFile={#SourcePath}\..\..\src\Liveolator.App\Liveolator.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The app must not be running while files are replaced (BASS + MIDI keep handles open).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Uninstall intentionally leaves %APPDATA%\Liveolator alone — it holds the user's music
; catalog, playlists, mappings, and logs, which must survive reinstalls and upgrades.
