using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DlpForge.App.Services;
using DlpForge.Core;
using DlpForge.Core.Tools;
using DlpForge.Core.YtDlp;

namespace DlpForge.App.ViewModels;

public enum Section { Downloads, History, Settings }

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private CancellationTokenSource? _inspectCts;

    public DownloadsViewModel Downloads { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private string _urlInput = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(InspectCommand))] private bool _isInspecting;
    [ObservableProperty] private string? _inspectError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsCardOpen))] private DownloadCardViewModel? _card;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadsSection), nameof(IsHistorySection), nameof(IsSettingsSection))]
    private Section _selectedSection = Section.Downloads;

    [ObservableProperty] private bool _toolsMissing;
    [ObservableProperty] private string _statusText = "";

    public bool IsCardOpen => Card != null;
    public bool IsDownloadsSection => SelectedSection == Section.Downloads;
    public bool IsHistorySection => SelectedSection == Section.History;
    public bool IsSettingsSection => SelectedSection == Section.Settings;

    /// <summary>Set by the window once it has a native handle.</summary>
    public TaskbarProgress? Taskbar { get; set; }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        Downloads = new DownloadsViewModel(services);
        History = new HistoryViewModel(services, url => _ = InspectAsync(url));
        Settings = new SettingsViewModel(services, RefreshToolsState);

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
        int queued = items.Count(i => i.Status == Core.Models.DownloadStatus.Queued);
        StatusText = active == 0 && queued == 0
            ? "Idle"
            : $"{active} downloading, {queued} queued";
    }

    [RelayCommand]
    private void Navigate(Section section) => SelectedSection = section;

    private bool CanInspect() => !IsInspecting;

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private Task Inspect() => InspectAsync(UrlInput);

    /// <summary>Inspects one link and opens the download card. Several links are queued straight away with default settings.</summary>
    public async Task InspectAsync(string? input)
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
        if (!LinkParser.LooksLikeInput(target))
        {
            InspectError = "That doesn't look like a link. Paste a URL, or search with ytsearch10:your words.";
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
        UrlInput = target;
        InspectError = null;
        IsInspecting = true;
        try
        {
            var info = await _services.Client.InspectAsync(target, ct);
            Card = new DownloadCardViewModel(info, _services, CloseCard);
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
