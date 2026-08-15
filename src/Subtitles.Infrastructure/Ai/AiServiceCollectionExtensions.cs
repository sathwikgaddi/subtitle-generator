using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subtitles.Domain.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;
using Subtitles.Infrastructure.Ai.Sarvam;

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
        services.AddScoped<PromptSeeder>();
        return services;
    }

    private static void AddSpeechToTextProvider(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Ai:SpeechToText:Provider"];
        switch (provider)
        {
            case "OpenAi":
                RegisterOpenAiSttClient(services, configuration);
                services.AddScoped<ISpeechToTextProvider>(sp => sp.GetRequiredService<OpenAiSpeechToTextProvider>());
                break;

            case "Sarvam":
                RegisterSarvamSttClient(services, configuration);
                services.AddScoped<ISpeechToTextProvider>(sp => sp.GetRequiredService<SarvamSpeechToTextProvider>());
                break;

            // Picks OpenAI or Sarvam per video by language hint (see RoutedSpeechToTextProvider)
            // — needs both concrete clients registered, unlike the single-provider cases above.
            case "Routed":
                RegisterOpenAiSttClient(services, configuration);
                RegisterSarvamSttClient(services, configuration);

                services.AddOptions<SpeechToTextRoutingOptions>()
                    .Bind(configuration.GetSection("Ai:SpeechToText:Routed"))
                    .ValidateOnStart();

                services.AddScoped<ISpeechToTextProvider, RoutedSpeechToTextProvider>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown speech-to-text provider '{provider}' (Ai:SpeechToText:Provider).");
        }
    }

    private static void RegisterOpenAiSttClient(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenAiSttOptions>()
            .Bind(configuration.GetSection("Ai:SpeechToText:OpenAi"))
            .PostConfigure(o => o.ApiKey = configuration["OPENAI_API_KEY"] ?? string.Empty)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<OpenAiSpeechToTextProvider>(
            client => client.BaseAddress = new Uri("https://api.openai.com/v1/"));
    }

    private static void RegisterSarvamSttClient(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SarvamSttOptions>()
            .Bind(configuration.GetSection("Ai:SpeechToText:Sarvam"))
            .PostConfigure(o => o.ApiKey = configuration["SARVAM_API_KEY"] ?? string.Empty)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<SarvamSpeechToTextProvider>(
            client => client.BaseAddress = new Uri("https://api.sarvam.ai/"));
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
