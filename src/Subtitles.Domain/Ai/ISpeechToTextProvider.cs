namespace Subtitles.Domain.Ai;

/// <summary>See docs/Architecture.md §3.1.</summary>
public interface ISpeechToTextProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    /// <summary>
    /// fileName's extension matters — Whisper's API determines audio format from it (e.g.
    /// "audio.mp3"), not from content-sniffing. Pass the real extension of what's in the stream.
    /// </summary>
    Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, CancellationToken ct);
}

public sealed record TranscriptionResult(
    string Text,
    string LanguageCode,
    double LanguageConfidence,
    IReadOnlyList<TranscriptionWord> Words);

public sealed record TranscriptionWord(string Text, int StartMs, int EndMs);
