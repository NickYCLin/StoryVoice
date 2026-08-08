using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class NarrationCompletionOutcomeResolverTests
{
    [Fact]
    public void Committed_but_acknowledgement_lost_accepts_completion_and_preserves_audio()
    {
        var outcome = NarrationCompletionOutcomeResolver.ResolveAfterAmbiguousCommand(true);

        Assert.True(outcome.AcceptCompletion);
        Assert.False(outcome.DeleteCandidateArtifact);
    }

    [Fact]
    public void Confirmed_uncommitted_completion_rejects_and_deletes_candidate_audio()
    {
        var outcome = NarrationCompletionOutcomeResolver.ResolveAfterAmbiguousCommand(false);

        Assert.False(outcome.AcceptCompletion);
        Assert.True(outcome.DeleteCandidateArtifact);
    }

    [Fact]
    public void Unreadable_completion_outcome_preserves_unique_candidate_audio()
    {
        var outcome = NarrationCompletionOutcomeResolver.ResolveAfterAmbiguousCommand(null);

        Assert.False(outcome.AcceptCompletion);
        Assert.False(outcome.DeleteCandidateArtifact);
    }
}
