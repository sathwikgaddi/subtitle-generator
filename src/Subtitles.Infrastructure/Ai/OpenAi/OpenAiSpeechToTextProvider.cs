using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;

namespace Subtitles.Infrastructure.Ai.OpenAi;

/// <summary>
/// OpenAI Whisper implementation of <see cref="ISpeechToTextProvider"/>. See
/// docs/Architecture.md §3.1.
/// </summary>
public class OpenAiSpeechToTextProvider(HttpClient httpClient, IOptions<OpenAiSttOptions> options)
    : ISpeechToTextProvider
{
    private readonly OpenAiSttOptions _options = options.Value;

    public string ProviderName => "openai";
    public string ModelName => _options.Model;

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(audioContent, "file", fileName);
        content.Add(new StringContent(_options.Model), "model");
        content.Add(new StringContent("verbose_json"), "response_format");
        content.Add(new StringContent("word"), "timestamp_granularities[]");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = content };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI transcription request failed with HTTP {(int)response.StatusCode}: {responseBody}");
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
        return new TranscriptionResult(text, language ?? string.Empty, language is null ? 0.0 : 1.0, words);
    }
}
