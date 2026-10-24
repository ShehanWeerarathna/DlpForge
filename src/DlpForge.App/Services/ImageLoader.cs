using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace DlpForge.App.Services;

/// <summary>
/// Attached property that loads a remote thumbnail into an Image:
/// &lt;Image svc:ImageLoader.Url="{Binding Thumbnail}" /&gt;. Keeps a small in-memory cache.
/// </summary>
public static class ImageLoader
{
    public static HttpClient? Http { get; set; }

    private const int MaxCached = 300;
    private static readonly Dictionary<string, Task<Bitmap?>> Cache = [];
    private static readonly LinkedList<string> Order = new();

    public static readonly AttachedProperty<string?> UrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Url", typeof(ImageLoader));

    static ImageLoader()
    {
        UrlProperty.Changed.AddClassHandler<Image>((image, e) => _ = LoadAsync(image, e.NewValue as string));
    }

    public static string? GetUrl(Image element) => element.GetValue(UrlProperty);
    public static void SetUrl(Image element, string? value) => element.SetValue(UrlProperty, value);

    private static async Task LoadAsync(Image image, string? url)
    {
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url) || Http is null) return;

        var bitmap = await Get(url);
        // The control may have been recycled for another item while we waited.
        if (GetUrl(image) == url) image.Source = bitmap;
    }

    private static Task<Bitmap?> Get(string url)
    {
        if (Cache.TryGetValue(url, out var existing)) return existing;
        var task = Fetch(url);
        Cache[url] = task;
        Order.AddLast(url);
        if (Order.Count > MaxCached)
        {
            Cache.Remove(Order.First!.Value);
            Order.RemoveFirst();
        }
        return task;
    }

    private static async Task<Bitmap?> Fetch(string url)
    {
        try
        {
            var bytes = await Http!.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            // Decode at list size to keep memory low.
            return await Task.Run(() => Bitmap.DecodeToWidth(ms, 320));
        }
        catch (Exception)
        {
            // Some thumbnails are webp or unavailable; show the placeholder instead.
            return null;
        }
    }
}
