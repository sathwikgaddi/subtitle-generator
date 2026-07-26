using Microsoft.Extensions.DependencyInjection;
using Subtitles.Domain.Pipeline;
using Subtitles.Infrastructure.Media;

namespace Subtitles.Infrastructure.Pipeline;

public static class PipelineServiceCollectionExtensions
{
    /// <summary>Registers ffmpeg support and every IPipelineStage implementation that exists so far.</summary>
    public static IServiceCollection AddSubtitlesPipeline(this IServiceCollection services)
    {
        services.AddSingleton<FfmpegRunner>();
        services.AddScoped<IPipelineStage, ExtractAudioStage>();

        return services;
    }
}
