using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using DlpForge.Core.Queue;
using DlpForge.Core.Storage;

namespace DlpForge.App.Services;

/// <summary>
/// Tray mode: an icon in the notification area, and closing the window hides it there so downloads
/// (and scheduled downloads) keep going. Quit from the icon's menu ends DlpForge.
/// </summary>
public sealed class TrayService
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly AppSettings _settings;
    private readonly TrayIcon _icon;
    private bool _quitting;
    private bool _hintShown;

    public TrayService(IClassicDesktopStyleApplicationLifetime desktop, Window window, AppSettings settings, DownloadQueue queue)
    {
        _desktop = desktop;
        _settings = settings;

        var menu = new NativeMenu();
        menu.Items.Add(Item("Show DlpForge", ShowMainWindow));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Pause all", queue.PauseAll));
        menu.Items.Add(Item("Resume all", queue.ResumeAll));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Quit DlpForge", Quit));

        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DlpForge/Assets/dlpforge.ico"))),
            ToolTipText = "DlpForge",
            Menu = menu,
            IsVisible = settings.CloseToTray,
        };
        _icon.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(Avalonia.Application.Current!, [_icon]);

        window.Closing += OnWindowClosing;
        desktop.ShutdownRequested += (_, _) => _icon.IsVisible = false;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Shows or hides the tray icon when the setting changes.</summary>
    public void SetEnabled(bool enabled) => _icon.IsVisible = enabled;

    /// <summary>Tray tooltip, e.g. "DlpForge · 2 downloading, 1 queued".</summary>
    public void SetStatus(string status) => _icon.ToolTipText = $"DlpForge · {status}";

    public static void ShowMainWindow()
    {
        if (Dialogs.MainWindow is not { } w) return;
        w.ShowInTaskbar = true;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    public void Quit()
    {
        _quitting = true;
        _desktop.Shutdown();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitting || !_settings.CloseToTray) return;
        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;

        e.Cancel = true;
        if (sender is Window w) w.Hide();
        if (!_hintShown)
        {
            _hintShown = true;
            NotificationService.Info("DlpForge is still running",
                "Downloads continue in the background. Use the tray icon to open DlpForge or quit.");
        }
    }
}
