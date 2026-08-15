using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace StoryVoice.Worker;

public sealed class FfmpegVoAiAudioComposer(
    IOptions<VoAiOptions> options,
    ILogger<FfmpegVoAiAudioComposer> logger) : IVoAiAudioComposer, IFfmpegAudioComposer
{
    public Task ComposeAsync(
        IReadOnlyList<VoAiAudioSegment> segments,
        string outputPath,
        CancellationToken cancellationToken) =>
        ComposeCoreAsync(
            segments.Select(segment => new FfmpegAudioSegment(
                segment.InputWavPath,
                segment.Volume,
                segment.PauseBeforeMs)).ToArray(),
            outputPath,
            options.Value.SampleRate,
            cancellationToken);

    Task IFfmpegAudioComposer.ComposeAsync(
        IReadOnlyList<FfmpegAudioSegment> segments,
        string outputPath,
        int outputSampleRate,
        CancellationToken cancellationToken) =>
        ComposeCoreAsync(segments, outputPath, outputSampleRate, cancellationToken);

    private async Task ComposeCoreAsync(
        IReadOnlyList<FfmpegAudioSegment> segments,
        string outputPath,
        int outputSampleRate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new ArgumentException("At least one audio segment is required.", nameof(segments));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An output path is required.", nameof(outputPath));
        }

        if (outputSampleRate is < 8_000 or > 192_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputSampleRate),
                "The output sample rate must be between 8000 and 192000 Hz.");
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDirectory);
        var workDirectory = Path.Combine(outputDirectory, $"audio-compose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        try
        {
            var normalizedPaths = new List<string>(segments.Count);
            for (var index = 0; index < segments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segment = segments[index];
                if (string.IsNullOrWhiteSpace(segment.InputWavPath) || !File.Exists(segment.InputWavPath))
                {
                    throw new InvalidOperationException("An input WAV segment is missing.");
                }

                if (segment.PauseBeforeMs < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(segments), "Audio pause must not be negative.");
                }

                var normalizedPath = Path.Combine(workDirectory, $"{index:00000}.wav");
                var filter = BuildAudioFilter(segment.Volume, segment.PauseBeforeMs);
                await RunFfmpegAsync(
                    [
                        "-y", "-hide_banner", "-loglevel", "error",
                        "-i", segment.InputWavPath,
                        "-af", filter,
                        "-ar", outputSampleRate.ToString(CultureInfo.InvariantCulture),
                        "-ac", "1",
                        "-c:a", "pcm_s16le",
                        normalizedPath
                    ],
                    cancellationToken);
                normalizedPaths.Add(normalizedPath);
                File.Delete(segment.InputWavPath);
            }

            var concatListPath = Path.Combine(workDirectory, "concat.txt");
            var concatLines = normalizedPaths.Select(path =>
                $"file '{path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal)}'");
            await File.WriteAllLinesAsync(concatListPath, concatLines, cancellationToken);

            var candidatePath = Path.Combine(workDirectory, "complete.mp3");
            await RunFfmpegAsync(
                [
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-f", "concat", "-safe", "0", "-i", concatListPath,
                    "-c:a", "libmp3lame", "-q:a", "2", "-f", "mp3",
                    candidatePath
                ],
                cancellationToken);
            if (!File.Exists(candidatePath) || new FileInfo(candidatePath).Length < 1)
            {
                throw new InvalidOperationException("Audio composition produced no MP3 output.");
            }

            var duration = await ProbeDurationSecondsAsync(candidatePath, cancellationToken);
            if (duration <= 0)
            {
                throw new InvalidOperationException("Audio composition produced a zero-duration MP3.");
            }

            // candidatePath and outputPath share a filesystem, so the final rename exposes either
            // the previous complete artifact or the new complete artifact, never a partial MP3.
            File.Move(candidatePath, outputPath, overwrite: true);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    internal static string BuildAudioFilter(string volume, int pauseBeforeMs)
    {
        var factor = ParseVolumeFactor(volume);
        var filter = $"volume={factor.ToString("0.####", CultureInfo.InvariantCulture)}";
        return pauseBeforeMs > 0
            ? $"{filter},adelay={pauseBeforeMs.ToString(CultureInfo.InvariantCulture)}:all=1"
            : filter;
    }

    internal static double ParseVolumeFactor(string volume)
    {
        if (string.IsNullOrWhiteSpace(volume))
        {
            throw new ArgumentException("Audio volume is required.", nameof(volume));
        }

        var value = volume.Trim();
        if (!value.EndsWith('%'))
        {
            throw new ArgumentException("Audio volume must be a signed percentage.", nameof(volume));
        }

        value = value[..^1];

        if (!double.TryParse(
                value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var percent)
            || !double.IsFinite(percent))
        {
            throw new ArgumentException("Audio volume must be a signed percentage.", nameof(volume));
        }

        return Math.Clamp(1 + (percent / 100), 0, 2);
    }

    private static async Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        _ = await RunProcessAsync("ffmpeg", arguments, cancellationToken);

    private static async Task<double> ProbeDurationSecondsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var output = await RunProcessAsync(
            "ffprobe",
            [
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                path
            ],
            cancellationToken);
        return double.TryParse(output.Trim(), CultureInfo.InvariantCulture, out var duration)
            ? duration
            : 0;
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start {fileName} for audio composition.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            _ = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{fileName} failed while composing audio (exit code {process.ExitCode}).");
            }

            return stdout;
        }
        catch
        {
            await TerminateAsync(process);
            await IgnoreFailureAsync(stdoutTask);
            await IgnoreFailureAsync(stderrTask);
            throw;
        }

    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Unable to remove an audio composition working directory");
        }
    }

    private static async Task TerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }
}
