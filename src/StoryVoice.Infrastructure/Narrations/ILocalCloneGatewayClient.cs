namespace StoryVoice.Infrastructure.Narrations;

public sealed record LocalCloneGatewayRequest(
    string Text,
    string ReferenceTranscript,
    byte[] ReferenceAudio);

public sealed record LocalCloneGatewayAudio(
    byte[] Content,
    string ContentType);

public interface ILocalCloneGatewayClient
{
    Task<LocalCloneGatewayAudio> SynthesizeAsync(
        LocalCloneGatewayRequest request,
        CancellationToken cancellationToken);
}
