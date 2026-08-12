using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Application.Books;
using StoryVoice.Application.Narrations;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Application.Series;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class MultiCharacterNarrationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Single_voice_creation_is_retired_while_series_admission_remains_gated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var disabledFactory = new ApiFactory(narrationAdmissionEnabled: false);
        using var client = await disabledFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(client, "synthetic gate test text", cancellationToken);

        using var legacyResponse = await client.PostWithCsrfAsync(
            $"/api/books/{book.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        await AssertSingleVoiceRetiredAsync(legacyResponse, cancellationToken);

        var series = await CreateSeriesAsync(client, cancellationToken);
        await AddBookAsync(client, series.Id, book.Id, cancellationToken);
        await AddCharacterAsync(client, series.Id, cancellationToken);
        await ConfirmOnlyChapterPlanAsync(client, series.Id, book, cancellationToken);
        using var seriesResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        await AssertAdmissionDisabledAsync(seriesResponse, cancellationToken);
    }

    [Fact]
    public async Task Owner_can_stage_a_complete_series_rebuild_without_exposing_book_text()
    {
        const string privateTextSentinel = "PRIVATE_TEXT_MUST_NOT_APPEAR_IN_REBUILD_RESPONSE";
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var stranger = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var book = await ImportTextAsync(owner, privateTextSentinel, cancellationToken);
        var series = await CreateSeriesAsync(owner, cancellationToken);
        await AddBookAsync(owner, series.Id, book.Id, cancellationToken);
        await AddCharacterAsync(owner, series.Id, cancellationToken);
        await ConfirmOnlyChapterPlanAsync(owner, series.Id, book, cancellationToken);

        using var stageResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var responseBody = await stageResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(
            stageResponse.StatusCode == HttpStatusCode.Created,
            $"Unexpected response: {responseBody}");
        Assert.DoesNotContain(privateTextSentinel, responseBody, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var batchId = root.GetProperty("id").GetGuid();
        Assert.Equal("Building", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("members").GetArrayLength());
        Assert.NotEqual(Guid.Empty, root.GetProperty("draftCastRevisionId").GetGuid());
        Assert.NotEqual(Guid.Empty, root.GetProperty("members")[0].GetProperty("stagedNarrationJobId").GetGuid());

        using var forbiddenResponse = await stranger.GetAsync(
            $"/api/series/{series.Id}/narration-rebuilds/{batchId}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var batch = await db.SeriesCastRebuildBatches
            .Include(item => item.Members)
            .SingleAsync(item => item.Id == batchId, cancellationToken);
        var member = Assert.Single(batch.Members);
        var stagedJob = await db.NarrationJobs.SingleAsync(
            job => job.Id == member.StagedNarrationJobId,
            cancellationToken);
        var planLinks = await db.NarrationJobSpeechPlans
            .Where(link => link.NarrationJobId == stagedJob.Id)
            .ToArrayAsync(cancellationToken);

        Assert.Equal(SeriesCastRebuildBatchStatus.Building, batch.Status);
        Assert.Equal(NarrationMode.MultiCharacter, stagedJob.Mode);
        Assert.Equal(NarrationArtifactVisibility.Staged, stagedJob.Visibility);
        Assert.Equal(NarrationJobStatus.Queued, stagedJob.Status);
        Assert.Single(planLinks);

        var completionTime = DateTimeOffset.UtcNow;
        stagedJob.Claim("rebuild-progress-test", completionTime.AddMinutes(5), completionTime);
        stagedJob.Complete("synthetic/rebuild.mp3", 42);
        await db.SaveChangesAsync(cancellationToken);

        var progress = scope.ServiceProvider.GetRequiredService<IStagedNarrationBatchProgressService>();
        await progress.SynchronizeAsync(stagedJob.Id, cancellationToken);

        Assert.Equal(SeriesCastRebuildBatchStatus.ReadyToActivate, batch.Status);
        Assert.Equal(SeriesCastRebuildMemberStatus.Ready, member.Status);
    }

    [Fact]
    public async Task A_3wa_series_can_stage_a_rebuild_mixing_an_edge_character_with_the_narrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(owner, "mixed provider synthetic text", cancellationToken);

        using var seriesResponse = await owner.PostWithCsrfAsync(
            "/api/series",
            new
            {
                name = $"3wa 混用測試系列 {Guid.NewGuid():N}",
                narratorProvider = "3wa-voxcpm2",
                narratorVoice = "custom",
                narratorRate = "-5%",
                narratorPitch = "+0Hz",
                narratorVolume = "+0%",
                defaultSpeakerPauseMs = 350
            },
            cancellationToken);
        var series = await seriesResponse.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, seriesResponse.StatusCode);
        Assert.NotNull(series);

        await AddBookAsync(owner, series.Id, book.Id, cancellationToken);

        using var characterResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters",
            new
            {
                canonicalName = "測試角色",
                role = "Main",
                voiceProvider = "edge",
                voice = "zh-TW-HsiaoChenNeural",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, characterResponse.StatusCode);

        await ConfirmOnlyChapterPlanAsync(owner, series.Id, book, cancellationToken);

        using var stageResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var responseBody = await stageResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(
            stageResponse.StatusCode == HttpStatusCode.Created,
            $"Unexpected response: {responseBody}");
    }

    [Fact]
    public async Task Terminal_replay_repairs_a_batch_whose_members_completed_before_its_ready_transition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var firstBook = await ImportTextAsync(owner, "first synthetic rebuild text", cancellationToken);
        var secondBook = await ImportTextAsync(owner, "second synthetic rebuild text", cancellationToken);
        var series = await CreateSeriesAsync(owner, cancellationToken);
        await AddBookAsync(owner, series.Id, firstBook.Id, cancellationToken);
        await AddBookAsync(owner, series.Id, secondBook.Id, cancellationToken, sortOrder: 2);
        await AddCharacterAsync(owner, series.Id, cancellationToken);
        await ConfirmOnlyChapterPlanAsync(owner, series.Id, firstBook, cancellationToken);
        await ConfirmOnlyChapterPlanAsync(owner, series.Id, secondBook, cancellationToken);

        using var stageResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var staged = await stageResponse.Content.ReadFromJsonAsync<SeriesNarrationRebuildResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, stageResponse.StatusCode);
        var batchId = Assert.IsType<SeriesNarrationRebuildResponse>(staged).Id;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var batch = await db.SeriesCastRebuildBatches
            .Include(candidate => candidate.Members)
            .SingleAsync(candidate => candidate.Id == batchId, cancellationToken);
        var stagedJobs = await db.NarrationJobs
            .Where(job => job.RebuildBatchId == batchId)
            .ToArrayAsync(cancellationToken);
        Assert.Equal(2, stagedJobs.Length);
        foreach (var job in stagedJobs)
        {
            var completedAt = DateTimeOffset.UtcNow;
            job.Claim($"terminal-replay-{job.Id:N}", completedAt.AddMinutes(5), completedAt);
            job.Complete($"synthetic/{job.Id:N}.mp3", 42);
        }

        // Model the only dangerous interleaving: two workers persisted their distinct member
        // completions, but neither observed the other completion when evaluating the batch.
        foreach (var member in batch.Members)
        {
            db.Entry(member).Property("Status").CurrentValue = SeriesCastRebuildMemberStatus.Ready;
        }
        await db.SaveChangesAsync(cancellationToken);

        var progress = scope.ServiceProvider.GetRequiredService<IStagedNarrationBatchProgressService>();
        await progress.SynchronizeAsync(stagedJobs[0].Id, cancellationToken);

        db.ChangeTracker.Clear();
        var repaired = await db.SeriesCastRebuildBatches
            .SingleAsync(candidate => candidate.Id == batchId, cancellationToken);
        Assert.Equal(SeriesCastRebuildBatchStatus.ReadyToActivate, repaired.Status);
    }

    [Fact]
    public async Task Retrying_after_a_failed_batch_purges_the_stale_batch_and_succeeds_with_an_unchanged_cast()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(owner, "synthetic retry rebuild text", cancellationToken);
        var series = await CreateSeriesAsync(owner, cancellationToken);
        await AddBookAsync(owner, series.Id, book.Id, cancellationToken);
        await AddCharacterAsync(owner, series.Id, cancellationToken);
        await ConfirmOnlyChapterPlanAsync(owner, series.Id, book, cancellationToken);

        using var firstResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstBatch = await firstResponse.Content.ReadFromJsonAsync<SeriesNarrationRebuildResponse>(cancellationToken);
        Assert.NotNull(firstBatch);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var stagedJob = await db.NarrationJobs.SingleAsync(
                job => job.RebuildBatchId == firstBatch!.Id,
                cancellationToken);
            var failedAt = DateTimeOffset.UtcNow;
            stagedJob.Claim("fingerprint-retry-test", failedAt.AddMinutes(5), failedAt);
            stagedJob.FailPermanently("synthetic_test_failure");
            await db.SaveChangesAsync(cancellationToken);

            var progress = scope.ServiceProvider.GetRequiredService<IStagedNarrationBatchProgressService>();
            await progress.SynchronizeAsync(stagedJob.Id, cancellationToken);

            var failedBatch = await db.SeriesCastRebuildBatches.SingleAsync(
                candidate => candidate.Id == firstBatch!.Id,
                cancellationToken);
            Assert.Equal(SeriesCastRebuildBatchStatus.Failed, failedBatch.Status);
        }

        // The cast (narrator + character voices) never changed between attempts, so retrying
        // must reuse the still-Draft cast revision from the failed attempt rather than trying to
        // insert a second row with the same (OwnerId, SeriesId, Fingerprint).
        using var retryResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(retryResponse.StatusCode == HttpStatusCode.Created, $"Unexpected response: {retryBody}");
        var retryBatch = JsonSerializer.Deserialize<SeriesNarrationRebuildResponse>(
            retryBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(retryBatch);
        Assert.NotEqual(firstBatch!.Id, retryBatch!.Id);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var revisionCount = await verifyDb.NarrationCastRevisions
            .Where(revision => revision.SeriesId == series.Id)
            .CountAsync(cancellationToken);
        Assert.Equal(1, revisionCount);
    }

    private static async Task AssertSingleVoiceRetiredAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        Assert.Equal(
            "single_voice_narration_retired",
            document.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertAdmissionDisabledAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        Assert.Equal(
            NarrationAdmissionDisabledException.StableCode,
            document.RootElement.GetProperty("code").GetString());
    }

    private static async Task ConfirmOnlyChapterPlanAsync(
        HttpClient client,
        Guid seriesId,
        BookDetailsResponse book,
        CancellationToken cancellationToken)
    {
        var chapter = Assert.Single(book.Chapters);
        using var buildResponse = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/books/{book.Id}/chapters/{chapter.Id}/speech-plan",
            new { },
            cancellationToken);
        var draft = await buildResponse.Content.ReadFromJsonAsync<ChapterSpeechPlanDraftResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, buildResponse.StatusCode);
        Assert.NotNull(draft);

        using var confirmResponse = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/speech-plan-drafts/{draft.Id}/confirm",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
    }

    private static async Task AddBookAsync(
        HttpClient client,
        Guid seriesId,
        Guid bookId,
        CancellationToken cancellationToken,
        int sortOrder = 1)
    {
        using var response = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/books",
            new { bookId, volumeLabel = "第一冊", sortOrder },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AddCharacterAsync(
        HttpClient client,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters",
            new
            {
                canonicalName = "測試角色",
                role = "Main",
                voiceProvider = "edge",
                voice = "zh-TW-HsiaoChenNeural",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<StorySeriesDetailsResponse> CreateSeriesAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            "/api/series",
            new
            {
                name = $"多聲線測試系列 {Guid.NewGuid():N}",
                narratorProvider = "edge",
                narratorVoice = "zh-TW-YunJheNeural",
                narratorRate = "-5%",
                narratorPitch = "+0Hz",
                narratorVolume = "+0%",
                defaultSpeakerPauseMs = 350
            },
            cancellationToken);
        var series = await response.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<StorySeriesDetailsResponse>(series);
    }

    private static async Task<BookDetailsResponse> ImportTextAsync(
        HttpClient client,
        string text,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"第一章\n{text}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", $"multi-character-{Guid.NewGuid():N}.txt");
        using var response = await client.PostMultipartWithCsrfAsync("/api/books/import", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Import response did not contain a book.");
    }
}
