#ifndef PayloadDir
  #error PayloadDir must point to the audited installer payload
#endif
#ifndef AppVersion
  #error AppVersion must be supplied from AsterVPN.csproj
#endif

[Setup]
AppId={{8F11916C-C49E-46C9-A627-1DEB0F92DE18}
AppName=Aster VPN
AppVersion={#AppVersion}
AppPublisher=Aster VPN
DefaultDirName={autopf}\Aster VPN
DefaultGroupName=Aster VPN
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex=Local\AsterVPN.Desktop
CloseApplications=no
RestartApplications=no
SetupIconFile=..\src\Assets\aster.ico
UninstallDisplayIcon={app}\AsterVPN.exe
OutputDir=..\dist\installer
OutputBaseFilename=AsterVPN-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
Uninstallable=yes
UninstallLogMode=append

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
#include "payload-files.iss"

[Icons]
Name: "{group}\Aster VPN"; Filename: "{app}\AsterVPN.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Aster VPN"; Filename: "{app}\AsterVPN.exe"; WorkingDir: "{app}"; Tasks: desktopicon

; Do not auto-run an elevated VPN client from setup. User starts it from the shortcut.
; No AppData entries, user settings, subscriptions, logs, or user-data deletion rules.
