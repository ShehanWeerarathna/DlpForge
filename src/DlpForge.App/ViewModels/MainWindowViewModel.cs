using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DlpForge.App.Services;
using DlpForge.Core;
using DlpForge.Core.Models;
using DlpForge.Core.YtDlp;

namespace DlpForge.App.ViewModels;

public enum Section { Downloads, History, Settings, Terminal }

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private CancellationTokenSource? _inspectCts;

    public DownloadsViewModel Downloads { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }
    public TerminalViewModel Terminal { get; }

    [ObservableProperty] private string _urlInput = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand), nameof(QuickInspectCommand), nameof(ImportListCommand))]
    private bool _isInspecting;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private string? _inspectError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsCardOpen))] private DownloadCardViewModel? _card;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadsSection), nameof(IsHistorySection), nameof(IsSettingsSection), nameof(IsTerminalSection))]
    private Section _selectedSection = Section.Downloads;

    [ObservableProperty] private bool _toolsMissing;
    [ObservableProperty] private string _statusText = "";

    public bool IsCardOpen => Card != null;
    public bool IsDownloadsSection => SelectedSection == Section.Downloads;
    public bool IsHistorySection => SelectedSection == Section.History;
    public bool IsSettingsSection => SelectedSection == Section.Settings;
    public bool IsTerminalSection => SelectedSection == Section.Terminal;

    /// <summary>Set by the window once it has a native handle.</summary>
    public TaskbarProgress? Taskbar { get; set; }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        Downloads = new DownloadsViewModel(services);
        History = new HistoryViewModel(services, url => _ = InspectAsync(url));
        Settings = new SettingsViewModel(services, RefreshToolsState);
        Terminal = new TerminalViewModel(services);

        var queue = services.Queue;
        queue.OverallProgressChanged += (active, pct) =>
        {
            Taskbar?.Update(active, pct);
            UpdateStatus();
        };
        queue.Items.CollectionChanged += (_, _) => UpdateStatus();
        queue.ItemCompleted += item =>
        {
            if (_services.Settings.ShowNotifications) NotificationService.DownloadFinished(item.Title, item.FilePath);
        };
        queue.ItemFailed += item =>
        {
            if (_services.Settings.ShowNotifications) NotificationService.DownloadFailed(item.Title, item.Error);
        };

        RefreshToolsState();
        UpdateStatus();
    }

    private void RefreshToolsState() => ToolsMissing = _services.Tools.YtDlp is null;

    private void UpdateStatus()
    {
        var items = _services.Queue.Items;
        int active = items.Count(i => i.IsActive);
        int queued = items.Count(i => i.Status == DownloadStatus.Queued && !i.IsScheduled);
        int scheduled = items.Count(i => i.IsScheduled);
        var parts = new List<string>();
        if (active > 0) parts.Add($"{active} downloading");
        if (queued > 0) parts.Add($"{queued} queued");
        if (scheduled > 0) parts.Add($"{scheduled} scheduled");
        StatusText = parts.Count == 0 ? "Idle" : string.Join(", ", parts);
    }

    [RelayCommand]
    private void Navigate(Section section) => SelectedSection = section;

    partial void OnSelectedSectionChanged(Section value)
    {
        // Templates may have been edited in Settings since the terminal was last shown.
        if (value == Section.Terminal) Terminal.ReloadTemplates();
    }

    private bool CanInspect() => !IsInspecting;

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private Task Inspect() => InspectAsync(UrlInput);

    /// <summary>Shift+Enter: download the link straight away with the last settings, whatever the Quick switch says.</summary>
    [RelayCommand(CanExecute = nameof(CanInspect))]
    private Task QuickInspect() => InspectAsync(UrlInput, quick: true);

    /// <summary>
    /// Opens the download card for one link, or for a search when the input is plain words.
    /// Several links are queued straight away. With quick download on, a link is queued without the card.
    /// </summary>
    public async Task InspectAsync(string? input, bool? quick = null)
    {
        input = input?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var links = LinkParser.Extract(input);
        if (links.Count > 1)
        {
            QueueWithDefaults(links);
            UrlInput = "";
            return;
        }

        var target = links.Count == 1 ? links[0] : input;
        CardSource source;
        if (LinkParser.IsUrl(target)) source = CardSource.Link;
        else if (LinkParser.IsSearch(target)) source = CardSource.Search;
        else if (LinkParser.AsBareLink(target) is { } bare) { target = bare; source = CardSource.Link; }
        else { target = LinkParser.ToSearch(input, _services.Settings.SearchResultCount); source = CardSource.Search; }

        if (_services.Tools.YtDlp is null)
        {
            InspectError = "yt-dlp isn't installed yet. Click \"Install tools\" above.";
            return;
        }

        // Quick download skips the card for links. Search results always need picking.
        bool useQuick = (quick ?? _services.Settings.QuickDownload) && source == CardSource.Link;

        _inspectCts?.Cancel();
        _inspectCts = new CancellationTokenSource();
        var ct = _inspectCts.Token;
        if (source == CardSource.Link) UrlInput = target;
        InspectError = null;
        BusyText = source == CardSource.Search ? "Searching…" : useQuick ? "Adding…" : "Reading link…";
        IsInspecting = true;
        try
        {
            var info = await _services.Client.InspectAsync(target, ct);
            if (source == CardSource.Search)
                info = WithTitle(info, $"\"{SearchWords(target)}\"");
            if (useQuick) QueueQuick(info);
            else Card = new DownloadCardViewModel(info, _services, CloseCard, source);
            UrlInput = "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (YtDlpException ex)
        {
            InspectError = ex.Message;
        }
        catch (Exception ex)
        {
            InspectError = "Could not read that link: " + ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsInspecting = false;
        }
    }

    private static string SearchWords(string search) => search[(search.IndexOf(':') + 1)..].Trim();

    private static MediaInfo WithTitle(MediaInfo info, string title) => new()
    {
        Id = info.Id,
        Url = info.Url,
        Title = title,
        Extractor = info.Extractor,
        IsPlaylist = true,
        Entries = info.Entries,
        Thumbnail = info.Entries.FirstOrDefault()?.Thumbnail,
    };

    /// <summary>Queues an inspected link without the card, using the last card settings.</summary>
    private void QueueQuick(MediaInfo info)
    {
        var queue = _services.Queue;
        var options = _services.Settings.QuickOptions(info.IsPlaylist);
        if (!info.IsPlaylist)
        {
            queue.Enqueue(info.Url, info.Title, options, info.Thumbnail, info.Uploader, info.DurationSeconds);
            NotificationService.ShowInApp("Added to queue", info.Title);
        }
        else
        {
            foreach (var e in info.Entries)
            {
                var perItem = options with { FilenameTemplate = TemplatePreview.FillPlaylistFields(options.FilenameTemplate, info.Title, e.Index) };
                queue.Enqueue(e.Url, e.Title, perItem, e.Thumbnail, e.Uploader ?? info.Uploader, e.DurationSeconds);
            }
            NotificationService.ShowInApp("Added to queue", $"{info.Entries.Count} items from {info.Title}");
        }
        SelectedSection = Section.Downloads;
    }

    [RelayCommand]
    private void CancelInspect()
    {
        _inspectCts?.Cancel();
        IsInspecting = false;
    }

    public void QueueWithDefaults(IReadOnlyList<string> links)
    {
        if (_services.Tools.YtDlp is null)
        {
            InspectError = "yt-dlp isn't installed yet. Click \"Install tools\" above.";
            return;
        }
        var options = _services.Settings.ToOptions();
        foreach (var link in links) _services.Queue.Enqueue(link, link, options);
        NotificationService.ShowInApp("Added to queue", $"{links.Count} links queued with your default settings.");
        SelectedSection = Section.Downloads;
    }

    // ---- Import list ----

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private async Task ImportList()
    {
        var path = await Dialogs.OpenFileAsync("Import a list of links or searches", Dialogs.TextFiles);
        if (path != null) await ImportFileAsync(path);
    }

    public async Task ImportFileAsync(string path)
    {
        string text;
        try
        {
            if (new FileInfo(path).Length > 5_000_000)
            {
                InspectError = "That file is too large to be a list of links.";
                return;
            }
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            InspectError = "Could not read the file: " + ex.Message;
            return;
        }
        await ImportAsync(LinkParser.ParseImportList(text), Path.GetFileName(path));
    }

    /// <summary>
    /// Reads every line of an import list (links, playlists and searches) and opens one card with all the
    /// videos they contain, so they can be picked and downloaded with the same settings.
    /// </summary>
    public async Task ImportAsync(IReadOnlyList<string> targets, string listName)
    {
        if (targets.Count == 0)
        {
            InspectError = "No links or searches found in that file. Put one link, playlist or search per line.";
            return;
        }
        if (_services.Tools.YtDlp is null)
        {
            InspectError = "yt-dlp isn't installed yet. Click \"Install tools\" above.";
            return;
        }

        _inspectCts?.Cancel();
        _inspectCts = new CancellationTokenSource();
        var ct = _inspectCts.Token;
        InspectError = null;
        IsInspecting = true;
        int done = 0;
        BusyText = $"Reading list: 0 of {targets.Count}";

        var results = new MediaInfo?[targets.Count];
        var errors = new List<string>();
        using var gate = new SemaphoreSlim(4);
        try
        {
            await Task.WhenAll(targets.Select(async (target, i) =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    results[i] = await _services.Client.InspectAsync(target, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (errors) errors.Add($"{target}: {ex.Message}");
                }
                finally
                {
                    gate.Release();
                    BusyText = $"Reading list: {Interlocked.Increment(ref done)} of {targets.Count}";
                }
            }));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsInspecting = false;
        }

        var entries = new List<PlaylistEntry>();
        foreach (var info in results.OfType<MediaInfo>())
        {
            if (info.IsPlaylist) entries.AddRange(info.Entries);
            else entries.Add(new PlaylistEntry
            {
                Id = info.Id, Url = info.Url, Title = info.Title, Uploader = info.Uploader,
                DurationSeconds = info.DurationSeconds, Thumbnail = info.Thumbnail,
            });
        }
        entries = entries.DistinctBy(e => e.Url)
            .Select((e, i) => new PlaylistEntry
            {
                Index = i + 1, Id = e.Id, Url = e.Url, Title = e.Title, Uploader = e.Uploader,
                DurationSeconds = e.DurationSeconds, Thumbnail = e.Thumbnail,
            })
            .ToList();

        if (errors.Count > 0)
            InspectError = $"Could not read {errors.Count} of {targets.Count} lines: " + string.Join("; ", errors.Take(3)) + (errors.Count > 3 ? " …" : "");
        if (entries.Count == 0) return;

        var list = new MediaInfo
        {
            Id = listName,
            Url = listName,
            Title = listName,
            Extractor = "Import list",
            IsPlaylist = true,
            Entries = entries,
            Thumbnail = entries[0].Thumbnail,
        };
        Card = new DownloadCardViewModel(list, _services, CloseCard, CardSource.Import);
    }

    private void CloseCard(bool queued)
    {
        Card = null;
        if (queued) SelectedSection = Section.Downloads;
    }

    [RelayCommand]
    private void CloseCardWithoutDownload() => Card = null;

    [RelayCommand]
    private void DismissError() => InspectError = null;

    [RelayCommand]
    private async Task InstallTools()
    {
        SelectedSection = Section.Settings;
        await Settings.InstallMissingAsync();
        RefreshToolsState();
    }
}
