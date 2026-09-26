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

## Requirements

- .NET 10 SDK
- A Wayland desktop with XWayland (the game runs as an XWayland client under Proton)
- Access to `/dev/uinput`, to move the mouse during a scan

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
dotnet build
```

## Trying it

There is no user interface yet. The platform layer can be driven from the command line: open a stash tab,
leave the game in front, and run

```bash
dotnet run --project Poe2StashPricer.App -- --debug-scan --prices
```

It finds the game window, reads the stash, hovers every slot, and prints each item with what poe.ninja says
it is worth. Leave off `--prices` to skip the download and just list what was read.

Once a tab has been scanned, its prices can be shown over the game itself:

```bash
dotnet run --project Poe2StashPricer.App -- --overlay
```

That follows the game on its own: open a stash tab it has scanned and the prices appear over the items. It
takes no clicks, so the game still gets all of them. On Hyprland add the window rules from
[packaging/hyprland.conf](packaging/hyprland.conf), or the compositor will draw its own border and shadow
around the overlay and let it take focus from the game.

There is no hotkey yet, so a scan still has to be started from the command line.

The scan borrows your clipboard, because copying items is how the game is read, and puts it back when it is
done. A clipboard belongs to a running program, though, so the text is restored only for as long as the app
is running: add `--hold` to keep it alive for a few seconds afterwards and see for yourself.

## Licence

MIT, see [LICENSE](LICENSE). Copyright of the original work belongs to its author; see
[UPSTREAM.md](UPSTREAM.md).
