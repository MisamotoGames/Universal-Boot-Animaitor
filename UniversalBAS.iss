#define MyAppName "Universal Boot Animation System"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Universal BAS contributors"

[Setup]
AppId={{A390788F-8D57-424C-9A83-45BE3A2C28C2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Universal BAS
DefaultGroupName=Universal BAS
UninstallDisplayName=Universal Boot Animation System
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=.
OutputBaseFilename=Universal-BAS-Setup-Payload
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=classic
WizardImageFile=WizardImage.bmp
WizardImageBackColor=clBlack
DisableProgramGroupPage=yes
LicenseFile=LICENSE
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "publish\Universal BAS.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "BASX64.EFI"; DestDir: "{app}"; Flags: ignoreversion
Source: "SampleAnimation.bas"; DestDir: "{app}"; Flags: ignoreversion
Source: "Create-BAS-Volume.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "BOOT-SETUP.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Universal BAS"; Filename: "{app}\Universal BAS.exe"
Name: "{group}\Boot setup guide"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\BOOT-SETUP.md"""
Name: "{autodesktop}\Universal BAS"; Filename: "{app}\Universal BAS.exe"; Tasks: desktopicon

[Code]
var
  AnimationChoicePage: TInputOptionWizardPage;
  AnimationPage: TInputDirWizardPage;
  AnimationDrive: string;

procedure InitializeWizard;
begin
  { Keep the installer gauge tied to real install progress and use a continuous fill. }
  WizardForm.ProgressGauge.Style := npbstNormal;
  AnimationDrive := ExpandConstant('{param:ANIMATIONDRIVE|}');
  AnimationChoicePage := CreateInputOptionPage(wpSelectDir,
    'Animation setup', 'Choose whether to install the sample animation',
    'You can add or export an animation later from the BAS editor.', True, False);
  AnimationChoicePage.Add('Install the sample animation now');
  AnimationChoicePage.Add('Skip animation setup for now');
  if AnimationDrive <> '' then
    AnimationChoicePage.SelectedValueIndex := 0
  else
    AnimationChoicePage.SelectedValueIndex := 1;

  AnimationPage := CreateInputDirPage(AnimationChoicePage.ID,
    'Animation storage', 'Choose a FAT32 volume for your animation',
    'Select the root of a mounted FAT32 volume to install BAS\animation.bas. This volume must be available to the UEFI player during boot.',
    False, '');
  AnimationPage.Add('FAT32 volume root:');
  AnimationPage.Values[0] := '';
  if AnimationDrive <> '' then
    AnimationPage.Values[0] := AnimationDrive + ':\';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if AnimationDrive <> '' then
    Result := (PageID = AnimationChoicePage.ID) or (PageID = AnimationPage.ID)
  else
    Result := (PageID = AnimationPage.ID) and
      (AnimationChoicePage.SelectedValueIndex = 1);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  RootPath: string;
begin
  Result := True;
  if CurPageID = AnimationPage.ID then
  begin
    RootPath := RemoveBackslashUnlessRoot(ExpandConstant(AnimationPage.Values[0]));
    if not DirExists(RootPath) then
    begin
      MsgBox('Choose the root directory of a mounted volume.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if CompareText(AddBackslash(RootPath), AddBackslash(ExtractFileDrive(RootPath))) <> 0 then
    begin
      MsgBox('Select the root of the FAT32 volume, such as E:\, not a folder inside it.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

function FindFreeMountLetter: string;
var
  I: Integer;
  Candidate: string;
begin
  Result := '';
  for I := Ord('Z') downto Ord('D') do
  begin
    Candidate := Chr(I) + ':';
    if not DirExists(Candidate + '\') then
    begin
      Result := Candidate;
      Exit;
    end;
  end;
end;

function IsWindowsEfiVolume(const Letter: string): Boolean;
begin
  Result := FileExists(Letter + '\EFI\Microsoft\Boot\bootmgfw.efi') and
    FileExists(Letter + '\EFI\Boot\bootx64.efi');
end;

procedure InstallEFIPlayer;
var
  Letter, EfiDirectory, SourceFile, TargetFile: string;
  ResultCode: Integer;
  MountedByInstaller: Boolean;
begin
  MountedByInstaller := False;
  if IsWindowsEfiVolume('Z:') then
    Letter := 'Z:'
  else
  begin
    Letter := FindFreeMountLetter;
    if Letter = '' then
      RaiseException('No free drive letter is available to mount the EFI System Partition. No firmware order was changed.');
    if not Exec(ExpandConstant('{sys}\mountvol.exe'), Letter + ' /S', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      RaiseException('Windows could not temporarily mount the EFI System Partition. The editor is installed, but the EFI player was not copied. See BOOT-SETUP.md.');
    MountedByInstaller := True;
  end;
  try
    EfiDirectory := Letter + '\EFI\BAS';
    if not DirExists(Letter + '\EFI') then
      RaiseException('The mounted system volume does not contain an EFI folder. The EFI player was not installed.');
    if not ForceDirectories(EfiDirectory) then
      RaiseException('Could not create EFI\BAS on the EFI System Partition.');
    SourceFile := ExpandConstant('{app}\BASX64.EFI');
    TargetFile := EfiDirectory + '\BASX64.EFI';
    if not CopyFile(SourceFile, TargetFile, True) then
      RaiseException('Could not copy BASX64.EFI to the EFI System Partition.');
  finally
    if MountedByInstaller then
      Exec(ExpandConstant('{sys}\mountvol.exe'), Letter + ' /D', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure InstallAnimationPackage;
var
  TargetDirectory, TargetFile, SourceFile: string;
begin
  if (AnimationDrive = '') and (AnimationChoicePage.SelectedValueIndex <> 0) then
    Exit;
  if AnimationDrive <> '' then
    TargetDirectory := AnimationDrive + ':\BAS'
  else
    TargetDirectory := AddBackslash(RemoveBackslashUnlessRoot(ExpandConstant(AnimationPage.Values[0]))) + 'BAS';
  if not ForceDirectories(TargetDirectory) then
    RaiseException('Could not create the BAS folder on the selected animation volume.');
  TargetFile := TargetDirectory + '\animation.bas';
  if not FileExists(TargetFile) then
  begin
    SourceFile := ExpandConstant('{app}\SampleAnimation.bas');
    if not CopyFile(SourceFile, TargetFile, False) then
      RaiseException('Could not copy the sample animation to the selected volume.');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    InstallEFIPlayer;
    InstallAnimationPackage;
  end;
end;
