using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using DlpForge.App.Services;
using DlpForge.App.ViewModels;
using DlpForge.App.Views;
using DlpForge.Core;
using DlpForge.Core.Queue;
using DlpForge.Core.Storage;
using DlpForge.Core.Tools;
using DlpForge.Core.YtDlp;

namespace DlpForge.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths();
            var settings = AppSettings.Load(paths.SettingsPath);
            ApplyTheme(settings.Theme);

            var locator = new ToolLocator(paths);
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DlpForge/0.1 (+https://github.com/ShehanWeerarathna)");
            ImageLoader.Http = http;

            var client = new YtDlpClient(locator);
            var store = new DownloadStore(paths.DatabasePath);
            var queue = new DownloadQueue(client, store, paths, new AvaloniaSynchronizationContext())
            {
                MaxConcurrent = settings.MaxConcurrentDownloads,
            };
            var services = new AppServices(paths, settings, locator, client, new ToolInstaller(locator, http), queue);

            var vm = new MainWindowViewModel(services);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            desktop.ShutdownRequested += (_, _) =>
            {
                queue.StopAllForExit();
                queue.FlushAsync().Wait(TimeSpan.FromSeconds(3));
            };

            queue.Restore();
            NotificationService.Initialize(window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(ThemeMode mode)
    {
        if (Current is null) return;
        Current.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
