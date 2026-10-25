# DlpForge feature list

Features are mapped from the [YTDLnis feature list](https://github.com/deniscerri/ytdlnis#-features) and
adapted for a desktop. Priority: **MVP** = version 1, **v1.1** = first follow-up, **Later** = after that.

## MVP

| Feature | Desktop version | Status in 0.1 |
| --- | --- | --- |
| Download audio or video from 1,000+ sites | Link bar always visible; drag and drop links, `.url` and `.txt` files | Done |
| Download card with format selection | Thumbnail, title, duration; presets Best, 2160p to 360p, audio formats; exact-format table | Done |
| Playlists, edit each item | List with checkboxes, all / none / invert, Shift/Ctrl multi-select, shared settings for the selection | Done (per-item settings later) |
| Queue, parallel downloads | Tabs All, Active, Queued, Completed, Failed; live speed and ETA; parallel limit 1 to 10 | Done |
| Download path and file name templates | Folder per type, template editor with live preview | Done |
| Embed subtitles, metadata, chapters, thumbnail | Toggles in the card with defaults in Settings | Done |
| Re-download cancelled or failed items | Retry button and right-click menu | Done |
| Notification with Open / Show in folder | Windows toast with both buttons; taskbar progress | Done |
| Log downloads | Log viewer per download with copy and open file | Done |
| Download history | Searchable, sortable history with thumbnails | Done |
| Update yt-dlp and components | Versions of yt-dlp, FFmpeg, Deno; install, update, channel switch, rollback | Done (aria2c later) |
| Theming | Follows Windows light/dark; manual override | Done (accent and Mica later) |

## v1.1

Shipped in version 0.2.

| Feature | Desktop version | Status in 0.2 |
| --- | --- | --- |
| Search inside the app | Words typed in the link bar search YouTube; results open in the list grid, unticked, with a result count set in Settings. `ytsearchN:` and other yt-dlp search prefixes still work | Done |
| Quick download mode | ⚡ Quick switch next to the link bar (also in Settings) and Shift+Enter: links skip the card and reuse the settings picked last time | Done |
| Cookies | Settings > Cookies: from Firefox, Chrome, Edge, Brave, Opera, Vivaldi, Chromium or Whale (with an optional profile), or a cookies.txt file. Used by every yt-dlp call. YouTube account: sign in once in DlpForge's own WebView2 window, which stays signed in; Refresh cookies saves fresh YouTube cookies without a password (like YTDLnis), Sign out ends the session and deletes the data. Cookies are saved from youtube.com/robots.txt as yt-dlp's guide recommends. Needs the Edge WebView2 Runtime. Verify checks a cookies.txt file the way yt-dlp reads it (header, JSON exports, byte order mark, bad lines, expiry, YouTube sign-in cookies) and asks YouTube whether the cookies still sign you in, without changing the file | Done |
| Scheduling | "Start at a set time" on the card, Schedule / Start now on queued items, a Scheduled tab; tray mode keeps DlpForge running with the window closed | Done |
| Cut by timestamps or chapters | Timeline with drag handles and From/To boxes, several parts, chapter ticks; each part is its own file. Optional precise cuts | Done |
| SponsorBlock | Per-category Off / Mark as chapter / Remove in Settings; a box on the card, ticked by default if you want | Done |
| Split by chapters | "Also save one file per chapter" on the card (`--split-chapters`) | Done |
| Edit metadata before download | Title and Artist on the card; the new title also names the file | Done |
| Custom commands and terminal | Command templates in Settings with import/export to JSON; Extra arguments on the card; a Terminal page that runs yt-dlp with any arguments | Done |
| Import list | Import list button or drop a .txt file: links, playlists, searches and plain words (searched), one per line; all videos open in one card | Done |

Also fixed in 0.2:

- File paths with characters outside the Windows code page (Sinhala, Japanese, fullwidth punctuation, en dashes) were garbled in yt-dlp's output, which broke Open and Show in folder. Every call now passes `--encoding utf-8`.
- Downloads into deep folders could fail on Windows' 260-character path limit. DlpForge now passes `--trim-filenames` sized to the download folder, and explains the error if it still happens.
- Items queued straight from links (several pasted links, quick download) now get their real title, thumbnail and length when the download starts.

## Later

| Feature | Desktop version |
| --- | --- |
| Share-menu equivalent | Global hotkey, clipboard watcher, browser extension, `dlpforge://` links (the tray icon came in 0.2) |
| Incognito mode | No history or logs for those items |
| Backup and restore | Settings, templates and history in one file |
| Accent colour and Mica | Windows 11 look |
| Command line for scripting | `DlpForge.exe --add <url> --type audio` |
| Translations | Strings externalised, community translation |
| Desktop extras | Keyboard shortcuts for all actions, channel subscriptions with `--download-archive`, bandwidth limits by time of day |

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| Ctrl+L | Focus the link bar |
| Enter (in the link bar) | Open the download card, or search for the words typed |
| Shift+Enter (in the link bar) | Quick download: queue the link with the last settings |
| Ctrl+V (outside a text box) | Paste the clipboard link and open it |
| Esc | Close the download card |
| Ctrl+1 / 2 / 3 / 4 | Downloads / History / Settings / Terminal |
| Ctrl+F (History) | Search |
| Delete | Remove the selected download or history entry |
| Space (list of items) | Tick or untick the highlighted rows |
| Left / Right (cut timeline) | Move the end of the part by one second; with Shift, the start |
| Up / Down (Terminal) | Earlier commands |
