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

| Feature | Desktop version |
| --- | --- |
| Search inside the app | `ytsearchN:` results in the playlist grid (works today by typing `ytsearch10:words` in the link bar) |
| Quick download mode | Skip the card and use the last settings |
| Cookies | `--cookies-from-browser` for Chrome, Edge, Firefox, or a cookies.txt file |
| Scheduling | Date and time picker on queued items; tray mode |
| Cut by timestamps or chapters | `--download-sections` with a timeline |
| SponsorBlock | Per-category remove or mark as chapters |
| Split by chapters | `--split-chapters` toggle |
| Edit metadata before download | Editable title and artist |
| Custom commands and terminal | Saved argument templates, import/export, terminal pane |
| Import list | Text file of links, playlists or searches (multi-link drop already queues them) |

## Later

| Feature | Desktop version |
| --- | --- |
| Share-menu equivalent | Tray icon, global hotkey, clipboard watcher, browser extension, `dlpforge://` links |
| Incognito mode | No history or logs for those items |
| Backup and restore | Settings, templates and history in one file |
| Accent colour and Mica | Windows 11 look |
| Command line for scripting | `DlpForge.exe --add <url> --type audio` |
| Translations | Strings externalised, community translation |
| Desktop extras | Keyboard shortcuts for all actions, channel subscriptions with `--download-archive`, bandwidth limits by time of day |

## Keyboard shortcuts in 0.1

| Keys | Action |
| --- | --- |
| Ctrl+L | Focus the link bar |
| Enter (in the link bar) | Open the download card |
| Ctrl+V (outside a text box) | Paste the clipboard link and open it |
| Esc | Close the download card |
| Ctrl+1 / 2 / 3 | Downloads / History / Settings |
| Ctrl+F (History) | Search |
| Delete | Remove the selected download or history entry |
| Space (playlist list) | Tick or untick the highlighted rows |
