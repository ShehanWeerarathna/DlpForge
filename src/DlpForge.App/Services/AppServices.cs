using DlpForge.Core;
using DlpForge.Core.Queue;
using DlpForge.Core.Storage;
using DlpForge.Core.Tools;
using DlpForge.Core.YtDlp;

namespace DlpForge.App.Services;

/// <summary>The app's long-lived objects, created once at startup and shared by the view models.</summary>
public sealed record AppServices(
    AppPaths Paths,
    AppSettings Settings,
    ToolLocator Tools,
    YtDlpClient Client,
    ToolInstaller Installer,
    DownloadQueue Queue)
{
    public void SaveSettings()
    {
        try { Settings.Save(Paths.SettingsPath); }
        catch (IOException) { /* Try again on the next change. */ }
    }
}
