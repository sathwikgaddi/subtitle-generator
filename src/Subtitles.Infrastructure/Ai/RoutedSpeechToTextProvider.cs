using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;
using Subtitles.Infrastructure.Ai.Sarvam;

namespace Subtitles.Infrastructure.Ai;

/// <summary>
/// Picks OpenAI or Sarvam per video based on its language hint (see
/// SpeechToTextRoutingOptions) — built after confirming live that Sarvam is meaningfully more
/// accurate for Telugu specifically (OpenAI's hosted whisper-1 doesn't even accept Telugu for
/// its own language-forcing parameter — see OpenAiSpeechToTextProvider), while OpenAI's
/// general-purpose strength on English is well established. Directly coupled to these two
/// concrete provider types rather than a generic named-provider registry, since there are only
/// two — revisit that choice if a third provider is ever added.
///
/// ProviderName/ModelName on this class itself aren't meaningful per call (which concrete
/// provider actually handles a given Transcribe call varies by that video's language hint) —
/// the real per-call values are on TranscriptionResult, which is what TranscribeStage actually
/// records as provenance.
/// </summary>
public class RoutedSpeechToTextProvider(
    OpenAiSpeechToTextProvider openAi, SarvamSpeechToTextProvider sarvam, IOptions<SpeechToTextRoutingOptions> options)
    : ISpeechToTextProvider
{
    private readonly SpeechToTextRoutingOptions _options = options.Value;

    public string ProviderName => "routed";
    public string ModelName => "routed";

    public Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, string? languageHint, CancellationToken ct)
    {
        var providerName = languageHint is not null && _options.ProviderByLanguageHint.TryGetValue(languageHint, out var mapped)
            ? mapped
            : _options.DefaultProvider;

        ISpeechToTextProvider selected = providerName switch
        {
            "OpenAi" => openAi,
            "Sarvam" => sarvam,
            _ => throw new InvalidOperationException(
                $"Unknown routed speech-to-text provider '{providerName}' (Ai:SpeechToText:Routed)."),
        };

        return selected.TranscribeAsync(audio, fileName, languageHint, ct);
    }
}
