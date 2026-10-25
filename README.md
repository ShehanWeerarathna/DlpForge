# DlpForge

DlpForge is a free Windows desktop app that puts a friendly interface on top of
[yt-dlp](https://github.com/yt-dlp/yt-dlp). Paste or drop a link, pick video or audio and a quality,
and DlpForge runs yt-dlp for you with a live queue, history and logs.

Its feature set is modelled on the [YTDLnis](https://github.com/deniscerri/ytdlnis) Android app and
reshaped for mouse, keyboard and large screens. DlpForge shares no code with YTDLnis.

Website: https://shehanweerarathna.github.io/DlpForge/

## Status

Version 0.2: the MVP plus the v1.1 features (search, quick download, cookies, scheduling and tray mode,
cutting, SponsorBlock, split by chapters, metadata editing, command templates and a terminal, import lists).
See [docs/FEATURES.md](docs/FEATURES.md) for what works today and what comes next.

## Features in this build

- Link bar that is always visible: paste a link and press Enter, type words to search YouTube, or drop links, `.url` shortcuts and `.txt` lists onto the window.
- Quick download (⚡ Quick or Shift+Enter): links skip the download card and reuse the last settings.
- Import lists: a text file of links, playlists and searches opens as one list to pick from.
- Download card with thumbnail, title, video/audio switch, quality presets, container or audio format, and an exact-format table.
- Playlists: every item listed with checkboxes, select all / none / invert, and Shift or Ctrl multi-select.
- Queue with parallel downloads (1 to 10), live progress, speed and ETA, pause, resume, cancel and retry. The queue survives restarts.
- Download folders per type and file name templates with a live preview.
- Embed thumbnail, metadata, chapters and subtitles. Edit the title and artist before downloading.
- Cut: download only parts of a video, picked on a timeline or by chapter, or split a video into one file per chapter.
- SponsorBlock: remove or mark sponsor segments and other categories.
- Scheduling: start downloads at a set time, with a tray mode that keeps DlpForge running when the window is closed.
- Cookies from a browser or a cookies.txt file, for videos that need a signed-in session. **YouTube account**: sign in once in DlpForge's own browser window, which stays signed in, so **Refresh cookies** gets fresh YouTube cookies later without your password. Needs the Microsoft Edge WebView2 Runtime. Verify checks a cookies.txt file and asks YouTube whether it still signs you in.
- Command templates (saved yt-dlp arguments, with import and export) and a Terminal page that runs yt-dlp directly.
- Windows notifications with Open and Show in folder, and progress on the taskbar button.
- Searchable, sortable history with thumbnails, and a log viewer for every download.
- Tools page that installs and updates yt-dlp, FFmpeg and Deno, switches yt-dlp channels and rolls back to a version.
- Light and dark themes that follow Windows by default.
- Portable mode.

## Build and run

Requirements: Windows 10 or 11 (x64) and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet run --project src/DlpForge.App
dotnet test
```

On first start, open **Settings > Tools** (or click **Install tools** on the banner) to download yt-dlp,
FFmpeg and Deno into `%LOCALAPPDATA%\DlpForge\tools`. DlpForge also uses copies already on your `PATH`.

## Project layout

| Path | What it is |
| --- | --- |
| `src/DlpForge.Core` | yt-dlp process runner, argument builder, output parser, queue, SQLite store, tool installer. No UI code. |
| `src/DlpForge.App` | Avalonia UI: views, view models, Windows notifications and taskbar progress. |
| `tests/DlpForge.Core.Tests` | xUnit tests for the core. |
| `docs/` | Architecture and feature notes. |
| `site/` | Project website, deployed to GitHub Pages. |

## Data locations

| What | Where |
| --- | --- |
| Settings | `%LOCALAPPDATA%\DlpForge\settings.json` |
| Queue and history | `%LOCALAPPDATA%\DlpForge\dlpforge.db` (SQLite) |
| Per-download logs | `%LOCALAPPDATA%\DlpForge\logs` |
| yt-dlp, FFmpeg, Deno | `%LOCALAPPDATA%\DlpForge\tools` |

Put an empty `portable.txt` next to `DlpForge.exe` to keep all of this in a `data` folder beside the exe instead.

## Licence

DlpForge is licensed under the [GNU General Public License v3.0](LICENSE).
