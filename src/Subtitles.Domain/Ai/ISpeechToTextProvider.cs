namespace Subtitles.Domain.Ai;

/// <summary>See docs/Architecture.md §3.1.</summary>
public interface ISpeechToTextProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken ct);
}

public sealed record TranscriptionResult(
    string Text,
    string LanguageCode,
    double LanguageConfidence,
    IReadOnlyList<TranscriptionWord> Words);

public sealed record TranscriptionWord(string Text, int StartMs, int EndMs);
