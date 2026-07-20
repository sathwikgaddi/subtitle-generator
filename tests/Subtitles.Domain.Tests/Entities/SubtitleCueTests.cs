using Subtitles.Domain.Entities;
using Xunit;

namespace Subtitles.Domain.Tests.Entities;

public class SubtitleCueTests
{
    [Fact]
    public void Text_WithNoManualEdit_ReturnsGeneratedText()
    {
        var cue = new SubtitleCue { GeneratedText = "ఈరోజు మనం మాట్లాడుకుందాం" };

        Assert.Equal("ఈరోజు మనం మాట్లాడుకుందాం", cue.Text);
        Assert.False(cue.IsManuallyEdited);
    }

    [Fact]
    public void ApplyManualEdit_SetsEditedTextAndFlag_WithoutTouchingGeneratedText()
    {
        var cue = new SubtitleCue { GeneratedText = "original" };
        var now = DateTimeOffset.UtcNow;

        cue.ApplyManualEdit("corrected", now);

        Assert.Equal("corrected", cue.Text);
        Assert.Equal("original", cue.GeneratedText);
        Assert.True(cue.IsManuallyEdited);
        Assert.Equal(now, cue.UpdatedAt);
    }
}
