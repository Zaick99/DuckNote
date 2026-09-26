; Installer di DuckNote. Si compila con ISCC.exe di Inno Setup 6.
;
;   ISCC.exe packaging\DuckNote.iss /DAppVersion=0.0.4 /DSourceExe=dist\DuckNote.exe
;
; L'eseguibile che imbarca e' gia' autosufficiente: dentro c'e' il runtime .NET,
; WPF e l'applicazione. L'installer non aggiunge prerequisiti, aggiunge il posto
; dove vivere, i collegamenti e la voce per disinstallare.

#ifndef AppVersion
  #define AppVersion "0.0.4"
#endif

#ifndef SourceExe
  #define SourceExe "..\dist\DuckNote.exe"
#endif

#define AppName "DuckNote"
#define AppPublisher "Zaick99"
#define AppUrl "https://github.com/Zaick99/DuckNote"

[Setup]
AppId={{8F3C7A21-5D4E-4B96-9C18-2E7A6B0D4F53}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Nella cartella dell'utente: cosi' non serve l'elevazione, e chi installa e'
; chi usera' l'applicazione.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\dist
OutputBaseFilename=DuckNote-{#AppVersion}-setup
SetupIconFile=..\assets\duck.ico
UninstallDisplayIcon={app}\DuckNote.exe

; L'eseguibile e' gia' compresso: comprimerlo ancora costa tempo e non rende.
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
LicenseFile=
MinVersion=10.0

[Languages]
Name: "italiano"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "DuckNote.exe"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\DuckNote.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\DuckNote.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DuckNote.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; La nota e il contenitore restano: li toglie l'applicazione stessa, da
; Impostazioni > Disinstallazione, che sovrascrive i byte prima di cancellarli.
; Un installer che li portasse via di nascosto cancellerebbe l'unica copia.
Type: dirifempty; Name: "{app}"
