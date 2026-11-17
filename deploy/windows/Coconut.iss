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

[Icons]
Name: "{group}\Coconut"; Filename: "{app}\Coconut.exe"; WorkingDir: "{app}"
Name: "{group}\Leia-me"; Filename: "{app}\LEIA-ME.txt"
Name: "{autodesktop}\Coconut"; Filename: "{app}\Coconut.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Coconut.exe"; Description: "Abrir Coconut"; Flags: nowait postinstall skipifsilent

; Não remover %LOCALAPPDATA%\Coconut: configurações e logs pertencem ao usuário.
