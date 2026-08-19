using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Series;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure.Narrations;

internal sealed class LocalClonePreviewService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    LocalCloneProfileSynthesizer profileSynthesizer,
    IOptions<LocalClonePreviewOptions> options) : ILocalClonePreviewService
{
    private const int MaximumPreviewTextLength = 200;

    public async Task<LocalClonePreviewAvailabilityResponse?> GetAvailabilityAsync(
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var link = await FindOwnerScopedLinkAsync(ownerId, seriesId, characterId, cancellationToken);
        if (link is null)
        {
            return null;
        }

        if (!options.Value.Enabled || link.CharacterProfileId is not { } characterProfileId)
        {
            return new LocalClonePreviewAvailabilityResponse(false, null);
        }

        var availability = await profileSynthesizer.GetAvailabilityAsync(
            ownerId,
            characterProfileId,
            cancellationToken);
        return new LocalClonePreviewAvailabilityResponse(
            availability.Available,
            availability.Label);
    }

    public async Task<LocalClonePreviewAudio?> PreviewAsync(
        Guid seriesId,
        Guid characterId,
        LocalClonePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var link = await FindOwnerScopedLinkAsync(ownerId, seriesId, characterId, cancellationToken);
        if (link is null)
        {
            return null;
        }

        var text = ValidatePreviewText(request);
        if (!options.Value.Enabled)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.Disabled);
        }

        if (link.CharacterProfileId is not { } characterProfileId)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.NotConfigured);
        }

        var audio = await profileSynthesizer.SynthesizeAsync(
            ownerId,
            characterProfileId,
            text,
            cancellationToken);
        return new LocalClonePreviewAudio(audio.Content, audio.ContentType);
    }

    public async Task<LocalClonePreviewAvailabilityResponse?> GetCharacterProfileAvailabilityAsync(
        Guid characterProfileId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        if (!await OwnerScopedProfileExistsAsync(ownerId, characterProfileId, cancellationToken))
        {
            return null;
        }

        if (!options.Value.Enabled)
        {
            return new LocalClonePreviewAvailabilityResponse(false, null);
        }

        var availability = await profileSynthesizer.GetAvailabilityAsync(
            ownerId,
            characterProfileId,
            cancellationToken);
        return new LocalClonePreviewAvailabilityResponse(
            availability.Available,
            availability.Label);
    }

    public async Task<LocalClonePreviewAudio?> PreviewCharacterProfileAsync(
        Guid characterProfileId,
        LocalClonePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        if (!await OwnerScopedProfileExistsAsync(ownerId, characterProfileId, cancellationToken))
        {
            return null;
        }

        var text = ValidatePreviewText(request);
        if (!options.Value.Enabled)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.Disabled);
        }

        var audio = await profileSynthesizer.SynthesizeAsync(
            ownerId,
            characterProfileId,
            text,
            cancellationToken);
        return new LocalClonePreviewAudio(audio.Content, audio.ContentType);
    }

    private async Task<SeriesCharacterLink?> FindOwnerScopedLinkAsync(
        Guid ownerId,
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken) =>
        await dbContext.SeriesCharacters
            .AsNoTracking()
            .Where(character => character.OwnerId == ownerId
                && character.SeriesId == seriesId
                && character.Id == characterId)
            .Select(character => new SeriesCharacterLink(character.CharacterProfileId))
            .SingleOrDefaultAsync(cancellationToken);

    private Task<bool> OwnerScopedProfileExistsAsync(
        Guid ownerId,
        Guid characterProfileId,
        CancellationToken cancellationToken) =>
        dbContext.CharacterProfiles
            .AsNoTracking()
            .AnyAsync(
                profile => profile.OwnerId == ownerId
                    && profile.Id == characterProfileId,
                cancellationToken);

    private static string ValidatePreviewText(LocalClonePreviewRequest request)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("試音文字不可為空白。", nameof(request));
        }

        if (text.Length > MaximumPreviewTextLength)
        {
            throw new ArgumentException(
                $"試音文字不可超過 {MaximumPreviewTextLength} 個字元。",
                nameof(request));
        }

        return text;
    }

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("目前使用者識別碼無效。");
        }

        return currentUser.UserId;
    }

    private sealed record SeriesCharacterLink(Guid? CharacterProfileId);
}
