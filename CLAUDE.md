# CLAUDE.md

Guidance for Claude Code and contributors working on this repository. `README.md` (English) and
`README.fr.md` (French) describe the project for users.

## The project

**ScreenClip**: a small open-source (MIT) Windows 11 screenshot app, resident in the notification area, in the
spirit of CleanShot X, ScreenSnap and shots.so: capture with one key, then (to come) export the capture with a nice
layout (background, rounded corners, shadow, fake macOS-style browser or window). UI in English and French,
light/dark/system theme. The name “ScreenClip” is never translated. Author: [QuentinPerou](https://github.com/quentinperou).
Repository: https://github.com/quentinperou/ScreenClip (public; the author pushes and publishes himself, unless he
asks: he asked Claude to tag and release v0.1.0 and v0.2.0).
Sister projects with the same stack and conventions: CropPdf, PageCompose, FolderTools (their CLAUDE.md files hold
the history of the shared pieces: updater, installer, Fluent theme, localization). Credits wording: “Developed by
QuentinPerou, with Claude Code (Anthropic)”.

**Talk to the author in French.** Code, comments, commit messages and these docs are in English.

**Performance comes first** (asked by the author): the app stays resident all day, so at rest it must cost nothing.
No window, no timer, no polling while waiting; the Fluent theme and every window are loaded only when first needed;
bitmaps are dropped and the working set trimmed after each capture (`MemoryTrim`). Measure a feature that stays
resident (Task Manager, private working set) before and after it.

## Project status (October 26, 2026)

**v0.2.0 released** (October 25, 2026, stable, tag `v0.2.0`: the Edit tab and its annotations, the zoom, the
notification after saving; bilingual release notes, as for 0.1.0). **v0.1.0** (October 19, 2026, stable, tag
`v0.1.0`; release notes bilingual like CropPdf 1.0.0). Commits in
the author's name (`QuentinPerou`, his GitHub noreply address, no Co-Authored-By line, as he asked); **the author
pushes and publishes himself** unless he asks (he asked for the 0.1.0 and 0.2.0 tags and releases). The annotation tools came
after 0.1.0 (proposal B of the mock-up, see below). Framing done with the
author on October 19, 2026 (questions, then an HTML mock-up validated by him: editor, area selection, thumbnail, tray
menu, settings, icon):

- trigger: resident app in the notification area + global shortcuts, everything on Print Screen (validated):
  area `PrintScreen`, window `Alt+PrintScreen`, full screen `Ctrl+PrintScreen`, last area again `Shift+PrintScreen`;
- after a capture: a floating thumbnail (CleanShot-style), bottom left; the editor opens only on demand (later, the
  author asked for the editor to open after every capture by default, the thumbnail when it is off);
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
Screenshots folder and the editor's pictures to `Pictures\ScreenClip`, two settings (asked by the author). Then an **All screens** mode (see Technical choices › Capture), checked on the
demo desktop (every screen lit), not the click itself (WPF ignores posted mouse buttons); the author tried it, and asked for a *Plain* preset and a
shadow that follows the screens, then rounded corners on every visible corner and a blurred background without
holes. “All screens” is now taken at once (no click after its button), and the editor opens after a capture
(`AppSettings.OpenEditorAfterCapture`, on by default, Settings › Capture; the thumbnail rows are greyed while it is
on). A demo desktop (`SCREENCLIP_DEMO_SCREEN`) is never saved automatically (three test captures had landed in the
author's Screenshots folder; moved to the Recycle Bin). The presets' “Reset” is a small button, not a link (asked by
the author). *Plain* is now the first preset, the default style (on the brand's violet) and what Reset gives (the
background kept), the author's idea: the editor opens after every capture. 160 tests.

October 24, 2026, the two **issues** opened by the author on GitHub: #1, a notification when a file is written, as in
PageCompose: done (see Technical choices › Editor), checked through UI Automation on the dev build (Save, the save
dialog's button pressed through Win32, the notification read and hidden after 8 s), not the clicks on its buttons;
#2, video capture and editing: designed in the mock-up only (no code yet). The mock-up (the author's design canvas on
claude.ai) now also has a **pointer tool** (asked by the author: a mouse pointer in a style of one's choice, Windows,
macOS or hand, a size slider, a click halo), a **proposal B** with two tabs, *Édition* (tools in a column, the
capture alone, the tool's options and the list of annotations on the right) and *Mise en forme* (today's editor)
(the author's idea, to compare with proposal A, tools in the command bar), and the **video** boards (recording bar;
editor with a timeline: trim handles, one bar per annotation, a pointer animated from A to B on a curve with a click
at the end; the real pointer recorded apart, so that it can be restyled).

The author chose **proposal B**, and the **annotations** were made (see Technical choices › Annotations): two tabs,
Edit and Layout; twelve tools in a column (select, crop, arrow single / double / curved with a handle to bend it,
rectangle and ellipse outline / filled / spotlight, line, pen, highlighter, text plain / label / bubble, numbered
steps, a mouse pointer Windows / macOS / hand with a size slider and a click halo, hide by pixelating / blurring / a
solid block), eight colors, three sizes, handles, the list of annotations, undo and redo, a letter per tool, and a
zoom (asked by the author after his first tries: buttons, Ctrl + wheel, scroll and drag). Checked by
Claude on the dev build with the Debug script player (`SCREENCLIP_EDIT_SCRIPT`: shapes, curved arrow, text typed in a
bubble, numbers, pointer, highlighter, mask, spotlight, select and move, crop, Layout tab, export, undo) and UI
Automation (tabs, tools, panels), light and dark themes; not with the real mouse and keyboard. 188 tests.

Then the **video**, step 1 of issue #2 (see Technical choices › Video): record an area, a window or a screen with the
sound of the computer and the microphone, then cut, crop, annotate, lay out and export it as MP4 or GIF in the editor.
New packages (Vortice.Direct3D11 / MediaFoundation 3.8.3, NAudio.Wasapi 2.2.1, all MIT), the Windows SDK target. 199
tests. After the author's first look: *Quit ScreenClip* moved from About to the end of General, and a **developer
mode** in About, both as in TextCase (see Technical choices › Developer mode); the recording bar's countdown shows
the number alone (“Recording in 3…” pushed the number out of sight) and the bar is centered again when its width
changes. Checked through UI Automation (both pages, the toggle, the log button, the log written) and the Debug
recording test (the bar during the countdown and the recording). Committed (`61c47b2`, not released).

Then **video step 2** (the author's answers: at the stop with the pointer apart, everything from the editor, nothing
to wait for; a new annotation shows all along; a smoothed movement as an option; and, his own idea, the pointer apart
is a choice: filmed with the screen when the video will not be edited): see Technical choices › Video, step 2. The
capture bar's **Pointer apart** toggle; the pointer recorded apart and redrawn (as it was, Windows, macOS, hand,
hidden; size; real or smoothed; clicks: none, halo, ripple, press); **tracks** under the timeline (a bar per
annotation, moved and stretched; the recorded pointer with its clicks); the **A → B pointer** (drag with the Pointer
tool, or its switch; curve or straight, smooth or steady, a click on arrival; diamonds for when it leaves and
arrives); the export draws every frame at its moment; a question before a recording kept nowhere leaves the editor.
Checked by Claude with the Debug recording test (a made-up pointer way, `PointerRecorder.Fake`; a still window,
`SCREENCLIP_RECORD_STILL=1`) and the script player (tracks, pointer options, A → B, export MP4 and GIF decoded),
light and dark, French and English; the question and the capture bar toggle through UI Automation (the screen frozen
was a demo picture); not with the author's own mouse and screen. 215 tests.

The author's first tries of step 2 (October 26, 2026): playing from the middle threw (“TimeSpan overflowed”: fixed);
**moving the playhead lagged** (“the big flaw”: 220 ms a move on a large video; see Technical choices › Video frames:
now ~20 ms, ~32 frames a second while dragging); shortening a bar moved the playhead (taken out, then asked back
once the playhead was fluid, to see where the layer stops); MP4 or GIF was only in the save dialog's list, and
exporting from the Edit tab applied a layout it did not show: **Save became Export**, a small window (see Technical
choices › Export); the pointer apart is now on by default (he had not seen the toggle); the list of annotations
can be reordered by dragging a row (asked by him: the order of the layers; the tracks follow it; its first version
did nothing: the row's lost capture, bubbling up, ended every drag); the 3, 2, 1 countdown can be skipped (▶ on the
recording's bar) or turned off (Settings › Capture › Video, `AppSettings.RecordCountdown`); the playhead's red ball
is grabbed too (the timeline reaches 10 px above the frames); with the Select tool, the annotation under the pointer
gets a light dashed frame (`_hoveredId`); a blur or pixelate mask now hides the annotations under it too; a *None*
preset comes first, the default and what Reset gives (`StylePresets`: no margin, corners, shadow, frame nor turn;
the capture as it is); the export's progress is centered over the greyed window (`ExportOverlay`); a double click on any slider puts it back
to its default; the notification of a file written is one line at the top right, 4 s (his choice among four shown to
him); an eye on each row of the list hides a layer (`Annotation.Hidden`, `RecordedPointerOptions.Hidden`; the
*Hidden* tile of the recorded pointer gave way to it); the recorded pointer's options start from the defaults at
each new video (no longer remembered); Copy is always a picture, “Copy as PNG” (the author, after a first try with
“Copy picture” / “Copy GIF”), its format in Settings › Saving (`AppSettings.CopyFormat`: PNG, JPG, WebP), the frame
shown for a video; the Export window tells the file's weight (a picture encoded on a worker, an MP4 from its bit
rate) and its cards are opaque (the Fluent flyout brush let the editor show through); the editor's questions and
messages are a Windows 11 style dialog over the greyed window (`EditorWindow.Dialog.cs`; the author: the classic
message box did not belong); the notification menu starts with **Open ScreenClip** (the editor, asked by the
author), and “Lay out a picture…” now asks for the picture; the menu opens inside the work area (it reached into the
taskbar, which sometimes covered its end); the Export window offers MP4, GIF, PNG and JPG for a video (“Image” said
nothing: the author) and its Copy copies what it would save, but an MP4 (hidden then); a preset keeps the file
format (applying one put PNG back, and a JPG made every tile look unchosen); the tracks' scroll bar has a column of
its own right of the timeline (over the bars, it hid their ends; hung in the margin at first, it still did once
widened by the hover, and the tracks, centered in the wider room, were 7 px off the timeline: now left-aligned), and
the playhead's line goes on from the timeline to the tracks without a break. He asked whether a rewrite
in Rust would be faster: no, the time went to how the frames were decoded, the same Media Foundation from any
language. 221 tests. Step 2 and these changes committed together (not released).

**Next**: the author tries the video again (steps 1 and 2); step 3 if needed (sound levels); then a 0.3.0 release
(asked to wait: “not the video version, it does not work” yet).

## Commands

```powershell
.\scripts\build.ps1                       # tests + dist\app (ReadyToRun) + dist\ScreenClip-Setup-<version>.exe
dotnet test tests\ScreenClip.Tests        # unit tests (Core + localization)
dotnet run --project src\ScreenClip       # run (opens the editor, after the Print Screen question at first start)
ScreenClip.exe <picture>                  # opens the picture in the editor (through the running instance if any)
powershell -File scripts\make-icons.ps1   # regenerate the icons (Windows PowerShell 5.1)
```

Environment variables: `SCREENCLIP_LANG=en|fr`, `SCREENCLIP_THEME=Light|Dark`, `SCREENCLIP_CAPTURABLE=1` (thumbnails
and the recording's bar and line stay in screenshots, as in developer mode), `SCREENCLIP_TRACE=1` (each element lookup written to `%TEMP%\ScreenClip-trace.txt`),
`SCREENCLIP_DEMO_SCREEN=<picture>` (a capture freezes that picture instead of the screen: README pictures),
`SCREENCLIP_EDIT_SCRIPT=<file>` (Debug builds only: plays pointer actions in the Edit tab, see
`EditorWindow.Script.cs`; writes `<file>.done` at the end), `SCREENCLIP_RECORD_TEST=<seconds>` (Debug builds only:
records a moving window of its own through the real recording, the pointer apart following a made-up way, then opens
it in the editor; `SCREENCLIP_RECORD_STILL=1`: the window stops after a second; `SCREENCLIP_RECORD_SIZE=1800x1000` and
`SCREENCLIP_RECORD_PICTURE=<file>`: a large window whose whole picture moves, the heaviest to decode),
`SCREENCLIP_INSTANCE=<name>` and `SCREENCLIP_SETTINGS=<file>` (Debug builds only: a second ScreenClip beside the one in
use, with its own single-instance names, temp folder and settings file: tests never touch the author's app nor his
settings; build it apart with `dotnet build src\ScreenClip -o <folder>`, since his dev build locks `bin`). Settings live in `%LOCALAPPDATA%\ScreenClip\settings.json`: delete it to see the first start
again (and the `HKCU\Software\ScreenClip` key).

## Working on the project

- **Memory of Claude** for this project is in `.local/claude/memory/` (git-ignored, read `MEMORY.md` there first);
  personal notes go in `CLAUDE.local.md` or `.local/` (git-ignored), not in this file.
- The Debug recording test films a part of the real screen: whatever shows over it (a Windows notification, a
  popup) is filmed too. Keep its window small, never larger than needed, and delete its recordings and the shots of
  them once checked (a 1800 × 1000 test window filmed a Snipping Tool notification of the author's: deleted).
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
The test `settings.json` of the script also holds the demo style; it is removed afterwards (the author's is put
aside and back). Made by `.local\readme\capture-pieces.ps1` (UI Automation and posted messages, fictitious save
folder `D:\Captures`; it takes the Settings window's Capture page as `demo-<lang>.png` first, then lays it out in
the editor; the menu is opened with `WM_CONTEXTMENU`, since a left click on the icon captures) then
`.local\readme\compose.ps1` (System.Drawing); `.local\README.md` lists the local tools. Retake them when the UI
changes (last: October 24, 2026, with the Edit and Layout tabs).

`.github/edit.png` / `edit.fr.png` (1600 × 1068, under the Edit tab in the READMEs): the real Edit tab on the
Settings capture (`pieces\demo-<lang>.png`), annotated by the Debug script player: `.local\readme\edit\edit-<lang>.txt`
played by `.local\readme\edit\script-run.ps1 -Script … -Lang … -Picture … -Shot …` (test settings, `PrintWindow`
of the editor), then laid out by `.local\readme\frame` (1600 px). The coordinates are the capture's pixels; the
French page is taller (its descriptions wrap), so each language has its own script.

`.github/selection.png` / `selection.fr.png` (1600 × 951, under “Capture” in the READMEs; asked by the author from the
mock-up, made from the real overlay instead since the mock-up's had handles, a delay and keys the app does not have):
the real area overlay over a made-up desktop, a sheet of prints outlined. `.local\readme\scene\scene.html` (`?lang=fr`)
rendered by headless Chrome to `scene-<lang>.png`; `.local\readme\shoot-selection.ps1 -Lang en|fr` starts the dev
build with `SCREENCLIP_DEMO_SCREEN` (that picture frozen on every monitor, one handle-less window per monitor: the
blocks come from the picture), posts `WM_HOTKEY` then `WM_MOUSEMOVE` at a point to the overlay (WPF takes the posted
position), `PrintWindow`s it and quits; `.local\readme\frame` (a console on Core) lays it out with `Composer`
(violet, 5 %, corners 14, shadow 55, 1600 px wide).

## Architecture

| Folder | Role |
|--------|------|
| `src/ScreenClip.Core` | Pure, tested logic, no UI text: `Hotkey` (shortcut model, text form, `IsAllowed`), `Selection` (drag rectangle, square, move, click vs drag, window under a point) + `CaptureMode`, `BoxDetector` (blocks drawn on a picture), `ElementChain` (elements and blocks under the pointer as wheel levels), `PixelRect`, `FileNaming` (template, unique path), `PrintScreenKey` (the Windows setting's logic), `SemanticVersion`, `UpdateFeed` (GitHub Releases). `Composition/`: `ClipStyle` (+ `StylePresets`, enums), `Backgrounds`, `ClipLayout` (sizes, pure), `Perspective` (3D tilt: corners + rect-to-quad matrix), `Composer` (SkiaSharp drawing, PNG/JPG/WebP encoding). `Video/`: `GifEncoder` (+ median cut, LZW), `Timecode`, `TrimRange`, `GifPacer`, `PointerTrack` (the pointer recorded apart: positions, shapes, clicks, smoothed way). `Annotations/`: `Annotation` (+ kinds and styles, `Palette`, `AnnotationOptions`, `AnnotationDefaults`, `AnnotationLayer`, `AnnotationHistory`), `AnnotationGeometry` (sizes, bounds, hit tests, handles), `AnnotationRenderer` (SkiaSharp drawing, `Flatten`, pixelation, the recorded pointer and click effects), `VideoTiming` (annotations in time: shown at a moment, the A → B pointer posed, bars moved and stretched, `Fingerprint`). `Typefaces` (Segoe UI for both). |
| `src/ScreenClip` | WPF .NET 10. `App` (single instance, startup, lazy theme, language, `OpenEditor`, `OpenSettings`), `TrayMenu`, `EditorWindow` (+ `EditorWindow.Annotate.cs`: the Edit tab; `EditorWindow.Video.cs`: a video, its timeline and export; `EditorWindow.Tracks.cs`: the tracks under the timeline and the recorded pointer's options; `EditorWindow.Export.cs`: the Export window and Copy; `EditorWindow.Dialog.cs`: its questions and messages; `EditorWindow.Script.cs`: the Debug script player), `RecordingBar`, `RecordingFrame`, `Video/` (`RecordingSession`, `ScreenRecorder`, `PointerRecorder`, `AudioRecorder`, `VideoReader`, `VideoPlayer`, `VideoExporter`, `Direct3D`, `RecordingTest` in Debug), `CaptureOverlay` (one per monitor), `ClipThumbnail` (+ the stack), `SettingsWindow`, `ShortcutWindow`, `PrintScreenWindow`, `UpdateBar`, `ShortcutText`, `Localization.cs`; `Services/`: `TrayHost` (hidden window: notification icon, `RegisterHotKey`, end of session), `CaptureController` + `CaptureSession` + `Clip`, `ScreenGrabber` (monitors, windows, GDI `BitBlt`), `ElementFinder` (UI Automation, background thread), `ClipActions` (clipboard, PNG, temp files), `Imaging` (WPF ↔ SkiaSharp pictures, files, clipboard), `WindowsSettings` (Print Screen, Run key), `AppSettings`, `SingleInstance`, `MemoryTrim`, `MicaBackdrop`, `Updater`, `NativeMethods`. |
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
- **Annotations** (the Edit tab; proposal B of the mock-up, chosen by the author on October 24, 2026): two tabs in the
  middle of the command bar, Edit and Layout (`AppSettings.EditorTab`, the last one used; Ctrl+1 / Ctrl+2). The
  annotations are records in the capture's pixels (`Core/Annotations`), held by an immutable `AnnotationLayer` (the
  items in drawing order, the crop, the next id); each change is a new layer, kept by `AnnotationHistory` (100 steps;
  changes in a row with the same key, a slider dragged or a nudge, make one step). One drawing, `AnnotationRenderer`
  (SkiaSharp), for the Edit view and the export: the Layout tab lays out `AnnotationRenderer.Flatten` (the capture with
  its annotations, cut to the crop, cached per layer), so both tabs copy and save the same picture. Sizes follow the
  capture as the layout does (`ScaleOf`: 1 up to 1600 px, at most 3). The Edit view scales the capture once per size
  (`_viewBase`) and draws the annotations over it at each change; WPF draws only the selection frame, its handles and
  the crop frame (`Overlay`), and the text box of the text being typed (`TextLayer`; the annotation is hidden
  meanwhile). A drag works on a live layer (`_live`), committed at the end (one step to undo; a number or a pointer
  put and moved at once is one step too). Spotlights darken everything outside them first, so that the other
  annotations stay bright on top; masks hide what is under them, the capture and the annotations before them in the
  list (`AnnotationRenderer.Below`; the author found a blur that left a line drawn under it sharp odd): pixelate = the
  average of each block, blur, a solid block (the only one that surely hides); the highlighter multiplies (black text stays black). The tools' options are kept
  (`AppSettings.Annotations`, `AnnotationDefaults`): a new annotation takes them, and changing the one chosen changes
  them too. The list of annotations (the one on top first) is reordered by dragging a row up or down (a line shows
  where; `AnnotationLayer.MoveTo`; the rows capture the mouse once it moved, so a click still chooses). After a shape
  the tool stays, the shape chosen (its options in the panel, its handles); the pointer,
  number and text tools pick their own kind (a pointer is moved, a text edited). The mouse code is
  `PointerDown` / `PointerMove` / `EndDrag` with explicit points, so that the Debug script player can draw without the
  real mouse (WPF ignores posted buttons, and the author may be using the PC). **Zoom** (asked by the author after his
  first tries): buttons at the bottom right of the picture (out, the percentage = 100 %, in, Fit), Ctrl + wheel
  around the pointer, Ctrl+0 / Ctrl++ / Ctrl+-; 5 % to 800 %, fitted by default (never above 100 %) and again for
  each new picture. Only the part in the window is drawn (`_viewOrigin`: the capture's pixel at its top left; the
  bitmap is never larger than the window): below 100 % from the capture scaled once per zoom, above from the capture
  itself, nearest pixel from 200 % (sharp squares, as in image editors). Zoomed in: scroll bars over the picture's
  edges, the wheel (Shift: sideways), the middle button or Space + drag move it.
- **Presets**: `StylePresets` (Core, tested). A preset never carries the frame text (it names the page captured) and
  carries a picture only when it is its background: `ApplyTo` keeps the current text and picture otherwise,
  `Matches` says which tile shows chosen (a built-in one first), `ToSave` is what a user preset keeps. The user's are
  `AppSettings.UserPresets` (`UserPreset`: name + style, tidied on load), saved at once. Each tile is a miniature that
  `Composer` renders (twice the tile size) on a made-up 480 × 300 app in greys (bar, card, lines; the author: a violet card got lost on violet backgrounds): built-in and user
  presets look like what they give; a picture background is read small (256 px) for it. The *Plain* preset (asked by
  the author) keeps the background chosen: its `Background` is `StylePresets.CurrentBackground`, which `ApplyTo`
  replaces by the current one (so its tile stays chosen whatever the background), and its miniature is drawn again
  on the current background when it changes. The *None* preset (asked by the author, October 26, 2026) keeps it too
  and adds nothing (no margin, corners, shadow, frame nor turn): it comes first and is the default
  (`StylePresets.Default`: none, violet kept for a margin) and what Reset gives (`StylePresets.Reset`: none,
  background kept); *Plain* was until then.
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
  the thumbnail; it is still copied and saved as the settings say). A file written (Save, or a capture saved automatically and
  then opened in the editor) shows a notification at the bottom left of the preview, as in PageCompose (issue #1):
  title, file name and folder, Open and Show in folder; it goes after 8 s unless the mouse is on it
  (`EditorWindow.ShowSaved`). The status line under the preview keeps the short messages (copied, no picture).
- **TFM `net10.0-windows10.0.22621.0`** (since the video): the Windows SDK projection for Windows.Graphics.Capture
  (`IsBorderRequired` needs the 22621 SDK; the app still runs from 19041). Its assembly (and Vortice, NAudio) is only
  loaded when a video is recorded or opened: measured at rest with them in, 4.3 MB working set, none of them loaded.
- **Video** (issue #2, step 1, October 25, 2026; the author's answers: sound of the computer and microphone at once,
  built in steps, Ctrl+Alt+Print Screen, MP4 and GIF): the capture bar's **Video** toggle (`CaptureSession.IsVideo`,
  the overlay's choice of area / window / screen, no “All screens”) starts a `RecordingSession`: a dashed red line
  around the area (`RecordingFrame`, click-through) and a bar under it (`RecordingBar`: time, pause, stop, start
  again, discard; never active, so the app filmed keeps the focus), both excluded from capture
  (`WDA_EXCLUDEFROMCAPTURE`); a 3 s countdown during which the `ScreenRecorder` is made on a worker thread (the first
  Media Foundation start takes ~2 s: made after the countdown, the first videos lost their first seconds). Area and
  window are recorded as the area of their monitor (menus and popups over them are filmed; a window followed by
  Windows.Graphics.Capture would lose its menus): `Direct3D.ForMonitor`, frames only when something changes, at most
  30 a second, the pointer included (`IsCursorCaptureEnabled`; drawn again in step 2), no yellow border, cut on the
  graphics card (`CopySubresourceRegion` into a texture of the area, even sizes for H.264) and given to a Media
  Foundation sink writer with the device manager (H.264 by the graphics card when it can, a key frame every third
  of a second for the editor's seeking, 0.12 bit per pixel and frame), the last frame repeated at the stop when nothing moved.
  Sound: `AudioRecorder` (NAudio 2.2.1: WASAPI loopback and the default communications microphone, resampled and mixed
  to 48 kHz stereo; a thread clocked on the recording hands out 20 ms chunks 120 ms behind, so that both devices'
  packets are in; silence where the loopback sends nothing; paused time left out), AAC 160 kbit/s. Stop (the bar, the
  shortcut, a click on the icon) → `CaptureController.DeliverVideo`: saved (when the captures are) in
  `AppSettings.EffectiveVideoFolder` (Videos\Screen Recordings, Settings › Saving), put on the clipboard as a file,
  opened in the editor (always). The editor: `EditorWindow.Video.cs`; the frame at the playhead becomes `_capture`
  (so the Edit tab, the crop and the Layout tab work on every frame; their caches follow the picture), read by
  `VideoReader` on a worker (seek to the key frame before, decode up to the time; RGB32's fourth byte is set opaque, or
  SkiaSharp copies it as alpha 0 and the frame is invisible); a timeline (strip of frames, yellow trim handles,
  playhead; `TrimRange` in Core); playing with `VideoPlayer` (frames decoded ahead, the sound through WASAPI and its
  position as the clock); Save → MP4 (`VideoExporter`: every frame of the part kept flattened with the annotations,
  laid out by `Composer`, on white, H.264 + the sound cut to the part) or GIF (`GifEncoder` in Core: per-frame median
  cut, only the rectangle that changed, unchanged frames merged; 15 fps, 960 px wide at most) or a picture of the frame
  shown; Copy → the MP4 in the temp folder, on the clipboard as a file. Checked by Claude with a Debug-only test
  (`SCREENCLIP_RECORD_TEST=<seconds>`: a moving window of its own recorded through the real session, then the script
  player in the editor: seek, trim, export MP4 and GIF, play; with and without the sound), the capture bar in Video
  mode through a posted `WM_HOTKEY`; not with the author's own screen, mouse and microphone.
- **Video, step 2** (October 25, 2026, the mock-up's VideoEdit board). **Pointer apart**
  (`AppSettings.RecordPointerApart`, JSON `PointerApart`, the capture bar's toggle; asked by the author: both ways, on
  by default since October 26 so that the pointer can be hidden or restyled without thinking of it): the
  screen is filmed without the pointer (`IsCursorCaptureEnabled` off) and `PointerRecorder` (a thread, ~100 looks a
  second while recording: `GetCursorInfo`, `GetAsyncKeyState`) keeps its tip, its shapes (each new cursor drawn by
  `DrawIconEx` on black and on white: the alpha from the difference; an inverting text beam drawn black outlined
  white) and its buttons as a `PointerTrack` (Core, tested: interpolation that does not glide across a still, clicks,
  a smoothed way: a 70 ms bell average over the whole track, no lag). Such a video is not copied nor saved at the
  stop (the author's choice): the editor makes it (Save, Copy), and asks before a recording kept nowhere leaves it
  (closing, another capture; not when the app quits). Filmed with the screen, as in step 1. The layer keeps the
  recorded pointer's options (`AnnotationLayer.Cursor`: style as it was / Windows / macOS / hand, hidden by the eye
  of its row, size 50–400 %, smoothed, click effect), so that they are undone as the rest; each new video starts from
  the defaults (asked by the author: at first the last ones were kept). **Annotations in time**: `Annotation.Time` (`ShowTime`, null: all along,
  the author's choice), an A → B pointer (`PointerMotion`: from, two handles of a cubic curve or straight, eased or
  steady, leave and arrive times, click effect); `VideoTiming` (Core, tested) poses them at a time, hit-tests what
  shows, moves and stretches bars (a bar stretched to the whole video is all along again). One drawing for the Edit
  view, the Layout tab and the export: `AnnotationRenderer.Flatten(capture, layer, VideoMoment)` and
  `DrawRecordedPointer`; click effects (halo, ripple, press) drawn by Core. The editor's tracks
  (`EditorWindow.Tracks.cs`): the recorded pointer (its clicks as ticks), then a bar per annotation; a bar is moved or
  its ends dragged (the playhead follows the moment being set, the last frame for an end: the author first found it
  in the way while the playhead lagged, then asked for it back to see where a layer stops); the diamonds are the leave
  and arrive times; one undo step per drag. **Playing** redraws the overlay ~60 times a second even when the screen sends no frame (the
  `VideoTiming.Fingerprint` of the moment changed). **Export**: 30 (MP4) or 15 (GIF) steps a second through the part
  kept, a frame written when the video's frame or the fingerprint changed (a still screen with a moving pointer is
  still smooth).
- **Video frames** (the author: “the timeline lags”; measured on a 1800 × 1000 video moving all over). `VideoReader`
  reads the decoder's own NV12 frames, decoded by the graphics card (DXVA: a D3D11 video device and a DXGI device
  manager on the source reader; the processor when there is no card), and turns only the frame shown into RGB, on
  all cores (`YuvToRgb`, BT.709 for HD, video range unless the type says otherwise; checked against Media
  Foundation's own conversion: 1 level at most). Asked a little ahead (up to 1 s), it decodes on from where it is
  instead of going back to the key frame; the frame already shown is not made again (`FrameAt(…, unlessAt)`).
  Recordings put a key frame every third of a second (`ScreenRecorder`, was every second). A move of the playhead:
  220 ms before (Media Foundation turned every decoded frame into RGB), now ~6–11 ms forward, ~20 ms back or
  anywhere; drawing the frame ~12 ms; dragging shows ~32 frames a second. The picture of the Layout tab and the Edit
  view is drawn again while the playhead moves inside one frame when something over it moves with the time.
- **Export** (asked by the author, October 26, 2026, in place of Save: he had not found GIF, and exporting from the
  Edit tab applied a layout it did not show): the **Export ▾** button (its format on it, the mock-up's “Save · MP4”)
  opens a popup (`EditorWindow.Export.cs`): the format (a video: MP4, GIF, or the frame shown as PNG or JPG,
  `AppSettings.VideoFormat` + the style's format, `FrameFormat`; a picture: PNG, JPG, WebP, the style's; presets keep
  it, `StylePresets.ApplyTo`), **Apply the layout** (`AppSettings.ExportLayout`, on by default; off, the
  capture with its annotations alone; the preset applied is named), a preview of what it gives (drawn at its size, as
  the Layout tab draws its own; asked by the author), the size and the weight (a picture encoded on a worker; an MP4
  about, from its bit rate; none for a GIF), then Copy and Save…. The window's Copy copies what Save would write: the
  GIF (a file in the temp folder) or the picture in the format chosen (“Copy the GIF”, “Copy as JPG”); hidden for an
  MP4 (the author). The bar's Copy (and Ctrl+C) always copies a picture (the frame shown, for a video), whatever the
  format of Export, in the format of Settings › Saving (`AppSettings.CopyFormat`, PNG by default; asked by the
  author: “Copy as PNG” on the button); both follow **Apply the layout**. PNG goes on the clipboard as itself (`"PNG"` stream); JPG and WebP have no clipboard format
  of their own, so they go as a file in the temp folder (`FileDrop`: chats, mails, folders take it); a bitmap on white
  goes with both. Ctrl+S uses the Export choices (it opens the save dialog at once). The Layout tab
  has no File row any more. The save dialog starts on the format chosen (a type chosen there becomes it).
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
- **Dialogs** (`EditorWindow.Dialog.cs`, asked by the author: the classic message box was not in the Windows 11
  look): the editor's questions and messages (a recording not saved leaving it, a preset deleted, errors) are a card
  over the greyed window (`DialogOverlay`): title, text, buttons of equal width, the first one accent; Enter takes
  the first, Escape the last; `AskAsync` gives the button's index, the editor's shortcuts wait meanwhile.
- **Tray menu**: **Open ScreenClip** first (the app's icon; the editor as it is), then the captures, “Lay out a
  picture…” (the editor, asking for the picture), the captures folder, Settings, Quit; a WPF `ContextMenu` built on
  each opening, under its host window put at the pointer but inside the work area (`PlacementMode.Bottom`, flipped
  above when there is no room): at the pointer itself, on the icon, it reached into the taskbar, which could cover
  it. A WPF menu of an app with no active window loses the
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
  (`Crop`) so that it can be freed. **All screens** (`CaptureMode.AllScreens`, `ShortcutAction.AllScreens` =
  Ctrl+Shift+PrintScreen, appended last so that the hotkey ids of the others stay; asked by the author): offered with
  several screens only (bar, tray menu, editor's New capture); taken at once, nothing to choose (the author: “two
  steps, one useless”): the shortcut grabs `PixelRect.Union` of the monitors without the overlay, the bar's button
  takes it from what is frozen (`CaptureSession.TakeAllScreens`); `Selection.ClearBetweenScreens` makes transparent
  what no screen shows. It is not a `CaptureMode`. The
  composer draws shadows only outside the window (`ClipPath` difference); a see-through picture (alpha) without a
  frame gets its shadow from what it shows (`SKImageFilter.CreateDropShadowOnly` on a recorded `SKPicture`) and no
  outline (the author found the shadow of the rectangle around the screens odd); in a window frame, what it leaves
  empty is the window's page (white, or dark). A bare picture (no frame, no shadow) has no outline either. Its rounded
  corners follow what it shows (the author: “some corners rounded, others in the middle not”): `Composer.Trace` turns
  its opaque part into rectangles (rows with the same opaque runs make a band; more than 64 rectangles, an icon with
  soft edges, keeps the rectangle around), joined with `SKPath.Op` and rounded with `SKPathEffect.CreateCorner`
  (concave corners only fill a little of the empty part); traced once per picture (`ConditionalWeakTable`). The
  blurred background fills what such a picture leaves empty with its average color, and the background blur is
  `Clamp`ed (with the default, the edges of every exported picture faded to transparent). The size label moves below the capture bar when it would be under it.
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
- **Developer mode** (Settings › About, asked by the author, as in TextCase; `AppSettings.DeveloperMode`, off by
  default): what ScreenClip hides from captures stays visible (`App.CapturableWindows`: the thumbnail, the
  recording's bar and line; they then show in the videos too, the description says so), the thumbnail stays until
  closed, and `DevLog` writes `%LOCALAPPDATA%\ScreenClip\screenclip.log` (cut back to its last half beyond 200 KB;
  nothing written while off; *Open the log* greyed while off): start (version, Windows, screens, refused shortcuts),
  captures (mode, screen, area chosen or cancelled, size, copied / saved), element lookups (only when the result
  changes), recordings (size, sound devices, length, errors), videos opened and exported, update check failures,
  unhandled errors. Never what a capture shows.
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
- WPF takes a posted `WM_MOUSEMOVE` (its position comes from the message) but not posted mouse buttons (it reads the
  real button state): a click on the overlay cannot be faked by messages.
- A window shown by a process started from a script does not get the foreground (no keyboard focus on the overlay);
  a real hotkey gives the foreground right to the app.

## To do

- Video, step 3 if needed: the levels of the sound of the computer and of the microphone.
- Private bytes after the first capture (see Memory).
- Optional: an area spanning several monitors.
- Later (the author's idea, October 24, 2026): a macOS version. Core (SkiaSharp composition, selection, presets) is
  portable; the UI, the capture, the hotkeys and the notification area are Windows code (WPF, GDI, Win32).
