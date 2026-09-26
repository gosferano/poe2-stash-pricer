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
| `src/MainForm.cs` | Core session controller + Avalonia main window | later | Phase 3/4: orchestration moves to Core, UI to Avalonia. |
| `src/OverlayForm.cs` | Avalonia or layer-shell overlay | later | Phase 4. |
| `src/Theme.cs` | Avalonia styles | later | Phase 4. |
| `src/Program.cs` | `Poe2StashPricer.App/Program.cs` | part | Rewritten for Avalonia; no single-instance guard or updater. |
| `src/Updater.cs` | — | no | In-app self-update from GitHub releases; not wanted here. |
| `src/app.manifest`, `src/app.ico` | — | no | Windows-specific. |
| `build.ps1` | — | no | Replaced by `dotnet build`. |
| `.github/workflows/build.yml` | — | no | Windows build and release workflow. |
| — (new) | `Poe2StashPricer.Core/Storage/Json.cs` | done | The shared `System.Text.Json` options, in place of upstream's per-file `new JavaScriptSerializer()`. |
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
| `GetClipboardSequenceNumber` | XFixes selection-owner notifications | A real counter, so a repeated identical copy is still seen. |
| `Clipboard.GetText` | `XConvertSelection` for `UTF8_STRING` | |
| `Clipboard.SetDataObject` with the history hints | owning `CLIPBOARD` and answering requests, offering `x-kde-passwordManagerHint` | Works between X clients. It does **not** reach Wayland applications; see below. |
| `SHQueryUserNotificationState` | — | Dropped with the exclusive-fullscreen concept. |

### The clipboard does not cross to Wayland

The game and this app are both X clients, so the scan reads what the game copies without any bridge. But on
this Hyprland session the XWayland clipboard is not mirrored to the Wayland one in either direction: a text
put on the Wayland clipboard with `wl-copy` cannot be read through X, and a selection this app owns is
invisible to `wl-paste`. Worse, taking the X selection clears whatever Wayland applications had copied.

So a scan currently destroys the user's clipboard rather than restoring it. Reading the game is unaffected.
Fixing it needs the Wayland side (`wlr-data-control` / `ext-data-control`, which `wl-clipboard` itself uses
and both Hyprland and KWin implement); that is not written yet.

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
- Upstream refused a poe.ninja answer over 64 MB while reading it into a string. There is no such cap here: the
  answer is parsed straight off the response stream, and `HttpClient.Timeout` bounds how long that can take.
- The poe.ninja user agent reports this port's name and version, not upstream's.
