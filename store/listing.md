# Microsoft Store listing

What goes into Partner Center for **ScreenClip** (product identity `QuentinPerou.ScreenClip`), page by page, ready to
paste, as for TextCase and NowPlaying. Kept here so that the next submissions start from it. The package:
`dist\store\ScreenClip_<version>.0_x64.msix` (`scripts\build-store.ps1`).

The Store version differs from the GitHub one in three ways the texts below take into account: the Store updates it
(no update check of its own, so no network access at all), the Print Screen switch of Windows is turned off by the user
in Windows Settings (ScreenClip opens the page), and it starts with Windows through its startup task.

## Pricing and availability

- Markets: all. Visibility: public. Price: free (no trial, no add-ons).
- Publish: as soon as it passes certification.

## Properties

- Category: **Productivity**. Secondary category: **Photo & video**.
- Does the app access, collect or transmit personal information? **Yes**: it reads the screen when the user captures
  or records it, and the microphone while a video records with the microphone turned on; everything stays on the PC
  (nothing sent). Hence the privacy policy below.
- Privacy policy URL: `https://github.com/quentinperou/ScreenClip#privacy-policy`
- Website: `https://github.com/quentinperou/ScreenClip`
- Support contact info: `https://github.com/quentinperou/ScreenClip/issues`
- Product declarations: the defaults (install to alternate drives, OneDrive backups); nothing else.
- System requirements: keyboard, mouse. Minimum: Windows 11 (the package needs build 22000).

## Age ratings (IARC questionnaire)

Category: utility / productivity / other. Every question **No**: no violence, no user interaction or shared content,
no location shared, no purchases, no unrestricted web access (Report a problem only opens a GitHub page in the user's
browser). Expected rating: 3+ (PEGI 3).

## Packages

Upload the `.msix`. Device family: Windows 10/11 Desktop (the package needs Windows 11).

## Submission options

**Why do you need the runFullTrust capability?** (500 characters at most)

```
Desktop screenshot app (WPF, .NET). Full trust is needed to: register its global shortcuts (RegisterHotKey, Print Screen); freeze the whole screen for the area selection (GDI) and read the UI element under the pointer (UI Automation) to outline it; show its selection overlays and recording frame over every app; record the screen (Windows.Graphics.Capture) with the computer's sound and the microphone (WASAPI); start with Windows (startup task). No data leaves the PC.
```

**Notes for certification**

```
No account, no sign-in, no network access. Start ScreenClip from the Start menu: its editor opens, with "Capture an area" and "Record a video"; ScreenClip also waits in the notification area (a left click on its icon captures an area, a right click opens its menu). Shortcuts: Print Screen (area), Alt+Print Screen (window), Ctrl+Print Screen (screen), Ctrl+Alt+Print Screen (video), Win+Shift+C (color picker). On a new Windows 11, Windows keeps Print Screen for the Snipping Tool: at the first start ScreenClip offers to open Windows Settings so that the user turns that switch off; the icon and the editor capture without it. The first video asks Windows for a capture without its yellow border. The microphone is off by default (capture bar of a video).
```

## Store listing: English (en-us)

**Description**

```
Screenshots for Windows 11, ready to share: one key, arrows and text if needed, then a nice layout around them.

Press Print Screen: the screen freezes and the part under the pointer is outlined as you move (a video player, a card of a web page, a panel, the whole window). Click to take it, or drag an area. Alt+Print Screen takes a window, Ctrl+Print Screen a screen, and all your screens at once are one click away.

The capture opens in the editor. Annotate it: arrows (straight or curved), rectangles, ellipses, spotlights, text and speech bubbles, numbered steps, a mouse pointer with a click halo, the highlighter, and blur or pixelate what must not be seen. Then lay it out: a gradient or your own background, a macOS or Windows 11 window, a browser, a laptop or a phone around it, rounded corners, shadow, a 3D tilt, and the format of a post, a story or a slide. Presets in one click, and your own.

Record videos too: an area, a window or a screen, with the sound of the computer and the microphone. Cut, split and remove pieces, crop, annotate in time (each annotation has its track), redraw or smooth the mouse pointer, move a pointer from A to B, set the volume and fades of each sound, then export an MP4 or a GIF. A still picture can be animated too.

Copy as PNG, export to PNG, JPG, WebP, MP4 or GIF, or drag the picture to an app. A color picker copies the code of any color on the screen.

Light on your PC: it waits in the notification area and uses almost nothing at rest. Your captures never leave your computer.

Free and open source (MIT).
```

**Product features** (one per field)

```
One key: Print Screen freezes the screen and outlines the part under the pointer
Area, window, screen or all screens; the last area again in one key
Arrows, shapes, spotlights, text, bubbles, numbered steps and a mouse pointer
Blur, pixelate or hide with a solid block what must not be seen
Layout: backgrounds, window and device frames, rounded corners, shadow, 3D tilt
Presets for posts, stories and slides, and your own
Screen recording with the computer's sound and the microphone, kept apart
Video editor: cut, split, crop, annotations in time, the pointer redrawn
Export PNG, JPG, WebP, MP4 or GIF; copy, or drag to an app
Color picker: the code of any color on the screen, copied
Light or dark theme, English and French
Waits in the notification area, uses almost nothing at rest
```

**Short description**

```
Screenshots for Windows 11, ready to share: one key, arrows and text if needed, a nice layout around them (background, window frame, rounded corners, shadow), and screen recordings to cut, annotate and export as MP4 or GIF.
```

**Screenshots** (in this order): `.github/screenshot.png` (1560 × 1000), `.github/selection.png` (1600 × 951),
`.github/edit.png` (1600 × 1068), `.github/video.png` (1600 × 1068). Optional trailer: the demo MP4
(`.local\readme\demo\out\screenclip-demo.mp4`, 1600 × 900, 20 s).

**Search terms** (one per field)

```
screenshot
screen capture
snipping tool
screen recorder
annotate screenshot
mockup
gif
```

**Copyright and trademark info**

```
Copyright (c) 2026 QuentinPerou.
```

**Developed by**: `QuentinPerou`. **What's new in this version**: empty for the first submission.

## Store listing: French (fr-fr)

**Description**

```
Des captures d'écran pour Windows 11, prêtes à partager : une touche, des flèches et du texte si besoin, puis une belle mise en forme autour.

Appuyez sur Impr. écran : l'écran se fige et la partie sous le pointeur s'entoure à mesure que vous bougez (un lecteur vidéo, une carte d'une page web, un panneau, la fenêtre entière). Un clic la prend, ou tracez une zone. Alt+Impr. écran prend une fenêtre, Ctrl+Impr. écran un écran, et tous vos écrans à la fois sont à un clic.

La capture s'ouvre dans l'éditeur. Annotez-la : flèches (droites ou courbes), rectangles, ellipses, projecteurs, texte et bulles, étapes numérotées, un pointeur de souris avec un halo de clic, le surligneur, et floutez ou pixelisez ce qui ne doit pas se voir. Puis mettez-la en forme : un dégradé ou votre propre fond, une fenêtre macOS ou Windows 11, un navigateur, un ordinateur portable ou un téléphone autour, des coins arrondis, une ombre, une inclinaison 3D, et le format d'une publication, d'une story ou d'une diapositive. Des préréglages en un clic, et les vôtres.

Enregistrez aussi des vidéos : une zone, une fenêtre ou un écran, avec le son de l'ordinateur et le micro. Coupez, scindez et retirez des passages, recadrez, annotez dans le temps (chaque annotation a sa piste), redessinez ou lissez le pointeur de la souris, déplacez un pointeur de A à B, réglez le volume et les fondus de chaque son, puis exportez en MP4 ou en GIF. Une image fixe peut aussi être animée.

Copiez en PNG, exportez en PNG, JPG, WebP, MP4 ou GIF, ou glissez l'image vers une application. Une pipette copie le code de n'importe quelle couleur de l'écran.

Léger pour votre PC : il attend dans la zone de notification et ne consomme presque rien au repos. Vos captures ne quittent jamais votre ordinateur.

Gratuit et open source (MIT).
```

**Fonctionnalités** (une par champ)

```
Une touche : Impr. écran fige l'écran et entoure la partie sous le pointeur
Zone, fenêtre, écran ou tous les écrans ; la dernière zone en une touche
Flèches, formes, projecteurs, texte, bulles, étapes numérotées et pointeur de souris
Floutez, pixelisez ou masquez d'un bloc ce qui ne doit pas se voir
Mise en forme : fonds, cadres de fenêtre et d'appareil, coins arrondis, ombre, 3D
Préréglages pour publications, stories et diapositives, et les vôtres
Enregistrement de l'écran avec le son de l'ordinateur et le micro, gardés à part
Éditeur vidéo : couper, scinder, recadrer, annotations dans le temps, pointeur redessiné
Export PNG, JPG, WebP, MP4 ou GIF ; copier, ou glisser vers une application
Pipette : le code de n'importe quelle couleur de l'écran, copié
Thème clair ou sombre, français et anglais
Attend dans la zone de notification, ne consomme presque rien au repos
```

**Description courte**

```
Des captures d'écran pour Windows 11, prêtes à partager : une touche, des flèches et du texte si besoin, une belle mise en forme autour (fond, cadre de fenêtre, coins arrondis, ombre), et des enregistrements de l'écran à couper, annoter et exporter en MP4 ou GIF.
```

**Captures d'écran** (dans cet ordre) : `.github/screenshot.fr.png` (1560 × 1000), `.github/selection.fr.png`
(1600 × 951), `.github/edit.fr.png` (1600 × 1068), `.github/video.fr.png` (1600 × 1068).

**Termes de recherche** (un par champ)

```
capture d'écran
capture écran
outil capture
enregistrer écran
annoter capture
mockup
gif
```

**Copyright et marques**

```
Copyright (c) 2026 QuentinPerou.
```

**Développé par** : `QuentinPerou`. **Nouveautés de cette version** : vide pour la première soumission.
