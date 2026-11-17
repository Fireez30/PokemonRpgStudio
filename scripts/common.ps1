# Shared helpers for the Windows scripts. Dot-sourced, not executed.

$ProjectRoot  = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$RepoRoot     = (Resolve-Path (Join-Path $ProjectRoot '..')).Path
$AppProject   = Join-Path $ProjectRoot 'src\PokemonRpgStudio.App\PokemonRpgStudio.App.csproj'
$TestProject  = Join-Path $ProjectRoot 'tests\PokemonRpgStudio.Tests\PokemonRpgStudio.Tests.csproj'
$DistDir      = Join-Path $ProjectRoot 'dist'
$AppName      = 'PokemonRpgStudio'
$DotnetChannel = '10.0'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

[xml]$props = Get-Content (Join-Path $ProjectRoot 'Directory.Build.props')
$AppVersion = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $AppVersion) { $AppVersion = '1.0.0' }

function Write-Info($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "OK  $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "!   $msg" -ForegroundColor Yellow }
function Fail($msg)       { Write-Host "ERROR $msg" -ForegroundColor Red; exit 1 }

# Returns the path of a dotnet executable with an SDK >= 10, or $null.
function Find-Dotnet {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    $candidates += (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    foreach ($c in $candidates | Select-Object -Unique) {
        if (-not (Test-Path $c)) { continue }
        $sdks = & $c --list-sdks 2>$null
        if ($sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 10 }) { return $c }
    }
    return $null
}

function Require-Dotnet {
    $d = Find-Dotnet
    if (-not $d) { Fail ".NET SDK $DotnetChannel not found. Run scripts\setup.ps1 first." }
    return $d
}

# MSBuild arguments pointing at the game data (defaults to .\pokemon_data and .\pokemon_images at the project root).
function Get-GameDataArgs {
    $data   = if ($env:POKEMON_DATA_DIR)   { $env:POKEMON_DATA_DIR }   else { Join-Path $ProjectRoot 'pokemon_data' }
    $images = if ($env:POKEMON_IMAGES_DIR) { $env:POKEMON_IMAGES_DIR } else { Join-Path $ProjectRoot 'pokemon_images' }
    foreach ($f in 'pokemon.json', 'moves.json', 'abilities.json') {
        if (-not (Test-Path (Join-Path $data $f))) { Fail "Missing game data: $data\$f (set POKEMON_DATA_DIR to override)" }
    }
    if (-not (Test-Path $images)) { Fail "Missing sprites folder: $images (set POKEMON_IMAGES_DIR to override)" }
    return @("-p:PokemonDataDir=$data", "-p:PokemonImagesDir=$images")
}

function Find-InnoSetup {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}
