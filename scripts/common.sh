#!/usr/bin/env bash
# Shared helpers for the Linux scripts. Sourced, not executed.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$PROJECT_ROOT/.." && pwd)"
APP_PROJECT="$PROJECT_ROOT/src/PokemonRpgStudio.App/PokemonRpgStudio.App.csproj"
ANDROID_PROJECT="$PROJECT_ROOT/src/PokemonRpgStudio.Android/PokemonRpgStudio.Android.csproj"
# Android toolchain installed by ./scripts/setup.sh --android (JDK and Android SDK in ./tools)
ANDROID_JDK_DIR="$PROJECT_ROOT/tools/jdk"
ANDROID_SDK_DIR="$PROJECT_ROOT/tools/android-sdk"
TEST_PROJECT="$PROJECT_ROOT/tests/PokemonRpgStudio.Tests/PokemonRpgStudio.Tests.csproj"
DIST_DIR="$PROJECT_ROOT/dist"
TOOLS_DIR="$PROJECT_ROOT/tools"
APP_NAME="PokemonRpgStudio"
APP_ID="pokemon-rpg-studio"
DOTNET_CHANNEL="10.0"

APP_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT_ROOT/Directory.Build.props" | head -n1)"
APP_VERSION="${APP_VERSION:-1.0.0}"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

info()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
ok()    { printf '\033[1;32m✔\033[0m %s\n' "$*"; }
warn()  { printf '\033[1;33m!\033[0m %s\n' "$*" >&2; }
die()   { printf '\033[1;31m✘ %s\033[0m\n' "$*" >&2; exit 1; }

# Finds a .NET SDK >= 10 on PATH, in $DOTNET_ROOT or in ~/.dotnet and exports DOTNET.
find_dotnet() {
    local candidates=()
    command -v dotnet >/dev/null 2>&1 && candidates+=("$(command -v dotnet)")
    [[ -n "${DOTNET_ROOT:-}" && -x "${DOTNET_ROOT}/dotnet" ]] && candidates+=("$DOTNET_ROOT/dotnet")
    [[ -x "$HOME/.dotnet/dotnet" ]] && candidates+=("$HOME/.dotnet/dotnet")
    local d
    for d in "${candidates[@]}"; do
        if "$d" --list-sdks 2>/dev/null | grep -Eq '^(1[0-9]|[2-9][0-9])\.'; then
            DOTNET="$d"
            DOTNET_ROOT="$(dirname "$(readlink -f "$d")")"
            export DOTNET_ROOT
            export PATH="$DOTNET_ROOT:$PATH"
            return 0
        fi
    done
    return 1
}

require_dotnet() {
    find_dotnet || die ".NET SDK $DOTNET_CHANNEL not found. Run ./scripts/setup.sh first."
}

# Maps the machine / requested arch to .NET RID and AppImage arch names.
resolve_arch() {
    local arch="${1:-}"
    [[ -z "$arch" ]] && arch="$(uname -m)"
    case "$arch" in
        x86_64|amd64|x64)  RID_ARCH="x64";   APPIMAGE_ARCH="x86_64" ;;
        aarch64|arm64)     RID_ARCH="arm64"; APPIMAGE_ARCH="aarch64" ;;
        *) die "Unsupported architecture: $arch (use x64 or arm64)" ;;
    esac
}

# Game data bundled with the app (defaults to ./pokemon_data and ./pokemon_images at the project root; override with env vars).
check_game_data() {
    local data="${POKEMON_DATA_DIR:-$PROJECT_ROOT/pokemon_data}"
    local images="${POKEMON_IMAGES_DIR:-$PROJECT_ROOT/pokemon_images}"
    local f
    for f in pokemon.json moves.json abilities.json; do
        [[ -f "$data/$f" ]] || die "Missing game data: $data/$f (set POKEMON_DATA_DIR to override)"
    done
    [[ -d "$images" ]] || die "Missing sprites folder: $images (set POKEMON_IMAGES_DIR to override)"
    MSBUILD_DATA_ARGS=("-p:PokemonDataDir=$data" "-p:PokemonImagesDir=$images")
}

# Deletes a directory only if it is inside dist/ (protects against bad paths).
clean_dist_dir() {
    local target="${1:?}"
    case "$(readlink -m "$target")" in
        "$(readlink -m "$DIST_DIR")"/*) rm -rf -- "${target:?}" ;;
        *) die "Refusing to delete outside dist/: $target" ;;
    esac
}

# Java and Android SDK for the APK: $JAVA_HOME / $ANDROID_HOME when set, otherwise the copies in ./tools.
find_android_toolchain() {
    ANDROID_JAVA="${JAVA_HOME:-}"
    [[ -n "$ANDROID_JAVA" && -x "$ANDROID_JAVA/bin/javac" ]] || ANDROID_JAVA="$ANDROID_JDK_DIR"
    ANDROID_SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
    [[ -n "$ANDROID_SDK" && -d "$ANDROID_SDK/platforms" ]] || ANDROID_SDK="$ANDROID_SDK_DIR"
    [[ -x "$ANDROID_JAVA/bin/javac" && -d "$ANDROID_SDK/platforms" ]]
}

require_android_toolchain() {
    "$DOTNET" workload list 2>/dev/null | grep -q '^android ' \
        || die "The .NET Android workload is missing. Run ./scripts/setup.sh --android first."
    find_android_toolchain || die "JDK / Android SDK not found. Run ./scripts/setup.sh --android first."
    ANDROID_MSBUILD_ARGS=("-p:JavaSdkDirectory=$ANDROID_JAVA" "-p:AndroidSdkDirectory=$ANDROID_SDK")
}
