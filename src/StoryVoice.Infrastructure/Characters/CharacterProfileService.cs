using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Characters;
using StoryVoice.Domain.Characters;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure.Characters;

/// <summary>
/// Owner-scoped CRUD for the character library. Deleting a character cascades its
/// <see cref="StoryVoice.Domain.Narrations.CharacterVoiceProfile"/> rows at the database level
/// (<c>FK_cvp_character_profile ON DELETE CASCADE</c>) but not their reference-audio files on
/// disk, so this service cleans those up itself. A character still linked from any
/// <see cref="StoryVoice.Domain.Series.SeriesCharacter"/> can't be deleted
/// (<c>FK_series_characters_character_profile ON DELETE RESTRICT</c>) — the series has to unlink
/// it first.
/// </summary>
internal sealed class CharacterProfileService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    LocalCharacterAvatarStorage avatarStorage,
    LocalCharacterVoiceAudioStorage voiceAudioStorage) : ICharacterProfileService
{
    public async Task<IReadOnlyList<CharacterProfileResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profiles = await dbContext.CharacterProfiles
            .AsNoTracking()
            .Where(profile => profile.OwnerId == ownerId)
            .OrderBy(profile => profile.CanonicalName)
            .ToListAsync(cancellationToken);
        return profiles.Select(ToResponse).ToArray();
    }

    public async Task<CharacterProfileResponse?> GetAsync(Guid characterProfileId, CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadAsync(ownerId, characterProfileId, cancellationToken);
        return profile is null ? null : ToResponse(profile);
    }

    public async Task<CharacterProfileResponse> CreateAsync(
        CreateCharacterProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var now = DateTimeOffset.UtcNow;
        var profile = CharacterProfile.Create(
            Guid.NewGuid(),
            ownerId,
            request.CanonicalName,
            avatarRelativePath: null,
            request.Age,
            request.Gender,
            request.Birthday,
            request.Personality,
            request.Catchphrase,
            request.Background,
            request.SpeakingStyle,
            now);

        dbContext.CharacterProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<CharacterProfileResponse?> UpdateAsync(
        Guid characterProfileId,
        UpdateCharacterProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadAsync(ownerId, characterProfileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        profile.Update(
            request.CanonicalName,
            request.Age,
            request.Gender,
            request.Birthday,
            request.Personality,
            request.Catchphrase,
            request.Background,
            request.SpeakingStyle,
            DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<CharacterProfileResponse?> SetAvatarAsync(
        Guid characterProfileId,
        Stream avatar,
        string fileName,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadAsync(ownerId, characterProfileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var previousAvatarPath = profile.AvatarRelativePath;
        var (relativePath, _) = await avatarStorage.SaveAsync(avatar, fileName, cancellationToken);
        profile.SetAvatar(relativePath, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (previousAvatarPath is not null)
        {
            await avatarStorage.DeleteAsync(previousAvatarPath, cancellationToken);
        }

        return ToResponse(profile);
    }

    public async Task<bool> DeleteAsync(Guid characterProfileId, CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadAsync(ownerId, characterProfileId, cancellationToken);
        if (profile is null)
        {
            return false;
        }

        var stillLinkedToASeries = await dbContext.SeriesCharacters.AnyAsync(
            character => character.OwnerId == ownerId && character.CharacterProfileId == characterProfileId,
            cancellationToken);
        if (stillLinkedToASeries)
        {
            throw new InvalidOperationException("這個角色目前還被某些系列使用中，請先從系列移除這個角色後再刪除。");
        }

        var voiceAudioPaths = await dbContext.CharacterVoiceProfiles
            .Where(voiceProfile => voiceProfile.OwnerId == ownerId && voiceProfile.CharacterProfileId == characterProfileId)
            .Select(voiceProfile => voiceProfile.ReferenceAudioRelativePath)
            .Where(path => path != null)
            .ToListAsync(cancellationToken);

        try
        {
            dbContext.CharacterProfiles.Remove(profile);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("這個角色目前還被某些系列使用中，請先從系列移除這個角色後再刪除。", exception);
        }

        if (profile.AvatarRelativePath is not null)
        {
            await avatarStorage.DeleteAsync(profile.AvatarRelativePath, cancellationToken);
        }

        foreach (var path in voiceAudioPaths)
        {
            await voiceAudioStorage.DeleteAsync(path!, cancellationToken);
        }

        return true;
    }

    public async Task<CharacterProfileAvatar?> GetAvatarAsync(Guid characterProfileId, CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var profile = await LoadAsync(ownerId, characterProfileId, cancellationToken);
        if (profile?.AvatarRelativePath is null)
        {
            return null;
        }

        return new CharacterProfileAvatar(
            avatarStorage.ResolveFullPath(profile.AvatarRelativePath),
            avatarStorage.ResolveContentType(profile.AvatarRelativePath));
    }

    private async Task<CharacterProfile?> LoadAsync(
        Guid ownerId,
        Guid characterProfileId,
        CancellationToken cancellationToken) =>
        await dbContext.CharacterProfiles.SingleOrDefaultAsync(
            profile => profile.OwnerId == ownerId && profile.Id == characterProfileId,
            cancellationToken);

    private static CharacterProfileResponse ToResponse(CharacterProfile profile) =>
        new(
            profile.Id,
            profile.CanonicalName,
            profile.AvatarRelativePath is not null,
            profile.Age,
            profile.Gender,
            profile.Birthday,
            profile.Personality,
            profile.Catchphrase,
            profile.Background,
            profile.SpeakingStyle,
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
