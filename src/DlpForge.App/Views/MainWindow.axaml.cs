using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DlpForge.App.Services;
using DlpForge.App.ViewModels;
using DlpForge.Core;

namespace DlpForge.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Vm != null && TryGetPlatformHandle()?.Handle is { } hwnd)
            Vm.Taskbar = new TaskbarProgress(hwnd);
        UrlBox.Focus();
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (e.Key == Key.Escape && Vm.IsCardOpen)
        {
            Vm.CloseCardWithoutDownloadCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.L)
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F && Vm.IsHistorySection)
        {
            HistoryPage.FocusSearch();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.V && FocusManager?.GetFocusedElement() is not TextBox && !Vm.IsCardOpen)
        {
            // Ctrl+V anywhere outside a text box: paste the clipboard link and open it.
            e.Handled = true;
            var text = Clipboard is { } cb ? await cb.TryGetTextAsync() : null;
            if (!string.IsNullOrWhiteSpace(text)) await Vm.InspectAsync(text);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var data = e.DataTransfer;
        e.DragEffects = data.Contains(DataFormat.Text) || data.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm is null) return;
        var links = new List<string>();

        if (e.DataTransfer.TryGetFiles() is { } files)
        {
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToList();
            // A dropped text file is an import list: links, playlists and searches, one per line.
            if (paths.Count == 1 && LinkParser.IsListFile(paths[0]))
            {
                await Vm.ImportFileAsync(paths[0]);
                return;
            }
            foreach (var path in paths)
            {
                try { links.AddRange(LinkParser.FromFile(path)); }
                catch (IOException) { }
            }
        }
        if (links.Count == 0 && e.DataTransfer.TryGetText() is { } text)
            links.AddRange(LinkParser.Extract(text));

        links = links.Distinct().ToList();
        if (links.Count == 1) await Vm.InspectAsync(links[0]);
        else if (links.Count > 1) Vm.QueueWithDefaults(links);
        else NotificationService.ShowInApp("No links found", "Drop a link, a .url shortcut or a .txt file with one link per line.", error: true);
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) =>
        Vm?.CloseCardWithoutDownloadCommand.Execute(null);
}
