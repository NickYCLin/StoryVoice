using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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
    public async Task Atomic_voice_switch_invalidates_pending_batch_and_cancels_job_without_staling_speech_plan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(owner, "voice switch keeps confirmed plan", cancellationToken);
        var series = await CreateSeriesAsync(owner, cancellationToken);
        await AddBookAsync(owner, series.Id, book.Id, cancellationToken);
        await AddCharacterAsync(owner, series.Id, cancellationToken);
        await ConfirmOnlyChapterPlanAsync(owner, series.Id, book, cancellationToken);

        using var stageResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var staged = await stageResponse.Content.ReadFromJsonAsync<SeriesNarrationRebuildResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, stageResponse.StatusCode);
        Assert.NotNull(staged);
        var stagedMember = Assert.Single(staged.Members);
        Assert.NotNull(stagedMember.StagedNarrationJobId);

        using var detailsResponse = await owner.GetAsync($"/api/series/{series.Id}", cancellationToken);
        var details = await detailsResponse.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(
            cancellationToken);
        var character = Assert.Single(Assert.IsType<StorySeriesDetailsResponse>(details).Characters);

        int confirmedPlanCount;
        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            var beforeDb = beforeScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            confirmedPlanCount = await beforeDb.ConfirmedSpeechPlanRevisions
                .CountAsync(revision => revision.SeriesId == series.Id, cancellationToken);
            Assert.True(confirmedPlanCount > 0);
        }

        using var switchResponse = await owner.PutWithCsrfAsync(
            $"/api/series/{series.Id}/voices",
            new
            {
                narratorProvider = "edge",
                narratorVoice = "zh-TW-HsiaoChenNeural",
                characters = new[]
                {
                    new
                    {
                        characterId = character.Id,
                        voiceProvider = "edge",
                        voice = "zh-TW-HsiaoYuNeural"
                    }
                }
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var invalidatedBatch = await verifyDb.SeriesCastRebuildBatches
            .SingleAsync(batch => batch.Id == staged.Id, cancellationToken);
        var cancelledJob = await verifyDb.NarrationJobs
            .SingleAsync(job => job.Id == stagedMember.StagedNarrationJobId, cancellationToken);
        var speechDraft = await verifyDb.ChapterSpeechPlanDrafts
            .SingleAsync(draft => draft.SeriesId == series.Id, cancellationToken);

        Assert.Equal(SeriesCastRebuildBatchStatus.Failed, invalidatedBatch.Status);
        Assert.Equal(NarrationJobStatus.Cancelled, cancelledJob.Status);
        Assert.True(cancelledJob.CancellationRequested);
        Assert.NotEqual(ChapterSpeechPlanDraftStatus.Stale, speechDraft.Status);
        Assert.Equal(
            confirmedPlanCount,
            await verifyDb.ConfirmedSpeechPlanRevisions.CountAsync(
                revision => revision.SeriesId == series.Id,
                cancellationToken));
        Assert.Null((await verifyDb.StorySeries.SingleAsync(
            candidate => candidate.Id == series.Id,
            cancellationToken)).ActiveCastRevisionId);
    }

    [Fact]
    public async Task Adding_a_new_book_invalidates_pending_batch_but_rejected_duplicate_does_not()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var setup = await CreateBuildingBatchAsync(owner, "book membership invalidation", cancellationToken);

        using var duplicateResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/books",
            new { bookId = setup.Book.Id, volumeLabel = "重複冊次", sortOrder = 2 },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        await AssertBatchAndJobStateAsync(
            setup.Batch,
            SeriesCastRebuildBatchStatus.Building,
            NarrationJobStatus.Queued,
            cancellationRequested: false,
            cancellationToken);

        var secondBook = await ImportTextAsync(owner, "new series membership", cancellationToken);
        using var addResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/books",
            new { bookId = secondBook.Id, volumeLabel = "第二冊", sortOrder = 2 },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        await AssertBatchAndJobStateAsync(
            setup.Batch,
            SeriesCastRebuildBatchStatus.Failed,
            NarrationJobStatus.Cancelled,
            cancellationRequested: true,
            cancellationToken);
    }

    [Fact]
    public async Task Adding_an_alias_invalidates_pending_batch_but_rejected_duplicate_does_not()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var setup = await CreateBuildingBatchAsync(owner, "alias invalidation", cancellationToken);

        using var firstAliasResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/characters/{setup.Character.Id}/aliases",
            new { alias = "隊長" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, firstAliasResponse.StatusCode);
        await AssertBatchAndJobStateAsync(
            setup.Batch,
            SeriesCastRebuildBatchStatus.Failed,
            NarrationJobStatus.Cancelled,
            cancellationRequested: true,
            cancellationToken);

        using var retryResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var retryBatch = await retryResponse.Content.ReadFromJsonAsync<SeriesNarrationRebuildResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);
        Assert.NotNull(retryBatch);

        using var duplicateAliasResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/characters/{setup.Character.Id}/aliases",
            new { alias = " 隊長 " },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateAliasResponse.StatusCode);
        await AssertBatchAndJobStateAsync(
            retryBatch,
            SeriesCastRebuildBatchStatus.Building,
            NarrationJobStatus.Queued,
            cancellationRequested: false,
            cancellationToken);

        using var newAliasResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{setup.Series.Id}/characters/{setup.Character.Id}/aliases",
            new { alias = "學長" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, newAliasResponse.StatusCode);
        await AssertBatchAndJobStateAsync(
            retryBatch,
            SeriesCastRebuildBatchStatus.Failed,
            NarrationJobStatus.Cancelled,
            cancellationRequested: true,
            cancellationToken);
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
    public async Task Rebuild_admission_rechecks_formal_bluemagpie_flag_for_an_existing_cast()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enabledFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlueMagpie:Enabled", "true");
            builder.UseSetting("BlueMagpie:FormalNarrationEnabled", "true");
            builder.UseSetting("BlueMagpie:InternalToken", new string('t', 32));
        });
        using var owner = await enabledFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(owner, "formal admission must be rechecked", cancellationToken);

        using var createResponse = await owner.PostWithCsrfAsync(
            "/api/series",
            new
            {
                name = $"BlueMagpie admission {Guid.NewGuid():N}",
                narratorProvider = "bluemagpie",
                narratorVoice = "female_voice",
                narratorRate = "+0%",
                narratorPitch = "+0Hz",
                narratorVolume = "+0%",
                defaultSpeakerPauseMs = 180
            },
            cancellationToken);
        var series = await createResponse.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(series);
        await AddBookAsync(owner, series.Id, book.Id, cancellationToken);

        using var addCharacterResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters",
            new
            {
                canonicalName = "測試角色",
                role = "Main",
                voiceProvider = "bluemagpie",
                voice = "hung_yi_lee",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, addCharacterResponse.StatusCode);
        await ConfirmOnlyChapterPlanAsync(owner, series.Id, book, cancellationToken);

        enabledFactory.Services.GetRequiredService<IOptions<BlueMagpieOptions>>()
            .Value.FormalNarrationEnabled = false;
        using var stageResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var responseBody = await stageResponse.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, stageResponse.StatusCode);
        Assert.Contains("正式小說配音尚未由管理員啟用", responseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_voai_series_stages_with_pinned_voice_and_provider_specific_cast_metadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(owner, "VoAI synthetic staging text", cancellationToken);

        using var seriesResponse = await owner.PostWithCsrfAsync(
            "/api/series",
            new
            {
                name = $"VoAI 測試系列 {Guid.NewGuid():N}",
                narratorProvider = "voai",
                narratorVoice = "v1:Neo:佑希:預設",
                narratorRate = "+0%",
                narratorPitch = "+0Hz",
                narratorVolume = "+0%",
                defaultSpeakerPauseMs = 180
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
                canonicalName = "VoAI 測試角色",
                role = "Main",
                voiceProvider = "voai",
                voice = "v1:Neo:佑希:預設",
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
        var staged = await stageResponse.Content.ReadFromJsonAsync<SeriesNarrationRebuildResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, stageResponse.StatusCode);
        Assert.NotNull(staged);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var revision = await db.NarrationCastRevisions
            .AsNoTracking()
            .Include(candidate => candidate.Assignments)
            .SingleAsync(candidate => candidate.Id == staged.DraftCastRevisionId, cancellationToken);
        Assert.Equal("voai", revision.NarratorProvider);
        Assert.Equal("voai-voice-api-v1", revision.NarratorProviderVersion);
        Assert.Equal("v1:Neo:佑希:預設", revision.NarratorVoice);
        Assert.Equal("voai-speech-turn-concat-v1", revision.CompositionVersion);
        Assert.Equal("wav-32khz-to-mp3-concat-v1", revision.FfmpegProfile);
        var assignment = Assert.Single(revision.Assignments);
        Assert.Equal("voai", assignment.VoiceProvider);
        Assert.Equal("voai-voice-api-v1", assignment.ProviderVersion);
        Assert.Equal("v1:Neo:佑希:預設", assignment.Voice);
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

    private static async Task<(
        StorySeriesDetailsResponse Series,
        BookDetailsResponse Book,
        StorySeriesCharacterResponse Character,
        SeriesNarrationRebuildResponse Batch)> CreateBuildingBatchAsync(
        HttpClient owner,
        string text,
        CancellationToken cancellationToken)
    {
        var book = await ImportTextAsync(owner, text, cancellationToken);
        var series = await CreateSeriesAsync(owner, cancellationToken);
        await AddBookAsync(owner, series.Id, book.Id, cancellationToken);
        await AddCharacterAsync(owner, series.Id, cancellationToken);
        await ConfirmOnlyChapterPlanAsync(owner, series.Id, book, cancellationToken);
        using var detailsResponse = await owner.GetAsync($"/api/series/{series.Id}", cancellationToken);
        var details = await detailsResponse.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(
            cancellationToken);
        var character = Assert.Single(Assert.IsType<StorySeriesDetailsResponse>(details).Characters);
        using var stageResponse = await owner.PostWithCsrfAsync(
            $"/api/series/{series.Id}/narration-rebuilds",
            new { rightsAttested = true },
            cancellationToken);
        var batch = await stageResponse.Content.ReadFromJsonAsync<SeriesNarrationRebuildResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, stageResponse.StatusCode);
        return (series, book, character, Assert.IsType<SeriesNarrationRebuildResponse>(batch));
    }

    private async Task AssertBatchAndJobStateAsync(
        SeriesNarrationRebuildResponse batchSnapshot,
        SeriesCastRebuildBatchStatus expectedBatchStatus,
        NarrationJobStatus expectedJobStatus,
        bool cancellationRequested,
        CancellationToken cancellationToken)
    {
        var stagedJobId = Assert.IsType<Guid>(Assert.Single(batchSnapshot.Members).StagedNarrationJobId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var batch = await db.SeriesCastRebuildBatches
            .SingleAsync(candidate => candidate.Id == batchSnapshot.Id, cancellationToken);
        var job = await db.NarrationJobs
            .SingleAsync(candidate => candidate.Id == stagedJobId, cancellationToken);
        Assert.Equal(expectedBatchStatus, batch.Status);
        Assert.Equal(expectedJobStatus, job.Status);
        Assert.Equal(cancellationRequested, job.CancellationRequested);
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
