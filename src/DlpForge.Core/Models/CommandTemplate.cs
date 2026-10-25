using System.Text.Json;

namespace DlpForge.Core.Models;

/// <summary>A saved set of extra yt-dlp arguments, picked on the download card or run in the terminal.</summary>
public sealed record CommandTemplate(string Name, string Arguments)
{
    public override string ToString() => Name;

    public static List<CommandTemplate> Samples() =>
    [
        new("Write description and info JSON", "--write-description --write-info-json"),
        new("Limit speed to 2 MB/s", "--limit-rate 2M"),
        new("Save thumbnail as a separate file", "--write-thumbnail --convert-thumbnails jpg"),
    ];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The import/export file: a JSON list of { "Name": ..., "Arguments": ... }.</summary>
    public static string Export(IEnumerable<CommandTemplate> templates) => JsonSerializer.Serialize(templates.ToList(), Json);

    /// <summary>Reads an exported file. Entries without a name or arguments are skipped.</summary>
    public static List<CommandTemplate> Import(string json)
    {
        var list = JsonSerializer.Deserialize<List<CommandTemplate>>(json, Json) ?? [];
        return list.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Arguments))
                   .Select(t => t with { Name = t.Name.Trim(), Arguments = t.Arguments.Trim() })
                   .ToList();
    }

    /// <summary>Adds imported templates; one with the same name replaces the existing one.</summary>
    public static List<CommandTemplate> Merge(IEnumerable<CommandTemplate> existing, IEnumerable<CommandTemplate> incoming)
    {
        var result = existing.ToList();
        foreach (var t in incoming)
        {
            int i = result.FindIndex(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) result[i] = t; else result.Add(t);
        }
        return result;
    }
}
