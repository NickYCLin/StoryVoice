namespace StoryVoice.Worker;

public interface IMultiVoiceNarrationProvider
{
    string ProviderName { get; }

    Task SynthesizeAsync(
        MultiVoiceNarrationRequest request,
        string outputPath,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken);
}

public interface INarrationProviderRegistry
{
    IMultiVoiceNarrationProvider Resolve(string providerName);
}

public sealed class NarrationProviderRegistry : INarrationProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IMultiVoiceNarrationProvider> _providersByName;

    public NarrationProviderRegistry(IEnumerable<IMultiVoiceNarrationProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providersByName = providers.ToDictionary(
            provider => provider.ProviderName,
            StringComparer.OrdinalIgnoreCase);
    }

    public IMultiVoiceNarrationProvider Resolve(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName)
            || !_providersByName.TryGetValue(providerName, out var provider))
        {
            throw new InvalidOperationException($"未知的多角色語音 provider：{providerName}。");
        }

        return provider;
    }
}
