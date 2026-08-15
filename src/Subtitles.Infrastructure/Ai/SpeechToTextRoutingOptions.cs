namespace Subtitles.Infrastructure.Ai;

/// <summary>
/// Config for <see cref="RoutedSpeechToTextProvider"/> — which concrete provider handles a
/// given video depends only on its language hint, since that's the only signal known before
/// Transcribe actually runs (a hint-less/auto-detect video has no language signal yet, hence
/// DefaultProvider rather than a per-language guess).
/// </summary>
public class SpeechToTextRoutingOptions
{
    /// <summary>Provider name ("OpenAi" or "Sarvam") used when there's no hint, or the hint
    /// isn't in <see cref="ProviderByLanguageHint"/>.</summary>
    public string DefaultProvider { get; set; } = null!;

    /// <summary>ISO-639-1 language hint -> provider name, e.g. { "en": "OpenAi" }.</summary>
    public Dictionary<string, string> ProviderByLanguageHint { get; set; } = new();
}
