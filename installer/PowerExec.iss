; Power-Exec – Installer (Inno Setup 6)
;
; Installiert pro Benutzer ohne Adminrechte nach %LOCALAPPDATA%\Programs\Power-Exec.
; Dort hat Power-Exec Schreibrechte, damit spätere Updates ohne UAC-Abfrage funktionieren.
;
; Bauen (wird von .github/workflows/release.yml erledigt):
;   ISCC.exe /DMyAppVersion=1.2.3 /DMyFileVersion=1.2.3 /DSourceDir=..\out\app installer\PowerExec.iss
;
; SourceDir ist eine Ordner-Veröffentlichung (self-contained, ReadyToRun): Power-Exec startet daraus
; ohne Entpacken in einen Temp-Ordner – schneller und ohne Ärger mit Virenscannern.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
; Rein numerische Version für die Dateieigenschaften (ohne "-beta.1" usw.)
#ifndef MyFileVersion
  #define MyFileVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\app"
#endif

#define MyAppName "Power-Exec"
#define MyAppExeName "PowerExec.exe"
#define MyAppPublisher "Ga-Re"
#define MyAppURL "https://github.com/Ga-Re/Power-Exec"
; Muss zum Mutex in App.xaml.cs passen, damit Setup eine laufende Instanz erkennt.
; "{{" ist in Inno Setup ein einzelnes "{".
#define MyAppMutex "PowerExec.SingleInstance.{{6C1D3E4B-8B0F-4C1A-9E2D-3A5B7C9D1E2F}"

[Setup]
AppId={{8F2B6C1E-4A7D-4E1B-9C3F-5D6A7B8C9E0F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyFileVersion}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Fester Ort pro Benutzer (Schreibrechte für Updates, sicheres Aufräumen alter Dateien)
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex={#MyAppMutex}
CloseApplications=yes
RestartApplications=no
OutputDir=..\out
OutputBaseFilename=PowerExec-Setup
SetupIconFile=..\src\PowerExec\Assets\PowerExec.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
; Hell oder dunkel wie Windows (Inno Setup 6.6 oder neuer)
WizardStyle=modern dynamic
ShowLanguageDialog=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Verknüpfung auf dem &Desktop erstellen"; GroupDescription: "Zusätzliche Verknüpfungen:"; Flags: unchecked
Name: "autostart"; Description: "Power-Exec mit &Windows starten"; GroupDescription: "Start:"

[InstallDelete]
; Reste älterer Versionen (andere Bibliotheksstände) entfernen. Nur Programmdateien – Einstellungen liegen in %APPDATA%.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\*.json"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Gleicher Eintrag, den Power-Exec selbst für "Mit Windows starten" verwendet.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Power-Exec"; \
    ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} jetzt starten"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Autostart abgewählt (z. B. bei einem erneuten Setup): vorhandenen Eintrag entfernen.
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('autostart') and not WizardSilent then
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'Power-Exec');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Autostart-Eintrag auch entfernen, wenn ihn Power-Exec selbst angelegt hat.
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'Power-Exec');

    SettingsDir := ExpandConstant('{userappdata}\Power-Exec');
    if DirExists(SettingsDir) and not UninstallSilent then
      if MsgBox('Sollen auch die Einstellungen (Ordner, Favoriten, Rechnerliste) gelöscht werden?' + #13#10#13#10 + SettingsDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(SettingsDir, True, True, True);
        DelTree(ExpandConstant('{localappdata}\Power-Exec'), True, True, True);
      end;
  end;
end;
