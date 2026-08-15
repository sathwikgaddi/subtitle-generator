namespace Subtitles.Domain.Ai;

/// <summary>See docs/Architecture.md §3.1.</summary>
public interface ISpeechToTextProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    /// <summary>
    /// fileName's extension matters — Whisper's API determines audio format from it (e.g.
    /// "audio.mp3"), not from content-sniffing. Pass the real extension of what's in the stream.
    /// languageHint (ISO-639-1, e.g. "te"), when given, skips the provider's own language
    /// auto-detection and biases transcription toward that language — null means auto-detect.
    /// </summary>
    Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, string? languageHint, CancellationToken ct);
}

/// <summary>
/// ProviderName/ModelName here (not just the ISpeechToTextProvider instance's own fixed
/// properties) are what TranscribeStage records as provenance — necessary once more than one
/// concrete provider can be in play for a single video (see RoutedSpeechToTextProvider), where
/// which provider actually handled a given call varies per call, not per app instance.
/// </summary>
public sealed record TranscriptionResult(
    string Text,
    string LanguageCode,
    double LanguageConfidence,
    IReadOnlyList<TranscriptionWord> Words,
    string ProviderName,
    string ModelName);

public sealed record TranscriptionWord(string Text, int StartMs, int EndMs);
