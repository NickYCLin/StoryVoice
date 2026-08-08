using System.Diagnostics;

namespace StoryVoice.Worker;

public sealed class EdgeTtsNarrationProvider(ILogger<EdgeTtsNarrationProvider> logger) : INarrationProvider
{
    public async Task SynthesizeAsync(
        string text,
        string outputPath,
        string voice,
        string rate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("語音來源文字不可為空白。", nameof(text));
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "edge_tts_provider.py");
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
        if (!process.Start())
        {
            throw new InvalidOperationException("無法啟動神經語音 provider。");
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            logger.LogWarning(
                "Edge TTS provider exited with code {ExitCode}; diagnostic length {DiagnosticLength}",
                process.ExitCode,
                stderr.Length);
            throw new InvalidOperationException("神經語音 provider 執行失敗。");
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1)
        {
            throw new InvalidOperationException("神經語音 provider 沒有產生音訊。");
        }
    }

    private static void TryKill(Process process)
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
    }
}
