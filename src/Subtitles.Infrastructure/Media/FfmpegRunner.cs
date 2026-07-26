using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Subtitles.Infrastructure.Media;

/// <summary>
/// Thin subprocess wrapper around the ffmpeg CLI — see docs/Architecture.md §5. Relies on
/// "ffmpeg" being resolvable on PATH (true both for a local dev machine with ffmpeg installed
/// and for a Worker container image with it apt-installed), rather than a configurable path,
/// since there's no real need to point at a specific binary location.
/// </summary>
public class FfmpegRunner(ILogger<FfmpegRunner> logger)
{
    // Generous but bounded — a hung or runaway ffmpeg process (bad input, a codec edge case,
    // whatever) must not be able to block a worker slot forever with nothing to stop it.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Extracts just the audio track, compressed to mono 64kbps MP3 at 16kHz — not raw WAV.
    /// A 60-minute raw WAV would be ~115MB, well over Whisper's ~25MB request limit; see
    /// docs/Architecture.md §3.1 and the P1.2 discussion for why this must be compressed here,
    /// not discovered as a surprise once real long videos show up.
    /// </summary>
    public Task ExtractCompressedAudioAsync(string inputVideoPath, string outputAudioPath, CancellationToken ct)
    {
        var arguments = $"-y -i \"{inputVideoPath}\" -vn -ac 1 -ar 16000 -codec:a libmp3lame -b:a 64k \"{outputAudioPath}\"";
        return RunAsync(arguments, ct);
    }

    private async Task RunAsync(string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        // ffmpeg writes its progress/diagnostic output to stderr, not stdout — this is normal
        // ffmpeg behavior, not an error signal by itself.
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        logger.LogInformation("Running ffmpeg {Arguments}", arguments);
        process.Start();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw new TimeoutException(
                $"ffmpeg did not complete within {DefaultTimeout} and was killed. Arguments: {arguments}");
        }
        catch (OperationCanceledException)
        {
            // Caller's own token, not our timeout — respect it, but still make sure the
            // subprocess doesn't keep running after we've stopped waiting on it.
            TryKillProcessTree(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}: {stderr}");
        }
    }

    private void TryKillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill ffmpeg process tree after cancellation/timeout.");
        }
    }
}
