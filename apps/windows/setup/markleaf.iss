; MarkLeaf Inno Setup installer.
; Build with:
;   ISCC /DMyAppVersion=1.4.1 /DBuildNumber=326 /DAppArchitecture=x64 /DAppArchitectureAllowed=x64compatible /DSelfContained=0 /DSourceDir=... markleaf.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef BuildNumber
  #define BuildNumber "0"
#endif
#ifndef AppArchitecture
  #define AppArchitecture "x64"
#endif
#ifndef AppArchitectureAllowed
  #define AppArchitectureAllowed "x64compatible"
#endif
#ifndef SelfContained
  #define SelfContained "1"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppDisplayName "MarkLeaf"
#define AppPublisher "MarkLeaf"
#define AppExeName "MarkLeaf.exe"
#define AppArchitectureLabel "win-" + AppArchitecture
; Windows file versions must be numeric, so strip a prerelease suffix such as -beta.1.
#define AppVersionNumber Pos("-", MyAppVersion) > 0 ? Copy(MyAppVersion, 1, Pos("-", MyAppVersion) - 1) : MyAppVersion
#if SelfContained == "1"
  #define RuntimeSuffix "-with-runtime"
#else
  #define RuntimeSuffix ""
#endif

[Setup]
AppId={{D2C5E4B7-9E4B-4D2A-9DA8-6CFBEF7E1A34}
AppName={#AppDisplayName}
AppVersion={#MyAppVersion}
AppVerName={#AppDisplayName} {#MyAppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/zhuanshunjishi2017/markleaf
AppSupportURL=https://github.com/zhuanshunjishi2017/markleaf/issues
DefaultDirName={autopf}\MarkLeaf
DefaultGroupName=MarkLeaf
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed={#AppArchitectureAllowed}
ArchitecturesInstallIn64BitMode={#AppArchitectureAllowed}
OutputDir=.
OutputBaseFilename=MarkLeaf-{#MyAppVersion}-{#AppArchitectureLabel}{#RuntimeSuffix}
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=License.rtf
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
AppCopyright=Copyright (c) MarkLeaf
VersionInfoVersion={#AppVersionNumber}
VersionInfoDescription=MarkLeaf {#MyAppVersion} (Build {#BuildNumber})
CloseApplications=yes
RestartApplications=no

[Languages]
; Keep the simplified Chinese language file beside this script because it is
; not included in every Inno Setup installation or GitHub runner image.
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.AssociateMarkdownFiles=关联 .md 文件
chinesesimplified.AssociateTextFiles=关联 .txt 文件
chinesesimplified.CreateDesktopShortcut=创建桌面快捷方式
chinesesimplified.AddToStartMenu=添加到开始菜单
english.AssociateMarkdownFiles=Associate .md files
english.AssociateTextFiles=Associate .txt files
english.CreateDesktopShortcut=Create a desktop shortcut
english.AddToStartMenu=Add to the Start menu

[Tasks]
Name: "associate_md"; Description: "{cm:AssociateMarkdownFiles}"
Name: "associate_txt"; Description: "{cm:AssociateTextFiles}"
Name: "desktopicon"; Description: "{cm:CreateDesktopShortcut}"; Flags: unchecked
Name: "startmenu"; Description: "{cm:AddToStartMenu}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\MarkLeaf"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: startmenu
Name: "{autodesktop}\MarkLeaf"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Register only in HKCU because this is a per-user installer.
Root: HKCU; Subkey: "Software\Classes\MarkLeaf.MarkdownDoc"; ValueType: string; ValueName: ""; ValueData: "MarkLeaf Markdown Document"; Flags: uninsdeletekey; Tasks: associate_md or associate_txt
Root: HKCU; Subkey: "Software\Classes\MarkLeaf.MarkdownDoc\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Resources\App\fileicon.ico"; Tasks: associate_md or associate_txt
Root: HKCU; Subkey: "Software\Classes\MarkLeaf.MarkdownDoc\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" --open-document ""%1"""; Tasks: associate_md or associate_txt
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MarkLeaf.MarkdownDoc"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate_md
Root: HKCU; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "MarkLeaf.MarkdownDoc"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate_md
Root: HKCU; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "MarkLeaf.MarkdownDoc"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate_txt

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppDisplayName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

; Inno Setup's Restart Manager closes running instances that lock files under
; the installation directory. Do not launch MarkLeaf with a synthetic quit
; argument here: older versions do not implement it and would block setup.

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssInstall then
    Exit;

  if not WizardIsTaskSelected('associate_md') then
  begin
    RegDeleteValue(HKCU, 'Software\Classes\.md\OpenWithProgids', 'MarkLeaf.MarkdownDoc');
    RegDeleteValue(HKCU, 'Software\Classes\.markdown\OpenWithProgids', 'MarkLeaf.MarkdownDoc');
  end;

  if not WizardIsTaskSelected('associate_txt') then
    RegDeleteValue(HKCU, 'Software\Classes\.txt\OpenWithProgids', 'MarkLeaf.MarkdownDoc');

  if not WizardIsTaskSelected('startmenu') then
    DeleteFile(ExpandConstant('{autoprograms}\MarkLeaf.lnk'));

  if not WizardIsTaskSelected('desktopicon') then
    DeleteFile(ExpandConstant('{autodesktop}\MarkLeaf.lnk'));
end;
