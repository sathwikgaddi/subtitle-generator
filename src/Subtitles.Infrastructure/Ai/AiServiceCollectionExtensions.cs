using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subtitles.Domain.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;

namespace Subtitles.Infrastructure.Ai;

/// <summary>
/// The provider-swap point described in docs/Architecture.md §3.2: adding Claude or Gemini
/// later means one new options class, one new provider class, and one new case here — never
/// touching the pipeline stages that consume ISpeechToTextProvider/ILlmProvider.
/// </summary>
public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddSubtitlesAi(this IServiceCollection services, IConfiguration configuration)
    {
        AddSpeechToTextProvider(services, configuration);
        AddLlmProvider(services, configuration);
        return services;
    }

    private static void AddSpeechToTextProvider(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Ai:SpeechToText:Provider"];
        switch (provider)
        {
            case "OpenAi":
                services.AddOptions<OpenAiSttOptions>()
                    .Bind(configuration.GetSection("Ai:SpeechToText:OpenAi"))
                    .PostConfigure(o => o.ApiKey = configuration["OPENAI_API_KEY"] ?? string.Empty)
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddHttpClient<ISpeechToTextProvider, OpenAiSpeechToTextProvider>(
                    client => client.BaseAddress = new Uri("https://api.openai.com/v1/"));
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown speech-to-text provider '{provider}' (Ai:SpeechToText:Provider).");
        }
    }

    private static void AddLlmProvider(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Ai:Llm:Provider"];
        switch (provider)
        {
            case "OpenAi":
                services.AddOptions<OpenAiLlmOptions>()
                    .Bind(configuration.GetSection("Ai:Llm:OpenAi"))
                    .PostConfigure(o => o.ApiKey = configuration["OPENAI_API_KEY"] ?? string.Empty)
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddHttpClient<ILlmProvider, OpenAiLlmProvider>(
                    client => client.BaseAddress = new Uri("https://api.openai.com/v1/"));
                break;

            // case "Anthropic": ... — future, per docs/Architecture.md §3.2
            // case "Gemini": ... — future

            default:
                throw new InvalidOperationException($"Unknown LLM provider '{provider}' (Ai:Llm:Provider).");
        }
    }
}
