using System.Diagnostics;
using System.Globalization;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// Thin wrapper over the <c>ffprobe</c> CLI (already required on any host that runs the Worker's
/// ffmpeg-based synthesis pipeline) for measuring a local audio file's duration. Used to record how
/// long an uploaded character voice reference recording is — never touches the file's content, only
/// its container metadata, so it's safe to run on private audio.
/// </summary>
public static class AudioDurationProbe
{
    public static async Task<double?> TryProbeSecondsAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo("ffprobe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-show_entries");
            startInfo.ArgumentList.Add("format=duration");
            startInfo.ArgumentList.Add("-of");
            startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
            startInfo.ArgumentList.Add(filePath);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            _ = await stderrTask;

            if (process.ExitCode != 0)
            {
                return null;
            }

            return double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? seconds
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // ffprobe missing or unable to start — duration is a nice-to-have display field, never
            // worth failing the whole voice profile upload over.
            return null;
        }
    }
}
