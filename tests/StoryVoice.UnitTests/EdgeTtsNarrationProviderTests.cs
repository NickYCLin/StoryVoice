using Microsoft.Extensions.Logging.Abstractions;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class EdgeTtsNarrationProviderTests
{
    [Fact]
    public void Cleanup_removes_partial_directories_left_by_killed_process_only()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"storyvoice-edge-cleanup-{Guid.NewGuid():N}");
        var stale = Path.Combine(root, "edge-tts-interrupted");
        var unrelated = Path.Combine(root, "keep-me");
        var output = Path.Combine(root, "book.mp3.tmp-token");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(unrelated);
        File.WriteAllBytes(Path.Combine(stale, "00000.mp3"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(unrelated, "marker.txt"), "keep");

        try
        {
            EdgeTtsNarrationProvider.CleanupTemporaryDirectories(
                output,
                NullLogger.Instance);

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
