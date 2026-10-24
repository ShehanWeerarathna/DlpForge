namespace DlpForge.Core;

/// <summary>
/// Where DlpForge keeps its data. Normally %LOCALAPPDATA%\DlpForge; in portable mode
/// (a file named "portable.txt" next to the exe) everything lives in a "data" folder beside the exe.
/// </summary>
public sealed class AppPaths
{
    public string DataDir { get; }
    public string ToolsDir => Path.Combine(DataDir, "tools");
    public string LogsDir => Path.Combine(DataDir, "logs");
    public string DatabasePath => Path.Combine(DataDir, "dlpforge.db");
    public string SettingsPath => Path.Combine(DataDir, "settings.json");
    public bool IsPortable { get; }

    public AppPaths(string? dataDirOverride = null)
    {
        var exeDir = AppContext.BaseDirectory;
        if (dataDirOverride != null)
        {
            DataDir = dataDirOverride;
        }
        else if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            IsPortable = true;
            DataDir = Path.Combine(exeDir, "data");
        }
        else
        {
            DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DlpForge");
        }

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ToolsDir);
        Directory.CreateDirectory(LogsDir);
    }

    public static string DefaultVideoFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "DlpForge");

    public static string DefaultAudioFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "DlpForge");
}
