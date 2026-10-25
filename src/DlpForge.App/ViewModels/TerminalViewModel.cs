using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DlpForge.App.Services;
using DlpForge.Core;
using DlpForge.Core.Models;

namespace DlpForge.App.ViewModels;

/// <summary>
/// The Terminal page: run yt-dlp with any arguments and watch its output. Commands run without a shell,
/// so only yt-dlp itself is started.
/// </summary>
public sealed partial class TerminalViewModel : ViewModelBase
{
    private const int MaxOutput = 200_000;
    private const int MaxHistory = 50;

    private readonly AppServices _services;
    private readonly StringBuilder _pending = new();
    private readonly DispatcherTimer _flush;
    private CancellationTokenSource? _cts;
    private int _historyIndex = -1;

    [ObservableProperty] private string _command = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(StopCommand))] private bool _isRunning;
    [ObservableProperty] private bool _useAppOptions = true;
    [ObservableProperty] private string _workingFolder;
    [ObservableProperty] private CommandTemplate? _selectedTemplate;
    [ObservableProperty] private IReadOnlyList<CommandTemplate> _templates = [];

    public TerminalViewModel(AppServices services)
    {
        _services = services;
        _workingFolder = services.Settings.VideoFolder;
        _flush = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Flush());
        ReloadTemplates();
    }

    /// <summary>Called when the page is shown, so templates edited in Settings appear.</summary>
    public void ReloadTemplates() => Templates = _services.Settings.CommandTemplates.ToList();

    partial void OnSelectedTemplateChanged(CommandTemplate? value)
    {
        if (value != null) Command = value.Arguments;
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        var text = Command.Trim();
        if (text.StartsWith("yt-dlp ", StringComparison.OrdinalIgnoreCase)) text = text[7..].Trim();
        var args = CommandLine.Split(text);
        if (args.Count == 0) return;
        if (_services.Tools.YtDlp is null)
        {
            Append("yt-dlp isn't installed yet. Open Settings > Tools and click Install.\n");
            return;
        }

        Remember(text);
        Append($"> yt-dlp {CommandLine.Join(args)}" + (UseAppOptions ? "   (with DlpForge's FFmpeg, Deno and cookie options)" : "") + "\n");
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _flush.Start();
        try
        {
            var folder = string.IsNullOrWhiteSpace(WorkingFolder) ? null : WorkingFolder;
            int code = await Task.Run(() => _services.Client.RunTerminalAsync(args, UseAppOptions, folder, Append, _cts.Token));
            Append($"[exit code {code}]\n\n");
        }
        catch (OperationCanceledException)
        {
            Append("[stopped]\n\n");
        }
        catch (Exception ex)
        {
            Append($"[could not run yt-dlp: {ex.Message}]\n\n");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            Flush();
            _flush.Stop();
        }
    }

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        lock (_pending) _pending.Clear();
        Output = "";
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (!string.IsNullOrWhiteSpace(WorkingFolder)) Shell.OpenFolder(WorkingFolder);
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await Dialogs.PickFolderAsync("Folder the command runs in", WorkingFolder) is { } path) WorkingFolder = path;
    }

    /// <summary>Saves the typed arguments as a command template, for the download card and this page.</summary>
    [RelayCommand]
    private void SaveAsTemplate()
    {
        var text = Command.Trim();
        if (text.Length == 0) return;
        var name = text.Length > 40 ? text[..40] + "…" : text;
        _services.Settings.CommandTemplates = CommandTemplate.Merge(_services.Settings.CommandTemplates, [new CommandTemplate(name, text)]);
        _services.SaveSettings();
        ReloadTemplates();
        NotificationService.ShowInApp("Template saved", $"\"{name}\" is now in Settings > Command templates.");
    }

    /// <summary>Up and Down in the command box walk through earlier commands.</summary>
    public void HistoryStep(int direction)
    {
        var history = _services.Settings.TerminalHistory;
        if (history.Count == 0) return;
        if (_historyIndex < 0)
        {
            if (direction > 0) return;
            _historyIndex = history.Count - 1;
        }
        else
        {
            _historyIndex = Math.Max(0, _historyIndex + direction);
            if (_historyIndex >= history.Count)
            {
                // Past the newest command: back to an empty box.
                _historyIndex = -1;
                Command = "";
                return;
            }
        }
        Command = history[_historyIndex];
    }

    private void Remember(string command)
    {
        var history = _services.Settings.TerminalHistory;
        history.Remove(command);
        history.Add(command);
        if (history.Count > MaxHistory) history.RemoveRange(0, history.Count - MaxHistory);
        _historyIndex = -1;
        _services.SaveSettings();
    }

    /// <summary>Output arrives on a background thread; it is batched and shown a few times a second.</summary>
    private void Append(string line)
    {
        lock (_pending)
        {
            _pending.Append(line);
            if (!line.EndsWith('\n')) _pending.Append('\n');
        }
        if (Dispatcher.UIThread.CheckAccess() && !IsRunning) Flush();
    }

    private void Flush()
    {
        string add;
        lock (_pending)
        {
            if (_pending.Length == 0) return;
            add = _pending.ToString();
            _pending.Clear();
        }
        var text = Output + add;
        Output = text.Length > MaxOutput ? text[^MaxOutput..] : text;
    }
}
