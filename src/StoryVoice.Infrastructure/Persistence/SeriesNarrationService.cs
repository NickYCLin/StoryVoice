using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Books;
using StoryVoice.Application.Narrations;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

/// <summary>
/// Creates immutable, staged series-wide multi-character narration cohorts. It deliberately never
/// changes active playback pointers; only PostgreSqlCastEpochActivationPublisher can publish a
/// completed cohort.
/// </summary>
internal sealed class SeriesNarrationService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    ChineseSpeechSegmenter segmenter,
    IOptions<NarrationAdmissionOptions> admissionOptions,
    IOptions<MultiCharacterNarrationOptions> compositionOptions,
    PostgreSqlCastEpochActivationPublisher activationPublisher) : ISeriesNarrationService
{
    public async Task<SeriesNarrationRebuildResponse?> CreateRebuildAsync(
        Guid seriesId,
        CreateSeriesNarrationRebuildRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!admissionOptions.Value.AdmissionEnabled)
        {
            throw new NarrationAdmissionDisabledException();
        }

        if (!request.RightsAttested)
        {
            throw new NarrationRightsRequiredException();
        }

        EnsureId(seriesId, nameof(seriesId));
        var ownerId = EnsureCurrentOwnerId();
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var series = await dbContext.StorySeries
                .AsSplitQuery()
                .Include(candidate => candidate.Books)
                .Include(candidate => candidate.Characters)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == seriesId && candidate.OwnerId == ownerId,
                    cancellationToken);
            if (series is null)
            {
                return null;
            }

            var memberships = series.Books
                .OrderBy(member => member.SortOrder)
                .ThenBy(member => member.Id)
                .ToArray();
            if (memberships.Length == 0)
            {
                throw new InvalidOperationException("系列至少要先加入一本含合法正文的書，才能建立多聲線朗讀批次。");
            }

            if (series.Characters.Count == 0)
            {
                throw new InvalidOperationException("系列至少要先設定一位角色與固定聲線，才能建立多聲線朗讀批次。");
            }

            if (await dbContext.SeriesCastRebuildBatches.AnyAsync(
                    batch => batch.OwnerId == ownerId
                        && batch.SeriesId == seriesId
                        && (batch.Status == SeriesCastRebuildBatchStatus.Draft
                            || batch.Status == SeriesCastRebuildBatchStatus.Building
                            || batch.Status == SeriesCastRebuildBatchStatus.ReadyToActivate),
                    cancellationToken))
            {
                throw new InvalidOperationException("這個系列已有尚未完成啟用的多聲線重建批次。");
            }

            EnsureSingleSynthesisProvider(series);
            var sourceBooks = await LoadSeriesBooksAsync(ownerId, memberships, cancellationToken);
            var confirmedPlans = await LoadLatestConfirmedPlansAsync(
                ownerId,
                seriesId,
                memberships.Select(member => member.BookId).ToArray(),
                cancellationToken);
            var sourceAndPlans = BuildSourceAndPlanSnapshots(
                memberships,
                sourceBooks,
                confirmedPlans,
                cancellationToken);

            var configuration = compositionOptions.Value;
            ValidateComposition(configuration);
            var now = DateTimeOffset.UtcNow;
            var castRevisionId = Guid.NewGuid();
            var nextCastRevisionNumber = await dbContext.NarrationCastRevisions
                .Where(revision => revision.OwnerId == ownerId && revision.SeriesId == seriesId)
                .Select(revision => revision.RevisionNumber)
                .OrderByDescending(revisionNumber => revisionNumber)
                .FirstOrDefaultAsync(cancellationToken) + 1;
            var assignments = series.Characters
                .OrderBy(character => character.Id)
                .Select(character => NarrationCastAssignment.Create(
                    Guid.NewGuid(),
                    ownerId,
                    seriesId,
                    castRevisionId,
                    character.Id,
                    character.CanonicalName,
                    character.VoiceProvider,
                    configuration.ProviderVersion,
                    character.Voice,
                    character.Rate,
                    character.Pitch,
                    character.Volume))
                .ToArray();
            var castRevision = NarrationCastRevision.Create(
                castRevisionId,
                ownerId,
                seriesId,
                nextCastRevisionNumber,
                series.NarratorProvider,
                configuration.ProviderVersion,
                series.NarratorVoice,
                series.NarratorRate,
                series.NarratorPitch,
                series.NarratorVolume,
                series.DefaultSpeakerPauseMs,
                configuration.ChapterPauseMs,
                configuration.CompositionVersion,
                configuration.FfmpegProfile,
                now,
                assignments);

            var batchId = Guid.NewGuid();
            var batch = SeriesCastRebuildBatch.Create(
                batchId,
                ownerId,
                seriesId,
                series.ActiveCastRevisionId,
                castRevisionId,
                memberships.Max(member => member.MembershipRevision),
                now,
                memberships.Select(member => SeriesCastRebuildMember.Create(
                    Guid.NewGuid(),
                    ownerId,
                    seriesId,
                    batchId,
                    member.Id,
                    member.BookId,
                    member.MembershipRevision,
                    member.ActiveNarrationJobId)));
            batch.StartBuilding(now);

            var stagedJobs = new List<NarrationJob>(memberships.Length);
            var planLinks = new List<NarrationJobSpeechPlan>();
            foreach (var membership in memberships)
            {
                var snapshot = sourceAndPlans[membership.BookId];
                var rebuildMember = batch.Members.Single(member => member.SeriesBookId == membership.Id);
                var primaryPlanId = snapshot.Plans
                    .OrderBy(plan => plan.ChapterSortOrder)
                    .ThenBy(plan => plan.Revision.Id)
                    .First()
                    .Revision.Id;
                var stagedJob = NarrationJob.CreateMultiCharacterStaged(
                    ownerId,
                    membership.BookId,
                    membership.BookId,
                    seriesId,
                    castRevisionId,
                    primaryPlanId,
                    batchId,
                    rebuildMember.Id,
                    snapshot.Source.SourceHash,
                    now);
                batch.AttachStagedJob(membership.Id, stagedJob.Id);
                stagedJobs.Add(stagedJob);
                planLinks.AddRange(snapshot.Plans.Select(plan => NarrationJobSpeechPlan.Create(
                    ownerId,
                    seriesId,
                    stagedJob.Id,
                    plan.ChapterSortOrder,
                    plan.Revision.Id)));
            }

            dbContext.NarrationCastRevisions.Add(castRevision);
            dbContext.SeriesCastRebuildBatches.Add(batch);
            dbContext.NarrationJobs.AddRange(stagedJobs);
            dbContext.NarrationJobSpeechPlans.AddRange(planLinks);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ToResponse(batch);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    public async Task<SeriesNarrationRebuildResponse?> GetRebuildAsync(
        Guid seriesId,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(batchId, nameof(batchId));
        var ownerId = EnsureCurrentOwnerId();
        var batch = await dbContext.SeriesCastRebuildBatches
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Members)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == batchId
                    && candidate.SeriesId == seriesId
                    && candidate.OwnerId == ownerId,
                cancellationToken);
        return batch is null ? null : ToResponse(batch);
    }

    public async Task<SeriesNarrationRebuildResponse?> ActivateRebuildAsync(
        Guid seriesId,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(batchId, nameof(batchId));
        var ownerId = EnsureCurrentOwnerId();
        var exists = await dbContext.SeriesCastRebuildBatches
            .AsNoTracking()
            .AnyAsync(
                batch => batch.Id == batchId
                    && batch.SeriesId == seriesId
                    && batch.OwnerId == ownerId,
                cancellationToken);
        if (!exists)
        {
            return null;
        }

        await activationPublisher.ActivateAsync(
            new CastEpochActivationCommand(ownerId, seriesId, batchId, DateTimeOffset.UtcNow),
            cancellationToken);
        return await GetRebuildAsync(seriesId, batchId, cancellationToken);
    }

    private async Task<Dictionary<Guid, Book>> LoadSeriesBooksAsync(
        Guid ownerId,
        IReadOnlyCollection<SeriesBook> memberships,
        CancellationToken cancellationToken)
    {
        var bookIds = memberships.Select(member => member.BookId).ToArray();
        var books = await dbContext.Books
            .AsNoTracking()
            .AsSplitQuery()
            .Include(book => book.Chapters)
            .Where(book => book.OwnerId == ownerId && bookIds.Contains(book.Id))
            .ToListAsync(cancellationToken);
        if (books.Count != bookIds.Length)
        {
            throw new InvalidOperationException("系列冊次包含不屬於目前擁有者的正文書籍。");
        }

        if (books.Any(book => !AuthorizedTextPolicy.IsProcessable(book)))
        {
            throw new NarrationTextUnavailableException();
        }

        return books.ToDictionary(book => book.Id);
    }

    private async Task<IReadOnlyDictionary<(Guid BookId, Guid ChapterId), ConfirmedSpeechPlanRevision>> LoadLatestConfirmedPlansAsync(
        Guid ownerId,
        Guid seriesId,
        IReadOnlyCollection<Guid> bookIds,
        CancellationToken cancellationToken)
    {
        var revisions = await dbContext.ConfirmedSpeechPlanRevisions
            .AsNoTracking()
            .Where(revision => revision.OwnerId == ownerId
                && revision.SeriesId == seriesId
                && bookIds.Contains(revision.BookId))
            .OrderByDescending(revision => revision.RevisionNumber)
            .ToListAsync(cancellationToken);
        return revisions
            .GroupBy(revision => (revision.BookId, revision.ChapterId))
            .ToDictionary(group => group.Key, group => group.First());
    }

    private Dictionary<Guid, SourceAndPlanSnapshot> BuildSourceAndPlanSnapshots(
        IReadOnlyCollection<SeriesBook> memberships,
        IReadOnlyDictionary<Guid, Book> sourceBooks,
        IReadOnlyDictionary<(Guid BookId, Guid ChapterId), ConfirmedSpeechPlanRevision> confirmedPlans,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<Guid, SourceAndPlanSnapshot>();
        foreach (var membership in memberships)
        {
            var book = sourceBooks[membership.BookId];
            var chapterPlans = new List<ChapterPlanSnapshot>();
            foreach (var chapter in book.Chapters.OrderBy(chapter => chapter.SortOrder).ThenBy(chapter => chapter.Id))
            {
                if (!confirmedPlans.TryGetValue((book.Id, chapter.Id), out var revision))
                {
                    throw new InvalidOperationException("系列所有冊次的每個章節都必須先確認 speech plan，才能建立多聲線批次。");
                }

                var currentPlan = segmenter.Segment(chapter.Title, chapter.OriginalText);
                if (!string.Equals(currentPlan.SourceHash, revision.SourceHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("至少一份確認過的 speech plan 已經過期，請重新產生並確認後再建立多聲線批次。");
                }

                chapterPlans.Add(new ChapterPlanSnapshot(chapter.SortOrder, revision));
            }

            result.Add(
                book.Id,
                new SourceAndPlanSnapshot(
                    NarrationSource.Create(book.Chapters.Select(chapter => new NarrationChapterSource(
                        chapter.Id,
                        chapter.SortOrder,
                        chapter.Title,
                        chapter.OriginalText))),
                    chapterPlans));
        }

        return result;
    }

    private static void EnsureSingleSynthesisProvider(StorySeries series)
    {
        if (series.Characters.Any(character => !string.Equals(
                character.VoiceProvider,
                series.NarratorProvider,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("目前多聲線合成必須使用與旁白相同的語音 provider。");
        }
    }

    private static void ValidateComposition(MultiCharacterNarrationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProviderVersion)
            || string.IsNullOrWhiteSpace(options.CompositionVersion)
            || string.IsNullOrWhiteSpace(options.FfmpegProfile)
            || options.ChapterPauseMs < 0)
        {
            throw new InvalidOperationException("多聲線合成設定不完整。");
        }
    }

    private static SeriesNarrationRebuildResponse ToResponse(SeriesCastRebuildBatch batch) =>
        new(
            batch.Id,
            batch.SeriesId,
            batch.BaseActiveCastRevisionId,
            batch.DraftCastRevisionId,
            batch.CohortMembershipRevision,
            batch.Status.ToString(),
            batch.CreatedAt,
            batch.UpdatedAt,
            batch.Members
                .OrderBy(member => member.MembershipRevision)
                .ThenBy(member => member.SeriesBookId)
                .Select(member => new SeriesNarrationRebuildMemberResponse(
                    member.Id,
                    member.SeriesBookId,
                    member.BookId,
                    member.MembershipRevision,
                    member.PreviousActiveNarrationJobId,
                    member.StagedNarrationJobId,
                    member.Status.ToString()))
                .ToArray());

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("目前使用者識別碼無效。");
        }

        return currentUser.UserId;
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }

    private sealed record SourceAndPlanSnapshot(
        NarrationSourceDocument Source,
        IReadOnlyList<ChapterPlanSnapshot> Plans);

    private sealed record ChapterPlanSnapshot(
        int ChapterSortOrder,
        ConfirmedSpeechPlanRevision Revision);
}
