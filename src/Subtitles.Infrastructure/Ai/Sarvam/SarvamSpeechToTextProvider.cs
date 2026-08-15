using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;

namespace Subtitles.Infrastructure.Ai.Sarvam;

/// <summary>
/// Sarvam AI implementation of <see cref="ISpeechToTextProvider"/> — a speech-to-text service
/// built specifically for Indian languages (see docs/Architecture.md §3.1's provider-swap
/// design). Being evaluated as a candidate for better Telugu/Hindi accuracy than OpenAI's
/// general-purpose Whisper, and specifically because OpenAI's hosted whisper-1 rejects Telugu
/// entirely for its language-forcing parameter (see OpenAiSpeechToTextProvider).
///
/// One real architectural gap versus Whisper: this API's own reference docs state plainly that
/// word-level timestamps are NOT supported — only chunk/phrase-level timing. Its "words" field
/// in the timestamps object is misleadingly named; each entry is often a multi-word phrase, not
/// a single word. To keep the rest of the pipeline (which expects a word list with timing, per
/// TranscriptionResult) working unchanged, each phrase's real [start, end] time is evenly split
/// across its constituent words — the same approximation approach already used elsewhere in
/// this codebase (see NativeCleanupStage) when only coarser-grained real timing is available,
/// not literal per-word ASR alignment.
/// </summary>
public class SarvamSpeechToTextProvider(HttpClient httpClient, IOptions<SarvamSttOptions> options)
    : ISpeechToTextProvider
{
    private readonly SarvamSttOptions _options = options.Value;

    public string ProviderName => "sarvam";
    public string ModelName => _options.Model;

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, string? languageHint, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(audioContent, "file", fileName);
        content.Add(new StringContent(_options.Model), "model");
        content.Add(new StringContent("true"), "with_timestamps");

        // Sarvam wants BCP-47 (e.g. "te-IN"), not the bare ISO-639-1 codes the rest of this
        // app uses ("te") — "unknown" is their documented value for auto-detect.
        var sarvamLanguageCode = string.IsNullOrWhiteSpace(languageHint) ? "unknown" : $"{languageHint}-IN";
        content.Add(new StringContent(sarvamLanguageCode), "language_code");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "speech-to-text") { Content = content };
        httpRequest.Headers.Add("api-subscription-key", _options.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Sarvam transcription request failed with HTTP {(int)response.StatusCode}: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var text = root.TryGetProperty("transcript", out var transcriptElement)
            ? transcriptElement.GetString() ?? string.Empty
            : string.Empty;

        // "hi-IN" -> "hi", to stay consistent with the bare codes the rest of this app uses
        // (video.LanguageHint, OpenAI's provider) rather than mixing two code formats.
        var rawLanguageCode = root.TryGetProperty("language_code", out var languageElement)
            ? languageElement.GetString()
            : null;
        var languageCode = rawLanguageCode?.Split('-', 2)[0] ?? string.Empty;

        // Confirmed only against Sarvam's documented example, not yet a real response — that
        // example showed language_probability as null with no further explanation, so this
        // needs live verification (see SarvamSpeechToTextProviderLiveTests) same as OpenAI's
        // equivalent gap was verified rather than assumed.
        double confidence;
        if (root.TryGetProperty("language_probability", out var probElement) && probElement.ValueKind == JsonValueKind.Number)
        {
            confidence = probElement.GetDouble();
        }
        else
        {
            confidence = string.IsNullOrEmpty(languageCode) ? 0.0 : 1.0;
        }

        var words = new List<TranscriptionWord>();
        if (root.TryGetProperty("timestamps", out var timestampsElement) &&
            timestampsElement.TryGetProperty("words", out var phrasesElement) &&
            timestampsElement.TryGetProperty("start_time_seconds", out var startsElement) &&
            timestampsElement.TryGetProperty("end_time_seconds", out var endsElement))
        {
            var phrases = phrasesElement.EnumerateArray().ToList();
            var starts = startsElement.EnumerateArray().ToList();
            var ends = endsElement.EnumerateArray().ToList();

            for (var i = 0; i < phrases.Count && i < starts.Count && i < ends.Count; i++)
            {
                var phraseText = phrases[i].GetString() ?? string.Empty;
                var phraseStartMs = (int)(starts[i].GetDouble() * 1000);
                var phraseEndMs = (int)(ends[i].GetDouble() * 1000);
                words.AddRange(SplitPhraseIntoApproximatelyTimedWords(phraseText, phraseStartMs, phraseEndMs));
            }
        }

        return new TranscriptionResult(text, languageCode, confidence, words, ProviderName, ModelName);
    }

    private static IEnumerable<TranscriptionWord> SplitPhraseIntoApproximatelyTimedWords(
        string phraseText, int phraseStartMs, int phraseEndMs)
    {
        var tokens = phraseText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            yield break;
        }

        var durationMs = Math.Max(phraseEndMs - phraseStartMs, 1);
        for (var i = 0; i < tokens.Length; i++)
        {
            var wordStartMs = phraseStartMs + durationMs * i / tokens.Length;
            var wordEndMs = phraseStartMs + durationMs * (i + 1) / tokens.Length;
            yield return new TranscriptionWord(tokens[i], wordStartMs, wordEndMs);
        }
    }
}
