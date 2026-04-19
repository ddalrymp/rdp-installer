; ============================================================
; RDP Launcher Installer - Inno Setup Script
; ============================================================
; Build with Inno Setup 6.x: https://jrsoftware.org/isinfo.php
;
; Before building:
; 1. Publish the .NET app: dotnet publish src/RdpLauncher -c Release
; 2. Place the published output in installer/publish/
; 3. Place your signing-cert.cer in installer/
; 4. (Optional) Place icon.ico in installer/assets/
; ============================================================

#define MyAppName "RDP Launcher"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Your Company Name"
#define MyAppExeName "RdpLauncher.exe"
#define MyAppId "RdpLauncher"

; TODO: Update this to your actual config URL before building
#define ConfigUrl "https://your-bucket.s3.amazonaws.com/rdp/config.json"

[Setup]
AppId={{8F2E4A6B-1C3D-4E5F-9A7B-0D8E6F2A4C1B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\{#MyAppId}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=RdpLauncherSetup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
SetupIconFile=assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Application files (from dotnet publish output)
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

; Signing certificate for RDP trust
Source: "signing-cert.cer"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Store the config URL so the launcher can find it
Root: HKCU; Subkey: "Software\{#MyAppId}"; ValueType: string; ValueName: "ConfigUrl"; ValueData: "{#ConfigUrl}"; Flags: uninsdeletekey

; Store the install path for reference
Root: HKCU; Subkey: "Software\{#MyAppId}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

; User code is written by the [Code] section after the input page

[Run]
; Import the signing certificate into TrustedPublisher after installation
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""try {{ $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('{app}\signing-cert.cer'); $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPublisher', 'CurrentUser'); $store.Open('ReadWrite'); $store.Add($cert); $store.Close(); Write-Host 'Certificate imported successfully.' }} catch {{ Write-Host 'Warning: Certificate import failed. You may see trust prompts.' }}"""; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Installing security certificate..."

; Optionally launch the app after install
Filename: "{app}\{#MyAppExeName}"; \
    Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the signing certificate from TrustedPublisher on uninstall
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""try {{ $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPublisher', 'CurrentUser'); $store.Open('ReadWrite'); $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('{app}\signing-cert.cer'); $found = $store.Certificates.Find('FindByThumbprint', $cert.Thumbprint, $false); foreach ($c in $found) {{ $store.Remove($c) }}; $store.Close() }} catch {{ }}"""; \
    Flags: runhidden waituntilterminated

[UninstallDelete]
; Clean up cached files
Type: filesandordirs; Name: "{localappdata}\{#MyAppId}\cache"

[Code]
var
  UserCodePage: TInputQueryWizardPage;

function GetUserCode(): String;
var
  CmdLineCode: String;
begin
  // Command-line param takes priority: /USERCODE=ORG1_U01
  CmdLineCode := ExpandConstant('{param:USERCODE|}');
  if CmdLineCode <> '' then
    Result := Trim(CmdLineCode)
  else if Assigned(UserCodePage) then
    Result := Trim(UserCodePage.Values[0])
  else
    Result := '';
end;

procedure InitializeWizard();
begin
  // Only show the input page if /USERCODE was NOT provided on the command line
  if ExpandConstant('{param:USERCODE|}') = '' then
  begin
    UserCodePage := CreateInputQueryPage(wpSelectDir,
      'User Code',
      'Enter the user code provided to you.',
      'This identifies your personal connection file (e.g., ORG1_U01).');
    UserCodePage.Add('User Code:', False);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if Assigned(UserCodePage) and (CurPageID = UserCodePage.ID) then
  begin
    if Trim(UserCodePage.Values[0]) = '' then
    begin
      MsgBox('Please enter your user code.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Write the user code to the registry
    RegWriteStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppId}',
      'UserCode', GetUserCode());
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
