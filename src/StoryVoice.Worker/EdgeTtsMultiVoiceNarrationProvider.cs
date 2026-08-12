using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StoryVoice.Worker;

public sealed class EdgeTtsMultiVoiceNarrationProvider(ILogger<EdgeTtsMultiVoiceNarrationProvider> logger)
    : IMultiVoiceNarrationProvider
{
    private const string ManifestSchemaVersion = "storyvoice:multi-voice-manifest:v1";
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web);

    public string ProviderName => "edge";

    public async Task SynthesizeAsync(
        MultiVoiceNarrationRequest request,
        string outputPath,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Turns.Count == 0)
        {
            throw new ArgumentException("多角色語音 manifest 至少要有一個 turn。", nameof(request));
        }

        foreach (var turn in request.Turns)
        {
            if (string.IsNullOrWhiteSpace(turn.Text))
            {
                throw new ArgumentException("每個 turn 的文字都不可為空白。", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(turn.Voice))
            {
                throw new ArgumentException("每個 turn 都必須指定聲線。", nameof(request));
            }
        }

        var manifestJson = JsonSerializer.Serialize(
            new Manifest(ManifestSchemaVersion, request.Turns),
            ManifestSerializerOptions);

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "edge_tts_multi_voice_provider.py");
        EdgeTtsNarrationProvider.CleanupTemporaryDirectories(outputPath, logger);
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

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("無法啟動多角色神經語音 provider。");
            }

            var stderrTask = ReadDiagnosticsAsync(process.StandardError, progressCallback, cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try
            {
                await process.StandardInput.WriteAsync(manifestJson.AsMemory(), cancellationToken);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken);
                var diagnostics = await stderrTask;
                _ = await stdoutTask;

                if (process.ExitCode != 0)
                {
                    // The provider's error messages only ever reference turn/chunk indices and
                    // tool exit codes (see edge_tts_multi_voice_provider.py) — never the
                    // synthesized text itself — so the tail is safe to log for diagnosis.
                    logger.LogWarning(
                        "Multi-voice Edge TTS provider exited with code {ExitCode}; diagnostic tail: {DiagnosticTail}",
                        process.ExitCode,
                        diagnostics.Tail);
                    throw new InvalidOperationException("多角色神經語音 provider 執行失敗。");
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
                throw new InvalidOperationException("多角色神經語音 provider 沒有產生音訊。");
            }
        }
        finally
        {
            EdgeTtsNarrationProvider.CleanupTemporaryDirectories(outputPath, logger);
        }
    }

    private const int DiagnosticTailLines = 30;

    private static async Task<(int Length, string Tail)> ReadDiagnosticsAsync(
        StreamReader reader,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        var diagnosticLength = 0;
        var tail = new Queue<string>(DiagnosticTailLines + 1);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            diagnosticLength += line.Length + 1;
            tail.Enqueue(line);
            if (tail.Count > DiagnosticTailLines)
            {
                tail.Dequeue();
            }

            if (progressCallback is not null && EdgeTtsNarrationProvider.TryParseProgress(line, out var progress))
            {
                await progressCallback(progress, cancellationToken);
            }
        }

        return (diagnosticLength, string.Join(" | ", tail));
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

    private sealed record Manifest(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("turns")] IReadOnlyList<NarrationTurn> Turns);
}
