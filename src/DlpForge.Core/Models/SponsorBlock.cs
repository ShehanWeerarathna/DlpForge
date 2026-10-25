namespace DlpForge.Core.Models;

public enum SponsorBlockAction { Off, Mark, Remove }

/// <summary>SponsorBlock segment categories yt-dlp understands, with a short description for the settings page.</summary>
public static class SponsorBlock
{
    public sealed record Category(string Id, string Name, string Description, bool CanRemove = true);

    public static readonly IReadOnlyList<Category> Categories =
    [
        new("sponsor", "Sponsor", "Paid promotion or advertisement"),
        new("selfpromo", "Self-promotion", "Unpaid promotion: merch, donations, shout-outs"),
        new("interaction", "Interaction reminder", "\"Like and subscribe\" in the middle of content"),
        new("intro", "Intro", "Intro animation or opening sequence"),
        new("outro", "Outro", "End cards and credits"),
        new("preview", "Preview / recap", "Clips of what is coming up or what happened before"),
        new("hook", "Hook / greeting", "Teasers and greetings at the start"),
        new("filler", "Filler", "Tangents and jokes not needed for the main content"),
        new("music_offtopic", "Non-music section", "Talking in music videos"),
        new("poi_highlight", "Highlight", "The point most people are looking for", CanRemove: false),
        new("chapter", "Chapters", "Chapter titles added by the community", CanRemove: false),
    ];

    public static Dictionary<string, SponsorBlockAction> DefaultActions() => new()
    {
        ["sponsor"] = SponsorBlockAction.Remove,
        ["selfpromo"] = SponsorBlockAction.Mark,
        ["interaction"] = SponsorBlockAction.Mark,
    };

    /// <summary>Turns per-category actions into yt-dlp's comma lists. Unknown categories are dropped.</summary>
    public static (string Mark, string Remove) ToArguments(IReadOnlyDictionary<string, SponsorBlockAction> actions)
    {
        var mark = new List<string>();
        var remove = new List<string>();
        foreach (var c in Categories)
        {
            if (!actions.TryGetValue(c.Id, out var action)) continue;
            if (action == SponsorBlockAction.Remove && c.CanRemove) remove.Add(c.Id);
            else if (action != SponsorBlockAction.Off) mark.Add(c.Id);
        }
        return (string.Join(',', mark), string.Join(',', remove));
    }
}
