using Microsoft.Extensions.Options;
using Subtitles.Domain.Storage;

namespace Subtitles.Infrastructure.Storage;

/// <summary>
/// Stores files as plain files on local disk, under RootPath/{videoId}/{fileName}. See
/// IVideoStorage for why this is the only implementation right now.
/// </summary>
public class LocalDiskVideoStorage(IOptions<LocalDiskOptions> options) : IVideoStorage
{
    private readonly string _rootPath = options.Value.RootPath;

    public async Task<string> SaveAsync(Guid videoId, string fileName, Stream content, CancellationToken ct)
    {
        var directory = Path.Combine(_rootPath, videoId.ToString());
        Directory.CreateDirectory(directory);

        var storagePath = Path.Combine(directory, fileName);
        await using var fileStream = File.Create(storagePath);
        await content.CopyToAsync(fileStream, ct);

        return storagePath;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        Stream stream = File.OpenRead(storagePath);
        return Task.FromResult(stream);
    }
}
