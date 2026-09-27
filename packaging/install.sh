#!/usr/bin/env bash
# Puts the app where the desktop can find it: the binary on PATH, an icon and a desktop entry.
# Run it from an unpacked release, or after "dotnet publish".
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
binary="${1:-$here/poe2-stash-pricer}"
prefix="${PREFIX:-$HOME/.local}"

[ -x "$binary" ] || { echo "no poe2-stash-pricer binary at $binary" >&2; exit 1; }

install -Dm755 "$binary"                      "$prefix/bin/poe2-stash-pricer"
install -Dm644 "$here/poe2-stash-pricer.desktop" "$prefix/share/applications/poe2-stash-pricer.desktop"
install -Dm644 "$here/icon.png"               "$prefix/share/icons/hicolor/256x256/apps/poe2-stash-pricer.png"

command -v update-desktop-database >/dev/null && update-desktop-database "$prefix/share/applications" || true
command -v gtk-update-icon-cache  >/dev/null && gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" 2>/dev/null || true

echo "installed to $prefix"
case ":$PATH:" in
  *":$prefix/bin:"*) ;;
  *) echo "note: $prefix/bin is not on your PATH" ;;
esac

if [ ! -e /dev/uinput ] || [ ! -w /dev/uinput ]; then
  echo
  echo "/dev/uinput is not writable, which the app needs to move the mouse during a scan."
  echo "Steam's own udev rule usually grants it; otherwise:"
  echo "  sudo cp $here/99-poe2-stash-pricer-uinput.rules /etc/udev/rules.d/"
  echo "  sudo udevadm control --reload-rules && sudo udevadm trigger"
fi
