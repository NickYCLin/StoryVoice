namespace StoryVoice.Worker;

/// <summary>
/// Routes a multi-character synthesis request to whichever <see cref="IMultiVoiceNarrationProvider"/>
/// is registered for the cast revision's <c>VoiceProvider</c> — the compatibility boundary that
/// lets a new provider (Azure Speech, a local TTS engine, ...) be added later without touching
/// any series/character/cast-revision IDs already on disk.
/// </summary>
public sealed class NarrationProviderDispatcher(INarrationProviderRegistry registry)
{
    public Task SynthesizeAsync(
        string providerName,
        MultiVoiceNarrationRequest request,
        string outputPath,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken) =>
        registry.Resolve(providerName).SynthesizeAsync(request, outputPath, progressCallback, cancellationToken);
}
