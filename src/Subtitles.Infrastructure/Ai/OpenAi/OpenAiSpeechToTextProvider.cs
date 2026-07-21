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

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(audioContent, "file", "audio");
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
            ? languageElement.GetString() ?? string.Empty
            : string.Empty;

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

        // NOTE: the transcription endpoint's verbose_json response does not include a
        // numeric language-detection confidence score alongside the detected language —
        // 1.0 is a placeholder here. This needs verifying against a real response (the
        // live smoke test below) before ProductRequirements.md §6.2's low-confidence
        // banner logic can be built on top of it in P1.3.
        return new TranscriptionResult(text, language, 1.0, words);
    }
}
