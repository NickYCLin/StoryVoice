using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.UnitTests;

public sealed class CharacterVoiceProfileOperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Operation_preserves_a_legacy_40_character_task_handle_as_durable_evidence()
    {
        var operation = CreateStagedOperation();
        var legacyTaskId = $"route_{new string('a', 34)}";

        operation.MarkRemotePrepared(legacyTaskId, "辨識草稿。", Now.AddMinutes(1));

        Assert.Equal(40, operation.RemoteTaskId!.Length);
        Assert.Equal(legacyTaskId, operation.RemoteTaskId);
        Assert.Equal(CharacterVoiceProfileOperationState.RemotePrepared, operation.State);
    }

    [Fact]
    public void NeedsAttention_retains_the_remote_task_and_blocks_activation()
    {
        var operation = CreateStagedOperation();
        operation.MarkRemotePrepared("clone-task-1", "辨識草稿。", Now.AddMinutes(1));

        operation.MarkNeedsAttention("remote_draft_contract_mismatch", Now.AddMinutes(2));

        Assert.Equal("clone-task-1", operation.RemoteTaskId);
        Assert.Equal(CharacterVoiceProfileOperationState.NeedsAttention, operation.State);
        Assert.Equal("remote_draft_contract_mismatch", operation.SafeErrorCode);
        Assert.Throws<InvalidOperationException>(() => operation.MarkActivated(Now.AddMinutes(3)));
    }

    [Fact]
    public void Definite_pre_send_rejection_is_terminal_but_retains_local_evidence()
    {
        var operation = CreateStagedOperation();

        operation.MarkRejected("remote_prepare_not_sent", Now.AddMinutes(1));

        Assert.Equal(CharacterVoiceProfileOperationState.Rejected, operation.State);
        Assert.Equal("remote_prepare_not_sent", operation.SafeErrorCode);
        Assert.Null(operation.RemoteTaskId);
        Assert.NotEmpty(operation.ReferenceAudioRelativePath);
    }

    [Fact]
    public void Staged_operation_persists_privacy_reduced_receipt_evidence_and_scopes()
    {
        var operation = CreateStagedOperation();

        Assert.Equal("voice-actor-alias", operation.RecorderName);
        Assert.Equal(new DateOnly(2026, 8, 17), operation.RecordingDate);
        Assert.Equal(new DateOnly(2026, 8, 18), operation.ConsentSignedDate);
        Assert.True(operation.PrivateEvaluationAllowed);
        Assert.True(operation.FormalNarrationAllowed);
        Assert.False(operation.PublicDistributionAllowed);
        Assert.False(operation.CommercialUseAllowed);
        Assert.Equal(
            [
                CharacterVoiceConsentScopes.PrivateEvaluation,
                CharacterVoiceConsentScopes.FormalNarration,
            ],
            operation.UsageScopes);
        Assert.Equal(new string('b', 64), operation.ConsentRecordSha256);
        Assert.Equal(new string('c', 64), operation.ConsentReceiptSha256);
        Assert.Equal(
            CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(operation.ExpectedTranscript),
            operation.ExpectedTranscriptSha256);
    }

    [Fact]
    public void Staging_rejects_evidence_for_a_different_canonical_transcript()
    {
        var evidence = CreateEvidence("另一份逐字稿。");

        Assert.Throws<ArgumentException>(() => CharacterVoiceProfileOperation.StageCreate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CharacterVoiceProfileKind.Base,
            sceneCode: null,
            evidence,
            "這是錄音中的實際內容。",
            "2026/08/reference.wav",
            new string('a', 64),
            10,
            Guid.NewGuid(),
            "key-v1",
            Now));
    }

    [Fact]
    public void BuildProfileName_is_deterministic_bounded_and_hash_distinguishes_long_names()
    {
        var sharedPrefix = new string('角', 140);

        var first = CharacterVoiceProfileService.BuildProfileName(
            sharedPrefix + "甲",
            CharacterVoiceProfileKind.Base,
            sceneCode: null);
        var same = CharacterVoiceProfileService.BuildProfileName(
            sharedPrefix + "甲",
            CharacterVoiceProfileKind.Base,
            sceneCode: null);
        var second = CharacterVoiceProfileService.BuildProfileName(
            sharedPrefix + "乙",
            CharacterVoiceProfileKind.Base,
            sceneCode: null);

        Assert.Equal(first, same);
        Assert.True(first.Length <= 120);
        Assert.True(second.Length <= 120);
        Assert.NotEqual(first, second);
        Assert.EndsWith("-base-", first[..^12], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProfileName_does_not_split_a_UTF16_surrogate_pair()
    {
        var result = CharacterVoiceProfileService.BuildProfileName(
            string.Concat(Enumerable.Repeat("🚀", 80)),
            CharacterVoiceProfileKind.Base,
            sceneCode: null);

        Assert.True(result.Length <= 120);
        Assert.False(char.IsHighSurrogate(result[^1]));
        Assert.DoesNotContain('�', result);
    }

    private static CharacterVoiceProfileOperation CreateStagedOperation() =>
        CharacterVoiceProfileOperation.StageCreate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CharacterVoiceProfileKind.Base,
            sceneCode: null,
            CreateEvidence("這是錄音中的實際內容。"),
            "這是錄音中的實際內容。",
            "2026/08/reference.wav",
            new string('a', 64),
            10,
            Guid.NewGuid(),
            "key-v1",
            Now);

    private static CharacterVoiceConsentEvidence CreateEvidence(string transcript) =>
        CharacterVoiceConsentEvidence.Create(
            "voice-actor-alias",
            new DateOnly(2026, 8, 17),
            new DateOnly(2026, 8, 18),
            CharacterVoiceConsentTypes.SelfRecorded,
            [
                CharacterVoiceConsentScopes.PrivateEvaluation,
                CharacterVoiceConsentScopes.FormalNarration,
            ],
            new string('b', 64),
            new string('c', 64),
            CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(transcript),
            CharacterVoiceConsentEvidence.CurrentEvidenceVersion,
            CharacterVoiceConsentEvidence.CurrentAttestationVersion,
            new DateOnly(2026, 8, 18));
}
