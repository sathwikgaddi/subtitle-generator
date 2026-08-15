using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;

namespace Subtitles.Infrastructure.Ai.OpenAi;

/// <summary>
/// OpenAI Whisper implementation of <see cref="ISpeechToTextProvider"/>. See
/// docs/Architecture.md §3.1.
/// </summary>
public class OpenAiSpeechToTextProvider(
    HttpClient httpClient, IOptions<OpenAiSttOptions> options, ILogger<OpenAiSpeechToTextProvider> logger)
    : ISpeechToTextProvider
{
    private readonly OpenAiSttOptions _options = options.Value;

    public string ProviderName => "openai";
    public string ModelName => _options.Model;

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, string? languageHint, CancellationToken ct)
    {
        // Buffered once (not streamed straight into the request) so a rejected language hint
        // can retry with the same audio without requiring the caller to hand us a seekable
        // stream — see the fallback below.
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);
        var audioBytes = buffer.ToArray();

        var (statusCode, responseBody) = await SendTranscriptionRequestAsync(audioBytes, fileName, languageHint, ct);

        // Confirmed live: this endpoint's "language" parameter rejects some languages Whisper
        // can perfectly well auto-detect — e.g. Telugu is auto-detected fine throughout this
        // whole project but is rejected as both "te" (unsupported_language) and "telugu"
        // (invalid_language_format) when forced via this parameter. Rather than hardcode and
        // maintain our own copy of whatever subset OpenAI currently allows here (undocumented
        // and apparently narrower than their 99-language detection support), fall back to
        // auto-detect on exactly this failure mode — a hint can only ever help or be ignored,
        // never turn a working auto-detect transcription into a hard failure.
        if (languageHint is not null && statusCode == HttpStatusCode.BadRequest && IsLanguageRejection(responseBody))
        {
            logger.LogWarning(
                "OpenAI rejected language hint '{LanguageHint}' ({Response}) — retrying with auto-detect.",
                languageHint, responseBody);
            (statusCode, responseBody) = await SendTranscriptionRequestAsync(audioBytes, fileName, null, ct);
        }

        if (statusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"OpenAI transcription request failed with HTTP {(int)statusCode}: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var text = root.GetProperty("text").GetString() ?? string.Empty;
        var language = root.TryGetProperty("language", out var languageElement)
            ? languageElement.GetString()
            : null;

        var words = new List<TranscriptionWord>();
        if (root.TryGetProperty("words", out var wordsElement))
        {
            foreach (var word in wordsElement.EnumerateArray())
            {
                var wordText = word.GetProperty("word").GetString() ?? string.Empty;
                var startMs = (int)(word.GetProperty("start").GetDouble() * 1000);
                var endMs = (int)(word.GetProperty("end").GetDouble() * 1000);
                words.Add(new TranscriptionWord(wordText, startMs, endMs));
            }
        }

        // Confirmed against a real response (OpenAiSpeechToTextProviderLiveTests): this
        // endpoint's verbose_json has no numeric language-detection confidence field at all —
        // not a low number, genuinely absent. What it does give us is "language": null when
        // nothing usable was detected (e.g. silence/non-speech audio), vs. a populated code
        // otherwise. That null/non-null signal is the only real confidence proxy this API
        // exposes, so 0.0/1.0 here means "did Whisper detect a language," not a true
        // probability — ProductRequirements.md §6.2's low-confidence UX should treat this
        // binary correctly rather than expecting a graded score.
        return new TranscriptionResult(text, language ?? string.Empty, language is null ? 0.0 : 1.0, words, ProviderName, ModelName);
    }

    private async Task<(HttpStatusCode StatusCode, string Body)> SendTranscriptionRequestAsync(
        byte[] audioBytes, string fileName, string? languageHint, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(audioContent, "file", fileName);
        content.Add(new StringContent(_options.Model), "model");
        content.Add(new StringContent("verbose_json"), "response_format");
        content.Add(new StringContent("word"), "timestamp_granularities[]");
        if (!string.IsNullOrWhiteSpace(languageHint))
        {
            content.Add(new StringContent(languageHint), "language");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = content };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, responseBody);
    }

    private static bool IsLanguageRejection(string responseBody) =>
        responseBody.Contains("unsupported_language", StringComparison.OrdinalIgnoreCase)
        || responseBody.Contains("invalid_language_format", StringComparison.OrdinalIgnoreCase);
}
