; Inno Setup script for the SQL Server Backup suite.
; Build the publish folders first:  pwsh scripts/publish.ps1
; Then compile with Inno Setup 6:   ISCC.exe installer\SqlBackup.iss

#define MyAppName "SQL Server Backup Suite"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SqlBackup"
#define ServiceName "SqlBackupService"
#define ServiceDisplayName "SQL Server Backup Service"

[Setup]
AppId={{7D9C31D2-30F4-4CF0-9C0A-51B1BB2FE001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SqlBackup
DefaultGroupName=SQL Server Backup
OutputDir=Output
OutputBaseFilename=SqlBackupSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\App\SqlBackup.App.exe
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "..\publish\Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs
Source: "..\publish\App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs

[Dirs]
; Shared data folder (config.json, history, logs). Users get modify rights so the
; control panel can save configuration without elevation; secrets inside are
; DPAPI-protected. Tighten this ACL if only administrators should edit jobs.
Name: "{commonappdata}\SqlBackup"; Permissions: users-modify

[Icons]
Name: "{group}\SQL Server Backup Control Panel"; Filename: "{app}\App\SqlBackup.App.exe"
Name: "{group}\Backup data folder"; Filename: "{commonappdata}\SqlBackup"
Name: "{autodesktop}\SQL Server Backup"; Filename: "{app}\App\SqlBackup.App.exe"; Tasks: desktopicon

[Run]
; (Re)register the Windows service. stop/delete tolerate a fresh install.
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; StatusMsg: "Stopping existing service..."
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\Service\SqlBackup.Service.exe\"""" start= auto DisplayName= ""{#ServiceDisplayName}"""; Flags: runhidden; StatusMsg: "Registering the backup service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Runs scheduled Microsoft SQL Server backups configured with the SQL Server Backup control panel."""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/300000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; StatusMsg: "Starting the backup service..."
Filename: "{app}\App\SqlBackup.App.exe"; Description: "Launch the Control Panel"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

; Note: {commonappdata}\SqlBackup (configuration, job history, logs) is kept on
; uninstall on purpose — backup configuration should survive reinstalls.
