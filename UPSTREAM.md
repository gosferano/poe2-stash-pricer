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
- **Structure.** Ported files keep upstream's class names, logic and comments. Nothing is reformatted or
  modernised that does not have to change, and `<Nullable>` is not enabled for ported code.
- **Platform calls.** Everything that touched Win32 (`Native.*`), WinForms or `System.Drawing.Bitmap` now goes
  through interfaces in `Poe2StashPricer.Core/Platform/`, implemented in the app project under `Platform/`.
- **Images.** `System.Drawing.Rectangle/Point/Size` are kept (they come from `System.Drawing.Primitives` and work
  on Linux). `Bitmap`, `Graphics` and `LockBits` are replaced: `PixelBuffer` is the only image boundary and is
  built either from raw BGRA bytes or from a SixLabors.ImageSharp image.
- **JSON.** `System.Web.Script.Serialization.JavaScriptSerializer` is replaced with `System.Text.Json`
  (`PropertyNameCaseInsensitive = true`); `[ScriptIgnore]` becomes `[JsonIgnore]`.
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
| `src/TabWatcher.cs` | `Poe2StashPricer.Core/Detection/TabWatcher.cs` | later | |
| `src/Scanner.cs` | `Poe2StashPricer.Core/Detection/Grid.cs`, `Scanning/{Scanner,ScanConfig,ScanPlan,ProbeGroup,ScanItem,ScanResult}.cs` | later | Split by type; `Grid.Capture` moves behind `IScreenCapture`. |
| `src/TabLibrary.cs` | `Poe2StashPricer.Core/Tabs/TabProfile.cs`, `Tabs/TabLibrary.cs` | done | Split: `TabProfile` gets its own file. |
| `src/TabResults.cs` | `Poe2StashPricer.Core/Tabs/ResultStore.cs` | later | Renamed after its main type; also holds `SavedItem`, `TabResult`, `PricedItem`. |
| `src/ItemParser.cs` | `Poe2StashPricer.Core/Pricing/ItemParser.cs` | done | Namespace rename only. |
| `src/PriceService.cs` | `Poe2StashPricer.Core/Pricing/PriceTable.cs`, `Pricing/PriceService.cs` | done | Split: `PriceInfo` and `PriceTable` get their own file. |
| `src/Settings.cs` | `Poe2StashPricer.Core/Storage/AppSettings.cs`, `Storage/AppPaths.cs` | done | Split: the data folder moves to `AppPaths`. |
| `src/Log.cs` | `Poe2StashPricer.Core/Storage/Log.cs` | done | |
| `src/Native.cs` | `Poe2StashPricer.Core/Platform/*.cs` + app `Platform/` | part | Not ported as a file: replaced by interfaces (done) and their Linux implementations (Phase 2). |
| `src/Hotkeys.cs` | `Poe2StashPricer.Core/Storage/Hotkey.cs` | done | Only the neutral key representation is kept; the WinForms `Keys` helpers and `KeyCaptureForm` are dropped. |
| `src/MainForm.cs` | Core session controller + Avalonia main window | later | Phase 3/4: orchestration moves to Core, UI to Avalonia. |
| `src/OverlayForm.cs` | Avalonia or layer-shell overlay | later | Phase 4. |
| `src/Theme.cs` | Avalonia styles | later | Phase 4. |
| `src/Program.cs` | `Poe2StashPricer.App/Program.cs` | later | Rewritten for Avalonia; no single-instance mutex or updater yet. |
| `src/Updater.cs` | — | no | In-app self-update from GitHub releases; not wanted here. |
| `src/app.manifest`, `src/app.ico` | — | no | Windows-specific. |
| `build.ps1` | — | no | Replaced by `dotnet build`. |
| `.github/workflows/build.yml` | — | no | Windows build and release workflow. |
| — (new) | `Poe2StashPricer.Core/Storage/Json.cs` | done | The shared `System.Text.Json` options, in place of upstream's per-file `new JavaScriptSerializer()`. |
| `tools/` | — | no | `DetectTest`, `LayoutTool`, `make-icon.ps1`, `make-layouts.ps1`: development tools, not ported. |

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
- poe.ninja rate limiting is detected from `HttpResponseMessage.StatusCode` (429/503) and
  `Headers.RetryAfter` instead of `WebException`.
- The poe.ninja user agent reports this port's name and version, not upstream's.
