using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DlpForge.App.Services;
using DlpForge.App.Views;
using DlpForge.Core.Models;
using DlpForge.Core.Queue;

namespace DlpForge.App.ViewModels;

public enum QueueFilter { All, Active, Queued, Scheduled, Completed, Failed }

/// <summary>The Downloads screen: the live queue with filter tabs and per-item actions.</summary>
public sealed partial class DownloadsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private DownloadQueue Queue => _services.Queue;

    public ObservableCollection<DownloadItem> Visible { get; } = [];

    [ObservableProperty] private QueueFilter _filter = QueueFilter.All;
    [ObservableProperty] private DownloadItem? _selected;
    [ObservableProperty] private string _allLabel = "All";
    [ObservableProperty] private string _activeLabel = "Active";
    [ObservableProperty] private string _queuedLabel = "Queued";
    [ObservableProperty] private string _scheduledLabel = "Scheduled";
    [ObservableProperty] private bool _hasScheduled;
    [ObservableProperty] private string _completedLabel = "Completed";
    [ObservableProperty] private string _failedLabel = "Failed";
    [ObservableProperty] private bool _isEmpty = true;

    public DownloadsViewModel(AppServices services)
    {
        _services = services;
        Queue.Items.CollectionChanged += OnItemsChanged;
        foreach (var i in Queue.Items) i.PropertyChanged += OnItemPropertyChanged;
        Refresh();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (DownloadItem i in e.NewItems) i.PropertyChanged += OnItemPropertyChanged;
        if (e.OldItems != null) foreach (DownloadItem i in e.OldItems) i.PropertyChanged -= OnItemPropertyChanged;
        Refresh();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.HiddenFromQueue) or nameof(DownloadItem.ScheduledAt)) Refresh();
    }

    partial void OnFilterChanged(QueueFilter value) => Refresh();

    private bool Matches(DownloadItem i, QueueFilter f) => !i.HiddenFromQueue && f switch
    {
        QueueFilter.Active => i.IsActive,
        QueueFilter.Queued => i.Status is DownloadStatus.Queued or DownloadStatus.Paused,
        QueueFilter.Scheduled => i.IsScheduled,
        QueueFilter.Completed => i.Status == DownloadStatus.Completed,
        QueueFilter.Failed => i.Status is DownloadStatus.Failed or DownloadStatus.Cancelled,
        _ => true,
    };

    /// <summary>Re-syncs the visible list in place (newest first) so selection and scroll position survive.</summary>
    private void Refresh()
    {
        var wanted = Queue.Items.Where(i => Matches(i, Filter)).Reverse().ToList();
        for (int i = Visible.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Visible[i])) Visible.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < Visible.Count && ReferenceEquals(Visible[i], wanted[i])) continue;
            int existing = Visible.IndexOf(wanted[i]);
            if (existing >= 0) Visible.Move(existing, i);
            else Visible.Insert(i, wanted[i]);
        }

        IsEmpty = Visible.Count == 0;
        AllLabel = Label("All", QueueFilter.All);
        ActiveLabel = Label("Active", QueueFilter.Active);
        QueuedLabel = Label("Queued", QueueFilter.Queued);
        ScheduledLabel = Label("Scheduled", QueueFilter.Scheduled);
        HasScheduled = Queue.Items.Any(i => Matches(i, QueueFilter.Scheduled)) || Filter == QueueFilter.Scheduled;
        CompletedLabel = Label("Completed", QueueFilter.Completed);
        FailedLabel = Label("Failed", QueueFilter.Failed);
    }

    private string Label(string name, QueueFilter f)
    {
        int n = Queue.Items.Count(i => Matches(i, f));
        return n == 0 ? name : $"{name} ({n})";
    }

    [RelayCommand] private void SetFilter(QueueFilter f) => Filter = f;
    [RelayCommand] private void Pause(DownloadItem? item) { if (item != null) Queue.Pause(item); }
    [RelayCommand] private void Resume(DownloadItem? item) { if (item != null) Queue.Resume(item); }
    [RelayCommand] private void Cancel(DownloadItem? item) { if (item != null) Queue.Cancel(item); }
    [RelayCommand] private void Retry(DownloadItem? item) { if (item != null) Queue.Retry(item); }
    [RelayCommand] private void OpenFile(DownloadItem? item) => Shell.Open(item?.FilePath);
    [RelayCommand] private void ShowInFolder(DownloadItem? item) => Shell.ShowInFolder(item?.FilePath ?? item?.Options.OutputFolder);
    [RelayCommand] private void OpenLink(DownloadItem? item) => Shell.Open(item?.Url);
    [RelayCommand] private void ViewLog(DownloadItem? item) { if (item != null) LogWindow.ShowFor(item.Title, item.LogPath); }

    [RelayCommand]
    private void Remove(DownloadItem? item)
    {
        if (item is null) return;
        // Finished items just leave this list (they stay in History); unfinished ones are cancelled and deleted.
        if (item.Status == DownloadStatus.Completed) Queue.HideFromQueue(item);
        else Queue.Remove(item);
    }

    /// <summary>Opens the schedule dialog for a queued or paused download.</summary>
    [RelayCommand]
    private async Task Schedule(DownloadItem? item)
    {
        if (item is null || !item.CanSchedule || Dialogs.MainWindow is not { } owner) return;
        var result = await ScheduleWindow.Ask(owner, item.Title, item.ScheduledAt);
        if (result is null || !Queue.Items.Contains(item)) return;
        Queue.Schedule(item, result.At);
    }

    [RelayCommand] private void StartNow(DownloadItem? item) { if (item != null) Queue.StartNow(item); }

    [RelayCommand] private void PauseAll() => Queue.PauseAll();
    [RelayCommand] private void ResumeAll() => Queue.ResumeAll();
    [RelayCommand] private void ClearFinished() => Queue.ClearFinished();
}
