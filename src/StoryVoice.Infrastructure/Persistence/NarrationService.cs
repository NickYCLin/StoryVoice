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
            .Where(job => job.OwnerId == currentUser.UserId
                && job.BookId == bookId
                && job.Visibility == NarrationArtifactVisibility.Published)
            .OrderByDescending(job => job.CreatedAt)
            .ToListAsync(cancellationToken);
        return jobs.Select(ToResponse).ToArray();
    }

    public Task<NarrationJobResponse?> CreateAsync(
        Guid bookId,
        CreateNarrationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<NarrationJobResponse?>(new SingleVoiceNarrationRetiredException());

    public async Task<NarrationJobResponse?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await OwnedRegularJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        return job is null ? null : ToResponse(job);
    }

    public async Task<NarrationJobResponse?> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            var job = await dbContext.NarrationJobs.SingleOrDefaultAsync(
                item => item.Id == jobId
                    && item.OwnerId == currentUser.UserId
                    && item.Visibility == NarrationArtifactVisibility.Published,
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
        var latest = await OwnedRegularJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        return latest is null ? null : ToResponse(latest);
    }

    public async Task<NarrationAudioDescriptor?> GetAudioAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await OwnedRegularJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null || !job.IsAvailableForRegularPlayback)
        {
            return null;
        }

        var root = Path.GetFullPath(options.Value.AudioRootPath);
        var path = Path.GetFullPath(Path.Combine(root, job.AudioRelativePath!));
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
                item => item.Id == jobId
                    && item.OwnerId == currentUser.UserId
                    && item.Visibility == NarrationArtifactVisibility.Published,
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
        var latest = await OwnedRegularJobs().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        return latest is null ? null : ToResponse(latest);
    }

    private IQueryable<Book> OwnedBooks() =>
        dbContext.Books.AsNoTracking().Where(book => book.OwnerId == currentUser.UserId);

    private IQueryable<NarrationJob> OwnedRegularJobs() =>
        dbContext.NarrationJobs.AsNoTracking().Where(job =>
            job.OwnerId == currentUser.UserId
            && job.Visibility == NarrationArtifactVisibility.Published);

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
