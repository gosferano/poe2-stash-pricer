# Upstream

This repository is an unofficial Linux port of

- **Repository:** <https://github.com/tugayilik/poe2-stash-pricing>
- **Base commit:** `888b145016b96d951fe71d4a971c9c2a9dde932a` (app version 1.4.1)

Upstream is Windows-only: .NET Framework 4.8 with WinForms, built by `build.ps1` with the `csc.exe` that ships
with Windows. This port targets .NET 10 with Avalonia and runs on Linux.

Upstream's git history is not imported; files were copied in from a clone of the base commit.

## Conventions used while porting

- **Namespaces.** Upstream uses one flat namespace, `PoeStashPricer`. Here the root is `Poe2StashPricer` and
  each file's namespace follows its folder: `Poe2StashPricer.Detection`, `.Scanning`, `.Tabs`, `.Pricing`,
  `.Platform`, `.Storage`. A ported file therefore differs from upstream in its namespace line and in the
  `using` directives for the folders it reaches into. The app project uses `Poe2StashPricer.App.*`.
- **Naming.** Private fields are `_camelCase` and constants are PascalCase, except where a constant mirrors a
  C header (`EV_KEY`, `O_WRONLY`, `ZPixmap`, `XA_ATOM`, `WL_REGISTRY_BIND`): those keep the spelling the
  header uses, so they can be checked against it. `var` and target-typed `new()` are not used.
- **Structure.** Ported files keep upstream's class names, logic and comments, and `<Nullable>` is not enabled
  for ported code. Two project-wide style rules are applied to every file, ported ones included, and are the
  only reformatting done: file-scoped namespaces and explicit `private`/`internal` modifiers (see
  `.editorconfig`). Both shift indentation, so comparing a ported file with upstream wants `diff -w`.
- **Platform calls.** Everything that touched Win32 (`Native.*`), WinForms or `System.Drawing.Bitmap` now goes
  through interfaces in `Poe2StashPricer.Core/Platform/`, implemented in the app project under `Platform/`.
- **Images.** `System.Drawing.Rectangle/Point/Size` are kept (they come from `System.Drawing.Primitives` and work
  on Linux). `Bitmap`, `Graphics` and `LockBits` are replaced: `PixelBuffer` is the only image boundary and is
  built either from raw BGRA bytes or from a SixLabors.ImageSharp image.
- **JSON.** `System.Web.Script.Serialization.JavaScriptSerializer` is replaced with `System.Text.Json`
  (`PropertyNameCaseInsensitive = true`); `[ScriptIgnore]` becomes `[JsonIgnore]`. Everything is read and
  written as a stream, never through an intermediate string: the settings, tab profiles, scan results and
  learned digits through `Storage/Json.cs`, the embedded layouts straight off the resource stream, and a
  poe.ninja answer straight off the response.
- **Data folder.** `%APPDATA%\PoeStashPricer` becomes `~/.config/poe2-stash-pricer` (`AppPaths.Dir`, which follows
  `XDG_CONFIG_HOME`).
- **Version.** This port has its own version numbers, starting at 0.1.0; it does not follow upstream's.

## File map

Status: **done** = ported, **part** = partly done, **later** = planned for a later phase,
**no** = intentionally not ported.

| Upstream path | Here | Status | Notes |
|---|---|---|---|
| `src/Layouts.json` | `Poe2StashPricer.Core/Layouts.json` | done | Copied as-is, embedded as `Poe2StashPricer.Layouts.json`. |
| `src/SlotDetector.cs` | `Poe2StashPricer.Core/Detection/PixelBuffer.cs`, `Detection/SlotDetector.cs` | done | Split: `PixelBuffer` gets its own file. |
| `src/StashLocator.cs` | `Poe2StashPricer.Core/Detection/StashLocator.cs` | done | Namespace rename only. |
| `src/DigitReader.cs` | `Poe2StashPricer.Core/Detection/DigitReader.cs` | done | |
| `src/TabWatcher.cs` | `Poe2StashPricer.Core/Detection/TabWatcher.cs` | done | Takes an `IScreenCapture` instead of calling `Grid.Capture`. |
| `src/Scanner.cs` | `Poe2StashPricer.Core/Detection/Grid.cs`, `Scanning/{Scanner,ScanConfig,ScanPlan,ProbeGroup,ScanItem,ScanResult}.cs` | done | Split by type; `Grid.Capture` moves behind `IScreenCapture`. |
| `src/TabLibrary.cs` | `Poe2StashPricer.Core/Tabs/TabProfile.cs`, `Tabs/TabLibrary.cs` | done | Split: `TabProfile` gets its own file. |
| `src/TabResults.cs` | `Poe2StashPricer.Core/Tabs/ResultStore.cs` | done | Renamed after its main type; also holds `SavedItem`, `TabResult`, `PricedItem`. |
| `src/ItemParser.cs` | `Poe2StashPricer.Core/Pricing/ItemParser.cs` | done | Namespace rename only. |
| `src/PriceService.cs` | `Poe2StashPricer.Core/Pricing/PriceTable.cs`, `Pricing/PriceService.cs` | done | Split: `PriceInfo` and `PriceTable` get their own file. |
| `src/Settings.cs` | `Poe2StashPricer.Core/Storage/AppSettings.cs`, `Storage/AppPaths.cs` | done | Split: the data folder moves to `AppPaths`. |
| `src/Log.cs` | `Poe2StashPricer.Core/Storage/Log.cs` | done | |
| `src/Native.cs` | `Poe2StashPricer.Core/Platform/*.cs` + `Poe2StashPricer.App/Platform/Linux/` | done | Not ported as a file: replaced by interfaces and their Linux implementations. |
| `src/Hotkeys.cs` | `Poe2StashPricer.Core/Storage/Hotkey.cs` | done | Only the neutral key representation is kept; the WinForms `Keys` helpers and `KeyCaptureForm` are dropped. |
| `src/MainForm.cs` | `Poe2StashPricer.Core/Session/` + Avalonia main window | part | Its orchestration is ported (`PricerSession` and the pieces around it); the window itself is Phase 4. |
| `src/OverlayForm.cs` | Avalonia or layer-shell overlay | later | Phase 4. |
| `src/Theme.cs` | Avalonia styles | later | Phase 4. |
| `src/Program.cs` | `Poe2StashPricer.App/Program.cs` | part | Rewritten for Avalonia; no single-instance guard or updater. |
| `src/Updater.cs` | — | no | In-app self-update from GitHub releases; not wanted here. |
| `src/app.manifest`, `src/app.ico` | — | no | Windows-specific. |
| `build.ps1` | — | no | Replaced by `dotnet build`. |
| `.github/workflows/build.yml` | — | no | Windows build and release workflow. |
| — (new) | `Poe2StashPricer.Core/Storage/Json.cs` | done | The shared `System.Text.Json` options, in place of upstream's per-file `new JavaScriptSerializer()`. |
| — (new) | `Poe2StashPricer.Core/Session/` | done | What `MainForm` did minus the window: `PricerSession`, `StashModel`, `StashWatcher`, `HoverPricer`, `ScanRunner`, `PriceFeed`, `OverlayContent`, `Money`, `Rows`. |
| `tools/` | — | no | `DetectTest`, `LayoutTool`, `make-icon.ps1`, `make-layouts.ps1`: development tools, not ported. |

## How the Linux platform layer answers each Win32 call

Measured against the running game on Hyprland; see the notes below for what is not settled.

| Upstream | Here | Notes |
|---|---|---|
| `FindGameWindow`, `IsGameWindow` | `_NET_CLIENT_LIST` + `WM_CLASS` | Under Proton the game is an XWayland client called `steam_app_<id>`. Class and title are settings. |
| `ClientRectOnScreen` | `XGetWindowAttributes` + `XTranslateCoordinates` | |
| `GetForegroundWindow` | `_NET_ACTIVE_WINDOW` on the root | |
| `SetForegroundWindow`, `ShowWindow` | — | No neutral equivalent: Hyprland ignores the EWMH `_NET_ACTIVE_WINDOW` request. The app reports that the game is not in front instead of raising it. |
| `Graphics.CopyFromScreen` | `XGetImage` on the game window | 26 ms for 3440x1440. Reading the root fails with BadMatch (Xwayland is rootless), so only the game is ever in the picture and the overlay cannot contaminate it. XComposite was measured and returns byte-identical pixels, so it is not used. |
| `SendInput` (mouse) | uinput absolute pointer | Axes span a constant 0..65535 and each move is scaled against the desktop's current size, as upstream did with `SM_CXVIRTUALSCREEN`. |
| `SendInput` (Ctrl+C) | uinput keyboard | |
| `GetAsyncKeyState` | `XQueryKeymap`, `XQueryPointer` | Both stay correct while the game has focus. |
| `GetClipboardSequenceNumber` | data-control `selection` events | A real counter, so a repeated identical copy is still seen. |
| `Clipboard.GetText` | the data-control offer, read through a pipe | |
| `Clipboard.SetDataObject` with the history hints | a data-control source, offering `x-kde-passwordManagerHint` | Clipboard managers skip an offer carrying that type, which is how "keep this out of the history" is said here. |
| `SHQueryUserNotificationState` | — | Dropped with the exclusive-fullscreen concept. |

### Why the clipboard goes through Wayland and not X

The obvious route was X11: the game is an XWayland client, so the app could own the `CLIPBOARD` selection
like any other X client. That was written first and it does work between X clients, but it is the wrong
layer on a Wayland desktop. The compositor mirrors what a *mapped, focused* X window copies into the Wayland
clipboard, and the game qualifies; a 1x1 helper window like ours does not. So our selection stayed invisible
to `wl-paste`, and merely taking it cleared what Wayland programs had copied. A scan destroyed the user's
clipboard instead of putting it back.

The data-control protocol is the right layer: it is what a clipboard manager uses, so it reads and sets the
selection without the app ever holding focus. Measured on Hyprland, the compositor mirrors every one of the
game's copies into it, so one connection covers both the game's Ctrl+C and the user's own clipboard, and the
answer arrives as quickly as XFixes did (13 ms median against 15 ms).

It exists twice with the same shape: `ext-data-control-v1`, the standardised version, and
`zwlr-data-control-unstable-v1`, the wlroots one it grew out of. Both are described in
`Platform/Linux/DataControlProtocol.cs` and whichever the compositor offers is used, `ext-` first. One
wrinkle: `primary_selection` is a device event from version 1 in `ext-` but only from version 2 in the
wlroots protocol, and declaring too few events makes libwayland drop the connection.

There is no code generator here, so the protocol tables libwayland expects are built by hand in unmanaged
memory (`Platform/Linux/WaylandInterop.cs`). Requests go through `wl_proxy_marshal_array_flags`, which takes
its arguments as an array; the variadic entry point the C headers use cannot be called safely from .NET.

`Platform/Linux/X11Clipboard.cs` is kept as the fallback for a plain X session or a compositor without the
protocol. There the user's clipboard cannot be put back, and the app says so.

A selection belongs to a running program on Wayland just as on X: when the app exits, what it put on the
clipboard goes with it unless a clipboard manager has taken a copy. That is normal for every application.

### The overlay is an ordinary X11 window

Drawing over the game was the part with two possible answers: an Avalonia window shaped to take no clicks,
or a native layer-shell surface rendered by hand. The first works, measured on Hyprland with the game in
front: it stays above the game, takes no focus from it, passes every click through (an empty `ShapeInput`
region), and with the compositor rules below leaves the picture behind it untouched. So the layer-shell
route is not needed, and the app keeps one renderer instead of two.

### Asking the compositor to leave the overlay alone

The overlay has to sit exactly over the stash, which means the compositor must not place it. A generic rule
in the user's own config is enough to break that - CachyOS's Hyprland defaults centre every floating window,
which put the overlay in the middle of the screen - so the app says `center = false` and
`persistent_size = false` for its own window alongside the rest.

Marking the window override-redirect, which is how an X11 overlay normally escapes the window manager
altogether, did not work here: Avalonia has already created and mapped the window by the time the attribute
can be set, and its hide/show does not remap it. Worth revisiting if the overlay ever moves to layer-shell.


There is no Wayland protocol for "do not blur behind me", and a compositor with blur on will blur the game
behind the overlay's transparent parts. On Hyprland the app asks over the compositor's IPC socket instead of
asking the user to edit a config file (`App/Platform/Hyprland/HyprlandRules.cs`), which is the one place
compositor-specific code earns its keep. It is optional, scoped to the app's own window class, and lasts
only until the compositor reloads. KWin will need its own answer when KDE is tested.

## Deliberate behaviour changes

Recorded here so they are not mistaken for porting bugs when comparing with upstream.

- No exclusive-fullscreen concept. `SHQueryUserNotificationState` has no Linux equivalent; a black capture is
  reported as "the capture was denied or came back black" instead of blaming exclusive fullscreen.
- No "must run on an STA thread" requirement: scans run on a background `Task`.
- `ResultStore.Load` no longer converts timestamps with `ToLocalTime()`; that worked around
  `JavaScriptSerializer` returning UTC `/Date()/` values.
- `TabLibrary` no longer touches the per-tab PNG screenshots that upstream versions before 1.2 left behind:
  neither the item-mask migration in `LoadLearned` nor the deletions in `Save`, `Delete` and `DeleteAll`.
- `AppSettings.ScanKey`/`OverlayKey` were WinForms `Keys` integers; they are now a neutral `Hotkey` record.
- `AppSettings` gained `GameWindowClass` and `GameWindowTitle`: upstream matched the process name, which says
  nothing about an X window.
- `ScanConfig.HotkeyVk` is gone: which key started the scan is the `IKeyState` implementation's business
  (`ScanTriggerHeld`), not the scanner's.
- poe.ninja rate limiting is detected from `HttpResponseMessage.StatusCode` (429/503) and
  `Headers.RetryAfter` instead of `WebException`.
- The watcher does not drop a recognised tab on the first look that fails to recognise it: the game draws a
  tab over several frames, so the first capture after it returns to the front can be too dark to match, and
  upstream's behaviour there made the overlay blink off and on at every alt-tab. Three looks in a row must
  fail, which is the same idea as the retries upstream already uses in `RecheckFrame`.
- `TabLibrary.Identify` accepts a built-in layout twice as close as the next one, even past upstream's 0.15
  limit (up to 0.5). Upstream measured that limit at 1080p, where the same tab scores 0.00 to 0.09. On other
  screens it does not hold: at 3440x1440 Currency scores 0.17 and Essence 0.31, and both were rejected
  outright while leading the runner-up by 0.40 and 0.48. How far off a picture is does not carry across
  resolutions; how far ahead of the others it is does.

  The ratio is the conservative part. In the look-alike families the runner-up is a sibling layout only 0.10
  (Runes / Kalguuran Runes) to 0.24 apart, so it is never twice as far, and there the rule declines to match
  rather than matching the wrong one - which leaves the tab unrecognised, the safe way to be wrong.
- Upstream refused a poe.ninja answer over 64 MB while reading it into a string. There is no such cap here: the
  answer is parsed straight off the response stream, and `HttpClient.Timeout` bounds how long that can take.
- The poe.ninja user agent reports this port's name and version, not upstream's.
