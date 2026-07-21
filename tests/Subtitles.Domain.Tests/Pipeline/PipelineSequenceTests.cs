using Subtitles.Domain.Pipeline;
using Xunit;

namespace Subtitles.Domain.Tests.Pipeline;

public class PipelineSequenceTests
{
    [Theory]
    [InlineData(JobType.ExtractAudio, JobType.Transcribe)]
    [InlineData(JobType.Transcribe, JobType.NativeCleanup)]
    [InlineData(JobType.NativeCleanup, JobType.TranslateToEnglish)]
    [InlineData(JobType.TranslateToEnglish, JobType.Romanize)]
    [InlineData(JobType.Romanize, JobType.GenerateHighlights)]
    public void GetNextStage_ReturnsTheNextStageInSequence(JobType current, JobType expectedNext)
    {
        Assert.Equal(expectedNext, PipelineSequence.GetNextStage(current));
    }

    [Theory]
    [InlineData(JobType.GenerateHighlights)]
    [InlineData(JobType.Export)]
    public void GetNextStage_ForTerminalStages_ReturnsNull(JobType terminal)
    {
        Assert.Null(PipelineSequence.GetNextStage(terminal));
    }
}
