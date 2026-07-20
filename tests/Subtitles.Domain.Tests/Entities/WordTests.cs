using Subtitles.Domain.Entities;
using Xunit;

namespace Subtitles.Domain.Tests.Entities;

public class WordTests
{
    [Fact]
    public void IsHighlighted_WithNoManualOverride_ReflectsAutoValue()
    {
        var word = new Word { IsHighlightedAuto = true };

        Assert.True(word.IsHighlighted);
    }

    [Fact]
    public void IsHighlighted_WithManualOverrideOff_WinsOverAutoOn()
    {
        var word = new Word { IsHighlightedAuto = true };

        word.SetManualHighlight(false);

        Assert.False(word.IsHighlighted);
    }

    [Fact]
    public void IsHighlighted_WithManualOverrideOn_WinsOverAutoOff()
    {
        var word = new Word { IsHighlightedAuto = false };

        word.SetManualHighlight(true);

        Assert.True(word.IsHighlighted);
    }

    [Fact]
    public void SetManualHighlight_Null_ClearsOverride_DefersToAuto()
    {
        var word = new Word { IsHighlightedAuto = true };
        word.SetManualHighlight(false);

        word.SetManualHighlight(null);

        Assert.True(word.IsHighlighted);
    }
}
