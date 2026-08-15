using Subtitles.Domain.Entities;
using Subtitles.Domain.Exporting;
using Xunit;

namespace Subtitles.Domain.Tests.Exporting;

public class SubtitleExportFormatterTests
{
    private static List<SubtitleCue> SampleCues() =>
    [
        new SubtitleCue { SequenceNumber = 1, StartTimeMs = 0, EndTimeMs = 1558, GeneratedText = "Hi, hello." },
        new SubtitleCue { SequenceNumber = 2, StartTimeMs = 61558, EndTimeMs = 63232, GeneratedText = "One minute later." },
    ];

    [Fact]
    public void ToSrt_FormatsTimestampsWithCommaAndSequentialIndices()
    {
        var srt = SubtitleExportFormatter.ToSrt(SampleCues());

        Assert.Equal(
            "1\r\n" +
            "00:00:00,000 --> 00:00:01,558\r\n" +
            "Hi, hello.\r\n" +
            "\r\n" +
            "2\r\n" +
            "00:01:01,558 --> 00:01:03,232\r\n" +
            "One minute later.\r\n" +
            "\r\n",
            srt);
    }

    [Fact]
    public void ToVtt_StartsWithHeaderAndFormatsTimestampsWithPeriod()
    {
        var vtt = SubtitleExportFormatter.ToVtt(SampleCues());

        Assert.StartsWith("WEBVTT\r\n\r\n", vtt);
        Assert.Contains("00:00:00.000 --> 00:00:01.558\r\nHi, hello.", vtt);
        Assert.Contains("00:01:01.558 --> 00:01:03.232\r\nOne minute later.", vtt);
        Assert.DoesNotContain("00:00:00,000", vtt); // no SRT-style comma millisecond separator
    }

    [Fact]
    public void ToSrt_UsesEditedTextWhenPresent()
    {
        var cues = new List<SubtitleCue>
        {
            new() { SequenceNumber = 1, StartTimeMs = 0, EndTimeMs = 1000, GeneratedText = "original", EditedText = "corrected" },
        };

        var srt = SubtitleExportFormatter.ToSrt(cues);

        Assert.Contains("corrected", srt);
        Assert.DoesNotContain("original", srt);
    }

    [Fact]
    public void ToSrt_OrdersCuesBySequenceNumberRegardlessOfInputOrder()
    {
        var cues = new List<SubtitleCue>
        {
            new() { SequenceNumber = 2, StartTimeMs = 1000, EndTimeMs = 2000, GeneratedText = "second" },
            new() { SequenceNumber = 1, StartTimeMs = 0, EndTimeMs = 1000, GeneratedText = "first" },
        };

        var srt = SubtitleExportFormatter.ToSrt(cues);

        Assert.True(srt.IndexOf("first", StringComparison.Ordinal) < srt.IndexOf("second", StringComparison.Ordinal));
    }
}
