namespace Subtitles.Domain.Storage;

/// <summary>
/// Where video/audio bytes actually live. One implementation for now — plain local disk — so
/// there's zero cloud-storage dependency while the core pipeline is being built. A future
/// cloud implementation (Azure Blob, S3, etc.) is a second class behind this same interface,
/// not a rewrite — same pattern as ISpeechToTextProvider/ILlmProvider.
/// </summary>
public interface IVideoStorage
{
    /// <summary>Saves content under a storage-implementation-defined path and returns that path.</summary>
    Task<string> SaveAsync(Guid videoId, string fileName, Stream content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);
}
