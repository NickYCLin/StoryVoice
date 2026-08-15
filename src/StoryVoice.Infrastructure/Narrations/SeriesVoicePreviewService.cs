using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Series;

namespace StoryVoice.Infrastructure.Narrations;

public sealed class SeriesVoicePreviewService(
    IBlueMagpieTtsClient client,
    IOptions<BlueMagpieOptions> options) : ISeriesVoicePreviewService
{
    internal const string PreviewSentence = "這是一段不含書籍正文的台灣華語聲線示範。";

    private static readonly HashSet<string> AllowedVoices =
    [
        BlueMagpieOptions.MaleVoice,
        BlueMagpieOptions.FemaleVoice,
    ];

    private readonly ConcurrentDictionary<string, Lazy<Task<SeriesVoicePreviewAudio>>> successfulPreviews =
        new(StringComparer.Ordinal);

    public async Task<SeriesVoicePreviewAudio> GenerateAsync(
        SeriesVoicePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Value.Enabled)
        {
            throw new SeriesVoicePreviewUnavailableException();
        }

        var provider = request.Provider?.Trim();
        var voice = request.Voice?.Trim();
        if (!string.Equals(provider, BlueMagpieOptions.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("目前只允許本機 BlueMagpie 台灣華語試音。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(voice) || !AllowedVoices.Contains(voice))
        {
            throw new ArgumentException("不是允許的本機台灣華語聲線。", nameof(request));
        }

        var cacheKey = $"{options.Value.ModelRevision}\n{voice}";
        Lazy<Task<SeriesVoicePreviewAudio>>? candidate = null;
        candidate = new Lazy<Task<SeriesVoicePreviewAudio>>(
            () => GenerateAndEvictOnFailureAsync(
                cacheKey,
                candidate!,
                voice),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var selected = successfulPreviews.GetOrAdd(cacheKey, candidate);

        // Cancellation stops only this HTTP waiter. If synthesis has already acquired the GPU,
        // the shared task continues to a terminal gateway response before releasing its lease.
        return await selected.Value.WaitAsync(cancellationToken);
    }

    private async Task<SeriesVoicePreviewAudio> GenerateAndEvictOnFailureAsync(
        string cacheKey,
        Lazy<Task<SeriesVoicePreviewAudio>> cacheEntry,
        string voice)
    {
        try
        {
            return await GenerateCoreAsync(voice);
        }
        catch
        {
            successfulPreviews.TryRemove(
                new KeyValuePair<string, Lazy<Task<SeriesVoicePreviewAudio>>>(cacheKey, cacheEntry));
            throw;
        }
    }

    private async Task<SeriesVoicePreviewAudio> GenerateCoreAsync(string voice)
    {
        // The gateway owns the same cross-process Redis GPU lock used by Ollama. The API must not
        // pre-acquire it or the gateway would deadlock waiting for the API's lease.
        var result = await client.SynthesizeAsync(
            PreviewSentence,
            voice,
            CancellationToken.None);
        return new SeriesVoicePreviewAudio(
            result.Content,
            result.ContentType,
            result.ModelRevision,
            result.Voice);
    }
}
