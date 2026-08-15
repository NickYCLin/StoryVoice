namespace StoryVoice.Infrastructure.Narrations;

public sealed record BlueMagpieSynthesisResult(
    byte[] Content,
    string ContentType,
    string ModelRevision,
    string ProviderVersion,
    string Voice);

public interface IBlueMagpieTtsClient
{
    Task<BlueMagpieSynthesisResult> SynthesizeAsync(
        string text,
        string voice,
        CancellationToken cancellationToken);
}
