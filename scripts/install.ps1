<#
.SYNOPSIS
    Installs Pokémon RPG Studio for the current user on Windows (no administrator rights needed).
.DESCRIPTION
    Copies the application to %LOCALAPPDATA%\Programs\PokemonRpgStudio, creates Start menu
    (and optionally desktop) shortcuts and registers an entry in "Installed apps" so it can be
    uninstalled from the Windows settings.
    Game data of an existing installation is updated: pokemon.json, moves.json, abilities.json,
    type_chart.json and the sprites of %LOCALAPPDATA%\PokemonRpgStudio\data are replaced by the
    package's ones (sprites you added are kept). Sheets, teams, zones and settings are never touched.
.PARAMETER Source
    Folder containing PokemonRpgStudio.exe. Defaults to the folder of this script when it sits
    next to the exe (release zip), otherwise to the newest dist\PokemonRpgStudio-*-win-* build.
.PARAMETER NoDesktopShortcut
    Don't create a desktop shortcut.
.PARAMETER KeepData
    Don't replace the existing game data files and sprites (missing ones are still added).
.PARAMETER Uninstall
    Remove the application, its shortcuts and its "Installed apps" entry.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1
    powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Source,
    [switch]$NoDesktopShortcut,
    [switch]$KeepData,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$AppName     = 'PokemonRpgStudio'
$DisplayName = 'Pokémon RPG Studio'
$InstallDir  = Join-Path $env:LOCALAPPDATA "Programs\$AppName"
$StartMenu   = Join-Path ([Environment]::GetFolderPath('Programs')) "$DisplayName.lnk"
$DesktopLnk  = Join-Path ([Environment]::GetFolderPath('Desktop')) "$DisplayName.lnk"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"

function Remove-Shortcuts {
    foreach ($l in $StartMenu, $DesktopLnk) { if (Test-Path $l) { Remove-Item -Force -LiteralPath $l } }
}

if ($Uninstall) {
    Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Shortcuts
    if (Test-Path $UninstallKey) { Remove-Item -Recurse -Force $UninstallKey }
    if (Test-Path $InstallDir) {
        # The uninstaller may run from inside the install folder: delete it once this script exits.
        if ($PSScriptRoot -and ((Resolve-Path $PSScriptRoot).Path -eq (Resolve-Path $InstallDir).Path)) {
            Start-Process -WindowStyle Hidden cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$InstallDir`""
        } else {
            Remove-Item -Recurse -Force -LiteralPath $InstallDir
        }
    }
    Write-Host "Pokémon RPG Studio has been uninstalled. Your sheets are kept in $env:LOCALAPPDATA\PokemonRpgStudio" -ForegroundColor Green
    exit 0
}

# Locate the build to install.
if (-not $Source) {
    if (Test-Path (Join-Path $PSScriptRoot "$AppName.exe")) {
        $Source = $PSScriptRoot
    } else {
        $dist = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'
        $Source = Get-ChildItem -Path $dist -Directory -Filter "$AppName-*-win-*" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $Source -or -not (Test-Path (Join-Path $Source "$AppName.exe"))) {
    Write-Host "No build found. Run: powershell -ExecutionPolicy Bypass -File scripts\build.ps1" -ForegroundColor Red
    exit 1
}
$Source = (Resolve-Path $Source).Path

Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force -LiteralPath $InstallDir }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $Source '*') -Destination $InstallDir
foreach ($extra in 'install.cmd', 'install.sh') {
    $p = Join-Path $InstallDir $extra
    if (Test-Path $p) { Remove-Item -Force -LiteralPath $p }
}
# Game data: only the data folder is written (sheets, teams, zones and settings.json are left alone).
$UserRoot = if ($env:POKEMON_RPG_STUDIO_HOME) { $env:POKEMON_RPG_STUDIO_HOME } else { Join-Path $env:LOCALAPPDATA $AppName }
$UserData = if ($env:POKEMON_RPG_STUDIO_DATA) { $env:POKEMON_RPG_STUDIO_DATA } else { Join-Path $UserRoot 'data' }
$UserImages = if ($env:POKEMON_RPG_STUDIO_IMAGES) { $env:POKEMON_RPG_STUDIO_IMAGES } else { Join-Path $UserData 'images' }
$Defaults = Join-Path $InstallDir 'DefaultData'
if (Test-Path $Defaults) {
    New-Item -ItemType Directory -Force -Path $UserData | Out-Null
    foreach ($f in 'pokemon.json', 'moves.json', 'abilities.json', 'type_chart.json') {
        $src = Join-Path $Defaults $f
        $dst = Join-Path $UserData $f
        if (Test-Path $src) {
            if (-not $KeepData -or -not (Test-Path $dst)) {
                Copy-Item -Force -LiteralPath $src -Destination $dst
                Write-Host "  data: $f -> $UserData"
            }
        } elseif ($f -eq 'type_chart.json' -and -not $KeepData -and (Test-Path $dst)) {
            # Not shipped: the application writes its built-in (up to date) chart on the next start.
            Remove-Item -Force -LiteralPath $dst
            Write-Host "  data: type_chart.json reset (rewritten from the application's chart on next start)"
        }
    }
    # Stamp of the data now in the folder, so the application doesn't copy everything again (and keeps the files with -KeepData).
    $stamp = Join-Path $Defaults 'data-version.txt'
    if (Test-Path $stamp) { Copy-Item -Force -LiteralPath $stamp -Destination (Join-Path $UserData 'data-version.txt') }
    $srcImages = Join-Path $Defaults 'images'
    if (Test-Path $srcImages) {
        New-Item -ItemType Directory -Force -Path $UserImages | Out-Null
        $sprites = Get-ChildItem -File -LiteralPath $srcImages
        $copied = 0
        foreach ($s in $sprites) {
            $dst = Join-Path $UserImages $s.Name
            if (-not $KeepData -or -not (Test-Path $dst)) { Copy-Item -Force -LiteralPath $s.FullName -Destination $dst; $copied++ }
        }
        if ($copied -gt 0) { Write-Host "  data: $copied sprites -> $UserImages" }
    }
}

# Keep a copy of this script in the install folder to act as the uninstaller.
$uninstaller = Join-Path $InstallDir 'uninstall.ps1'
Copy-Item -Force $PSCommandPath $uninstaller
$installedScript = Join-Path $InstallDir 'install.ps1'
if (Test-Path $installedScript) { Remove-Item -Force -LiteralPath $installedScript }

$exe = Join-Path $InstallDir "$AppName.exe"
$shell = New-Object -ComObject WScript.Shell
function New-Shortcut($path) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $InstallDir
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'Create, save and edit Pokémon RPG character sheets'
    $lnk.Save()
}
New-Shortcut $StartMenu
if (-not $NoDesktopShortcut) { New-Shortcut $DesktopLnk }

$version = (Get-Item $exe).VersionInfo.ProductVersion
New-Item -Force -Path $UninstallKey | Out-Null
$size = [int]((Get-ChildItem -Recurse $InstallDir | Measure-Object -Property Length -Sum).Sum / 1KB)
$values = @{
    DisplayName     = $DisplayName
    DisplayVersion  = "$version"
    Publisher       = 'Faeres'
    InstallLocation = $InstallDir
    DisplayIcon     = "$exe,0"
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`" -Uninstall"
    NoModify        = 1
    NoRepair        = 1
    EstimatedSize   = $size
}
foreach ($k in $values.Keys) {
    $type = if ($values[$k] -is [int]) { 'DWord' } else { 'String' }
    New-ItemProperty -Force -Path $UninstallKey -Name $k -Value $values[$k] -PropertyType $type | Out-Null
}

Write-Host "Pokémon RPG Studio $version installed in $InstallDir" -ForegroundColor Green
Write-Host 'Launch it from the Start menu (or the desktop shortcut).'
