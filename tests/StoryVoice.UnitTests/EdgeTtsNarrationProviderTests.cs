using Microsoft.Extensions.Logging.Abstractions;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class EdgeTtsNarrationProviderTests
{
    [Theory]
    [InlineData("STORYVOICE_PROGRESS 1/12", 1, 12)]
    [InlineData("STORYVOICE_PROGRESS 12/12", 12, 12)]
    public void Progress_marker_parser_accepts_only_bounded_chunk_counts(
        string line,
        int expectedCompleted,
        int expectedTotal)
    {
        var parsed = EdgeTtsNarrationProvider.TryParseProgress(line, out var progress);

        Assert.True(parsed);
        Assert.Equal(expectedCompleted, progress.CompletedChunks);
        Assert.Equal(expectedTotal, progress.TotalChunks);
    }

    [Theory]
    [InlineData("ordinary diagnostic")]
    [InlineData("STORYVOICE_PROGRESS 0/12")]
    [InlineData("STORYVOICE_PROGRESS 13/12")]
    [InlineData("STORYVOICE_PROGRESS 1/0")]
    public void Progress_marker_parser_rejects_invalid_lines(string line)
    {
        Assert.False(EdgeTtsNarrationProvider.TryParseProgress(line, out _));
    }

    [Theory]
    [InlineData(1, 10, 18)]
    [InlineData(5, 10, 52)]
    [InlineData(10, 10, 95)]
    public void Chunk_progress_maps_to_active_range_below_completion(
        int completed,
        int total,
        int expectedPercent)
    {
        Assert.Equal(
            expectedPercent,
            StoryPipelineWorker.CalculateSynthesisProgress(completed, total));
    }

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
