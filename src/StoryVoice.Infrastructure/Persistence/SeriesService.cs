using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Insights;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        if (book is null || book.Status == BookStatus.Linked || book.IsArchived)
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

    public async Task<StorySeriesDetailsResponse?> SetPointOfViewCharacterAsync(
        Guid seriesId,
        SetSeriesPointOfViewCharacterRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        ArgumentNullException.ThrowIfNull(request);
        var series = await repository.GetForMutationAsync(seriesId, cancellationToken);
        if (series is null)
        {
            return null;
        }

        series.SetPointOfViewCharacter(request.CharacterId);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(series, cancellationToken);
    }

    public async Task<StorySeriesDetailsResponse?> ApplyAnalyzedCharactersAsync(
        Guid seriesId,
        ApplyAnalyzedSeriesCharactersRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        ArgumentNullException.ThrowIfNull(request);
        EnsureId(request.BookId, nameof(request.BookId));
        if (request.Characters is null || request.Characters.Count is < 1 or > 60)
        {
            throw new ArgumentException("每次至少選擇 1 位、最多 60 位角色候選。", nameof(request));
        }

        var ownerId = EnsureCurrentOwnerId();
        var seriesExists = await dbContext.StorySeries.AsNoTracking().AnyAsync(
            candidate => candidate.OwnerId == ownerId && candidate.Id == seriesId,
            cancellationToken);
        if (!seriesExists)
        {
            return null;
        }

        var analysis = await dbContext.BookLocalLlmCharacterAnalyses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OwnerId == ownerId && item.BookId == request.BookId,
                cancellationToken);
        if (analysis is null)
        {
            throw new InvalidOperationException("請先完成這本書的本機 LLM 角色分析。");
        }

        var analyzedCandidates = JsonSerializer.Deserialize<LocalLlmCharacterAnalysisCandidateResponse[]>(
            analysis.CandidatesJson,
            JsonOptions) ?? [];
        var candidatesByName = analyzedCandidates.ToDictionary(
            candidate => NormalizeIdentityKey(candidate.Name),
            candidate => candidate,
            StringComparer.Ordinal);
        foreach (var selection in request.Characters)
        {
            ArgumentNullException.ThrowIfNull(selection);
            if (!candidatesByName.ContainsKey(NormalizeIdentityKey(selection.SourceName)))
            {
                throw new ArgumentException("角色候選已過期，請重新執行分析後再套用。", nameof(request));
            }

            if (selection.Aliases is null || selection.Aliases.Count > 24)
            {
                throw new ArgumentException("單一角色最多可套用 24 個 alias。", nameof(request));
            }
        }

        var contentBookId = analysis.ContentBookId;
        var book = await dbContext.Books.SingleOrDefaultAsync(
            candidate => candidate.Id == contentBookId && candidate.OwnerId == ownerId,
            cancellationToken);
        if (book is null || book.Status == BookStatus.Linked || book.IsArchived)
        {
            return null;
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var series = await repository.GetForMutationAsync(seriesId, cancellationToken);
            if (series is null)
            {
                return null;
            }

            if (series.Books.All(member => member.BookId != contentBookId))
            {
                var nextSortOrder = series.Books.Count == 0
                    ? 1
                    : checked(series.Books.Max(member => member.SortOrder) + 1);
                series.AddBook(book, book.Title, nextSortOrder);
            }

            foreach (var selection in request.Characters)
            {
                var voice = ResolveVoice(selection.VoiceProvider, selection.Voice);
                var canonicalKey = NormalizeIdentityKey(selection.CanonicalName);
                var existingIdentity = series.IdentityKeys.SingleOrDefault(
                    key => string.Equals(key.NormalizedValue, canonicalKey, StringComparison.Ordinal));
                var character = existingIdentity is null
                    ? series.AddCharacter(
                        selection.CanonicalName,
                        selection.Role,
                        voice.Provider,
                        voice.Voice,
                        selection.Rate,
                        selection.Pitch,
                        selection.Volume,
                        "由本機 LLM 角色候選建立")
                    : series.Characters.Single(item => item.Id == existingIdentity.CharacterId);

                var aliases = new[] { selection.SourceName }
                    .Concat(selection.Aliases)
                    .Select(alias => alias?.Trim() ?? string.Empty)
                    .Where(alias => alias.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                foreach (var alias in aliases)
                {
                    var aliasKey = NormalizeIdentityKey(alias);
                    var identity = series.IdentityKeys.SingleOrDefault(
                        key => string.Equals(key.NormalizedValue, aliasKey, StringComparison.Ordinal));
                    if (identity is not null)
                    {
                        if (identity.CharacterId != character.Id)
                        {
                            throw new InvalidOperationException($"「{alias}」已屬於系列內另一位角色，無法自動合併。");
                        }

                        continue;
                    }

                    series.AddAlias(character.Id, alias);
                }
            }

            await SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return await ToDetailsAsync(series, cancellationToken);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
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
            series.PointOfViewCharacterId,
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

    private static string NormalizeIdentityKey(string? value)
    {
        var display = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
        if (display.Length == 0)
        {
            throw new ArgumentException("角色名稱不可空白。", nameof(value));
        }

        return string.Join(' ', display.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }
}
