# DlpForge

DlpForge is a free Windows desktop app that puts a friendly interface on top of
[yt-dlp](https://github.com/yt-dlp/yt-dlp). Paste or drop a link, pick video or audio and a quality,
and DlpForge runs yt-dlp for you with a live queue, history and logs.

Its feature set is modelled on the [YTDLnis](https://github.com/deniscerri/ytdlnis) Android app and
reshaped for mouse, keyboard and large screens. DlpForge shares no code with YTDLnis.

Website: https://shehanweerarathna.github.io/DlpForge/

## Status

Version 0.1, the first MVP build. See [docs/FEATURES.md](docs/FEATURES.md) for what works today and what comes next.

## Features in this build

- Link bar that is always visible: paste a link, press Enter, or drop links, `.url` shortcuts and `.txt` link lists onto the window.
- Download card with thumbnail, title, video/audio switch, quality presets, container or audio format, and an exact-format table.
- Playlists: every item listed with checkboxes, select all / none / invert, and Shift or Ctrl multi-select.
- Queue with parallel downloads (1 to 10), live progress, speed and ETA, pause, resume, cancel and retry. The queue survives restarts.
- Download folders per type and file name templates with a live preview.
- Embed thumbnail, metadata, chapters and subtitles.
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
