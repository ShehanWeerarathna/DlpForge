using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace DlpForge.App.Services;

/// <summary>File and folder pickers, owned by the main window, for view models that have no view of their own.</summary>
public static class Dialogs
{
    public static Window? MainWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static readonly FilePickerFileType TextFiles = new("Text files") { Patterns = ["*.txt", "*.list"] };
    public static readonly FilePickerFileType JsonFiles = new("JSON files") { Patterns = ["*.json"] };
    public static readonly FilePickerFileType CookieFiles = new("Cookie files") { Patterns = ["*.txt", "*.cookies"] };

    public static async Task<string?> OpenFileAsync(string title, params FilePickerFileType[] types)
    {
        if (MainWindow is not { } top) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [.. types, FilePickerFileTypes.All],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> SaveFileAsync(string title, string suggestedName, FilePickerFileType type)
    {
        if (MainWindow is not { } top) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [type],
            DefaultExtension = type.Patterns?.FirstOrDefault()?.TrimStart('*', '.'),
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> PickFolderAsync(string title, string? startPath)
    {
        if (MainWindow is not { } top) return null;
        var start = string.IsNullOrWhiteSpace(startPath) ? null : await top.StorageProvider.TryGetFolderFromPathAsync(startPath);
        var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = start,
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }
}
