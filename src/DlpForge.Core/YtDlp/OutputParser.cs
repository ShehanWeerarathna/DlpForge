using System.Text.Json;
using DlpForge.Core.Models;

namespace DlpForge.Core.YtDlp;

/// <summary>What one line of yt-dlp output means to DlpForge.</summary>
public abstract record OutputLine
{
    public sealed record Progress(DownloadProgress Value) : OutputLine;
    public sealed record FinalPath(string Path) : OutputLine;
    public sealed record Error(string Message) : OutputLine;
    public sealed record Text(string Line) : OutputLine;
}

public static class OutputParser
{
    public static OutputLine Parse(string line)
    {
        if (line.StartsWith(ArgumentBuilder.ProgressMarker, StringComparison.Ordinal))
        {
            var p = ParseProgressJson(line[ArgumentBuilder.ProgressMarker.Length..]);
            return p is null ? new OutputLine.Text(line) : new OutputLine.Progress(p);
        }
        if (line.StartsWith(ArgumentBuilder.PostprocessMarker, StringComparison.Ordinal))
        {
            var name = line[ArgumentBuilder.PostprocessMarker.Length..].Trim();
            return new OutputLine.Progress(new DownloadProgress
            {
                Stage = "processing",
                Postprocessor = name is "" or "NA" ? null : name,
            });
        }
        if (line.StartsWith(ArgumentBuilder.FileMarker, StringComparison.Ordinal))
            return new OutputLine.FinalPath(line[ArgumentBuilder.FileMarker.Length..].Trim());
        if (line.StartsWith("ERROR:", StringComparison.Ordinal))
            return new OutputLine.Error(line["ERROR:".Length..].Trim());
        return new OutputLine.Text(line);
    }

    internal static DownloadProgress? ParseProgressJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;

            long? done = r.Long("downloaded_bytes");
            long? total = r.Long("total_bytes") ?? r.Long("total_bytes_estimate");
            double? percent = null;
            if (done is long d && total is long t && t > 0)
                percent = Math.Clamp(d * 100.0 / t, 0, 100);
            else if (r.Num("fragment_index") is double fi && r.Num("fragment_count") is double fc && fc > 0)
                percent = Math.Clamp(fi * 100.0 / fc, 0, 100);

            var status = r.Str("status") ?? "downloading";
            if (status == "finished") percent = 100;

            return new DownloadProgress
            {
                Stage = status,
                Percent = percent,
                DownloadedBytes = done,
                TotalBytes = total,
                SpeedBytesPerSecond = r.Num("speed"),
                EtaSeconds = r.Num("eta"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
