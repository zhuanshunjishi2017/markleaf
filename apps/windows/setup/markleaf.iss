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
SetupIconFile=..\MarkLeaf\Resources\App\App.ico
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

[Languages]
; Keep the simplified Chinese language file beside this script because it is
; not included in every Inno Setup installation or GitHub runner image.
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\MarkLeaf"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"

[Registry]
; Register only in HKCU because this is a per-user installer.
Root: HKCU; Subkey: "Software\Classes\MarkLeaf.MarkdownDoc"; ValueType: string; ValueName: ""; ValueData: "MarkLeaf Markdown Document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\MarkLeaf.MarkdownDoc\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Resources\App\fileicon.ico"
Root: HKCU; Subkey: "Software\Classes\MarkLeaf.MarkdownDoc\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" --open-document ""%1"""
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MarkLeaf.MarkdownDoc"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "MarkLeaf.MarkdownDoc"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "MarkLeaf.MarkdownDoc"; ValueData: ""; Flags: uninsdeletevalue

; Deliberately no [Run] section: finishing installation must not launch MarkLeaf.

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\{#AppExeName}')) then
  begin
    Exec(ExpandConstant('{app}\{#AppExeName}'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
  Result := '';
end;
