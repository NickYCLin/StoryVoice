using Microsoft.Extensions.Logging.Abstractions;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class ThreeWaVoxCpm2NarrationProviderTests
{
    [Fact]
    public async Task A_later_design_turn_is_rejected_before_workdir_creation_or_any_submission()
    {
        var client = new RecordingSynthesisClient();
        var provider = new ThreeWaVoxCpm2NarrationProvider(
            client,
            NullLogger<ThreeWaVoxCpm2NarrationProvider>.Instance);
        var request = new MultiVoiceNarrationRequest(
        [
            new NarrationTurn("第一句", "clone:ready-profile-task", "+0%", "+0Hz", "+0%", 0),
            new NarrationTurn("第二句", "design:legacy prompt", "+0%", "+0Hz", "+0%", 0),
        ]);
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"storyvoice-design-preflight-{Guid.NewGuid():N}");

        try
        {
            var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(
                () => provider.SynthesizeAsync(
                    request,
                    Path.Combine(outputDirectory, "result.mp3"),
                    progressCallback: null,
                    TestContext.Current.CancellationToken));

            Assert.Equal(ThreeWaSynthesisCapabilities.DesignVoiceUnavailableCode, exception.ErrorCode);
            Assert.Equal(0, client.SubmitCount);
            Assert.False(Directory.Exists(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_legacy_custom_narrator_is_rejected_before_workdir_creation_or_any_submission()
    {
        var client = new RecordingSynthesisClient();
        var provider = new ThreeWaVoxCpm2NarrationProvider(
            client,
            NullLogger<ThreeWaVoxCpm2NarrationProvider>.Instance);
        var request = new MultiVoiceNarrationRequest(
        [
            new NarrationTurn("旁白", "custom", "+0%", "+0Hz", "+0%", 0),
        ]);
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"storyvoice-legacy-3wa-narrator-{Guid.NewGuid():N}");

        try
        {
            var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(
                () => provider.SynthesizeAsync(
                    request,
                    Path.Combine(outputDirectory, "result.mp3"),
                    progressCallback: null,
                    TestContext.Current.CancellationToken));

            Assert.Equal(
                ThreeWaSynthesisCapabilities.LegacyNarratorVoiceUnavailableCode,
                exception.ErrorCode);
            Assert.Equal(0, client.SubmitCount);
            Assert.False(Directory.Exists(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private sealed class RecordingSynthesisClient : IThreeWaSynthesisClient
    {
        public int SubmitCount { get; private set; }

        public Task<ThreeWaSynthesisTaskHandle> SubmitAsync(
            ThreeWaSynthesisRequest request,
            CancellationToken cancellationToken)
        {
            SubmitCount++;
            throw new InvalidOperationException("The provider preflight should reject Design before submission.");
        }

        public Task<string> GetTaskStatusAsync(string statusUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ThreeWaSynthesisArtifact>> GetResultArtifactsAsync(
            string resultUrl,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DownloadArtifactAsync(
            string artifactUrlTemplate,
            string artifactId,
            Stream destination,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
