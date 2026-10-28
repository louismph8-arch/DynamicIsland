; Installeur d'Opaline (Inno Setup 6) → artifacts\Opaline-Setup.exe
; À construire avec installer\build.ps1 (publication Release autonome puis compilation de ce script).
;
; Installation pour l'utilisateur seul (pas de droits administrateur) : les mises à jour automatiques
; relancent cet installeur en « /SILENT » sans demande UAC, puis il relance l'app.
; Nom affiché « Opaline » (5H) ; l'exe, l'AppId, la valeur de démarrage et les dossiers de réglages gardent
; « DynamicIsland » : une installation d'avant le nouveau nom est mise à jour sur place, dans son dossier.

#define AppName "Opaline"
#define AppExe "DynamicIsland.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{8F3C5A62-4B7E-4D0A-9E51-2C6D7B1A9F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Louis
AppPublisherURL=https://github.com/louismph8-arch/DynamicIsland
AppUpdatesURL=https://github.com/louismph8-arch/DynamicIsland/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=Opaline-Setup
SetupIconFile=..\src\DynamicIsland\Assets\app.ico
; Images aux couleurs d'Opaline (générées par « DynamicIsland.exe --snapshot <dossier> --render-logo »), 100 à 200 %
WizardImageFile=images\opaline-grande-*.bmp
WizardSmallImageFile=images\opaline-petite-*.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; L'app est d'abord fermée proprement par « --quit » (voir [Code]) ; le gestionnaire de redémarrage de
; Windows ne sert qu'en secours.
CloseApplications=yes
RestartApplications=no

; Langue de l'installeur = celle de Windows (français, sinon anglais), sans fenêtre de choix.
ShowLanguageDialog=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.StartupTask=Lancer Opaline au démarrage de Windows
english.StartupTask=Start Opaline with Windows
french.OptionsGroup=Options :
english.OptionsGroup=Options:
french.ThemeType=Thème {#AppName}
english.ThemeType={#AppName} theme
french.DeleteSettings=Effacer aussi tes réglages Opaline (profils, thèmes importés, fonds dessinés) ?
english.DeleteSettings=Also delete your Opaline settings (profiles, imported themes, drawn wallpapers)?
french.DeleteSettingsDetail=Windows est déjà remis comme avant. Choisis « Non » pour les retrouver si tu réinstalles Opaline.
english.DeleteSettingsDetail=Windows is already back to how it was. Choose “No” to keep them if you reinstall Opaline.
french.FirewallRemove=Opaline va retirer la règle du pare-feu de sa télécommande. Windows va demander l'autorisation administrateur.
english.FirewallRemove=Opaline will remove its remote control firewall rule. Windows will ask for administrator permission.

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:OptionsGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Raccourcis d'avant le nouveau nom (« Dynamic Island ») : remplacés par ceux d'Opaline.
Type: files; Name: "{autoprograms}\Dynamic Island.lnk"
Type: files; Name: "{autodesktop}\Dynamic Island.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Démarrage avec Windows : même valeur que l'option de l'app (Services/StartupService).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DynamicIsland"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue
; Double-clic sur un thème .opaline (ou un ancien .bloomtheme) ; l'app réécrit aussi ces associations à chaque démarrage.
Root: HKCU; Subkey: "Software\Classes\.opaline"; ValueType: string; ValueName: ""; ValueData: "DynamicIsland.Theme"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.bloomtheme"; ValueType: string; ValueName: ""; ValueData: "DynamicIsland.Theme"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\DynamicIsland.Theme"; ValueType: string; ValueName: ""; ValueData: "{cm:ThemeType}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DynamicIsland.Theme\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\DynamicIsland.Theme\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --import ""%1"""

[Run]
; Aussi en mode silencieux : une mise à jour automatique relance l'app. « /NOLAUNCH » l'empêche (tests).
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall; Check: not NoLaunch

[UninstallRun]
; Ferme l'app proprement et remet la barre des tâches avant de supprimer les fichiers.
Filename: "{app}\{#AppExe}"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitApp"
; Puis remet Windows comme avant l'app : fond d'écran (s'il vient d'Opaline), couleur d'accent, thème clair/sombre, curseur.
Filename: "{app}\{#AppExe}"; Parameters: "--restore-windows"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreWindows"

[Code]
function NoLaunch: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/NOLAUNCH') = 0 then
      Result := True;
end;

{ Mise à jour : l'app qui tourne se ferme proprement (barre des tâches remise) avant le remplacement des fichiers. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Télécommande (6D) : si la règle « Opaline Remote » du pare-feu est encore là (lire ne demande pas de droits), on
  prévient puis on la retire avec la fenêtre administrateur de Windows. }
procedure RemoveFirewallRule;
var
  ResultCode: Integer;
  Netsh: String;
begin
  Netsh := ExpandConstant('{sys}\netsh.exe');
  if not Exec(Netsh, 'advfirewall firewall show rule name="Opaline Remote"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
     or (ResultCode <> 0) then
    Exit;
  MsgBox(CustomMessage('FirewallRemove'), mbInformation, MB_OK);
  ShellExec('runas', Netsh, 'advfirewall firewall delete rule name="Opaline Remote"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Désinstallation : retire aussi le lancement au démarrage activé depuis l'app (s'il vise bien cette installation) et la
  règle du pare-feu de la télécommande, puis propose (désinstallation normale seulement, « Non » par défaut) d'effacer
  les réglages, profils et fonds. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DynamicIsland', Command)
     and (Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Command)) > 0) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DynamicIsland');
  if not UninstallSilent then
    RemoveFirewallRule;
  if (not UninstallSilent) and DirExists(ExpandConstant('{userappdata}\DynamicIsland'))
     and (MsgBox(CustomMessage('DeleteSettings') + #13#10 + #13#10 + CustomMessage('DeleteSettingsDetail'),
                 mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
  begin
    DelTree(ExpandConstant('{userappdata}\DynamicIsland'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\DynamicIsland'), True, True, True);
  end;
end;
