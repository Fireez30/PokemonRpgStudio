; Inno Setup script for Pokémon RPG Studio (used by scripts\build.ps1 when Inno Setup 6 is installed).
; Values are passed on the command line: /DAppVersion=1.0.0 /DSourceDir=... /DOutputDir=... /DArch=x64

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{6F0B8A0E-5C1D-4E8B-9A57-3C2B7D1E9F41}
AppName=Pokémon RPG Studio
AppVersion={#AppVersion}
AppPublisher=Faeres
DefaultDirName={localappdata}\Programs\PokemonRpgStudio
DefaultGroupName=Pokémon RPG Studio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=PokemonRpgStudio-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\..\src\PokemonRpgStudio.App\Assets\app-icon.ico
UninstallDisplayIcon={app}\PokemonRpgStudio.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "install.ps1,install.cmd"; Flags: ignoreversion recursesubdirs createallsubdirs

; Game data: the files of the user's data folder are replaced by the package's ones on every install
; (sprites the user added are kept). Sheets, teams, zones and settings.json are never touched, and
; uninstalling leaves the data folder in place.
Source: "{#SourceDir}\DefaultData\*"; DestDir: "{localappdata}\PokemonRpgStudio\data"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall skipifsourcedoesntexist

[InstallDelete]
; type_chart.json is only shipped when the build has one: otherwise the old file is removed and the
; application writes its built-in chart on the next start. ([InstallDelete] runs before [Files].)
Type: files; Name: "{localappdata}\PokemonRpgStudio\data\type_chart.json"

[Icons]
Name: "{autoprograms}\Pokémon RPG Studio"; Filename: "{app}\PokemonRpgStudio.exe"
Name: "{autodesktop}\Pokémon RPG Studio"; Filename: "{app}\PokemonRpgStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PokemonRpgStudio.exe"; Description: "{cm:LaunchProgram,Pokémon RPG Studio}"; Flags: nowait postinstall skipifsilent
