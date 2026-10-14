; Instalador de Cuentitas (Inno Setup 6). Ver docs/DESARROLLO.md § Instalador.
; Se compila con tools\publish.ps1 (después de publicar dist\Cuentitas).
; - Instalación por usuario: no pide permisos de administrador.
; - Instalar una versión nueva encima actualiza la app y CONSERVA los datos (%LOCALAPPDATA%\Cuentitas).
; - Desinstalar quita la tarea de avisos, el protocolo "cuentitas:" y, si el usuario quiere, sus datos.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Cuentitas"
#define AppExe "Cuentitas.exe"
#define AppUserModelId "Cuentitas.App"

[Setup]
AppId={{6F1C2B9E-4A7D-4E2B-9C1A-5B8D3E7F2A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Matias Speratti
AppPublisherURL=https://github.com/sperattidev/cuentitas
AppCopyright=Copyright (C) 2026 Matias Speratti
AppComments=Finanzas personales para Argentina
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Cuentitas-Setup-{#AppVersion}
SetupIconFile=..\src\Cuentitas.App\Assets\cuentitas.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "..\dist\Cuentitas\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"; Comment: "Finanzas personales"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"; Tasks: desktopicon

[Registry]
; Nombre e ícono de las notificaciones (la app también lo registra al abrir).
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelId}"; ValueType: string; ValueName: "DisplayName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelId}"; ValueType: string; ValueName: "IconUri"; ValueData: "{app}\Assets\AppIcon.png"
; Protocolo "cuentitas:" (clic en una notificación → abre Vencimientos).
Root: HKCU; Subkey: "Software\Classes\cuentitas"; ValueType: string; ValueName: ""; ValueData: "URL:Cuentitas"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\cuentitas"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\cuentitas\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir Cuentitas"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Quita la tarea programada de avisos (si existía). Si no existe, schtasks falla en silencio.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""Cuentitas\Recordatorios"" /F"; Flags: runhidden; RunOnceId: "QuitarAvisos"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\Cuentitas');
    if DirExists(DataDir) and not UninstallSilent then
    begin
      if MsgBox('¿Querés borrar también tus datos de Cuentitas (cuentas, movimientos, metas)?' + #13#10 + #13#10 +
                'Si pensás volver a instalarla, elegí No y tus datos van a seguir ahí.' + #13#10 +
                'Las copias de seguridad de Documentos\Cuentitas no se borran.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
