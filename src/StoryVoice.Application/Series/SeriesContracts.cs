using StoryVoice.Domain.Series;

namespace StoryVoice.Application.Series;

public sealed record CreateStorySeriesRequest(
    string Name,
    string NarratorProvider,
    string NarratorVoice,
    string NarratorRate,
    string NarratorPitch,
    string NarratorVolume,
    int DefaultSpeakerPauseMs);

public sealed record AddSeriesBookRequest(
    Guid BookId,
    string VolumeLabel,
    int SortOrder);

public sealed record AddSeriesCharacterRequest(
    string CanonicalName,
    SeriesCharacterRole Role,
    string VoiceProvider,
    string Voice,
    string Rate,
    string Pitch,
    string Volume,
    string? Notes,
    Guid? CharacterProfileId = null);

public sealed record UpdateSeriesCharacterRequest(
    string CanonicalName,
    SeriesCharacterRole Role,
    string VoiceProvider,
    string Voice,
    string Rate,
    string Pitch,
    string Volume,
    string? Notes,
    Guid? CharacterProfileId = null);

public sealed record AddSeriesCharacterAliasRequest(string Alias);

public sealed record ApplyAnalyzedSeriesCharacterRequest(
    string SourceName,
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    SeriesCharacterRole Role,
    string VoiceProvider,
    string Voice,
    string Rate,
    string Pitch,
    string Volume);

public sealed record ApplyAnalyzedSeriesCharactersRequest(
    Guid BookId,
    IReadOnlyList<ApplyAnalyzedSeriesCharacterRequest> Characters);

public sealed record SetSeriesPointOfViewCharacterRequest(Guid? CharacterId);

public sealed record SeriesVoiceOptionResponse(
    string Provider,
    string Voice,
    string DisplayName,
    string Locale);

public sealed record StorySeriesSummaryResponse(
    Guid Id,
    string Name,
    int BookCount,
    int CharacterCount,
    Guid? ActiveCastRevisionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StorySeriesBookResponse(
    Guid Id,
    Guid BookId,
    string BookTitle,
    string VolumeLabel,
    int SortOrder,
    int MembershipRevision,
    Guid? ActiveNarrationJobId);

public sealed record StorySeriesAliasResponse(
    Guid Id,
    string Value);

public sealed record StorySeriesCharacterResponse(
    Guid Id,
    string CanonicalName,
    string Role,
    string VoiceProvider,
    string Voice,
    string Rate,
    string Pitch,
    string Volume,
    string? Notes,
    Guid? CharacterProfileId,
    IReadOnlyList<StorySeriesAliasResponse> Aliases,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StorySeriesDetailsResponse(
    Guid Id,
    string Name,
    string NarratorProvider,
    string NarratorVoice,
    string NarratorRate,
    string NarratorPitch,
    string NarratorVolume,
    int DefaultSpeakerPauseMs,
    Guid? ActiveCastRevisionId,
    Guid? PointOfViewCharacterId,
    IReadOnlyList<StorySeriesBookResponse> Books,
    IReadOnlyList<StorySeriesCharacterResponse> Characters,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
