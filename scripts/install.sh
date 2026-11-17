#!/usr/bin/env bash
# Installs Pokémon RPG Studio for the current user (or system-wide with --system).
#
# Usage: ./scripts/install.sh [options]
#   --appimage FILE   install this AppImage (default: the newest one in ./dist)
#   --folder DIR      install a published folder (default: dist/PokemonRpgStudio-*-linux-*)
#   --system          install into /opt + /usr/local (run with sudo)
#   --keep-data       don't update the game data of an existing installation (see below)
#   --uninstall       remove the application (your sheets and data files are kept)
# Without --appimage/--folder the AppImage is preferred when one is available.
# The script also works from inside an extracted release folder (it installs that folder).
# Game data: pokemon.json, moves.json, abilities.json, type_chart.json and the sprites of your data
# folder are replaced by the package's ones (sprites you added are kept). Sheets, teams, zones and
# settings are never touched. With --system, the data of the user running sudo is updated.
# AppImage: the data is inside it and the application itself updates the data folder (--keep-data has no effect).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="PokemonRpgStudio"
APP_ID="pokemon-rpg-studio"

MODE=""; SOURCE=""; SYSTEM=0; UNINSTALL=0; UPDATE_DATA=1
while [[ $# -gt 0 ]]; do
    case "$1" in
        --appimage) MODE="appimage"; SOURCE="${2:-}"; shift ;;
        --folder)   MODE="folder";   SOURCE="${2:-}"; shift ;;
        --system)   SYSTEM=1 ;;
        --uninstall) UNINSTALL=1 ;;
        --update-data) UPDATE_DATA=1 ;;   # default, kept for older command lines
        --keep-data) UPDATE_DATA=0 ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
    shift
done

if [[ $SYSTEM -eq 1 ]]; then
    [[ $EUID -eq 0 ]] || { echo "--system needs root: sudo $0 --system" >&2; exit 1; }
    APP_DIR="/opt/$APP_ID"
    BIN_DIR="/usr/local/bin"
    DESKTOP_DIR="/usr/local/share/applications"
    ICON_ROOT="/usr/local/share/icons/hicolor"
else
    DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
    APP_DIR="$HOME/.local/opt/$APP_ID"
    BIN_DIR="$HOME/.local/bin"
    DESKTOP_DIR="$DATA_HOME/applications"
    ICON_ROOT="$DATA_HOME/icons/hicolor"
fi
ICON_DIR="$ICON_ROOT/256x256/apps"

refresh_caches() {
    if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q "$DESKTOP_DIR" 2>/dev/null || true; fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then gtk-update-icon-cache -q -t "$ICON_ROOT" 2>/dev/null || true; fi
}

remove_app_dir() {
    # APP_DIR always ends with /pokemon-rpg-studio; double-check before deleting.
    [[ "${APP_DIR:?}" == */"$APP_ID" ]] || { echo "Unexpected install dir: $APP_DIR" >&2; exit 1; }
    rm -rf -- "${APP_DIR:?}"
}

if [[ $UNINSTALL -eq 1 ]]; then
    remove_app_dir
    rm -f -- "${BIN_DIR:?}/$APP_ID" "${DESKTOP_DIR:?}/$APP_ID.desktop" "${ICON_DIR:?}/$APP_ID.png"
    # Native libraries extracted by the .NET single-file host on first launch.
    if [[ $SYSTEM -eq 0 && -d "${HOME:?}/.net/$APP_NAME" ]]; then rm -rf -- "${HOME:?}/.net/${APP_NAME:?}"; fi
    refresh_caches
    echo "✔ Pokémon RPG Studio uninstalled. Your sheets and data files are kept in ~/.local/share/PokemonRpgStudio"
    exit 0
fi

# Locate what to install.
if [[ -z "$MODE" ]]; then
    if [[ -x "$HERE/$APP_NAME" ]]; then
        MODE="folder"; SOURCE="$HERE"                       # running from an extracted release
    else
        DIST="$(cd "$HERE/.." && pwd)/dist"
        SOURCE="$(ls -t "$DIST"/$APP_NAME-*.AppImage 2>/dev/null | head -n1 || true)"
        if [[ -n "$SOURCE" ]]; then
            MODE="appimage"
        else
            SOURCE="$(ls -dt "$DIST"/$APP_NAME-*-linux-*/ 2>/dev/null | head -n1 || true)"
            SOURCE="${SOURCE%/}"
            [[ -n "$SOURCE" ]] && MODE="folder"
        fi
    fi
fi
if [[ -z "$MODE" || -z "$SOURCE" || ! -e "$SOURCE" ]]; then
    echo "Nothing to install. Build first: ./scripts/build.sh linux appimage" >&2
    exit 1
fi

ICON_SRC=""
for candidate in "$HERE/$APP_ID.png" "$HERE/../packaging/linux/$APP_ID.png" "$SOURCE/$APP_ID.png"; do
    if [[ -f "$candidate" ]]; then ICON_SRC="$candidate"; break; fi
done

[[ -e "$APP_DIR" ]] && remove_app_dir
mkdir -p "$APP_DIR" "$BIN_DIR" "$DESKTOP_DIR" "$ICON_DIR"

if [[ "$MODE" == "appimage" ]]; then
    install -m 755 "$SOURCE" "$APP_DIR/$APP_NAME.AppImage"
    EXEC="$APP_DIR/$APP_NAME.AppImage"
    # The game data is inside the AppImage: the application updates the data folder itself on its next start.
    DEFAULTS=""
    echo "  data: included in the AppImage, updated by the application on its next start"
else
    cp -a "$SOURCE/." "$APP_DIR/"
    rm -f -- "${APP_DIR:?}/install.sh"
    chmod +x "$APP_DIR/$APP_NAME"
    EXEC="$APP_DIR/$APP_NAME"
    DEFAULTS="$APP_DIR/DefaultData"
fi

# Game data: plain files in the user's data folder (the application reads them from there).
# Only data/ is written: sheets/, teams/, zones/ and settings.json are left alone.
# Missing files are always added; existing ones are replaced unless --keep-data is given.
DATA_OWNER=""
if [[ $SYSTEM -eq 1 ]]; then
    # System install: update the data of the user who ran sudo (other users get the missing files on first start).
    if [[ -n "${SUDO_USER:-}" && "$SUDO_USER" != "root" ]]; then
        USER_HOME="$(getent passwd "$SUDO_USER" | cut -d: -f6)"
        [[ -n "$USER_HOME" ]] && DATA_OWNER="$SUDO_USER"
        if [[ -n "$DATA_OWNER" ]]; then
            USER_DATA="$USER_HOME/.local/share/PokemonRpgStudio/data"
            USER_IMAGES="$USER_DATA/images"
        fi
    fi
else
    USER_DATA="${POKEMON_RPG_STUDIO_DATA:-${POKEMON_RPG_STUDIO_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/PokemonRpgStudio}/data}"
    USER_IMAGES="${POKEMON_RPG_STUDIO_IMAGES:-$USER_DATA/images}"
fi

update_data() {
    local f updated=0
    mkdir -p "$USER_DATA"
    for f in pokemon.json moves.json abilities.json; do
        [[ -f "$DEFAULTS/$f" ]] || continue
        if [[ ! -f "$USER_DATA/$f" || $UPDATE_DATA -eq 1 ]]; then
            cp "$DEFAULTS/$f" "$USER_DATA/$f"
            echo "  data: $f → $USER_DATA"
        fi
    done
    # type_chart.json: the package's copy when it has one; otherwise the old file is removed and the
    # application writes its built-in (up to date) chart on the next start.
    if [[ -f "$DEFAULTS/type_chart.json" ]]; then
        if [[ ! -f "$USER_DATA/type_chart.json" || $UPDATE_DATA -eq 1 ]]; then
            cp "$DEFAULTS/type_chart.json" "$USER_DATA/type_chart.json"
            echo "  data: type_chart.json → $USER_DATA"
        fi
    elif [[ $UPDATE_DATA -eq 1 && -f "$USER_DATA/type_chart.json" ]]; then
        rm -f -- "$USER_DATA/type_chart.json"
        echo "  data: type_chart.json reset (rewritten from the application's chart on next start)"
    fi
    # Stamp of the data now in the folder, so the application doesn't copy everything again
    # (and leaves the files alone with --keep-data).
    [[ -f "$DEFAULTS/data-version.txt" ]] && cp "$DEFAULTS/data-version.txt" "$USER_DATA/data-version.txt"
    # Sprites: every sprite of the package is copied over yours; sprites that only exist in your folder are kept.
    if [[ -d "$DEFAULTS/images" ]]; then
        mkdir -p "$USER_IMAGES"
        if [[ $UPDATE_DATA -eq 1 ]]; then
            cp -a "$DEFAULTS/images/." "$USER_IMAGES/"
            updated="$(find "$DEFAULTS/images" -maxdepth 1 -type f | wc -l)"
        else
            cp -a -n "$DEFAULTS/images/." "$USER_IMAGES/" 2>/dev/null || true
        fi
        [[ $updated -gt 0 ]] && echo "  data: $updated sprites → $USER_IMAGES"
    fi
    return 0
}

if [[ -n "$DEFAULTS" && -d "$DEFAULTS" && -n "${USER_DATA:-}" && ( $SYSTEM -eq 0 || -n "$DATA_OWNER" ) ]]; then
    # Folders are created as the user so a sudo install leaves nothing owned by root in their home.
    [[ -n "$DATA_OWNER" ]] && sudo -u "$DATA_OWNER" mkdir -p "$USER_IMAGES"
    update_data
    [[ -n "$DATA_OWNER" ]] && chown -R "$DATA_OWNER": "$USER_DATA"
fi

ln -sf "$EXEC" "$BIN_DIR/$APP_ID"
if [[ -n "$ICON_SRC" ]]; then install -m 644 "$ICON_SRC" "$ICON_DIR/$APP_ID.png"; fi

cat > "$DESKTOP_DIR/$APP_ID.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Pokémon RPG Studio
GenericName=Pokémon RPG sheet manager
Comment=Create, save and edit Pokémon RPG character sheets, teams and encounter zones
Exec="$EXEC"
Icon=$APP_ID
Terminal=false
Categories=Game;RolePlaying;Utility;
Keywords=pokemon;rpg;sheet;dice;tabletop;
StartupWMClass=PokemonRpgStudio
DESKTOP

refresh_caches
echo "✔ Installed ($MODE) into $APP_DIR"
echo "  Launch it from your application menu or run: $APP_ID"
case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *) echo "  (add $BIN_DIR to your PATH to use the '$APP_ID' command)" ;;
esac
