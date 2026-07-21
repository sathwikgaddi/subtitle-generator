using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subtitles.Domain.Storage;

namespace Subtitles.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddSubtitlesStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LocalDiskOptions>()
            .Bind(configuration.GetSection("Storage:LocalDisk"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IVideoStorage, LocalDiskVideoStorage>();

        return services;
    }
}
