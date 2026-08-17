using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Books;
using StoryVoice.Application.Narrations;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Application.Series;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class BlueMagpieNarrationAdmissionApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Oversized_chunk_estimate_rejects_retry_without_purging_failed_batch_or_creating_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enabledFactory = CreateEnabledFactory();
        using var owner = await enabledFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var setup = await CreateBlueMagpieSeriesAsync(
            owner,
            new string('甲', 130),
            cancellationToken);

        using var firstResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var firstBatch = await firstResponse.Content.ReadFromJsonAsync<SeriesNarrationRebuildResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.NotNull(firstBatch);

        await using (var failureScope = enabledFactory.Services.CreateAsyncScope())
        {
            var db = failureScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var stagedJob = await db.NarrationJobs.SingleAsync(
                job => job.RebuildBatchId == firstBatch!.Id,
                cancellationToken);
            var failedAt = DateTimeOffset.UtcNow;
            stagedJob.Claim("bluemagpie-budget-test", failedAt.AddMinutes(5), failedAt);
            stagedJob.FailPermanently("synthetic_budget_setup");
            await db.SaveChangesAsync(cancellationToken);

            var progress = failureScope.ServiceProvider
                .GetRequiredService<IStagedNarrationBatchProgressService>();
            await progress.SynchronizeAsync(stagedJob.Id, cancellationToken);
        }

        var before = await ReadPersistenceCountsAsync(
            enabledFactory.Services,
            setup.Series.Id,
            cancellationToken);
        enabledFactory.Services.GetRequiredService<IOptions<BlueMagpieOptions>>()
            .Value.MaximumChunksPerJob = 1;

        using var retryResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, retryResponse.StatusCode);
        Assert.Contains("預估分段數", retryBody, StringComparison.Ordinal);
        Assert.Equal(
            before,
            await ReadPersistenceCountsAsync(
                enabledFactory.Services,
                setup.Series.Id,
                cancellationToken));

        await using var verifyScope = enabledFactory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        Assert.Equal(
            SeriesCastRebuildBatchStatus.Failed,
            (await verifyDb.SeriesCastRebuildBatches.SingleAsync(
                batch => batch.Id == firstBatch!.Id,
                cancellationToken)).Status);
    }

    [Fact]
    public async Task Oversized_conservative_pcm_estimate_rejects_before_creating_cast_batch_or_job()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enabledFactory = CreateEnabledFactory(builder =>
        {
            builder.UseSetting("BlueMagpie:MaximumChunksPerJob", "10000");
            builder.UseSetting("BlueMagpie:MaximumJobAudioBytes", "67108864");
        });
        using var owner = await enabledFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var setup = await CreateBlueMagpieSeriesAsync(
            owner,
            new string('乙', 900),
            cancellationToken);
        var before = await ReadPersistenceCountsAsync(
            enabledFactory.Services,
            setup.Series.Id,
            cancellationToken);

        using var response = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("預估 PCM 音訊量", body, StringComparison.Ordinal);
        Assert.Equal(
            before,
            await ReadPersistenceCountsAsync(
                enabledFactory.Services,
                setup.Series.Id,
                cancellationToken));
        Assert.Equal(new PersistenceCounts(0, 0, 0, 0), before);
    }

    private WebApplicationFactory<Program> CreateEnabledFactory(
        Action<IWebHostBuilder>? configure = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlueMagpie:Enabled", "true");
            builder.UseSetting("BlueMagpie:FormalNarrationEnabled", "true");
            builder.UseSetting("BlueMagpie:InternalToken", new string('t', 32));
            configure?.Invoke(builder);
        });

    private static async Task<(StorySeriesDetailsResponse Series, BookDetailsResponse Book)>
        CreateBlueMagpieSeriesAsync(
            HttpClient owner,
            string text,
            CancellationToken cancellationToken)
    {
        var book = await ImportTextAsync(owner, text, cancellationToken);
        using var createResponse = await owner.PostWithCsrfAsync(
            "/api/series",
            new
            {
                name = $"BlueMagpie budget {Guid.NewGuid():N}",
                narratorProvider = "bluemagpie",
                narratorVoice = BlueMagpieOptions.FemaleVoice,
                narratorRate = "+0%",
                narratorPitch = "+0Hz",
                narratorVolume = "+0%",
                defaultSpeakerPauseMs = 180,
            },
            cancellationToken);
        var series = await createResponse.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(series);

        using var addBookResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books",
            new { bookId = book.Id, volumeLabel = "第一冊", sortOrder = 1 },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, addBookResponse.StatusCode);

        using var addCharacterResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters",
            new
            {
                canonicalName = "測試角色",
                role = "Main",
                voiceProvider = "bluemagpie",
                voice = BlueMagpieOptions.MaleVoice,
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null,
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, addCharacterResponse.StatusCode);

        var chapter = Assert.Single(book.Chapters);
        using var buildResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapter.Id}/speech-plan",
            new { },
            cancellationToken);
        var draft = await buildResponse.Content.ReadFromJsonAsync<ChapterSpeechPlanDraftResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, buildResponse.StatusCode);
        Assert.NotNull(draft);

        using var confirmResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/speech-plan-drafts/{draft.Id}/confirm",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        return (series, book);
    }

    private static async Task<BookDetailsResponse> ImportTextAsync(
        HttpClient client,
        string text,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"第一章\n{text}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", $"bluemagpie-budget-{Guid.NewGuid():N}.txt");
        using var response = await client.PostMultipartWithCsrfAsync(
            "/api/books/import",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Import response did not contain a book.");
    }

    private static async Task<PersistenceCounts> ReadPersistenceCountsAsync(
        IServiceProvider services,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        return new PersistenceCounts(
            await db.NarrationCastRevisions.CountAsync(
                revision => revision.SeriesId == seriesId,
                cancellationToken),
            await db.SeriesCastRebuildBatches.CountAsync(
                batch => batch.SeriesId == seriesId,
                cancellationToken),
            await db.NarrationJobs.CountAsync(
                job => job.SeriesId == seriesId,
                cancellationToken),
            await db.NarrationJobSpeechPlans.CountAsync(
                link => link.SeriesId == seriesId,
                cancellationToken));
    }

    private sealed record PersistenceCounts(
        int CastRevisions,
        int Batches,
        int Jobs,
        int PlanLinks);
}
