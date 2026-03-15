; ============================================================
;  JanotAi — Inno Setup Script
;  Creates a setup.exe installer (no admin rights required)
;  Stores the Mistral API key in the user environment
; ============================================================

#define AppName    "JanotAi"
#define AppVersion "1.0.0"
#define AppExe     "janotai.exe"
#define AppURL     "https://github.com/YOUR_USERNAME/janotai"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=JanotAi
AppPublisherURL={#AppURL}
DefaultDirName={localappdata}\Programs\JanotAi
DefaultGroupName={#AppName}
OutputDir={#SourcePath}\Output
OutputBaseFilename=JanotAi-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile={#SourcePath}\janot.ico
WizardStyle=modern
WizardSizePercent=110

; Add install folder to user PATH
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Main binaries
Source: "dist\janotai.exe";         DestDir: "{app}"; Flags: ignoreversion
Source: "dist\ShellMcpServer.exe";  DestDir: "{app}"; Flags: ignoreversion

; Config (don't overwrite if already exists — preserve user config)
Source: "dist\appsettings.json";    DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
; Desktop shortcut
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Launch JanotAi"
; Start menu
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}";    Filename: "{uninstallexe}"

[Registry]
; Add install folder to user PATH
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
  ValueData: "{olddata};{app}"; \
  Check: NeedsAddPath(ExpandConstant('{app}'))

[Code]
// ── Check if path is already in PATH ─────────────────────────
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

[Messages]
WelcomeLabel2=This wizard will install [name/ver] on your computer.%n%nJanotAi is an AI agent that can control your PC, execute commands, manage files, and answer questions about your documents.%n%nClose all applications before continuing.
