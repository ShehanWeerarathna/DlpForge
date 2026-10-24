using System.Diagnostics;

namespace DlpForge.App.Services;

/// <summary>Opens files, folders and links with Windows.</summary>
public static class Shell
{
    public static void Open(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            NotificationService.ShowInApp("Could not open", ex.Message, error: true);
        }
    }

    public static void ShowInFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
        }
        else
        {
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir)) Open(dir);
        }
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Open(path);
    }
}
