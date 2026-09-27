# PoE2 Stash Pricer (Linux)

An unofficial Linux port of [tugayilik/poe2-stash-pricing](https://github.com/tugayilik/poe2-stash-pricing),
a tool that scans the special stash tabs of Path of Exile 2 on screen and prices the items with
[poe.ninja](https://poe.ninja/docs/api).

**On Windows, use [the original](https://github.com/tugayilik/poe2-stash-pricing) instead.** This port only
targets Linux: the game running under Steam/Proton (XWayland) on a Wayland desktop, with Hyprland and
KDE Plasma 6 as the supported compositors.

Not affiliated with, endorsed by or connected to Grinding Gear Games or poe.ninja.

## Status

Early work in progress; not usable yet. See [UPSTREAM.md](UPSTREAM.md) for what has been ported so far.

## Installing

Download the latest release, unpack it and run the installer:

```bash
./install.sh
```

That puts `poe2-stash-pricer` in `~/.local/bin`, with an icon and a desktop entry so it appears in your
launcher and taskbar. The binary is a single 23 MB file carrying its own copy of .NET; nothing else has to
be installed.

Four files go in, and nothing else is touched:

```
~/.local/bin/poe2-stash-pricer
~/.local/bin/poe2-stash-pricer-uninstall
~/.local/share/applications/poe2-stash-pricer.desktop
~/.local/share/icons/hicolor/256x256/apps/poe2-stash-pricer.png
```

To remove them again, from anywhere - the download is not needed:

```bash
poe2-stash-pricer-uninstall
```

It takes itself with it. Your settings, learned tabs and scans in `~/.config/poe2-stash-pricer` are kept;
`poe2-stash-pricer-uninstall --purge` removes those as well. Set `PREFIX` on either script to install
somewhere other than `~/.local`.

## Requirements

- A Wayland desktop with XWayland (the game runs as an XWayland client under Proton)
- Access to `/dev/uinput`, to move the mouse during a scan
- .NET 10 SDK, only to build it yourself

Steam already sets that access up: `/usr/lib/udev/rules.d/60-steam-input.rules` hands `/dev/uinput` to the
logged-in user, so if you launch Path of Exile 2 through Steam there is nothing to do. Otherwise install the
rule that ships here:

```bash
sudo cp packaging/99-poe2-stash-pricer-uinput.rules /etc/udev/rules.d/
```

```bash
sudo udevadm control --reload-rules && sudo udevadm trigger
```

## Building

```bash
dotnet build src/Poe2StashPricer.slnx
```

Package versions all live in [src/Directory.Packages.props](src/Directory.Packages.props); a project names the
package it wants and nothing else.

For a standalone binary like the released one:

```bash
dotnet publish src/Poe2StashPricer.App -c Release -r linux-x64 --self-contained -o publish
```

## Running it

```bash
dotnet run --project src/Poe2StashPricer.App
```

A window lists every tab you have scanned, what each is worth and what is in it, with the prices drawn over
the game as well. `--overlay` leaves the window out and shows only the prices.

On a desktop with a system tray the app puts an icon there and closing the window only puts it away: the
prices stay on the game and the keys keep working. The icon's menu reopens the window, scans, toggles the
prices and quits; so does starting the app again, which brings the copy already running to the front rather
than opening a second one. Quitting from a terminal:

```bash
poe2-stash-pricer --quit
```

Where the desktop has no tray, there would be no way back to a hidden window, so closing it ends the app
instead.

The command line still drives the platform layer directly for testing: open a stash tab, leave the game in
front, and run

```bash
dotnet run --project src/Poe2StashPricer.App -- --debug-scan --prices
```

It finds the game window, reads the stash, hovers every slot, and prints each item with what poe.ninja says
it is worth. Leave off `--prices` to skip the download and just list what was read.

Once a tab has been scanned, its prices can be shown over the game itself:

```bash
dotnet run --project src/Poe2StashPricer.App -- --overlay
```

That follows the game on its own: open a stash tab it has scanned and the prices appear over the items. It
takes no clicks, so the game still gets all of them.

On Hyprland this needs no setting up: the app asks the compositor over its IPC socket to leave the overlay
alone. That matters because blur is usually on, and a compositor blurs whatever sits behind a transparent
window - which here is the game, so the stash would end up looking washed out. The same rules stop the
overlay taking focus, being faded by `inactive_opacity`, and getting a border and shadow of its own.

The rules are scoped to this app's own window and last until the compositor reloads its config; nothing of
yours is written to. Set `"ApplyCompositorRules": false` in the settings file to stop the app asking, and
put the rules in your own config instead - [packaging/hyprland.lua](packaging/hyprland.lua) has them.

On any other compositor none of this runs, and the overlay looks however that compositor draws a
transparent window.

### Scanning with a key

F7 scans the open tab and F8 hides or shows the prices. A compositor will not give a key to a window that is
not focused, so the app cannot listen for one while you play. It asks the desktop instead, two ways, in this
order:

- The desktop's own global shortcuts, over xdg-desktop-portal. Shortcuts registered this way appear in the
  desktop's settings and can be rebound there. The portal only answers an app it can put a name to, and a
  plain binary started from a launcher often has no app id it will accept; then this is skipped.
- Hyprland's own bindings, set when the app starts and handed back when it exits, so there is nothing to set
  up. Each key runs the app again with `--scan` or `--toggle-overlay`, and that copy tells the running one
  what to do through a socket.

Which one is in use is in the log. On any other desktop, bind the command yourself:

```bash
poe2-stash-pricer --scan
```

`--toggle-overlay` is the other one. [packaging/hyprland.lua](packaging/hyprland.lua) has both as config, for
anyone who would rather not have the app do it.

A scan is a snapshot: the prices stay where the items were when you scanned. Move things about and press the
key again. Only tabs you have scanned show anything - each tab needs its own scan.

The scan borrows your clipboard, because copying items is how the game is read, and puts it back when it is
done. A clipboard belongs to a running program, though, so the text is restored only for as long as the app
is running: add `--hold` to keep it alive for a few seconds afterwards and see for yourself.

## Licence

MIT, see [LICENSE](LICENSE). Copyright of the original work belongs to its author; see
[UPSTREAM.md](UPSTREAM.md).
