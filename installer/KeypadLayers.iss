#ifndef AppVersion
  #define AppVersion "0.3.3"
#endif
#define AppName "Keypad Layers"
#define AppExeName "KeypadLayers.exe"

[Setup]
#ifdef IsolatedSmokeBuild
AppId={{7D86082B-2461-4B91-B9BC-B0AAB66C88AC}
#else
AppId={{A4F0B0C2-69C3-4B44-9D7A-97B8978249CF}
#endif
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Cesarsk
DefaultDirName={localappdata}\Programs\Keypad Layers
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UsePreviousAppDir=yes
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\Assets\app.ico
OutputDir=..\out\installer
OutputBaseFilename=KeypadLayersSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\out\win-x64\KeypadLayers.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent
