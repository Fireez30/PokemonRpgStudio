#!/usr/bin/env bash
# Builds Pokémon RPG Studio release packages into ./dist
#
# Usage: ./scripts/build.sh [targets...] [options]
#   targets   linux     self-contained Linux executable (folder + .tar.gz)
#             appimage  Linux AppImage (run ./scripts/setup.sh first for appimagetool)
#             windows   self-contained Windows executable (folder + .zip), cross-compiled
#             all       linux + appimage + windows (default)
#   options   --arch x64|arm64   CPU architecture (default: this machine)
#             --skip-tests       don't run the unit tests first
#             --offline          restore packages from the local NuGet cache only (no network)
#             --debug            Debug configuration instead of Release
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

TARGETS=()
ARCH=""
RUN_TESTS=1
OFFLINE=0
CONFIG="Release"
while [[ $# -gt 0 ]]; do
    case "$1" in
        linux|appimage|windows) TARGETS+=("$1") ;;
        all) TARGETS+=(linux appimage windows) ;;
        --arch) ARCH="${2:-}"; shift ;;
        --skip-tests) RUN_TESTS=0 ;;
        --offline) OFFLINE=1 ;;
        --debug) CONFIG="Debug" ;;
        -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
        *) die "Unknown argument: $1 (see --help)" ;;
    esac
    shift
done
[[ ${#TARGETS[@]} -eq 0 ]] && TARGETS=(linux appimage windows)

require_dotnet
check_game_data
resolve_arch "$ARCH"
mkdir -p "$DIST_DIR"

# --offline: the packages are taken from the local cache (filled by a previous online build/setup).
RESTORE_ARGS=()
if [[ $OFFLINE -eq 1 ]]; then
    NUGET_CACHE="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
    [[ -d "$NUGET_CACHE" ]] || die "No local NuGet cache at $NUGET_CACHE: run once online (./scripts/setup.sh)."
    RESTORE_ARGS=("-p:RestoreSources=$NUGET_CACHE")
    info "Offline build: packages from $NUGET_CACHE"
fi

has_target() { local t; for t in "${TARGETS[@]}"; do [[ "$t" == "$1" ]] && return 0; done; return 1; }

if [[ $RUN_TESTS -eq 1 ]]; then
    info "Running unit tests"
    "$DOTNET" test "$TEST_PROJECT" -c "$CONFIG" --nologo -v quiet "${MSBUILD_DATA_ARGS[@]}" "${RESTORE_ARGS[@]}"
    ok "Tests passed"
fi

publish() { # rid output_dir
    info "Publishing $1 ($CONFIG)"
    [[ -e "$2" ]] && clean_dist_dir "$2"
    "$DOTNET" publish "$APP_PROJECT" -c "$CONFIG" -r "$1" --self-contained true \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=None -p:DebugSymbols=false "${MSBUILD_DATA_ARGS[@]}" "${RESTORE_ARGS[@]}" \
        -o "$2" --nologo -v quiet
    cp "$PROJECT_ROOT/README.md" "$2/README.md"
}

make_zip() { # source_dir zip_file
    [[ -e "$2" ]] && clean_dist_dir "$2"
    if command -v zip >/dev/null 2>&1; then
        (cd "$(dirname "$1")" && zip -qr "$2" "$(basename "$1")")
    else
        python3 - "$1" "$2" <<'PY'
import os, sys, zipfile
src, dst = sys.argv[1], sys.argv[2]
base = os.path.dirname(src)
with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(src):
        for f in files:
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, base))
PY
    fi
}

LINUX_DIR="$DIST_DIR/$APP_NAME-$APP_VERSION-linux-$RID_ARCH"

if has_target linux || has_target appimage; then
    publish "linux-$RID_ARCH" "$LINUX_DIR"
    chmod +x "$LINUX_DIR/$APP_NAME"
    cp "$PROJECT_ROOT/packaging/linux/$APP_ID.png" "$LINUX_DIR/$APP_ID.png"
    cp "$PROJECT_ROOT/packaging/linux/$APP_ID.desktop" "$LINUX_DIR/$APP_ID.desktop"
    cp "$SCRIPT_DIR/install.sh" "$LINUX_DIR/install.sh"
    if has_target linux; then
        tar -C "$DIST_DIR" -czf "$LINUX_DIR.tar.gz" "$(basename "$LINUX_DIR")"
        ok "Linux build: dist/$(basename "$LINUX_DIR")/ and dist/$(basename "$LINUX_DIR").tar.gz"
    fi
fi

if has_target appimage; then
    tool="$TOOLS_DIR/appimagetool-$APPIMAGE_ARCH.AppImage"
    if [[ ! -x "$tool" ]] && command -v appimagetool >/dev/null 2>&1; then tool="$(command -v appimagetool)"; fi
    [[ -x "$tool" ]] || die "appimagetool not found. Run ./scripts/setup.sh (or install appimagetool)."

    info "Building the AppImage"
    APPDIR="$DIST_DIR/AppDir"
    [[ -e "$APPDIR" ]] && clean_dist_dir "$APPDIR"
    mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
    cp -a "$LINUX_DIR/." "$APPDIR/usr/bin/"
    # The game data (JSON files and sprites, DefaultData/) is inside the AppImage, so it works on its own
    # (Gear Lever, AppImageLauncher…); the application updates the user's data folder when it changes.
    for extra in install.sh "$APP_ID.desktop" "$APP_ID.png"; do
        [[ -e "$APPDIR/usr/bin/$extra" ]] && clean_dist_dir "$APPDIR/usr/bin/$extra"
    done
    install -m 755 "$PROJECT_ROOT/packaging/linux/AppRun" "$APPDIR/AppRun"
    cp "$PROJECT_ROOT/packaging/linux/$APP_ID.desktop" "$APPDIR/$APP_ID.desktop"
    cp "$PROJECT_ROOT/packaging/linux/$APP_ID.desktop" "$APPDIR/usr/share/applications/"
    cp "$PROJECT_ROOT/packaging/linux/$APP_ID.png" "$APPDIR/$APP_ID.png"
    cp "$PROJECT_ROOT/packaging/linux/$APP_ID.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/"
    ln -sf "$APP_ID.png" "$APPDIR/.DirIcon"

    APPIMAGE="$DIST_DIR/$APP_NAME-$APP_VERSION-$APPIMAGE_ARCH.AppImage"
    [[ -e "$APPIMAGE" ]] && clean_dist_dir "$APPIMAGE"
    # APPIMAGE_EXTRACT_AND_RUN lets appimagetool run without FUSE (containers, CI).
    ARCH="$APPIMAGE_ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream "$APPDIR" "$APPIMAGE"
    chmod +x "$APPIMAGE"
    clean_dist_dir "$APPDIR"
    # Leftovers of older builds, when the data was delivered beside the AppImage.
    [[ -e "$DIST_DIR/$APP_NAME-data" ]] && clean_dist_dir "$DIST_DIR/$APP_NAME-data"
    for old in "$DIST_DIR"/$APP_NAME-*-data.zip; do [[ -e "$old" ]] && clean_dist_dir "$old"; done
    ok "AppImage (game data included): dist/$(basename "$APPIMAGE")"
fi

if has_target windows; then
    WIN_DIR="$DIST_DIR/$APP_NAME-$APP_VERSION-win-$RID_ARCH"
    publish "win-$RID_ARCH" "$WIN_DIR"
    cp "$SCRIPT_DIR/install.ps1" "$WIN_DIR/install.ps1"
    cp "$PROJECT_ROOT/packaging/windows/install.cmd" "$WIN_DIR/install.cmd"
    make_zip "$WIN_DIR" "$WIN_DIR.zip"
    ok "Windows build: dist/$(basename "$WIN_DIR")/ and dist/$(basename "$WIN_DIR").zip"
fi

ok "Done. Packages are in $DIST_DIR"
