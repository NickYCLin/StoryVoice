using Microsoft.Extensions.Logging.Abstractions;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class EdgeTtsMultiVoiceNarrationProviderTests
{
    [Fact]
    public void ProviderName_is_edge()
    {
        var provider = new EdgeTtsMultiVoiceNarrationProvider(NullLogger<EdgeTtsMultiVoiceNarrationProvider>.Instance);

        Assert.Equal("edge", provider.ProviderName);
    }

    [Fact]
    public async Task SynthesizeAsync_rejects_a_manifest_with_no_turns_before_spawning_any_process()
    {
        var provider = new EdgeTtsMultiVoiceNarrationProvider(NullLogger<EdgeTtsMultiVoiceNarrationProvider>.Instance);
        var request = new MultiVoiceNarrationRequest([]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.SynthesizeAsync(request, "unused.mp3", null, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_rejects_a_turn_with_blank_text()
    {
        var provider = new EdgeTtsMultiVoiceNarrationProvider(NullLogger<EdgeTtsMultiVoiceNarrationProvider>.Instance);
        var request = new MultiVoiceNarrationRequest([new NarrationTurn("   ", "voice", "+0%", 0)]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.SynthesizeAsync(request, "unused.mp3", null, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_rejects_a_turn_with_no_voice()
    {
        var provider = new EdgeTtsMultiVoiceNarrationProvider(NullLogger<EdgeTtsMultiVoiceNarrationProvider>.Instance);
        var request = new MultiVoiceNarrationRequest([new NarrationTurn("你好", "", "+0%", 0)]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.SynthesizeAsync(request, "unused.mp3", null, CancellationToken.None));
    }
}

public sealed class NarrationProviderRegistryTests
{
    [Fact]
    public void Resolve_finds_a_provider_case_insensitively()
    {
        var provider = new FakeMultiVoiceProvider("edge");
        var registry = new NarrationProviderRegistry([provider]);

        Assert.Same(provider, registry.Resolve("EDGE"));
    }

    [Fact]
    public void Resolve_throws_for_an_unknown_provider_name()
    {
        var registry = new NarrationProviderRegistry([new FakeMultiVoiceProvider("edge")]);

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("azure"));
    }

    [Fact]
    public void Resolve_throws_for_a_blank_provider_name()
    {
        var registry = new NarrationProviderRegistry([new FakeMultiVoiceProvider("edge")]);

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("  "));
    }
}

public sealed class NarrationProviderDispatcherTests
{
    [Fact]
    public async Task SynthesizeAsync_delegates_to_the_provider_resolved_for_the_requested_name()
    {
        var provider = new FakeMultiVoiceProvider("edge");
        var dispatcher = new NarrationProviderDispatcher(new NarrationProviderRegistry([provider]));
        var request = new MultiVoiceNarrationRequest([new NarrationTurn("你好", "voice", "+0%", 0)]);

        await dispatcher.SynthesizeAsync("edge", request, "output.mp3", null, CancellationToken.None);

        Assert.Same(request, provider.LastRequest);
        Assert.Equal("output.mp3", provider.LastOutputPath);
    }
}

internal sealed class FakeMultiVoiceProvider(string providerName) : IMultiVoiceNarrationProvider
{
    public string ProviderName { get; } = providerName;
    public MultiVoiceNarrationRequest? LastRequest { get; private set; }
    public string? LastOutputPath { get; private set; }

    public Task SynthesizeAsync(
        MultiVoiceNarrationRequest request,
        string outputPath,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastOutputPath = outputPath;
        return Task.CompletedTask;
    }
}
