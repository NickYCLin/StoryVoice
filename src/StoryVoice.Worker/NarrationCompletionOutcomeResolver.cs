namespace StoryVoice.Worker;

internal readonly record struct NarrationCompletionOutcome(
    bool AcceptCompletion,
    bool DeleteCandidateArtifact);

internal static class NarrationCompletionOutcomeResolver
{
    public static NarrationCompletionOutcome ResolveAfterAmbiguousCommand(bool? durableCompletionConfirmed) =>
        durableCompletionConfirmed switch
        {
            true => new NarrationCompletionOutcome(AcceptCompletion: true, DeleteCandidateArtifact: false),
            false => new NarrationCompletionOutcome(AcceptCompletion: false, DeleteCandidateArtifact: true),
            null => new NarrationCompletionOutcome(AcceptCompletion: false, DeleteCandidateArtifact: false)
        };
}
