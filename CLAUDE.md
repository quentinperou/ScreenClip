# CLAUDE.md

Guidance for Claude Code and contributors working on this repository. `README.md` (English) and
`README.fr.md` (French) describe the project for users.

## The project

**ScreenClip**: a small open-source (MIT) Windows 11 screenshot app, resident in the notification area, in the
spirit of CleanShot X, ScreenSnap and shots.so: capture with one key, then (to come) export the capture with a nice
layout (background, rounded corners, shadow, fake macOS-style browser or window). UI in English and French,
light/dark/system theme. The name “ScreenClip” is never translated. Author: [QuentinPerou](https://github.com/quentinperou).
Repository: https://github.com/quentinperou/ScreenClip (to be created; the author commits and publishes himself).
Sister projects with the same stack and conventions: CropPdf, PageCompose, FolderTools (their CLAUDE.md files hold
the history of the shared pieces: updater, installer, Fluent theme, localization). Credits wording: “Developed by
QuentinPerou, with Claude Code (Anthropic)”.

**Talk to the author in French.** Code, comments, commit messages and these docs are in English.

**Performance comes first** (asked by the author): the app stays resident all day, so at rest it must cost nothing.
No window, no timer, no polling while waiting; the Fluent theme and every window are loaded only when first needed;
bitmaps are dropped and the working set trimmed after each capture (`MemoryTrim`). Measure a feature that stays
resident (Task Manager, private working set) before and after it.

## Project status (October 19, 2026)

**Version 0.1.0, not released.** Local git repository (branch `main`) created on October 19, 2026 with a first commit
made in the author's name (`QuentinPerou`, his GitHub noreply address, no Co-Authored-By line, as he asked); **nothing
is pushed: the author publishes himself** (no remote yet). Framing done with the
author on October 19, 2026 (questions, then an HTML mock-up validated by him: editor, area selection, thumbnail, tray
menu, settings, icon):

- trigger: resident app in the notification area + global shortcuts, everything on Print Screen (validated):
  area `PrintScreen`, window `Alt+PrintScreen`, full screen `Ctrl+PrintScreen`, last area again `Shift+PrintScreen`;
- after a capture: a floating thumbnail (CleanShot-style), bottom left; the editor opens only on demand;
- v1 scope: **layout editor** (background, padding, rounded corners, shadow, ratio, 3D tilt, styles) and **fake
  windows** (macOS-style browser, macOS window, Windows 11 window, light/dark). Later: annotations, video/GIF, device
  mock-ups. Uses: README screenshots, social posts, tutorials, slides;
- the Print Screen key: at first start a window offers to turn off Windows' “Use the Print screen key to open screen
  capture” for the user; Settings → Capture can restore it (both asked by the author); the uninstaller restores it
  if ScreenClip took it;
- icon: violet → pink gradient (#7B61FF → #E5489A), white capture corners around a plain rounded window (the author
  had the traffic-light dots of the mock-up removed).

**0.1.0 does**: tray icon + menu, the four shortcuts (changeable, conflicts checked), frozen-screen selection with one
overlay per monitor (area with magnifier, Shift square, Space move; window; screen), thumbnail (copy, save/show,
open, pin, drag to an app, auto-hide, stacked, excluded from captures), copy (bitmap + PNG), automatic save, settings
(General, Capture, Saving, Updates, About), Print Screen question and restore, start with Windows, single instance,
updates from GitHub Releases. 90 unit tests pass; `scripts\build.ps1` builds the app and the installer.

**Checked by Claude on the author's PC** (3 monitors, Windows 11 26200, dark theme), without mouse or keyboard input:
the Print Screen question window, Settings (General, Capture), the area overlay on all monitors (posted `WM_HOTKEY`,
cancelled through UI Automation), the thumbnail of a repeated area, WM_CLOSE from outside (Restart Manager path),
memory at rest (~5 MB working set after trim, 0 CPU). **Not checked yet**: dragging and clicking in the overlay, the
hover buttons and drag-and-drop of the thumbnail, changing a shortcut, the Print Screen takeover (registry writes
from Claude's sandbox are not seen by other processes), the installer and the update, light theme.

Then, the same day, after the author's first tries: the **notification menu did not open** (it closed as soon as it
opened, see Technical choices › Tray menu: fixed, checked with the app brought to the front by UI Automation, and the
author's own click opened it); the **taskbar icon** looked heavy (the small sizes had square, thick corners: redrawn
with the proportions of the large design, 30 and 36 px added); the **README picture** was made (see README
pictures below).

Then the **layout editor** (asked by the author: started by hand, ScreenClip opens it, with “Import a picture”), as in
the mock-up: styles (4 presets + Reset), 13 backgrounds (8 gradients, blurred capture, white, black, transparent, the
user's picture), frames (none, macOS-style browser with an address, macOS window, Windows 11 window with a title,
light/dark), margin / corners / shadow sliders with their values, tilt left/right, ratio (auto, 16:9, 4:3, 1:1, 4:5,
9:16), PNG/JPG/WebP; copy, save as, drag the preview out; import, paste, drop a picture; “New capture” from the
editor; the thumbnail's top right button and double click open the capture in it; “Lay out a picture…” in the tray
menu; `ScreenClip.exe <picture>` opens it (also through the running instance). Checked by Claude through UI
Automation (styles, frames, backgrounds, tilt, ratio, New capture → cancel → back), not the clicks, copy, save
dialog and drags. 109 tests. The README pictures now show the editor.

After his tries of the editor, the author asked for: a clearer “blurred capture” swatch (it now shows the capture
blurred), the frame rows hidden when they do not apply, a tilt by hand (two sliders), the Settings button at the far
right of the bar (as in Crop PDF), “Save” without “…”, “Presets” instead of “Styles” (as in PageCompose), corners up
to 80 px, the app icon in the empty editor, and a laptop and a phone among the frames: all done, 116 tests.

Then **element detection in Area mode** (asked by the author, “go”): on hover, the UI element under the pointer (a
YouTube player, a card of a page, a panel…) is outlined and a click captures it; the mouse wheel goes to the larger /
smaller element; dragging still makes a free area; apps that expose nothing fall back to the window (see Technical
choices › Elements under the pointer). Probed on Chrome without input (YouTube player and a page card found, ~130–150
ms a lookup); the hover itself is for the author to try. Also asked by the author: a **Keyboard shortcuts** switch in
Settings › Capture (off, ScreenClip registers no key at all; the notification menu still captures), and device
screens that keep **real proportions** (an odd capture gave an odd device). 129 tests.

The author's tries: still only the whole page outlined in Chrome, and “even if the picture has to be analysed,
find a solution”: the page window walk, the tool-window overlays and the blocks seen on the picture (see Technical
choices › Elements under the pointer); captures made from the editor are now copied and saved as the settings say
(only the thumbnail gives way to the editor). Then: the thumbnail's corner buttons were black on the dark theme
(their style is not based on the theme's button: `Foreground` set), and a setting for the thumbnail's screen. 137
tests.

Then the author's **own presets** (“on enchaîne”): **+ Save** tile after the built-in ones, a name asked in a popup
under the tile (“My style 1” proposed, names unique, built-in ones included), right click: rename, replace with the
current style, delete (confirmed). Every preset tile is now drawn by the composer on a small made-up app. Checked by
Claude through UI Automation (save, chosen, switch and back), not the right-click menu. 146 tests.

Then: the README preset has no frame (asked by the author), and a README picture of the area selection (see README
pictures), which showed that moving inside the first element found never looked again (fixed: 8 px). A window
frame now takes the corners of the real window (Windows 8 px, macOS and browser 10). A left click on the notification
icon captures an area (the menu is on the right click), with the choice in Settings › General (capture, menu,
editor). In the empty editor, “Capture an area” is now the accent button, before “Import a picture” (the author's
question; people who open the editor by hand mostly do not know the shortcut yet). The captures now go to Windows'
Screenshots folder and the editor's pictures to `Pictures\ScreenClip`, two settings (asked by the author). 151 tests.

**Next**: the author tries the presets; then annotations, video/GIF.

## Commands

```powershell
.\scripts\build.ps1                       # tests + dist\app (ReadyToRun) + dist\ScreenClip-Setup-<version>.exe
dotnet test tests\ScreenClip.Tests        # unit tests (Core + localization)
dotnet run --project src\ScreenClip       # run (opens the editor, after the Print Screen question at first start)
ScreenClip.exe <picture>                  # opens the picture in the editor (through the running instance if any)
powershell -File scripts\make-icons.ps1   # regenerate the icons (Windows PowerShell 5.1)
```

Environment variables: `SCREENCLIP_LANG=en|fr`, `SCREENCLIP_THEME=Light|Dark`, `SCREENCLIP_CAPTURABLE=1` (thumbnails
stay in screenshots), `SCREENCLIP_TRACE=1` (each element lookup written to `%TEMP%\ScreenClip-trace.txt`),
`SCREENCLIP_DEMO_SCREEN=<picture>` (a capture freezes that picture instead of the screen: README pictures). Settings live in `%LOCALAPPDATA%\ScreenClip\settings.json`: delete it to see the first start
again (and the `HKCU\Software\ScreenClip` key).

## Working on the project

- **Memory of Claude** for this project is in `.local/claude/memory/` (git-ignored, read `MEMORY.md` there first);
  personal notes go in `CLAUDE.local.md` or `.local/` (git-ignored), not in this file.
- The author may be using the PC while work is in progress: drive the app through UI Automation or posted window
  messages, not by sending input to the screen. A posted `WM_HOTKEY` (wParam = `ShortcutAction` + 1) to the hidden
  window titled “ScreenClip” (class `HwndWrapper[ScreenClip;…]`, not visible) starts a capture; `WM_CLOSE` to it quits
  the app. The overlay's cancel button is found by UI Automation (its name: “Cancel (Esc)”/“Annuler (Échap)”).
  Screenshots: `PrintWindow` (`PW_RENDERFULLCONTENT`) for normal windows; thumbnails are excluded from captures
  unless `SCREENCLIP_CAPTURABLE=1`.
- Clean up after a test on the author's PC: quit the app, delete a test `settings.json`, never leave the Print Screen
  setting changed.
- **Claude Code's sandbox**: registry writes made from its shell (and by processes it starts) are not seen by
  processes started outside it. Test the Print Screen takeover and the Run key through the installed app, or ask the
  author.
- Docs (both READMEs, this file) are kept up to date at the end of each work session, “Project status” included.
- The notification menu cannot be opened from a script unless the app is in front: bring it there first with UI
  Automation (`SetFocus` on the editor or Settings window), then post the icon's callback message (`WM_APP + 1`,
  lParam `NIN_SELECT` = 0x400) to the hidden window. The editor's controls are reached by their accessible names
  (styles, backgrounds, frames, segments: `SelectionItemPattern`; buttons: `InvokePattern`); its “New capture” menu
  is a popup window of its own.

### README pictures

`.github/screenshot.png` (English) and `.github/screenshot.fr.png` (French), 1560 × 1000, at the top of the READMEs:
the real editor (laying out a capture of the Settings window, `.local\readme\pieces\demo-<lang>.png`, on the “ocean”
background, no frame), the notification menu and the capture bar, light theme, on the gradient of the icon with
rounded corners and shadows. The author first had the Settings window there, then asked for the editor in its place.
The test `settings.json` of the script also holds the demo style; it is removed afterwards. Made by `.local\readme\capture-pieces.ps1`
(UI Automation and posted messages, fictitious save folder `D:\Captures`) then `.local\readme\compose.ps1`
(System.Drawing); `.local\README.md` lists the local tools. Retake them when the UI changes.

`.github/selection.png` / `selection.fr.png` (1600 × 951, under “Capture” in the READMEs; asked by the author from the
mock-up, made from the real overlay instead since the mock-up's had handles, a delay and keys the app does not have):
the real area overlay over a made-up desktop, a sheet of prints outlined. `.local
eadme\scene\scene.html` (`?lang=fr`)
rendered by headless Chrome to `scene-<lang>.png`; `.local
eadme\shoot-selection.ps1 -Lang en|fr` starts the dev
build with `SCREENCLIP_DEMO_SCREEN` (that picture frozen on every monitor, one handle-less window per monitor: the
blocks come from the picture), posts `WM_HOTKEY` then `WM_MOUSEMOVE` at a point to the overlay (WPF takes the posted
position), `PrintWindow`s it and quits; `.local
eadmerame` (a console on Core) lays it out with `Composer`
(violet, 5 %, corners 14, shadow 55, 1600 px wide).

## Architecture

| Folder | Role |
|--------|------|
| `src/ScreenClip.Core` | Pure, tested logic, no UI text: `Hotkey` (shortcut model, text form, `IsAllowed`), `Selection` (drag rectangle, square, move, click vs drag, window under a point) + `CaptureMode`, `BoxDetector` (blocks drawn on a picture), `ElementChain` (elements and blocks under the pointer as wheel levels), `PixelRect`, `FileNaming` (template, unique path), `PrintScreenKey` (the Windows setting's logic), `SemanticVersion`, `UpdateFeed` (GitHub Releases). `Composition/`: `ClipStyle` (+ `StylePresets`, enums), `Backgrounds`, `ClipLayout` (sizes, pure), `Perspective` (3D tilt: corners + rect-to-quad matrix), `Composer` (SkiaSharp drawing, PNG/JPG/WebP encoding). |
| `src/ScreenClip` | WPF .NET 10. `App` (single instance, startup, lazy theme, language, `OpenEditor`, `OpenSettings`), `TrayMenu`, `EditorWindow`, `CaptureOverlay` (one per monitor), `ClipThumbnail` (+ the stack), `SettingsWindow`, `ShortcutWindow`, `PrintScreenWindow`, `UpdateBar`, `ShortcutText`, `Localization.cs`; `Services/`: `TrayHost` (hidden window: notification icon, `RegisterHotKey`, end of session), `CaptureController` + `CaptureSession` + `Clip`, `ScreenGrabber` (monitors, windows, GDI `BitBlt`), `ElementFinder` (UI Automation, background thread), `ClipActions` (clipboard, PNG, temp files), `Imaging` (WPF ↔ SkiaSharp pictures, files, clipboard), `WindowsSettings` (Print Screen, Run key), `AppSettings`, `SingleInstance`, `MemoryTrim`, `MicaBackdrop`, `Updater`, `NativeMethods`. |
| `installer/ScreenClip.iss` | Inno Setup (English/French): Program Files, Start menu, .NET 10 runtime check, closes the running app, gives Print Screen back, cleans HKCU and temp on uninstall. |
| `scripts/` | `build.ps1`, `make-icons.ps1` (icon; generated files are committed). |
| `.github/workflows/build.yml` | CI: build + tests on every push; on a `v*` tag, publishes the installer in a Release (pre-release when the tag has a `-`). |

### Technical choices (and why)

- **WPF + Fluent** `ThemeMode`, framework-dependent, updates from GitHub Releases (Stable/Beta, SHA-256 check,
  silent install, `/RELAUNCH=1`, started again with `--background`): same base as the sister projects. The author
  asked whether WPF was the best fit: yes (transparent full-screen overlays are easy in WPF, hard in WinUI 3;
  Windows.Graphics.Capture for the video later works from WPF; shared code with the sister apps).
- **Composition: SkiaSharp 3.119.1** (in Core, so the drawing is tested; 4.x was three weeks old when chosen), as
  proposed to the author: one drawing for the preview and the export, perspective for the 3D tilt (WPF 2D has none),
  fast blur/shadows, WebP export (WIC cannot write WebP). Loaded only when the editor first draws: the resident app
  does not carry it. The capture is never resized: the picture grows around it (`ClipLayout`); the margin is a share
  of the capture's mean side; the bar and the corner radius grow with captures over 1600 px (`Scale`, up to 3), so
  that a style looks the same at any DPI. The tilt is a 3D turn
  seen in perspective (the angles of the style), its corners fitted back inside the window's place, drawn with a projective `SKMatrix` from the
  rectangle to that quadrilateral (Heckbert). The blurred background is the capture shrunk to 24 px and enlarged with
  a cubic filter (a strong blur at any size). Measured: a preview in ~5 ms, a 1600 × 900 export in ~16 ms, so the
  preview is drawn again on every change, at the size it is shown, coalesced at `Background` priority.
- **Editor**: the style lives in `AppSettings.EditorStyle` (the last one used, saved when the editor closes). Tiles
  and segments apply on `Checked` (so the keyboard and UI Automation work too), guarded while the controls are synced;
  the “your picture” tile asks for a file when it has none, and again when clicked while already chosen; the
  “blurred capture” tile shows the capture loaded, blurred (asked by the author: the generic swatch was not clear).
  The frame theme and title rows are hidden with no frame (asked by the author). The tilt is two angles in the style
  (`TurnDegrees` −30…30, `TiltDegrees` −20…20, asked by the author: tilt by hand); Left / None / Right are quick
  values (−16°/4°, 0, 16°/4°) shown checked when the sliders match them; a double click on a slider puts it back to 0.
  Corners up to 80 px (asked by the author: 32 was not enough), never more than half the window. Choosing a window
  frame sets the corners of the real window (`ClipStyle.WithFrame` / `NaturalRadius`: Windows 11 8 px, macOS and
  the browser 10; asked by the author); no frame and the devices keep them; presets bring their own.
- **Presets**: `StylePresets` (Core, tested). A preset never carries the frame text (it names the page captured) and
  carries a picture only when it is its background: `ApplyTo` keeps the current text and picture otherwise,
  `Matches` says which tile shows chosen (a built-in one first), `ToSave` is what a user preset keeps. The user's are
  `AppSettings.UserPresets` (`UserPreset`: name + style, tidied on load), saved at once. Each tile is a miniature that
  `Composer` renders (twice the tile size) on a made-up 480 × 300 app (bar, gradient card, lines): built-in and user
  presets look like what they give; a picture background is read small (256 px) for it.
- **Devices** (asked by the author, October 19, 2026): `FrameKind.Laptop` and `Phone`, drawn by `Composer`, no brand.
  `DeviceShape` gives their bezel and the room they take around the capture (`ClipLayout.CaptureX/Y` place the capture
  inside the device's box): laptop = black lid (camera dot) with a metal rim, a base 14 % wider with a thumb notch;
  phone = metal edge, black border, screen corners 8.5 % of the short side (they clip the capture), a camera hole,
  side buttons; upright or sideways from the capture's shape. Light = silver, dark = graphite. Their screen keeps
  real proportions (`DeviceShape.Screen`, asked by the author: an odd capture gave an odd device): laptop 3:2 to 16:9,
  phone 16:9 to 20:9; outside, the capture is cropped (`ClipLayout.Shown`), a tall one keeping its top (where a page
  starts), a wide one its middle. Still never resized. With a device, the
  title row and the corner slider are hidden. Export: copy =
  PNG stream (transparency kept) + a bitmap flattened on white; save = a save dialog in `AppSettings.EffectiveExportFolder` (`Pictures\ScreenClip`), the format
  of the extension chosen becoming the style's; drag = a temp file in the export format + the bitmap. “New capture”
  hides the editor and goes through `CaptureController.RunForEditor` (the capture comes back to the editor instead of
  the thumbnail; it is still copied and saved as the settings say).
- **TFM `net10.0-windows`** without the Windows SDK projection (a large assembly, nothing uses WinRT yet). The video
  capture will need `net10.0-windows10.0.19041.0` (Windows.Graphics.Capture).
- **Resident part**: `TrayHost` is an `HwndSource` that is never shown (top-level `WS_POPUP` + `WS_EX_TOOLWINDOW`, not
  message-only, so that the Restart Manager's `WM_CLOSE` and the end-of-session messages reach it). Notification icon
  through `Shell_NotifyIcon` (`NOTIFYICON_VERSION_4`, re-added on `TaskbarCreated`), no WinForms. The app icon comes
  from the exe resources (id 32512) at the small-icon size. Hotkeys with `MOD_NOREPEAT`; the id is
  `ShortcutAction + 1`. `ShutdownMode=OnExplicitShutdown`. `AppSettings.ShortcutsEnabled` off (Settings › Capture,
  asked by the author) registers none: the shortcut rows are greyed, the Print Screen card hidden, the menu shows no
  keys, and the Print Screen question is not asked.
- **Lazy theme**: `App.xaml` sets no `ThemeMode`; `App.EnsureTheme()` applies it before the first window or menu.
  Styles that use `BasedOn="{StaticResource {x:Type Button}}"` or `AccentButtonStyle` need it applied first.
- **Notification icon clicks**: a left click (`NIN_SELECT`, or `NIN_KEYSELECT` from the keyboard) does what
  `AppSettings.TrayClick` says (Settings › General, asked by the author): an area capture by default (through
  `RunAfterMenu`, so that a flyout of hidden icons is gone), or the menu, or the editor; a right click
  (`WM_CONTEXTMENU`) always opens the menu.
- **Tray menu**: a WPF `ContextMenu` built on each opening. A WPF menu of an app with no active window loses the
  mouse capture as it opens and closes at once (0.1.0 as first written: the author could not open it). So it belongs
  to a 1 × 1 transparent window put under the pointer, shown and activated first (the click on the icon gives the
  app the right to come to the front), `PlacementTarget` of the menu, closed with it. Do **not** activate the menu's
  own popup (`SetForegroundWindow` on it): the host then deactivates and the menu closes. Captures started from the
  menu wait 250 ms (`RunAfterMenu`) so the menu is not in them.
- **Capture**: the whole virtual screen is grabbed with GDI `BitBlt` (`CAPTUREBLT`) the moment the shortcut is pressed
  (frozen screen), plus the visible windows of other processes (front to back, `DWMWA_EXTENDED_FRAME_BOUNDS`, cloaked,
  click-through, `Progman`/`WorkerW` skipped). **One overlay per monitor**, each at its own DPI (sharp on mixed-DPI
  setups); an area cannot span two monitors. The overlay is placed in physical pixels with `SetWindowPos`; on
  `WM_DPICHANGED` the suggested rectangle is replaced by the monitor's own before WPF applies it. Pointer positions
  are turned into pixels with monitor size / window size. The chosen pixels are copied out of the frozen screen
  (`Crop`) so that it can be freed.
- **Elements under the pointer** (Area mode, asked by the author): two sources, merged.
  - **UI Automation** (`ElementFinder`): the tree of the window under the pointer, from its handle
    (`ScreenGrabber.VisibleWindow` keeps it; `ElementFromPoint` would find the overlay, which is on top), down to the
    deepest element holding the point; then the same from the two smallest child windows there, because Chrome's page
    lives in `Chrome_RenderWidgetHostHWND`, whose tree is not always linked to the window's (seen: from the top
    window the walk stops at an empty `View`, from the page window it reaches the document and its blocks). Several
    children can hold the point (Chrome's empty “Intermediate D3D Window” layer): the smallest first, then up to two
    more if that branch ends at once. Raw view, `BoundingRectangle` cached, depth 60, 600 ms at most. Chrome exposes
    its pages only once it believes an assistive tool is there; what decides it was not found (a fresh Chrome stayed
    empty after UIA, MSAA, IAccessible2, focus listening; `--force-renderer-accessibility` fills it), and the author's
    Chrome was sometimes full, sometimes not. When it builds, it is a little after the first request (depth 9, then
    41 a second later), so the session looks under the pointer as soon as it opens, and for 3 s after the first
    lookup into a window the finder looks at the last point again every 250 ms. The overlays are `WS_EX_TOOLWINDOW`:
    Chrome's occlusion check skips tool windows; covered by an ordinary full-screen window it takes its page for hidden.
  - **Blocks seen on the frozen screen** (`BoxDetector`, Core, tested; the author's idea: “even by analysing the
    picture”): lines where two neighbouring pixels differ by 10 or more on a channel, kept when 20 px long or more
    (gaps of 3 allowed), per row and per column (read row by row); a block = a left and a right side passing the
    pointer's row that start and end together (±24 px), and lines across near where they start and end, which reach
    the sides but for the corners (as round as the sides say: square cards side by side stay two). Built once per
    window and monitor in a capture (~15 ms for 1920 × 912, Release), ~1–10 ms a query. It finds panels, cards,
    thumbnails, fields; not what has no visible edge (YouTube's dark player on a dark video: 15 against 8 to 13;
    a contrast of 6 found it no better and filled pictures with blocks), nor pill buttons (no straight side).
  - `ElementChain` (Core, tested) merges them into the levels the wheel goes through: clipped to the window and the
    monitor, a block that crosses an element or another block dropped (a wrong guess), sorted by size, each inside the
    next (±4 px), under 12 px dropped, same edges (±4 px) merged, the window itself as the largest. A new lookup is
    asked when the pointer leaves the smallest one or has moved 8 px since the last lookup (a first look on the
    background of a window gives the whole window: moving onto a card inside must find the card); the level is kept
    when the smallest did not change. `SCREENCLIP_TRACE=1` writes every lookup to `%TEMP%\ScreenClip-trace.txt`
    (measured in the author's Chrome: 37 elements and 7 blocks, 144 ms the first lookup, ~30 ms the next ones).
- **Thumbnail**: layered window (`AllowsTransparency`) with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (never takes the
  focus) and `WDA_EXCLUDEFROMCAPTURE` (not in the next captures). Placed in physical pixels at the bottom left of the
  work area of a monitor, one stack per monitor, the newest at the bottom, at most 4. Which monitor:
  `AppSettings.ThumbnailScreen` (Settings › Capture, asked by the author): the capture's (default), the primary one, or
  the one under the pointer when it appears. Drag and drop gives a file
  (`%TEMP%\ScreenClip`, emptied at each start, or the saved file) and the bitmap, **Copy effect only** (a Move onto a
  folder of the same drive would take the saved file away).
- **Clipboard**: bitmap (`SetImage`) and `"PNG"` stream, retried while another app holds the clipboard.
- **Folders** (asked by the author): the captures (automatic save, Save on the thumbnail, the tray menu's folder) go
  to Windows' own Screenshots folder by default, where Win+Print Screen and the Snipping Tool save
  (`WindowsSettings.ScreenshotsFolder`: `SHGetKnownFolderPath(FOLDERID_Screenshots, KF_FLAG_CREATE)`, since OneDrive
  may move it and Windows gives it its translated name when it creates it); the pictures laid out by the editor go to
  `Pictures\ScreenClip`. Both can be changed in Settings › Saving (`SaveFolder`, `ExportFolder`; null = default).
- **Print Screen**: `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` (missing = on from build 22621,
  off before), written for the user with a `WM_SETTINGCHANGE` broadcast; Windows may apply it only at the next
  sign-in, so the shortcuts are registered again and a refused Print Screen shows “after sign-in”. On this PC,
  `RegisterHotKey(PrintScreen)` succeeded while Windows' setting was on: whether both then react is to be checked
  by the author. `HKCU\Software\ScreenClip\TookPrintScreen` marks a takeover, for the uninstaller.
- **Start with Windows**: `HKCU\…\Run\ScreenClip` = `"<exe>" --background`, written by the app (on by default once,
  only for the installed exe in Program Files: a dev build never registers itself).
- **Single instance**: named mutex + event (`Local\ScreenClip.*`), waited on by the thread pool (no thread); a second
  start writes its arguments to `%TEMP%\ScreenClip-activate.txt` and wakes the first one, which opens its editor
  (with the picture given, if any).
- **Memory**: `ConcurrentGarbageCollection=false`, `PublishReadyToRun=true`; `MemoryTrim.Soon()` (2 s after the app is
  idle with no window) collects and calls `SetProcessWorkingSetSize(-1, -1)`. Measured on 0.1.0 (ReadyToRun build):
  91.8 MB working set just started, 5.3 MB at rest, 88 MB with a thumbnail, 4.7 MB at rest again; with the editor
  (SkiaSharp loaded): 126 MB open on a 940 × 680 picture, 10.7 MB once closed and trimmed (113 MB private); private bytes
  grow from 63 to 97 MB after the first capture (WPF and theme resources, paged out): worth a look later.

### Localization

`Properties/Strings.resx` (English) and `Strings.fr.resx` (French), read through `Strings.Get/Format/Find` and
`{local:Tr Key}`. Keys built at run time: `"Shortcut" + ShortcutAction`, `"Key" + part` (`ShortcutText`: “Maj” for
Shift, “Impr. écran”…), checked by `LocalizationTests.KeysBuiltAtRunTime_Exist`. French strings use no-break spaces
before `: ; ? !` and inside « ». The default file name is a string (`DefaultFileName`), not saved in the settings
while unchanged, so that it follows the language.

## Conventions

- Same as the sister projects: comments say why, in plain English; user-facing texts short and concrete; the
  settings apply at once (no OK button); errors that do not matter (settings file, registry) are swallowed.
- New UI strings go in both `.resx` (the tests fail otherwise).
- `.ps1` and `.iss` files: UTF-8 with BOM, CRLF (Windows PowerShell 5.1 and Inno Setup read BOM-less files as ANSI).

## Known pitfalls

- `CaptureMode` exists in `System.Windows.Input` too: `CaptureOverlay.xaml.cs` aliases the Core one.
- `Path` is ambiguous (`System.IO` is a global using in WPF code): write `System.Windows.Shapes.Path`.
- PrintScreen only sends a key-up to WPF: `ShortcutWindow` reads it in `PreviewKeyUp`. The app's own shortcuts are
  unregistered while a new one is typed.
- A window shown by a process started from a script does not get the foreground (no keyboard focus on the overlay);
  a real hotkey gives the foreground right to the app.

## To do

- Annotations, video/GIF.
- Private bytes after the first capture (see Memory).
- Optional: an area spanning several monitors.
