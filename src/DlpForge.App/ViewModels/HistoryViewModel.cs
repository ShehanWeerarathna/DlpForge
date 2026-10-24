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

public enum HistorySort { Newest, Oldest, Title, Size }

/// <summary>The History screen: every completed download, searchable and sortable.</summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<string> _inspect;

    public ObservableCollection<DownloadItem> Items { get; } = [];

    public IReadOnlyList<Choice<HistorySort>> SortChoices { get; } =
    [
        new(HistorySort.Newest, "Newest first"),
        new(HistorySort.Oldest, "Oldest first"),
        new(HistorySort.Title, "Title A–Z"),
        new(HistorySort.Size, "Largest first"),
    ];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private Choice<HistorySort> _sort;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private DownloadItem? _selected;

    public HistoryViewModel(AppServices services, Action<string> inspect)
    {
        _services = services;
        _inspect = inspect;
        _sort = SortChoices[0];
        services.Queue.Items.CollectionChanged += OnItemsChanged;
        foreach (var i in services.Queue.Items) i.PropertyChanged += OnItemPropertyChanged;
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
        if (e.PropertyName == nameof(DownloadItem.Status)) Refresh();
    }

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnSortChanged(Choice<HistorySort> value) => Refresh();

    private void Refresh()
    {
        var all = _services.Queue.Items.Where(i => i.Status == DownloadStatus.Completed).ToList();
        IEnumerable<DownloadItem> q = all;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            q = q.Where(i => i.Title.Contains(s, StringComparison.OrdinalIgnoreCase)
                             || (i.Uploader?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                             || i.Url.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        q = Sort.Value switch
        {
            HistorySort.Oldest => q.OrderBy(i => i.CompletedAt ?? i.CreatedAt),
            HistorySort.Title => q.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
            HistorySort.Size => q.OrderByDescending(i => i.SizeBytes ?? 0),
            _ => q.OrderByDescending(i => i.CompletedAt ?? i.CreatedAt),
        };

        Items.Clear();
        foreach (var i in q) Items.Add(i);
        IsEmpty = Items.Count == 0;
        long total = all.Sum(i => i.SizeBytes ?? 0);
        Summary = all.Count == 0 ? "" : $"{all.Count} downloads · {Formatting.Bytes(total)}";
    }

    [RelayCommand] private void OpenFile(DownloadItem? item) => Shell.Open(item?.FilePath);
    [RelayCommand] private void ShowInFolder(DownloadItem? item) => Shell.ShowInFolder(item?.FilePath);
    [RelayCommand] private void OpenLink(DownloadItem? item) => Shell.Open(item?.Url);
    [RelayCommand] private void ViewLog(DownloadItem? item) { if (item != null) LogWindow.ShowFor(item.Title, item.LogPath); }

    /// <summary>Downloads the same link again with the same settings.</summary>
    [RelayCommand]
    private void DownloadAgain(DownloadItem? item)
    {
        if (item is null) return;
        _services.Queue.Enqueue(item.Url, item.Title, item.Options, item.Thumbnail, item.Uploader, item.DurationSeconds);
        NotificationService.ShowInApp("Added to queue", item.Title);
    }

    /// <summary>Opens the download card for the link so the user can pick different settings.</summary>
    [RelayCommand]
    private void DownloadWithOtherSettings(DownloadItem? item)
    {
        if (item != null) _inspect(item.Url);
    }

    [RelayCommand]
    private void RemoveFromHistory(DownloadItem? item)
    {
        if (item != null) _services.Queue.Remove(item);
    }
}
