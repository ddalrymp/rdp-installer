; ============================================================
; RDP Launcher Installer - Inno Setup Script
; ============================================================
; Build with Inno Setup 6.x: https://jrsoftware.org/isinfo.php
;
; Before building:
; 1. Publish the .NET app: dotnet publish src/RdpLauncher -c Release
; 2. Place the published output in installer/publish/
; 3. Place FreeRDP files in installer/freerdp/ (wfreerdp.exe + DLLs)
; 4. Place your signing-cert.cer in installer/
; 5. (Optional) Place icon.ico in installer/assets/
; ============================================================

#define MyAppName "RDP Launcher"
#define MyAppVersion "1.1.0"
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

; FreeRDP binaries (bundled, pinned version)
Source: "freerdp\*"; DestDir: "{app}\freerdp"; Flags: ignoreversion recursesubdirs

; Signing certificate for RDP trust (mstsc fallback)
Source: "signing-cert.cer"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Store the config URL so the launcher can find it
Root: HKCU; Subkey: "Software\{#MyAppId}"; ValueType: string; ValueName: "ConfigUrl"; ValueData: "{#ConfigUrl}"; Flags: uninsdeletekey

; Store the install path for reference
Root: HKCU; Subkey: "Software\{#MyAppId}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

; OrgId and UserId are written by the [Code] section after the input page

[Run]
; Import the signing certificate into TrustedPublisher after installation (for mstsc fallback)
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""try {{ $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('{app}\signing-cert.cer'); $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPublisher', 'CurrentUser'); $store.Open('ReadWrite'); $store.Add($cert); $store.Close(); Write-Host 'Certificate imported successfully.' }} catch {{ Write-Host 'Warning: Certificate import failed.' }}"""; \
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
  IdentityPage: TInputQueryWizardPage;

function GetOrgId(): String;
var
  CmdLineVal: String;
begin
  CmdLineVal := ExpandConstant('{param:ORGID|}');
  if CmdLineVal <> '' then
    Result := Trim(CmdLineVal)
  else if Assigned(IdentityPage) then
    Result := Trim(IdentityPage.Values[0])
  else
    Result := '';
end;

function GetUserId(): String;
var
  CmdLineVal: String;
begin
  CmdLineVal := ExpandConstant('{param:USERID|}');
  if CmdLineVal <> '' then
    Result := Trim(CmdLineVal)
  else if Assigned(IdentityPage) then
    Result := Trim(IdentityPage.Values[1])
  else
    Result := '';
end;

procedure InitializeWizard();
begin
  // Only show the input page if neither /ORGID nor /USERID were provided
  if (ExpandConstant('{param:ORGID|}') = '') or (ExpandConstant('{param:USERID|}') = '') then
  begin
    IdentityPage := CreateInputQueryPage(wpSelectDir,
      'Organization & User',
      'Enter your organization and user identifiers.',
      'These identify your connection settings (e.g., ORG1 and U01).');
    IdentityPage.Add('Organization ID:', False);
    IdentityPage.Add('User ID:', False);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if Assigned(IdentityPage) and (CurPageID = IdentityPage.ID) then
  begin
    if (Trim(IdentityPage.Values[0]) = '') or (Trim(IdentityPage.Values[1]) = '') then
    begin
      MsgBox('Please enter both Organization ID and User ID.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // Write identity to the registry
    RegWriteStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppId}',
      'OrgId', GetOrgId());
    RegWriteStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppId}',
      'UserId', GetUserId());

    // Suppress the one-time RDP educational warning dialog (for mstsc fallback)
    RegWriteDWordValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Terminal Server Client',
      'RdpLaunchConsentAccepted', 1);

    // Revert the KB5083769 RDP security/redirection dialog (for mstsc fallback)
    if not ShellExec('runas', 'reg.exe',
      'add "HKLM\Software\Policies\Microsoft\Windows NT\Terminal Services\Client" /v RedirectionWarningDialogVersion /t REG_DWORD /d 1 /f',
      '', SW_HIDE, ewWaitUntilTerminated, ErrorCode) then
    begin
      Log('Warning: Could not set RDP redirection dialog policy. Error code: ' + IntToStr(ErrorCode));
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ErrorCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove the RDP educational consent flag
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Terminal Server Client',
      'RdpLaunchConsentAccepted');

    // Remove the RDP redirection dialog policy
    ShellExec('runas', 'reg.exe',
      'delete "HKLM\Software\Policies\Microsoft\Windows NT\Terminal Services\Client" /v RedirectionWarningDialogVersion /f',
      '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);

    // Clean up credential registry entries
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\{#MyAppId}');
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
