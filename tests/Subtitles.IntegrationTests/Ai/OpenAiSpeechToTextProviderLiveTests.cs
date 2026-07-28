using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;
using Xunit;
using Xunit.Abstractions;

namespace Subtitles.IntegrationTests.Ai;

/// <summary>
/// Hits the real OpenAI Whisper endpoint — costs a fraction of a cent per run. Excluded from
/// normal `dotnet test` / CI via the LiveOpenAI trait; run manually with:
/// dotnet test --filter "Category=LiveOpenAI"
///
/// Purpose: OpenAiSpeechToTextProvider hard-codes LanguageConfidence to 1.0 because Whisper's
/// verbose_json response was never actually inspected for a real confidence field — this test
/// exists to look at the raw response and confirm (or correct) that assumption. It uses a
/// synthetic tone, not real speech, since there's no cross-platform way to synthesize speech
/// here — transcription *quality* on real speech is verified separately at the P1.5
/// walking-skeleton checkpoint. This test is purely about the response's shape/fields.
/// </summary>
[Trait("Category", "LiveOpenAI")]
public class OpenAiSpeechToTextProviderLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TranscribeAsync_AgainstRealOpenAi_ReturnsAWellFormedResult()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not set in the environment. Export it before running LiveOpenAI tests.");
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_TEST_STT_MODEL") ?? "whisper-1";

        var audioPath = await CreateSyntheticToneAsync();
        try
        {
            using var loggingHandler = new LoggingDelegatingHandler(output.WriteLine);
            using var httpClient = new HttpClient(loggingHandler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
            var options = Options.Create(new OpenAiSttOptions { ApiKey = apiKey, Model = model });
            var provider = new OpenAiSpeechToTextProvider(httpClient, options);

            await using var audioStream = File.OpenRead(audioPath);
            var result = await provider.TranscribeAsync(audioStream, "tone.mp3", CancellationToken.None);

            output.WriteLine("--- PARSED RESULT ---");
            output.WriteLine($"Model:      {provider.ModelName}");
            output.WriteLine($"Text:       {result.Text}");
            output.WriteLine($"Language:   {result.LanguageCode}");
            output.WriteLine($"Confidence: {result.LanguageConfidence}");
            output.WriteLine($"Word count: {result.Words.Count}");

            Assert.NotNull(result.LanguageCode);
        }
        finally
        {
            File.Delete(audioPath);
        }
    }

    private static async Task<string> CreateSyntheticToneAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stt-live-fixture-{Guid.NewGuid():N}.mp3");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -f lavfi -i \"sine=frequency=440:duration=3\" -c:a libmp3lame \"{path}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"ffmpeg fixture generation did not exit within 30s: {stderr}");
        }

        if (process.ExitCode != 0 || !File.Exists(path))
        {
            throw new InvalidOperationException($"Failed to generate the synthetic tone fixture via ffmpeg: {stderr}");
        }

        return path;
    }
}
