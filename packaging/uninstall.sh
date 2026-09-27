#!/usr/bin/env bash
# Removes what install.sh put in place. Your scans and settings are kept unless you ask for them to go.
set -euo pipefail

prefix="${PREFIX:-$HOME/.local}"
config="${XDG_CONFIG_HOME:-$HOME/.config}/poe2-stash-pricer"
purge=false
[ "${1:-}" = "--purge" ] && purge=true

# A copy that is still running would put its hotkeys back on the way out; ask it to stop first.
if command -v poe2-stash-pricer >/dev/null && [ -S "${XDG_RUNTIME_DIR:-/tmp}/poe2-stash-pricer.sock" ]; then
  echo "a copy is still running - close it first, so it gives its hotkeys back"
fi

for f in "$prefix/bin/poe2-stash-pricer" \
         "$prefix/share/applications/poe2-stash-pricer.desktop" \
         "$prefix/share/icons/hicolor/256x256/apps/poe2-stash-pricer.png"; do
  if [ -e "$f" ]; then rm -f "$f"; echo "removed $f"; fi
done

command -v update-desktop-database >/dev/null && update-desktop-database "$prefix/share/applications" 2>/dev/null || true
command -v gtk-update-icon-cache  >/dev/null && gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" 2>/dev/null || true

if [ "$purge" = true ]; then
  rm -rf "$config"
  echo "removed $config (settings, learned tabs, scans and the log)"
else
  [ -d "$config" ] && echo "kept $config - settings, learned tabs and scans. Pass --purge to remove it too."
fi

if [ -e /etc/udev/rules.d/99-poe2-stash-pricer-uinput.rules ]; then
  echo
  echo "the udev rule was installed system-wide; remove it with:"
  echo "  sudo rm /etc/udev/rules.d/99-poe2-stash-pricer-uinput.rules"
  echo "  sudo udevadm control --reload-rules"
fi

echo "done"
