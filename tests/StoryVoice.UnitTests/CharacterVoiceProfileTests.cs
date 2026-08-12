using StoryVoice.Domain.Narrations;

namespace StoryVoice.UnitTests;

public sealed class CharacterVoiceProfileTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid SeriesId = Guid.NewGuid();
    private static readonly Guid CharacterId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static CharacterVoiceProfile CreateBaseClone() =>
        CharacterVoiceProfile.CreateClone(
            Guid.NewGuid(),
            OwnerId,
            SeriesId,
            CharacterId,
            CharacterVoiceProfileKind.Base,
            sceneCode: null,
            CharacterVoiceConsentTypes.SelfRecorded,
            referenceAudioRelativePath: "voices/a.wav",
            referenceAudioSha256: new string('a', 64),
            rightsConfirmedByUserId: Guid.NewGuid(),
            Now);

    [Fact]
    public void CreateClone_starts_pending_and_carries_rights_confirmation()
    {
        var profile = CreateBaseClone();

        Assert.Equal(CharacterVoiceProfileStatus.Pending, profile.Status);
        Assert.Equal(CharacterVoiceProfileMode.Clone, profile.Mode);
        Assert.NotNull(profile.RightsConfirmedAt);
        Assert.Null(profile.VoicePromptText);
    }

    [Fact]
    public void CreateClone_rejects_an_unknown_consent_type()
    {
        Assert.Throws<ArgumentException>(() => CharacterVoiceProfile.CreateClone(
            Guid.NewGuid(), OwnerId, SeriesId, CharacterId, CharacterVoiceProfileKind.Base, null,
            "totally_made_up", "voices/a.wav", new string('a', 64), Guid.NewGuid(), Now));
    }

    [Fact]
    public void CreateClone_rejects_a_malformed_sha256()
    {
        Assert.Throws<ArgumentException>(() => CharacterVoiceProfile.CreateClone(
            Guid.NewGuid(), OwnerId, SeriesId, CharacterId, CharacterVoiceProfileKind.Base, null,
            CharacterVoiceConsentTypes.SelfRecorded, "voices/a.wav", "not-a-hash", Guid.NewGuid(), Now));
    }

    [Fact]
    public void CreateClone_rejects_a_scene_code_on_a_base_profile()
    {
        Assert.Throws<ArgumentException>(() => CharacterVoiceProfile.CreateClone(
            Guid.NewGuid(), OwnerId, SeriesId, CharacterId, CharacterVoiceProfileKind.Base, "nervous",
            CharacterVoiceConsentTypes.SelfRecorded, "voices/a.wav", new string('a', 64), Guid.NewGuid(), Now));
    }

    [Fact]
    public void CreateClone_rejects_a_missing_scene_code_on_a_scene_profile()
    {
        Assert.Throws<ArgumentException>(() => CharacterVoiceProfile.CreateClone(
            Guid.NewGuid(), OwnerId, SeriesId, CharacterId, CharacterVoiceProfileKind.Scene, null,
            CharacterVoiceConsentTypes.SelfRecorded, "voices/a.wav", new string('a', 64), Guid.NewGuid(), Now));
    }

    [Fact]
    public void CreateClone_rejects_an_unrecognized_scene_code()
    {
        Assert.Throws<ArgumentException>(() => CharacterVoiceProfile.CreateClone(
            Guid.NewGuid(), OwnerId, SeriesId, CharacterId, CharacterVoiceProfileKind.Scene, "furious",
            CharacterVoiceConsentTypes.SelfRecorded, "voices/a.wav", new string('a', 64), Guid.NewGuid(), Now));
    }

    [Fact]
    public void CreateDesign_is_immediately_ready_with_no_consent_lifecycle()
    {
        var profile = CharacterVoiceProfile.CreateDesign(
            Guid.NewGuid(), OwnerId, SeriesId, CharacterId, CharacterVoiceProfileKind.Scene, "happy",
            "一位活潑開朗的年輕女性，語速稍快。", Now);

        Assert.Equal(CharacterVoiceProfileStatus.Ready, profile.Status);
        Assert.Equal(CharacterVoiceProfileMode.Design, profile.Mode);
        Assert.Null(profile.ConsentType);
        Assert.Null(profile.RightsConfirmedAt);
    }

    [Fact]
    public void AttachDraftTranscript_moves_a_clone_profile_to_awaiting_confirmation()
    {
        var profile = CreateBaseClone();

        profile.AttachDraftTranscript("task-123", "這是自動轉錄的草稿。", Now.AddMinutes(1));

        Assert.Equal(CharacterVoiceProfileStatus.AwaitingTranscriptConfirmation, profile.Status);
        Assert.Equal("task-123", profile.VoiceProfileTaskId);
        Assert.Null(profile.TranscriptConfirmedAt);
    }

    [Fact]
    public void ConfirmTranscript_locks_the_transcript_and_marks_ready()
    {
        var profile = CreateBaseClone();
        profile.AttachDraftTranscript("task-123", "草稿文字。", Now.AddMinutes(1));

        profile.ConfirmTranscript("修正後的正確文字。", Now.AddMinutes(2));

        Assert.Equal(CharacterVoiceProfileStatus.Ready, profile.Status);
        Assert.Equal("修正後的正確文字。", profile.Transcript);
        Assert.NotNull(profile.TranscriptConfirmedAt);
    }

    [Fact]
    public void ConfirmTranscript_before_a_draft_exists_is_rejected()
    {
        var profile = CreateBaseClone();

        Assert.Throws<InvalidOperationException>(() => profile.ConfirmTranscript("文字。", Now.AddMinutes(1)));
    }

    [Fact]
    public void AttachDraftTranscript_after_confirmation_is_rejected()
    {
        var profile = CreateBaseClone();
        profile.AttachDraftTranscript("task-123", "草稿。", Now.AddMinutes(1));
        profile.ConfirmTranscript("確認文字。", Now.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(
            () => profile.AttachDraftTranscript("task-456", "新的草稿。", Now.AddMinutes(3)));
    }

    [Fact]
    public void ReattachRebuiltTask_requires_a_previously_confirmed_transcript()
    {
        var profile = CreateBaseClone();

        Assert.Throws<InvalidOperationException>(
            () => profile.ReattachRebuiltTask("task-rebuilt", Now.AddMinutes(1)));
    }

    [Fact]
    public void ReattachRebuiltTask_restores_ready_status_after_rebuilding_on_a_new_station()
    {
        var profile = CreateBaseClone();
        profile.AttachDraftTranscript("task-123", "草稿。", Now.AddMinutes(1));
        profile.ConfirmTranscript("確認文字。", Now.AddMinutes(2));
        profile.MarkFailed(Now.AddMinutes(3));

        profile.ReattachRebuiltTask("task-rebuilt", Now.AddMinutes(4));

        Assert.Equal(CharacterVoiceProfileStatus.Ready, profile.Status);
        Assert.Equal("task-rebuilt", profile.VoiceProfileTaskId);
    }

    [Fact]
    public void Clone_only_operations_are_rejected_on_a_design_profile()
    {
        var profile = CharacterVoiceProfile.CreateDesign(
            Guid.NewGuid(), OwnerId, SeriesId, CharacterId, CharacterVoiceProfileKind.Base, null,
            "沉穩的台灣男性技師。", Now);

        Assert.Throws<InvalidOperationException>(() => profile.AttachDraftTranscript("t", "x", Now));
        Assert.Throws<InvalidOperationException>(() => profile.ConfirmTranscript("x", Now));
        Assert.Throws<InvalidOperationException>(() => profile.ReattachRebuiltTask("t", Now));
    }
}
