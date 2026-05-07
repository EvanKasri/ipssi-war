; ====================================================================
;  IPSSI-WAR - Script Inno Setup
;  Genere un installateur Windows pour le jeu Tank LAN
; ====================================================================

#define MyAppName       "IPSSI-WAR"
#define MyAppVersion    "1.0"
#define MyAppPublisher  "IPSSI 2026 - Evan / Wael / Mathis"
#define MyAppExeName    "Tank Game.exe"

; Dossier qui contient le build Unity (a adapter si tu changes l'emplacement)
#define MyBuildDir      "C:\Users\yeetm\Desktop\3D-Tanks-Game-Unity-master\jeux-release"

[Setup]
AppId={{B12F7A30-2E94-4C0B-9A7E-IPSSIWARGAME01}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=C:\Users\yeetm\Desktop\3D-Tanks-Game-Unity-master\Installer
OutputBaseFilename=IPSSI-WAR-Setup-v{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
WizardStyle=modern
DisableDirPage=auto
DisableProgramGroupPage=auto
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Creer un raccourci sur le bureau"; GroupDescription: "Raccourcis :"; Flags: checkedonce

[Files]
; Tout le contenu du dossier de build, recursif
Source: "{#MyBuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";        Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent
