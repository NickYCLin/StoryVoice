using System.Diagnostics;

namespace StoryVoice.Worker;

public sealed class EdgeTtsNarrationProvider(ILogger<EdgeTtsNarrationProvider> logger) : INarrationProvider
{
    public async Task SynthesizeAsync(
        string text,
        string outputPath,
        string voice,
        string rate,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("語音來源文字不可為空白。", nameof(text));
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "edge_tts_provider.py");
        CleanupTemporaryDirectories(outputPath, logger);
        var startInfo = new ProcessStartInfo("python3")
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--voice");
        startInfo.ArgumentList.Add(voice);
        startInfo.ArgumentList.Add($"--rate={rate}");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("無法啟動神經語音 provider。");
            }

            var stderrTask = ReadDiagnosticsAsync(
                process.StandardError,
                progressCallback,
                cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try
            {
                await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken);
                var diagnosticLength = await stderrTask;
                _ = await stdoutTask;

                if (process.ExitCode != 0)
                {
                    logger.LogWarning(
                        "Edge TTS provider exited with code {ExitCode}; diagnostic length {DiagnosticLength}",
                        process.ExitCode,
                        diagnosticLength);
                    throw new InvalidOperationException("神經語音 provider 執行失敗。");
                }
            }
            catch
            {
                await TerminateAsync(process);
                await IgnoreFailureAsync(stderrTask);
                await IgnoreFailureAsync(stdoutTask);
                throw;
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1)
            {
                throw new InvalidOperationException("神經語音 provider 沒有產生音訊。");
            }
        }
        finally
        {
            CleanupTemporaryDirectories(outputPath, logger);
        }
    }

    internal static bool TryParseProgress(
        string line,
        out NarrationSynthesisProgress progress)
    {
        const string prefix = "STORYVOICE_PROGRESS ";
        progress = new NarrationSynthesisProgress(0, 0);
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = line.AsSpan(prefix.Length);
        var separator = value.IndexOf('/');
        if (separator < 1
            || separator == value.Length - 1
            || !int.TryParse(value[..separator], out var completed)
            || !int.TryParse(value[(separator + 1)..], out var total)
            || completed < 1
            || total < 1
            || completed > total)
        {
            return false;
        }

        progress = new NarrationSynthesisProgress(completed, total);
        return true;
    }

    private static async Task<int> ReadDiagnosticsAsync(
        StreamReader reader,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        var diagnosticLength = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            diagnosticLength += line.Length + 1;
            if (progressCallback is not null && TryParseProgress(line, out var progress))
            {
                await progressCallback(progress, cancellationToken);
            }
        }

        return diagnosticLength;
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

    internal static void CleanupTemporaryDirectories(string outputPath, ILogger logger)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (var temporaryDirectory in Directory.EnumerateDirectories(
                directory,
                "edge-tts-*",
                SearchOption.TopDirectoryOnly))
            {
                try
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        exception,
                        "Unable to remove an Edge TTS temporary directory");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Unable to enumerate Edge TTS temporary directories");
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
}
