using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

/// <summary>
/// Owner-scoped lifecycle service for <see cref="CharacterVoiceProfile"/>. Design-mode profiles are
/// ready the moment they're created (no 3wa round trip needed); Clone-mode profiles proxy the 3wa
/// Cluster API's profile_prepare/status/confirm sequence, keeping the locally stored WAV + confirmed
/// transcript as the canonical, rebuildable asset behind whatever opaque task id the pinned station
/// currently has.
/// </summary>
internal sealed class CharacterVoiceProfileService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    LocalCharacterVoiceAudioStorage audioStorage,
    IThreeWaVoiceProfileClient threeWaClient) : ICharacterVoiceProfileService
{
    public async Task<IReadOnlyList<CharacterVoiceProfileResponse>?> ListAsync(
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var character = await LoadCharacterAsync(ownerId, seriesId, characterId, cancellationToken);
        if (character is null)
        {
            return null;
        }

        var profiles = await dbContext.CharacterVoiceProfiles
            .Where(profile => profile.OwnerId == ownerId
                && profile.SeriesId == seriesId
                && profile.CharacterId == characterId)
            .OrderBy(profile => profile.Kind)
            .ThenBy(profile => profile.SceneCode)
            .ToListAsync(cancellationToken);
        return profiles.Select(ToResponse).ToArray();
    }

    public async Task<CharacterVoiceProfileResponse?> CreateClonedAsync(
        Guid seriesId,
        Guid characterId,
        string kind,
        string? sceneCode,
        string consentType,
        Stream referenceAudio,
        string referenceAudioFileName,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var parsedKind = ParseKind(kind);
        var character = await LoadCharacterAsync(ownerId, seriesId, characterId, cancellationToken);
        if (character is null)
        {
            return null;
        }

        await EnsureNoDuplicateAsync(ownerId, characterId, parsedKind, sceneCode, cancellationToken);

        var stored = await audioStorage.SaveAsync(referenceAudio, referenceAudioFileName, cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var profile = CharacterVoiceProfile.CreateClone(
                Guid.NewGuid(),
                ownerId,
                seriesId,
                characterId,
                parsedKind,
                sceneCode,
                consentType,
                stored.RelativePath,
                stored.Sha256Hex,
                ownerId,
                now);

            await using (var uploadStream = File.OpenRead(audioStorage.ResolveFullPath(stored.RelativePath)))
            {
                var prepared = await threeWaClient.PrepareAsync(
                    uploadStream,
                    referenceAudioFileName,
                    BuildProfileName(character.CanonicalName, parsedKind, sceneCode),
                    consentType,
                    promptText: null,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(prepared.DraftTranscript))
                {
                    profile.AttachDraftTranscript(prepared.TaskId, prepared.DraftTranscript, now);
                }
                else
                {
                    profile.AttachPendingTask(prepared.TaskId, now);
                }
            }

            dbContext.CharacterVoiceProfiles.Add(profile);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToResponse(profile);
        }
        catch
        {
            await audioStorage.DeleteAsync(stored.RelativePath, cancellationToken);
            throw;
        }
    }

    public async Task<CharacterVoiceProfileResponse?> CreateDesignedAsync(
        Guid seriesId,
        Guid characterId,
        string kind,
        string? sceneCode,
        CreateDesignedVoiceProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var parsedKind = ParseKind(kind);
        var character = await LoadCharacterAsync(ownerId, seriesId, characterId, cancellationToken);
        if (character is null)
        {
            return null;
        }

        await EnsureNoDuplicateAsync(ownerId, characterId, parsedKind, sceneCode, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var profile = CharacterVoiceProfile.CreateDesign(
            Guid.NewGuid(),
            ownerId,
            seriesId,
            characterId,
            parsedKind,
            sceneCode,
            request.VoicePrompt,
            now);

        dbContext.CharacterVoiceProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<CharacterVoiceProfileResponse?> RefreshStatusAsync(
        Guid seriesId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadProfileAsync(ownerId, seriesId, profileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        if (profile.Mode != CharacterVoiceProfileMode.Clone
            || profile.Status is CharacterVoiceProfileStatus.Ready or CharacterVoiceProfileStatus.Failed)
        {
            return ToResponse(profile);
        }

        var status = await threeWaClient.GetStatusAsync(profile.VoiceProfileTaskId!, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(status.TaskStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            profile.MarkFailed(now);
        }
        else if (!status.TranscriptConfirmed && !string.IsNullOrWhiteSpace(status.DraftTranscript))
        {
            profile.AttachDraftTranscript(profile.VoiceProfileTaskId!, status.DraftTranscript, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<CharacterVoiceProfileResponse?> ConfirmTranscriptAsync(
        Guid seriesId,
        Guid profileId,
        ConfirmVoiceProfileTranscriptRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadProfileAsync(ownerId, seriesId, profileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        await threeWaClient.ConfirmAsync(profile.VoiceProfileTaskId!, request.Transcript, cancellationToken);
        profile.ConfirmTranscript(request.Transcript, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<CharacterVoiceProfileResponse?> RebuildAsync(
        Guid seriesId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadProfileAsync(ownerId, seriesId, profileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var character = await LoadCharacterAsync(ownerId, seriesId, profile.CharacterId, cancellationToken)
            ?? throw new InvalidOperationException("找不到這個聲線所屬的角色。");

        await using var uploadStream = File.OpenRead(audioStorage.ResolveFullPath(profile.ReferenceAudioRelativePath!));
        var prepared = await threeWaClient.PrepareAsync(
            uploadStream,
            Path.GetFileName(profile.ReferenceAudioRelativePath!),
            BuildProfileName(character.CanonicalName, profile.Kind, profile.SceneCode),
            profile.ConsentType!,
            promptText: profile.Transcript,
            cancellationToken);

        await threeWaClient.ConfirmAsync(prepared.TaskId, profile.Transcript!, cancellationToken);
        profile.ReattachRebuiltTask(prepared.TaskId, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<bool> DeleteAsync(Guid seriesId, Guid profileId, CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadProfileAsync(ownerId, seriesId, profileId, cancellationToken);
        if (profile is null)
        {
            return false;
        }

        dbContext.CharacterVoiceProfiles.Remove(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (profile.ReferenceAudioRelativePath is not null)
        {
            await audioStorage.DeleteAsync(profile.ReferenceAudioRelativePath, cancellationToken);
        }

        return true;
    }

    public async Task<CharacterVoiceProfileAudio?> GetReferenceAudioAsync(
        Guid seriesId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadProfileAsync(ownerId, seriesId, profileId, cancellationToken);
        if (profile?.ReferenceAudioRelativePath is null)
        {
            return null;
        }

        return new CharacterVoiceProfileAudio(
            audioStorage.ResolveFullPath(profile.ReferenceAudioRelativePath),
            "audio/wav");
    }

    private async Task<SeriesCharacter?> LoadCharacterAsync(
        Guid ownerId,
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken) =>
        await dbContext.SeriesCharacters.SingleOrDefaultAsync(
            character => character.OwnerId == ownerId
                && character.SeriesId == seriesId
                && character.Id == characterId,
            cancellationToken);

    private async Task<CharacterVoiceProfile?> LoadProfileAsync(
        Guid ownerId,
        Guid seriesId,
        Guid profileId,
        CancellationToken cancellationToken) =>
        await dbContext.CharacterVoiceProfiles.SingleOrDefaultAsync(
            profile => profile.OwnerId == ownerId && profile.SeriesId == seriesId && profile.Id == profileId,
            cancellationToken);

    private async Task EnsureNoDuplicateAsync(
        Guid ownerId,
        Guid characterId,
        CharacterVoiceProfileKind kind,
        string? sceneCode,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.CharacterVoiceProfiles.AnyAsync(
            profile => profile.OwnerId == ownerId
                && profile.CharacterId == characterId
                && (kind == CharacterVoiceProfileKind.Base
                    ? profile.Kind == CharacterVoiceProfileKind.Base
                    : profile.SceneCode == sceneCode),
            cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException(
                kind == CharacterVoiceProfileKind.Base
                    ? "這個角色已經有基礎聲線了。"
                    : "這個角色在這個情境下已經有聲線了。");
        }
    }

    private static string BuildProfileName(string characterName, CharacterVoiceProfileKind kind, string? sceneCode) =>
        kind == CharacterVoiceProfileKind.Base
            ? $"{characterName}-base"
            : $"{characterName}-{sceneCode}";

    private static CharacterVoiceProfileKind ParseKind(string kind)
    {
        if (!Enum.TryParse<CharacterVoiceProfileKind>(kind, ignoreCase: true, out var parsed))
        {
            throw new ArgumentException("聲線類型無效。", nameof(kind));
        }

        return parsed;
    }

    private static CharacterVoiceProfileResponse ToResponse(CharacterVoiceProfile profile) =>
        new(
            profile.Id,
            profile.CharacterId,
            profile.Kind.ToString(),
            profile.SceneCode,
            profile.Mode.ToString(),
            profile.ConsentType,
            profile.VoicePromptText,
            profile.Transcript,
            profile.TranscriptConfirmedAt is not null,
            profile.Status.ToString(),
            profile.CreatedAt,
            profile.UpdatedAt);

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("目前使用者識別碼無效。");
        }

        return currentUser.UserId;
    }
}
