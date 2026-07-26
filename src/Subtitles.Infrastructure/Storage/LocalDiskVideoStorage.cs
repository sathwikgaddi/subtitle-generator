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
        // SECURITY: fileName is untrusted (comes straight from an HTTP upload). Path.GetFileName
        // strips any directory components — including "..", and, critically, an absolute path
        // like "C:\Windows\System32\x" — because Path.Combine("root", "C:\Windows\x") would
        // otherwise silently discard "root" and use the absolute path verbatim (a real .NET
        // Path.Combine gotcha, not a hypothetical one). The resolved-path check below is
        // defense in depth on top of that, not a substitute for it.
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException("File name must not be empty.", nameof(fileName));
        }

        var directory = Path.Combine(_rootPath, videoId.ToString());
        Directory.CreateDirectory(directory);

        var storagePath = Path.Combine(directory, safeFileName);

        var resolvedDirectory = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var resolvedPath = Path.GetFullPath(storagePath);
        if (!resolvedPath.StartsWith(resolvedDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved storage path escaped the intended directory.");
        }

        // Write to a temp file then rename into place — File.Move within the same directory
        // is atomic on both NTFS and Linux filesystems, so a crash or cancellation mid-write
        // never leaves a truncated file sitting at the "real" path looking valid.
        var tempPath = storagePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                await content.CopyToAsync(fileStream, ct);
            }

            File.Move(tempPath, storagePath, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath); // best-effort cleanup of the partial temp file
            throw;
        }

        return storagePath;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        Stream stream = File.OpenRead(storagePath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        File.Delete(storagePath);
        return Task.CompletedTask;
    }
}
