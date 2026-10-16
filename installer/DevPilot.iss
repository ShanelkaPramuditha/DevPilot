; DevPilot installer (Inno Setup 6.3 or later).
;
; Built by .github/workflows/release.yml for each architecture:
;   ISCC.exe /DAppVersion=1.2.0 /DNumericVersion=1.2.0 /DArch=x64 ^
;            /DSourceDir=artifacts\publish\x64 /DOutputDir=artifacts installer\DevPilot.iss
;
; The defaults below let you compile it locally after:
;   dotnet publish DevPilot.csproj -c Release -r win-x64 --self-contained -o artifacts\publish\x64

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "DevPilot"
#define AppExe "DevPilot.exe"
#define AppPublisher "Shanelka Pramuditha"

[Setup]
; Never change AppId: Windows uses it to recognise upgrades of the same app.
AppId={{47391CDA-BB77-4575-9FB8-506AA03AACCE}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoVersion={#NumericVersion}.0
VersionInfoProductVersion={#NumericVersion}.0
VersionInfoDescription={#AppName} {#AppVersion} setup ({#Arch})

; Install for the current user without admin rights; the user can choose all users instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
; x64 also installs on ARM64 Windows 11 (runs under emulation); the arm64 build is faster there.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

; WSL 2 needs Windows 10 version 2004 (build 19041) or later.
MinVersion=10.0.19041

OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\Assets\DevPilot.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; Close a running DevPilot during upgrades and uninstall, and restart it afterwards.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; In-app updates run the setup silently with /LAUNCH: start DevPilot again afterwards,
; as the signed-in user even when the update needed admin rights.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: LaunchAfterUpdate

; User preferences in %LocalAppData%\DevPilot are kept on uninstall, like most apps.

[Code]
// True when DevPilot started this setup to update itself (see Services/Updates/UpdateService.cs).
function LaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and (Pos('/LAUNCH', Uppercase(GetCmdTail)) > 0);
end;
