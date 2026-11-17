#!/usr/bin/env bash
# Prepares a Linux machine to build Pokémon RPG Studio:
#   * installs the .NET 10 SDK in ~/.dotnet if no suitable SDK is found
#   * checks the game data (pokemon.json, moves.json, abilities.json, sprites)
#   * restores NuGet packages
#   * downloads appimagetool into ./tools (needed for the AppImage)
#   * with --android: installs the .NET Android workload, a JDK (./tools/jdk) and the
#     Android SDK (./tools/android-sdk), needed for the APK (./scripts/build.sh android).
#     An existing JDK / SDK is used instead when JAVA_HOME / ANDROID_HOME point to them.
#
# Usage: ./scripts/setup.sh [--no-appimage] [--force-dotnet] [--android]
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

WITH_APPIMAGE=1
WITH_ANDROID=0
FORCE_DOTNET=0
for arg in "$@"; do
    case "$arg" in
        --no-appimage) WITH_APPIMAGE=0 ;;
        --force-dotnet) FORCE_DOTNET=1 ;;
        --android) WITH_ANDROID=1 ;;
        -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
        *) die "Unknown option: $arg" ;;
    esac
done

download() { # url dest
    if command -v curl >/dev/null 2>&1; then curl -fsSL "$1" -o "$2"
    elif command -v wget >/dev/null 2>&1; then wget -q "$1" -O "$2"
    else die "curl or wget is required"; fi
}

# 1. .NET SDK
if [[ $FORCE_DOTNET -eq 0 ]] && find_dotnet; then
    ok ".NET SDK found: $DOTNET ($("$DOTNET" --version))"
else
    info "Installing the .NET $DOTNET_CHANNEL SDK into ~/.dotnet"
    installer="$(mktemp --suffix=.sh)"
    download https://dot.net/v1/dotnet-install.sh "$installer"
    bash "$installer" --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet"
    rm -f -- "${installer:?}"
    find_dotnet || die "The .NET SDK installation failed."
    ok ".NET SDK installed: $("$DOTNET" --version)"
    if ! grep -qs 'DOTNET_ROOT' "$HOME/.bashrc"; then
        warn "Add this to your shell profile to use dotnet directly:"
        warn '  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$PATH"'
    fi
fi

# 2. Game data shipped with the app
check_game_data
ok "Game data found (pokemon.json, moves.json, abilities.json, sprites)"

# 3. NuGet packages
info "Restoring NuGet packages"
"$DOTNET" restore "$PROJECT_ROOT/PokemonRpgStudio.sln" "${MSBUILD_DATA_ARGS[@]}"
ok "Packages restored"

# 4. appimagetool
if [[ $WITH_APPIMAGE -eq 1 ]]; then
    resolve_arch
    mkdir -p "$TOOLS_DIR"
    tool="$TOOLS_DIR/appimagetool-$APPIMAGE_ARCH.AppImage"
    if [[ -x "$tool" ]]; then
        ok "appimagetool already present"
    else
        info "Downloading appimagetool ($APPIMAGE_ARCH)"
        download "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$APPIMAGE_ARCH.AppImage" "$tool"
        chmod +x "$tool"
        ok "appimagetool downloaded to tools/"
    fi
fi

# 5. Android toolchain (APK)
if [[ $WITH_ANDROID -eq 1 ]]; then
    if "$DOTNET" workload list 2>/dev/null | grep -q '^android '; then
        ok ".NET Android workload already installed"
    else
        info "Installing the .NET Android workload"
        "$DOTNET" workload install android
        ok ".NET Android workload installed"
    fi

    if find_android_toolchain; then
        ok "JDK ($ANDROID_JAVA) and Android SDK ($ANDROID_SDK) found"
    else
        # The Android workload's InstallAndroidDependencies target downloads the JDK it supports and the
        # SDK packages the project needs (platform, build tools, platform tools).
        info "Installing a JDK (tools/jdk) and the Android SDK (tools/android-sdk); Android SDK licenses are accepted"
        mkdir -p "$ANDROID_JDK_DIR" "$ANDROID_SDK_DIR"
        "$DOTNET" build "$ANDROID_PROJECT" -t:InstallAndroidDependencies -f net10.0-android \
            "-p:JavaSdkDirectory=$ANDROID_JDK_DIR" "-p:AndroidSdkDirectory=$ANDROID_SDK_DIR" \
            -p:AcceptAndroidSDKLicenses=True "${MSBUILD_DATA_ARGS[@]}" --nologo -v quiet
        find_android_toolchain || die "The JDK / Android SDK installation failed."
        ok "JDK and Android SDK installed in tools/"
    fi
fi

ok "Setup complete. Next: ./scripts/build.sh"
