; O payload é produzido por publish-app.ps1: .NET, WinUI, VC++ e nativos locais.
#ifndef AppVersion
  #error AppVersion obrigatorio
#endif
#ifndef PayloadDir
  #error PayloadDir obrigatorio
#endif
#ifndef OutputDir
  #error OutputDir obrigatorio
#endif
#ifndef PackageName
  #define PackageName "Coconut-" + AppVersion + "-win-x64"
#endif

[Setup]
AppId={{F5D7B2A9-BF31-4A1F-9229-92A2B27D0E66}
AppName=Coconut
AppVersion={#AppVersion}
AppPublisher=Coconut contributors
AppPublisherURL=https://github.com/Tarcitani/coconut-call
AppSupportURL=https://github.com/Tarcitani/coconut-call/issues
AppUpdatesURL=https://github.com/Tarcitani/coconut-call/releases
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\Coconut
DefaultGroupName=Coconut
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename={#PackageName}-Setup
SetupIconFile=..\..\src\Coconut.App\Assets\Coconut.ico
UninstallDisplayIcon={app}\Coconut.exe
LicenseFile=..\..\LICENSE
InfoAfterFile=LEIA-ME.txt
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=Coconut.exe
RestartApplications=no
Uninstallable=yes
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[InstallDelete]
; Evita carregar um servidor antigo ao trocar um pacote particular pelo público.
Type: files; Name: "{app}\coconut.defaults.json"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Classes\coconut"; ValueType: string; ValueName: ""; ValueData: "URL:Coconut invitation"
Root: HKCU; Subkey: "Software\Classes\coconut"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\coconut\DefaultIcon"; ValueType: string; ValueData: """{app}\Coconut.exe"",0"
Root: HKCU; Subkey: "Software\Classes\coconut\shell\open\command"; ValueType: string; ValueData: """{app}\Coconut.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CoconutCall"; ValueData: """{app}\Coconut.exe"" --startup"; Check: StartupEnabled

[Icons]
Name: "{group}\Coconut"; Filename: "{app}\Coconut.exe"; WorkingDir: "{app}"
Name: "{group}\Leia-me"; Filename: "{app}\LEIA-ME.txt"
Name: "{autodesktop}\Coconut"; Filename: "{app}\Coconut.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Coconut.exe"; Description: "Abrir Coconut"; Flags: nowait postinstall skipifsilent; Check: NotUpdateInstall
Filename: "{app}\Coconut.exe"; Flags: nowait; Check: IsUpdateInstall

; Não remover %LOCALAPPDATA%\Coconut: configurações e logs pertencem ao usuário.

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingApp: String;
  VersionMS, VersionLS: Cardinal;
  ExitCode: Integer;
begin
  Result := '';
  ExistingApp := ExpandConstant('{app}\Coconut.exe');
  if GetVersionNumbers(ExistingApp, VersionMS, VersionLS) then
    if VersionMS >= 3 then
    begin
      Exec(ExistingApp, '--exit-if-idle', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
      Sleep(500);
    end;
end;

function StartupEnabled: Boolean;
begin
  Result := RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CoconutCall');
end;

function IsUpdateInstall: Boolean;
begin
  Result := ExpandConstant('{param:COCONUTUPDATE|0}') = '1';
end;

function NotUpdateInstall: Boolean;
begin
  Result := not IsUpdateInstall;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Classes\coconut\shell\open\command', '', Command) then
      if CompareText(Command, ExpandConstant('"{app}\Coconut.exe" "%1"')) = 0 then
        RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\coconut');
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CoconutCall', Command) then
      if CompareText(Command, ExpandConstant('"{app}\Coconut.exe" --startup')) = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CoconutCall');
  end;
end;
