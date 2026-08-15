using System.Text;
using Subtitles.Domain.Entities;

namespace Subtitles.Domain.Exporting;

/// <summary>
/// Renders a subtitle track's cues as SRT or VTT text — pure formatting over data already in
/// Postgres (no FFmpeg, no AI call), which is why this doesn't need the job queue the way
/// BurnedInMp4 export will (P1.14): it's fast enough to run synchronously inside the request
/// that asked for it. See docs/API.md §4.
///
/// Uses an explicit "\r\n" rather than StringBuilder.AppendLine()/Environment.NewLine —
/// deliberately, not a style choice: this is a file format with external consumers (video
/// players, browsers, YouTube), so its line endings must not depend on whatever OS the server
/// happens to run on (CI runs Linux, dev is Windows — AppendLine would silently produce
/// different bytes on each). CRLF is valid for both SRT (its traditional convention) and VTT
/// (the W3C spec explicitly allows LF, CR, or CRLF), so one fixed choice works for both.
/// </summary>
public static class SubtitleExportFormatter
{
    private const string NewLine = "\r\n";

    public static string ToSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var cue in cues.OrderBy(c => c.SequenceNumber))
        {
            sb.Append(index++).Append(NewLine);
            sb.Append(FormatSrtTimestamp(cue.StartTimeMs))
                .Append(" --> ")
                .Append(FormatSrtTimestamp(cue.EndTimeMs))
                .Append(NewLine);
            sb.Append(cue.Text).Append(NewLine);
            sb.Append(NewLine);
        }
        return sb.ToString();
    }

    public static string ToVtt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        sb.Append("WEBVTT").Append(NewLine).Append(NewLine);
        foreach (var cue in cues.OrderBy(c => c.SequenceNumber))
        {
            sb.Append(FormatVttTimestamp(cue.StartTimeMs))
                .Append(" --> ")
                .Append(FormatVttTimestamp(cue.EndTimeMs))
                .Append(NewLine);
            sb.Append(cue.Text).Append(NewLine);
            sb.Append(NewLine);
        }
        return sb.ToString();
    }

    /// <summary>SRT uses a comma before milliseconds: 00:00:01,558.</summary>
    private static string FormatSrtTimestamp(int totalMs) => FormatTimestamp(totalMs, ',');

    /// <summary>VTT uses a period before milliseconds: 00:00:01.558.</summary>
    private static string FormatVttTimestamp(int totalMs) => FormatTimestamp(totalMs, '.');

    private static string FormatTimestamp(int totalMs, char msSeparator)
    {
        var time = TimeSpan.FromMilliseconds(totalMs);
        return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}{msSeparator}{time.Milliseconds:D3}";
    }
}
