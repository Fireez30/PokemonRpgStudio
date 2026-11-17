<#
.SYNOPSIS
    Builds Pokémon RPG Studio release packages into .\dist
.PARAMETER Target
    windows (default), linux or all. Linux builds can be cross-compiled from Windows,
    but the AppImage must be built on Linux (scripts/build.sh appimage).
.PARAMETER Arch
    x64 (default) or arm64.
.PARAMETER SkipTests
    Don't run the unit tests first.
.PARAMETER Offline
    Restore packages from the local NuGet cache only (no network).
.PARAMETER NoInstaller
    Don't build the Inno Setup installer even if Inno Setup is installed.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build.ps1
    powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Target all -Arch x64
#>
[CmdletBinding()]
param(
    [ValidateSet('windows', 'linux', 'all')][string]$Target = 'windows',
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Offline,
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$dotnet = Require-Dotnet
$dataArgs = Get-GameDataArgs
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$restoreArgs = @()
if ($Offline) {
    $cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    if (-not (Test-Path $cache)) { Fail "No local NuGet cache at ${cache}: run once online (scripts\setup.ps1)." }
    $restoreArgs = @("-p:RestoreSources=$cache")
    Write-Info "Offline build: packages from $cache"
}

if (-not $SkipTests) {
    Write-Info 'Running unit tests'
    & $dotnet test $TestProject -c $Configuration --nologo -v quiet @dataArgs @restoreArgs
    if ($LASTEXITCODE -ne 0) { Fail 'Unit tests failed.' }
    Write-Ok 'Tests passed'
}

function Publish-App([string]$rid, [string]$outDir) {
    Write-Info "Publishing $rid ($Configuration)"
    if (Test-Path $outDir) { Remove-Item -Recurse -Force -LiteralPath $outDir }
    # Windows: a plain folder (exe + DLLs). A single-file exe that extracts native libraries at
    # runtime is a packer-like pattern that Windows Defender often flags as a false positive.
    $single = if ($rid -like 'win-*') { @('-p:PublishSingleFile=false') }
              else { @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') }
    & $dotnet publish $AppProject -c $Configuration -r $rid --self-contained true `
        @single -p:DebugType=None -p:DebugSymbols=false @dataArgs @restoreArgs -o $outDir --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed for $rid." }
    Copy-Item (Join-Path $ProjectRoot 'README.md') $outDir
}

if ($Target -in 'windows', 'all') {
    $winDir = Join-Path $DistDir "$AppName-$AppVersion-win-$Arch"
    Publish-App "win-$Arch" $winDir
    Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $winDir
    $zip = "$winDir.zip"
    if (Test-Path $zip) { Remove-Item -Force -LiteralPath $zip }
    Compress-Archive -Path $winDir -DestinationPath $zip
    Write-Ok "Windows build: dist\$(Split-Path $winDir -Leaf)\ and dist\$(Split-Path $zip -Leaf)"

    $iscc = if ($NoInstaller) { $null } else { Find-InnoSetup }
    if ($iscc) {
        Write-Info 'Building the setup .exe with Inno Setup'
        & $iscc /Q "/DAppVersion=$AppVersion" "/DSourceDir=$winDir" "/DOutputDir=$DistDir" "/DArch=$Arch" `
            (Join-Path $ProjectRoot 'packaging\windows\installer.iss')
        if ($LASTEXITCODE -ne 0) { Fail 'Inno Setup failed.' }
        Write-Ok "Installer: dist\$AppName-$AppVersion-win-$Arch-setup.exe"
    } elseif (-not $NoInstaller) {
        Write-Warn 'Inno Setup not found: no setup .exe built. It is the recommended Windows package (install it from https://jrsoftware.org/isdl.php); the zip + install.ps1 still work.'
    }
}

if ($Target -in 'linux', 'all') {
    $linuxDir = Join-Path $DistDir "$AppName-$AppVersion-linux-$Arch"
    Publish-App "linux-$Arch" $linuxDir
    Copy-Item (Join-Path $PSScriptRoot 'install.sh') $linuxDir
    Copy-Item (Join-Path $ProjectRoot 'packaging\linux\pokemon-rpg-studio.png') $linuxDir
    Copy-Item (Join-Path $ProjectRoot 'packaging\linux\pokemon-rpg-studio.desktop') $linuxDir
    Write-Ok "Linux build: dist\$(Split-Path $linuxDir -Leaf)\ (run 'chmod +x PokemonRpgStudio install.sh' on Linux; build the AppImage on Linux)"
}

Write-Ok "Done. Packages are in $DistDir"
