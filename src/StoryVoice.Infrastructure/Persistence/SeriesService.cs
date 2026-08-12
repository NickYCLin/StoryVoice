using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Series;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class SeriesService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    IStorySeriesRepository repository,
    IOptions<SeriesVoiceCatalogOptions> voiceCatalogOptions) : ISeriesService
{
    private readonly IReadOnlyList<SeriesVoiceCatalogEntry> _voiceCatalog =
        voiceCatalogOptions.Value.Voices.ToArray();

    public async Task<IReadOnlyList<StorySeriesSummaryResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        return await dbContext.StorySeries
            .AsNoTracking()
            .Where(series => series.OwnerId == ownerId)
            .OrderBy(series => series.Name)
            .Select(series => new StorySeriesSummaryResponse(
                series.Id,
                series.Name,
                series.Books.Count,
                series.Characters.Count,
                series.ActiveCastRevisionId,
                series.CreatedAt,
                series.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<StorySeriesDetailsResponse?> GetAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        var ownerId = EnsureCurrentOwnerId();
        var series = await dbContext.StorySeries
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Books)
            .Include(candidate => candidate.Characters)
            .Include(candidate => candidate.IdentityKeys)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == seriesId && candidate.OwnerId == ownerId,
                cancellationToken);
        return series is null
            ? null
            : await ToDetailsAsync(series, cancellationToken);
    }

    public async Task<StorySeriesDetailsResponse> CreateAsync(
        CreateStorySeriesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var narratorVoice = ResolveVoice(request.NarratorProvider, request.NarratorVoice);
        var series = StorySeries.Create(
            ownerId,
            request.Name,
            narratorVoice.Provider,
            narratorVoice.Voice,
            request.NarratorRate,
            request.NarratorPitch,
            request.NarratorVolume,
            request.DefaultSpeakerPauseMs);
        if (await dbContext.StorySeries.AsNoTracking().AnyAsync(
                candidate => candidate.OwnerId == ownerId
                    && candidate.NormalizedName == series.NormalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException("同一位使用者不可建立名稱相同的系列。");
        }

        await repository.AddAsync(series, cancellationToken);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(series, cancellationToken);
    }

    public async Task<StorySeriesDetailsResponse?> AddBookAsync(
        Guid seriesId,
        AddSeriesBookRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        ArgumentNullException.ThrowIfNull(request);
        EnsureId(request.BookId, nameof(request.BookId));
        var ownerId = EnsureCurrentOwnerId();
        var series = await repository.GetForMutationAsync(seriesId, cancellationToken);
        if (series is null)
        {
            return null;
        }

        var book = await dbContext.Books.SingleOrDefaultAsync(
            candidate => candidate.Id == request.BookId && candidate.OwnerId == ownerId,
            cancellationToken);
        if (book is null || book.Status == BookStatus.Linked)
        {
            return null;
        }

        series.AddBook(book, request.VolumeLabel, request.SortOrder);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(series, cancellationToken);
    }

    public async Task<StorySeriesDetailsResponse?> AddCharacterAsync(
        Guid seriesId,
        AddSeriesCharacterRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        ArgumentNullException.ThrowIfNull(request);
        var voice = ResolveVoice(request.VoiceProvider, request.Voice);
        var ownerId = EnsureCurrentOwnerId();
        await EnsureCharacterProfileOwnedAsync(ownerId, request.CharacterProfileId, cancellationToken);
        var series = await repository.GetForMutationAsync(seriesId, cancellationToken);
        if (series is null)
        {
            return null;
        }

        series.AddCharacter(
            request.CanonicalName,
            request.Role,
            voice.Provider,
            voice.Voice,
            request.Rate,
            request.Pitch,
            request.Volume,
            request.Notes,
            request.CharacterProfileId);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(series, cancellationToken);
    }

    public async Task<StorySeriesDetailsResponse?> UpdateCharacterAsync(
        Guid seriesId,
        Guid characterId,
        UpdateSeriesCharacterRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(characterId, nameof(characterId));
        ArgumentNullException.ThrowIfNull(request);
        var voice = ResolveVoice(request.VoiceProvider, request.Voice);
        var ownerId = EnsureCurrentOwnerId();
        await EnsureCharacterProfileOwnedAsync(ownerId, request.CharacterProfileId, cancellationToken);
        var series = await repository.GetForMutationAsync(seriesId, cancellationToken);
        if (series is null || series.Characters.All(character => character.Id != characterId))
        {
            return null;
        }

        series.UpdateCharacter(
            characterId,
            request.CanonicalName,
            request.Role,
            voice.Provider,
            voice.Voice,
            request.Rate,
            request.Pitch,
            request.Volume,
            request.Notes,
            request.CharacterProfileId);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(series, cancellationToken);
    }

    public async Task<StorySeriesDetailsResponse?> AddAliasAsync(
        Guid seriesId,
        Guid characterId,
        AddSeriesCharacterAliasRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(characterId, nameof(characterId));
        ArgumentNullException.ThrowIfNull(request);
        var series = await repository.GetForMutationAsync(seriesId, cancellationToken);
        if (series is null || series.Characters.All(character => character.Id != characterId))
        {
            return null;
        }

        series.AddAlias(characterId, request.Alias);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(series, cancellationToken);
    }

    public IReadOnlyList<SeriesVoiceOptionResponse> ListVoiceOptions() =>
        _voiceCatalog
            .OrderBy(voice => voice.Locale, StringComparer.Ordinal)
            .ThenBy(voice => voice.DisplayName, StringComparer.Ordinal)
            .Select(voice => new SeriesVoiceOptionResponse(
                voice.Provider,
                voice.Voice,
                voice.DisplayName,
                voice.Locale))
            .ToArray();

    private SeriesVoiceCatalogEntry ResolveVoice(string provider, string voice)
    {
        var resolved = _voiceCatalog.SingleOrDefault(candidate =>
            string.Equals(candidate.Provider, provider?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Voice, voice?.Trim(), StringComparison.Ordinal));
        return resolved
            ?? throw new ArgumentException("指定的語音不在伺服器允許清單內。", nameof(voice));
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("系列資料與既有資料衝突，請重新整理後再試。", exception);
        }
    }

    private async Task<StorySeriesDetailsResponse> ToDetailsAsync(
        StorySeries series,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var bookIds = series.Books.Select(book => book.BookId).ToArray();
        var bookTitles = await dbContext.Books
            .AsNoTracking()
            .Where(book => book.OwnerId == ownerId && bookIds.Contains(book.Id))
            .Select(book => new { book.Id, book.Title })
            .ToDictionaryAsync(book => book.Id, book => book.Title, cancellationToken);
        var aliasesByCharacter = series.IdentityKeys
            .Where(key => key.Kind == SeriesCharacterIdentityKeyKind.Alias)
            .GroupBy(key => key.CharacterId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StorySeriesAliasResponse>)group
                    .OrderBy(key => key.Value, StringComparer.Ordinal)
                    .Select(key => new StorySeriesAliasResponse(key.Id, key.Value))
                    .ToArray());

        return new StorySeriesDetailsResponse(
            series.Id,
            series.Name,
            series.NarratorProvider,
            series.NarratorVoice,
            series.NarratorRate,
            series.NarratorPitch,
            series.NarratorVolume,
            series.DefaultSpeakerPauseMs,
            series.ActiveCastRevisionId,
            series.Books
                .OrderBy(book => book.SortOrder)
                .Select(book => new StorySeriesBookResponse(
                    book.Id,
                    book.BookId,
                    bookTitles.GetValueOrDefault(book.BookId, "未知書籍"),
                    book.VolumeLabel,
                    book.SortOrder,
                    book.MembershipRevision,
                    book.ActiveNarrationJobId))
                .ToArray(),
            series.Characters
                .OrderBy(character => character.CanonicalName, StringComparer.Ordinal)
                .Select(character => new StorySeriesCharacterResponse(
                    character.Id,
                    character.CanonicalName,
                    character.Role.ToString(),
                    character.VoiceProvider,
                    character.Voice,
                    character.Rate,
                    character.Pitch,
                    character.Volume,
                    character.Notes,
                    character.CharacterProfileId,
                    aliasesByCharacter.GetValueOrDefault(
                        character.Id,
                        Array.Empty<StorySeriesAliasResponse>()),
                    character.CreatedAt,
                    character.UpdatedAt))
                .ToArray(),
            series.CreatedAt,
            series.UpdatedAt);
    }

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("系列管理需要已驗證的使用者。");
        }

        return currentUser.UserId;
    }

    private async Task EnsureCharacterProfileOwnedAsync(
        Guid ownerId,
        Guid? characterProfileId,
        CancellationToken cancellationToken)
    {
        if (characterProfileId is null)
        {
            return;
        }

        var exists = await dbContext.CharacterProfiles.AnyAsync(
            profile => profile.OwnerId == ownerId && profile.Id == characterProfileId,
            cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("找不到指定的角色庫角色。");
        }
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
