<#
.SYNOPSIS
    Prepares a Windows machine to build Pokémon RPG Studio.
.DESCRIPTION
    * Installs the .NET 10 SDK (per-user, in %LOCALAPPDATA%\Microsoft\dotnet) when no suitable SDK is found
    * Checks the game data (pokemon.json, moves.json, abilities.json, sprites)
    * Restores the NuGet packages
    * Optionally reports whether Inno Setup is available (for the setup.exe installer)
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
#>
[CmdletBinding()]
param(
    [switch]$ForceDotnet
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

# 1. .NET SDK
$dotnet = if ($ForceDotnet) { $null } else { Find-Dotnet }
if ($dotnet) {
    Write-Ok ".NET SDK found: $dotnet ($(& $dotnet --version))"
} else {
    Write-Info "Installing the .NET $DotnetChannel SDK for the current user"
    $installDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    $installer = Join-Path ([IO.Path]::GetTempPath()) 'dotnet-install.ps1'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel $DotnetChannel -InstallDir $installDir
    Remove-Item $installer -Force
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath -notlike "*$installDir*") {
        [Environment]::SetEnvironmentVariable('Path', "$installDir;$userPath", 'User')
        Write-Warn "Added $installDir to your user PATH (open a new terminal to use 'dotnet' directly)."
    }
    $dotnet = Find-Dotnet
    if (-not $dotnet) { Fail 'The .NET SDK installation failed.' }
    Write-Ok ".NET SDK installed: $(& $dotnet --version)"
}

# 2. Game data
$dataArgs = Get-GameDataArgs
Write-Ok 'Game data found (pokemon.json, moves.json, abilities.json, sprites)'

# 3. NuGet packages
Write-Info 'Restoring NuGet packages'
& $dotnet restore (Join-Path $ProjectRoot 'PokemonRpgStudio.sln') @dataArgs
if ($LASTEXITCODE -ne 0) { Fail 'dotnet restore failed.' }
Write-Ok 'Packages restored'

# 4. Inno Setup (optional)
if (Find-InnoSetup) {
    Write-Ok 'Inno Setup found: build.ps1 will also produce a setup .exe'
} else {
    Write-Warn 'Inno Setup not found (optional). Install it from https://jrsoftware.org/isdl.php to get a setup .exe.'
}

Write-Ok 'Setup complete. Next: powershell -ExecutionPolicy Bypass -File scripts\build.ps1'
