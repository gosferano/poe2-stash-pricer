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

## Building

```
dotnet build
```

## Licence

MIT, see [LICENSE](LICENSE). Copyright of the original work belongs to its author; see
[UPSTREAM.md](UPSTREAM.md).
