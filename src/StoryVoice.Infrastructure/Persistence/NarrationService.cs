using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Books;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class NarrationService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    IOptions<NarrationOptions> options) : INarrationService
{
    public async Task<IReadOnlyList<NarrationJobResponse>?> ListAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var bookExists = await OwnedBooks().AnyAsync(book => book.Id == bookId, cancellationToken);
        if (!bookExists)
        {
            return null;
        }

        var jobs = await dbContext.NarrationJobs
            .AsNoTracking()
            .Where(job => job.OwnerId == currentUser.UserId && job.BookId == bookId)
            .OrderByDescending(job => job.CreatedAt)
            .ToListAsync(cancellationToken);
        return jobs.Select(ToResponse).ToArray();
    }

    public async Task<NarrationJobResponse?> CreateAsync(
        Guid bookId,
        CreateNarrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.RightsAttested)
        {
            throw new NarrationRightsRequiredException();
        }

        var target = await OwnedBooks()
            .Include(book => book.Chapters)
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var content = target.ContentBookId is Guid contentBookId
            ? await OwnedBooks().Include(book => book.Chapters)
                .SingleOrDefaultAsync(book => book.Id == contentBookId, cancellationToken)
            : target;
        if (!AuthorizedTextPolicy.IsProcessable(content))
        {
            throw new NarrationTextUnavailableException();
        }

        var source = NarrationSource.Create(content!.Chapters.Select(chapter =>
            new NarrationChapterSource(chapter.Id, chapter.SortOrder, chapter.Title, chapter.OriginalText)));
        var configuration = options.Value;
        var existing = await dbContext.NarrationJobs.AsNoTracking().SingleOrDefaultAsync(
            job => job.OwnerId == currentUser.UserId
                && job.Mode == NarrationMode.SingleVoice
                && job.BookId == target.Id
                && job.ContentBookId == content.Id
                && job.SourceHash == source.SourceHash
                && job.Voice == configuration.Voice
                && job.Rate == configuration.Rate,
            cancellationToken);
        if (existing is not null)
        {
            return await RequeueIfTerminalAsync(existing.Id, cancellationToken);
        }

        var job = NarrationJob.Create(
            currentUser.UserId,
            target.Id,
            content.Id,
            source.SourceHash,
            configuration.Voice,
            configuration.Rate,
            DateTimeOffset.UtcNow);
        dbContext.NarrationJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToResponse(job);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            existing = await dbContext.NarrationJobs.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.OwnerId == currentUser.UserId
                    && candidate.Mode == NarrationMode.SingleVoice
                    && candidate.BookId == target.Id
                    && candidate.ContentBookId == content.Id
                    && candidate.SourceHash == source.SourceHash
                    && candidate.Voice == configuration.Voice
                    && candidate.Rate == configuration.Rate,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return await RequeueIfTerminalAsync(existing.Id, cancellationToken);
        }
    }

    public async Task<NarrationJobResponse?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await OwnedJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        return job is null ? null : ToResponse(job);
    }

    public async Task<NarrationJobResponse?> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            var job = await dbContext.NarrationJobs.SingleOrDefaultAsync(
                item => item.Id == jobId && item.OwnerId == currentUser.UserId,
                cancellationToken);
            if (job is null)
            {
                return null;
            }

            if (job.Status is NarrationJobStatus.Completed or NarrationJobStatus.Failed or NarrationJobStatus.Cancelled)
            {
                return ToResponse(job);
            }

            job.RequestCancellation();
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return ToResponse(job);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt == 4)
                {
                    break;
                }
            }
        }

        dbContext.ChangeTracker.Clear();
        var latest = await OwnedJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        return latest is null ? null : ToResponse(latest);
    }

    public async Task<NarrationAudioDescriptor?> GetAudioAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await OwnedJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job?.Status != NarrationJobStatus.Completed || string.IsNullOrWhiteSpace(job.AudioRelativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(options.Value.AudioRootPath);
        var path = Path.GetFullPath(Path.Combine(root, job.AudioRelativePath));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.Ordinal) || !File.Exists(path))
        {
            return null;
        }

        return new NarrationAudioDescriptor(path, "audio/mpeg");
    }

    private async Task<NarrationJobResponse?> RequeueIfTerminalAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            var job = await dbContext.NarrationJobs.SingleOrDefaultAsync(
                item => item.Id == jobId && item.OwnerId == currentUser.UserId,
                cancellationToken);
            if (job is null)
            {
                return null;
            }

            if (job.Status is not (NarrationJobStatus.Failed or NarrationJobStatus.Cancelled))
            {
                return ToResponse(job);
            }

            job.Requeue(DateTimeOffset.UtcNow);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return ToResponse(job);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt == 4)
                {
                    break;
                }
            }
        }

        dbContext.ChangeTracker.Clear();
        var latest = await OwnedJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        return latest is null ? null : ToResponse(latest);
    }

    private IQueryable<Book> OwnedBooks() =>
        dbContext.Books.AsNoTracking().Where(book => book.OwnerId == currentUser.UserId);

    private IQueryable<NarrationJob> OwnedJobs() =>
        dbContext.NarrationJobs.AsNoTracking().Where(job => job.OwnerId == currentUser.UserId);

    private static NarrationJobResponse ToResponse(NarrationJob job) =>
        new(
            job.Id,
            job.BookId,
            job.ContentBookId,
            job.SourceHash,
            job.Voice,
            job.Rate,
            job.Status.ToString(),
            job.ProgressPercent,
            job.Attempts,
            job.CancellationRequested,
            job.ErrorCode,
            job.AudioBytes,
            job.RightsAttestedAt,
            job.CreatedAt,
            job.UpdatedAt,
            job.CompletedAt);
}
