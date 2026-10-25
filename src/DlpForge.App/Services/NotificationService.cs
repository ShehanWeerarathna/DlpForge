using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DlpForge.App.Services;

/// <summary>
/// Windows toast notifications with "Open" and "Show in folder" buttons, plus small in-app
/// notifications for errors and confirmations.
/// </summary>
public static class NotificationService
{
    private static WindowNotificationManager? _inApp;
    private static bool _toastsAvailable;

    public static void Initialize(Window window)
    {
        _inApp = new WindowNotificationManager(window) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
        try
        {
            ToastNotificationManagerCompat.OnActivated += e =>
            {
                var args = ToastArguments.Parse(e.Argument);
                if (!args.TryGetValue("path", out var path))
                {
                    // A click on the notification itself brings DlpForge back, also from the tray.
                    Dispatcher.UIThread.Post(() => TrayService.ShowMainWindow());
                    return;
                }
                if (args.TryGetValue("action", out var action) && action == "folder") Shell.ShowInFolder(path);
                else Shell.Open(path);
            };
            _toastsAvailable = true;
        }
        catch (Exception)
        {
            // Toasts need a desktop session with notification support; fall back to in-app notices.
            _toastsAvailable = false;
        }
    }

    public static void DownloadFinished(string title, string? filePath)
    {
        if (_toastsAvailable)
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText("Download finished")
                    .AddText(title);
                if (filePath != null)
                {
                    builder.AddButton(new ToastButton().SetContent("Open").AddArgument("action", "open").AddArgument("path", filePath));
                    builder.AddButton(new ToastButton().SetContent("Show in folder").AddArgument("action", "folder").AddArgument("path", filePath));
                }
                builder.Show();
                return;
            }
            catch (Exception)
            {
                _toastsAvailable = false;
            }
        }
        ShowInApp("Download finished", title);
    }

    public static void DownloadFailed(string title, string? error)
    {
        if (_toastsAvailable)
        {
            try
            {
                new ToastContentBuilder().AddText("Download failed").AddText(title).AddText(error ?? "").Show();
                return;
            }
            catch (Exception)
            {
                _toastsAvailable = false;
            }
        }
        ShowInApp("Download failed", title, error: true);
    }

    /// <summary>A plain Windows notification, for when the window may be hidden (for example in the tray).</summary>
    public static void Info(string title, string message)
    {
        if (_toastsAvailable)
        {
            try
            {
                new ToastContentBuilder().AddText(title).AddText(message).Show();
                return;
            }
            catch (Exception)
            {
                _toastsAvailable = false;
            }
        }
        ShowInApp(title, message);
    }

    public static void ShowInApp(string title, string message, bool error = false)
    {
        Dispatcher.UIThread.Post(() =>
            _inApp?.Show(new Notification(title, message, error ? NotificationType.Error : NotificationType.Information)));
    }
}
