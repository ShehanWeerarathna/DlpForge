using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using DlpForge.App.Services;

namespace DlpForge.App.Views;

/// <summary>Shows the full yt-dlp output for one download.</summary>
public partial class LogWindow : Window
{
    private string? _path;

    public LogWindow() => InitializeComponent();

    public static void ShowFor(string title, string? logPath)
    {
        var window = new LogWindow { _path = logPath };
        window.Heading.Text = title;
        window.LogText.Text = ReadLog(logPath);
        var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner != null) window.Show(owner); else window.Show();
        window.LogText.CaretIndex = window.LogText.Text?.Length ?? 0;
    }

    private static string ReadLog(string? path)
    {
        if (path is null || !File.Exists(path)) return "No log yet. It is written when the download starts.";
        try
        {
            // The log may still be open for writing by a running download.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            return "Could not read the log: " + ex.Message;
        }
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(LogText.Text ?? "");
    }

    private void OnOpenFile(object? sender, RoutedEventArgs e) => Shell.Open(_path);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
