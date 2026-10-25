# DlpForge architecture

DlpForge is a C# app on .NET 10 with an [Avalonia UI](https://avaloniaui.net/) 12 front end. It has three layers:

```
┌────────────────────────── DlpForge.App (Avalonia) ──────────────────────────┐
│ Views (XAML)  ⇄  View models (CommunityToolkit.Mvvm)                        │
│ Windows services: toast notifications, taskbar progress, shell open        │
└──────────────────────────────────┬──────────────────────────────────────────┘
                                   │ commands / observable items
┌──────────────────────────── DlpForge.Core ──────────────────────────────────┐
│ DownloadQueue  →  YtDlpClient  →  ArgumentBuilder / OutputParser            │
│      │                 └──────→  ProcessRunner (Job Object, kill tree)      │
│      └──→ DownloadStore (SQLite)        AppSettings (JSON)                  │
│ ToolLocator / ToolInstaller (yt-dlp, FFmpeg, Deno)                          │
└──────────────────────────────────┬──────────────────────────────────────────┘
                                   │ child processes
                    yt-dlp.exe  →  ffmpeg.exe / ffprobe.exe, deno.exe
```

The UI only sends commands and renders state. All process work happens in the core on background
threads; the queue marshals updates back to the UI thread through a `SynchronizationContext`.

## How DlpForge talks to yt-dlp

DlpForge runs the official standalone `yt-dlp.exe` and reads only machine-readable output, never yt-dlp's
normal console text.

| Call | Arguments | Used for |
| --- | --- | --- |
| Every call | `--encoding utf-8` | yt-dlp.exe ignores `PYTHONIOENCODING`, so without this it writes paths and messages in the Windows code page and drops characters outside it. |
| Inspect | `-J --flat-playlist` | One JSON document with title, thumbnail, formats, subtitles, chapters, or playlist entries. Feeds the download card. Searches (`ytsearchN:words`) and import-list lines go through the same call. |
| Download | `--newline --progress --progress-template "download:[dlpforge:dl]%(progress)j"` | One JSON progress object per line: bytes, total, speed, ETA. |
| | `--progress-template "postprocess:[dlpforge:pp]%(progress.postprocessor)s"` | Which post-processor is running (merging, converting, embedding). |
| | `--print "after_move:[dlpforge:file]%(filepath)s"` | The final file path after merging and conversion, for Open and Show in folder. |
| | `--print "before_dl:[dlpforge:meta]%(.{title,uploader,channel,duration,thumbnail})j" --no-simulate` | Title, thumbnail and length when the download starts, for items queued straight from a link. |
| | `--trim-filenames N` | N is worked out from the download folder so the full path stays under the Windows 260-character limit. |
| | `--download-sections "*start-end"`, `--split-chapters`, `--sponsorblock-mark/remove`, `--parse-metadata` | Cut parts, chapter files, SponsorBlock and edited title/artist. Edited text is escaped so yt-dlp reads it literally. |
| Terminal | the user's arguments | The Terminal page. DlpForge's tool and cookie options go first so typed ones win. No shell is involved. |
| Sign-in test | `-J --flat-playlist --playlist-items 0 --cookies <copy> -- https://www.youtube.com/playlist?list=WL` | Settings > Cookies > Verify. Watch Later needs an account, and yt-dlp warns "cookies are no longer valid" when YouTube rejects them. It runs on a temporary copy because yt-dlp saves the cookies back into the file and drops rejected sign-in cookies. Before this, `CookieFileCheck` reads the file with yt-dlp's own rules, offline. |
| Version | `--version` | Settings > Tools. |
| Update | `--update-to stable / nightly / master / channel@tag` | Updating, switching channel and rolling back. |

`ArgumentBuilder` builds every argument list and is covered by unit tests. Each call also passes
`--ffmpeg-location` and `--js-runtimes deno:<path>` when those tools are found, and `--cookies-from-browser`
or `--cookies` when cookies are turned on. Extra arguments for a download (from a command template or typed on
the card) come after DlpForge's own, so they win on conflicts, and every download ends with `-- <url>` so a
link can never be read as an option.

## Process rules

- One yt-dlp process per active download. The parallel limit (default 3) is set in Settings.
- Every child process joins a Windows Job Object with kill-on-close, so yt-dlp and its FFmpeg children die
  with DlpForge, even on a crash. Cancel and pause kill the whole process tree.
- Pause is cancel plus resume: yt-dlp continues its `.part` file on the next run.
- Downloads running when the app closes go back into the queue and restart on the next launch.
- Every run writes its full output to `logs\<id>.log`, which the log viewer shows.
- A scheduled download waits in the queue with a start time. A timer wakes the queue at the next start time
  (and at least hourly, in case the clock changed or the PC slept). DlpForge must be running, so tray mode
  hides the window on close instead of exiting; Quit in the tray menu exits.
- Each cut part is queued as its own download with one `--download-sections` range and the part's times in
  the file name, so progress and retry work per part.

## Data

- **Settings**: `settings.json`, saved on every change.
- **Queue and history**: one SQLite table, `downloads`. A row is a queue entry while it runs and a history
  entry once it completes. "Clear finished" hides rows from the Downloads view without deleting them.
  Columns added by later versions (0.2: `scheduled_at`) are added on startup to databases made by older ones.
- **Command templates and terminal history** live in `settings.json`. Templates export to and import from a
  JSON list of `{ "Name", "Arguments" }`.
- **Playlists**: each selected item is queued as its own download. Because yt-dlp then no longer knows the
  playlist, DlpForge writes the playlist title and index into the file name template as literal text.

## Libraries

| Library | Why |
| --- | --- |
| Avalonia 12 + Fluent theme | Cross-platform XAML UI; Windows first, macOS and Linux possible later. |
| CommunityToolkit.Mvvm | Observable properties and commands via source generators. |
| Microsoft.Data.Sqlite | Queue and history storage. |
| Microsoft.Toolkit.Uwp.Notifications | Windows toast notifications for an unpackaged app. |

## Decisions

- **Stack**: C# with Avalonia, chosen over Tauri 2 because C# is the owner's stronger language. Avalonia keeps
  macOS and Linux open for later.
- **Name**: DlpForge. The YTDLnis name may not be used by derivatives.
- **Code reuse**: none from YTDLnis (GPL-3.0); only its feature ideas.
- **Tools**: downloaded on first run rather than bundled, which keeps the app download small.
