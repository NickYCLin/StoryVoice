namespace StoryVoice.Worker;

public sealed record VoAiSpeechSynthesisRequest(
    string Text,
    string Model,
    string Speaker,
    string Style,
    double Speed,
    int PitchShift);

public interface IVoAiTtsClient
{
    Task SynthesizeWavAsync(
        VoAiSpeechSynthesisRequest request,
        Stream destination,
        CancellationToken cancellationToken);
}
