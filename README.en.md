# PPT Slider

[한국어](README.md) | [日本語](README.ja.md) | **English**

A Windows desktop app for opening `.pptx` files without PowerPoint, making simple edits, and presenting while drawing on the slides (WPF, .NET 10).

## Features

- **Open / Save**: open `.pptx` files (drag and drop, "Open with" supported), Save, Save As.
  Edits change the OpenXML document directly, so elements the app cannot draw are kept as they are when you save.
  - **Recent files**: the last 10 files appear in the list next to the Open button and on the empty screen.
  - **Autosave**: every 2 minutes a copy of the deck being edited is kept, and deleted on a normal exit. If the app ended abnormally, opening the same file again offers to recover the newer copy.
  - **Export to PDF**: one page per slide, with or without ink.
  - **Ink Notes**: only the slides you drew on, with their ink, as a PDF (lecture notes).
- **Editing**
  - Select objects, drag to move, resize from the bottom-right corner, delete
  - Double-click (or F2) to edit text in place (the original font, size, color, alignment and other formatting are kept)
  - Bold / Italic / Underline, font, size, color, paragraph alignment (while editing text, bold, italic, underline, font and size apply only to the selected characters, and the font and size boxes show the values of the selected characters)
  - Insert text boxes and pictures (pictures: the [Picture] button, paste with Ctrl+V, or drop an image file); new, duplicate and delete slides; undo
  - Drag thumbnails to change the slide order
  - Text set to "shrink text on overflow" (normAutofit) is drawn smaller to fit its box.
- **Screen ink**: pen, highlighter, eraser, 7 colors, adjustable width. Ink is drawn on a transparent window over the screen, not on the slide,
  so you can draw over the editor, the slide show and other apps too (the show's monitor during a show, otherwise the editor's).
  - Ink is remembered per slide: when you change slides, the ink you drew on that slide comes back.
  - On-screen toolbar: pointer (keep the ink and click the screen underneath), pen, highlighter, eraser, colors, width, undo, clear, hide. It can be dragged aside.
  - Hold Shift while drawing for a straight line. Ctrl+Z takes back ink strokes first when there are any.
  - **Blank ink page** (Ctrl+Enter): inserts a blank slide after the current one and switches to the pen.
  - Ink is not saved in the .pptx and is gone when the app closes. To keep it, export to PDF or Ink Notes.
- **Slide show**: from the beginning (F5) / from the current slide (Shift+F5). You can choose the monitor for the show (default: the secondary monitor).
  - Black / white screen, laser pointer, go to a slide by number (number + Enter), zoom (Z, then drag an area)
  - Current time and elapsed time in the bottom-right corner; the cursor hides after 3 seconds without mouse movement
- **Presenter View**: with "Use Presenter View" on, a monitor other than the show's displays the current and next slide, speaker notes (adjustable text size) and elapsed time.
  Ink tools picked in Presenter View draw on the show's screen, and it uses the same shortcuts as the show.
- **Break Time**: a full-screen break timer from 5 minutes to 2 hours in 5-minute steps (T).
- **Language**: 한국어 · 日本語 · English. By default the app follows the Windows display language (English when it is neither Korean nor Japanese). Changing it in the language list on the [Settings] tab takes effect after a restart. The choice is saved to `%APPDATA%\PptSlider\language.txt`.
- **Shortcut settings**: every command's shortcut can be changed in [Settings] tab → [Shortcut Settings], and is saved to `%APPDATA%\PptSlider\shortcuts.json`. Press F1 to see the list.

### Default shortcuts

| Group | Command | Keys |
|---|---|---|
| Navigate | Next / previous slide | → ↓ PageDown Space Enter N / ← ↑ PageUp Backspace |
| Navigate | First / last slide | Home / End |
| Navigate | Go to slide by number (show) | Number + Enter |
| Ink | Pen / highlighter / eraser | P / H / E |
| Ink | Laser pointer | Ctrl+L |
| Ink | Colors 1–7 | 1–7 (numeric keypad too) |
| Ink | Thinner / thicker | `[` `-` / `]` `=` |
| Ink | Clear ink on this slide | C |
| Ink | Insert blank ink page | Ctrl+Enter |
| Show | From beginning / from current slide | F5 / Shift+F5 |
| Show | Black screen / white screen | B `.` / W `,` |
| Show | Zoom | Z |
| Show | Break Time | T |
| File | Open / Save / Save As | Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| Edit | Undo (ink first) · edit text · delete object | Ctrl+Z · F2 · Delete |
| Edit | Bold / Italic / Underline | Ctrl+B / Ctrl+I / Ctrl+U |
| Edit | Increase / decrease font size | Ctrl+Shift+. / Ctrl+Shift+, |
| Slides | New slide / duplicate | Ctrl+M / Ctrl+D |
| Help | Shortcut list | F1 |

In the show, the number keys are shared by go-to-slide and pen colors. A single number with nothing after it for 1 second sets the pen color; Enter or another number within that time makes it a slide number.

Esc is a fixed key: it cancels text editing, ends the zoom, returns to the Select tool, and ends the slide show (one at a time, in that order).

### Not drawn yet

Charts, effects (shadows etc.), table styles other than the default, text autofit and animations are not shown on screen. They stay in the file unchanged.
SmartArt is drawn from the picture PowerPoint saved in the file (drawing.xml); files without that picture show only a placeholder box.

## Build

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
cd src
dotnet build -c Release
```

The output is `bin\Release\net10.0-windows\PptSlider.exe` at the repository root.
The build output folders (`bin/`, `obj/`) are moved to the root by `src/Directory.Build.props` and excluded from Git.

### Self-test

```powershell
PptSlider.exe --selftest deck.pptx outDir
```

Renders every slide to PNG and checks the edit → save → reopen round trip, plus the slide show, Presenter View, screen ink, picture insert, reordering, text autofit, PDF, SmartArt, recent files, autosave and the UI text in all three languages.
The show and presenter windows open off screen and key presses go only to them, so you can keep working while it runs.
Results are written to `outDir\selftest.txt`; exit code 0 means pass. App data goes to `outDir\local`, so your real recent files and autosaves are not touched. The self-test always runs in Korean.

## MSIX package

```powershell
.\build-msix.ps1           # for local testing (self-signed)
.\build-msix.ps1 -Store    # for Store submission (unsigned)
```

The script runs a self-contained publish → adds `Package\AppxManifest.xml` and the `Package\Assets` logos to the layout → `makeappx pack` → signs.
The result is `bin\msix\PptSlider_<version>_x64.msix`. The package version follows `Version` in `src\PptSlider.csproj`.
Without a Windows SDK, it downloads the NuGet package `Microsoft.Windows.SDK.BuildTools` (which contains `makeappx` and `signtool`) to `obj\tools`.

The manifest declares a full-trust (runFullTrust) desktop app, the `.pptx` file association, and Korean, Japanese and English (ko-KR, ja-JP, en-US) resources.
The Store asks for a Store listing (description, screenshots) in each of these three languages.
The logos can be redrawn with `Package\make-assets.ps1`.

### Local test install

The script creates a self-signed certificate whose name matches the manifest's Publisher in `Cert:\CurrentUser\My` (reusing it if it already exists) and exports it to `bin\msix\PptSlider-test.cer`.
Trust this certificate once, then install (administrator PowerShell):

```powershell
Import-Certificate -FilePath bin\msix\PptSlider-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage bin\msix\PptSlider_<version>_x64.msix
```

### Microsoft Store submission

1. Reserve the app name in [Partner Center](https://partner.microsoft.com/dashboard).
2. Update `Package\AppxManifest.xml` with the values on the **Product management → Product identity** page.
   - `Name` of `Identity` ← Package/Identity/Name
   - `Publisher` of `Identity` ← Package/Identity/Publisher (in the form `CN=XXXXXXXX-...`, copy it exactly)
   - `PublisherDisplayName` ← Package/Properties/PublisherDisplayName
   - `DisplayName` and the `DisplayName` of `uap:VisualElements` ← the reserved app name
3. Raise `Version` in `src\PptSlider.csproj`. The Store does not accept the same version twice.
4. Upload the `.msix` built with `.\build-msix.ps1 -Store` to the Packages section of the submission. The Store signs it.
5. `runFullTrust` is a restricted capability, so the submission must explain why it is used (e.g. "A WPF desktop app that reads and writes the .pptx files the user opens").

> **About the app name**: "PPT" is associated with Microsoft PowerPoint and may be flagged as a trademark issue in Store certification.
> It is safer to keep "PPT" and "PowerPoint" out of the reserved name, and to mention the file format only, e.g. "opens .pptx files", in the description.

In a packaged install, writes to `%LOCALAPPDATA%` and `%APPDATA%` may be redirected to a package-private folder. Recent files, autosaves, shortcut settings and the language choice are stored there and are removed when the app is uninstalled.

## Folder structure

```
build-msix.ps1          Builds the MSIX package
Package/                AppxManifest.xml, Assets (logos), make-assets.ps1
src/
├─ App.xaml(.cs)        Entry point (--selftest, opening a file at startup)
├─ Views/               MainWindow (editor), ShowWindow (slide show), PresenterWindow (Presenter View),
│                       InkOverlay (screen ink), BreakWindow (break timer), ShortcutsWindow (shortcut settings)
├─ Models/              Deck (.pptx document · editing · saving · undo), PenState (ink tool state), InkHistory (ink undo)
├─ Rendering/           SlideRenderer (PPTX → WPF rendering, including SmartArt)
├─ Services/            Shortcuts, Monitors (monitor list), PdfExport (PDF),
│                       RecentFiles, AutoSave, AppPaths (app data folder), Loc (Korean · Japanese · English UI text)
└─ Diagnostics/         SelfTest (editing · show · ink · viewer checks)
```

Dependency: [DocumentFormat.OpenXml](https://www.nuget.org/packages/DocumentFormat.OpenXml)
