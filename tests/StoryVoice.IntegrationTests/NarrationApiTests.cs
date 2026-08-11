using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Application.Books;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class NarrationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Rights_attestation_and_strict_uploaded_text_provenance_are_required()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var uploaded = await ImportTextAsync(client, cancellationToken);

        using var noAttestation = await client.PostWithCsrfAsync(
            $"/api/books/{uploaded.Id}/narrations/",
            new CreateNarrationRequest(false),
            cancellationToken);
        using var noAttestationProblem = JsonDocument.Parse(
            await noAttestation.Content.ReadAsStreamAsync(cancellationToken));

        using var syntheticCreate = await client.PostWithCsrfAsync(
            "/api/books/",
            new CreateBookRequest(
                "合成書",
                "測試作者",
                "zh-TW",
                "synthetic.txt",
                [new CreateChapterRequest(1, "第一章", "未經檔案上傳的文字。")]),
            cancellationToken);
        var synthetic = await syntheticCreate.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);
        Assert.NotNull(synthetic);
        using var noProvenance = await client.PostWithCsrfAsync(
            $"/api/books/{synthetic.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        using var noProvenanceProblem = JsonDocument.Parse(
            await noProvenance.Content.ReadAsStreamAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, noAttestation.StatusCode);
        Assert.Equal(
            NarrationRightsRequiredException.StableCode,
            noAttestationProblem.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, noProvenance.StatusCode);
        Assert.Equal(
            NarrationTextUnavailableException.StableCode,
            noProvenanceProblem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Narration_is_idempotent_owner_scoped_cancellable_and_range_streamed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var other = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(owner, cancellationToken);

        using var firstCreate = await owner.PostWithCsrfAsync(
            $"/api/books/{book.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        var first = await firstCreate.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(first);
        using var secondCreate = await owner.PostWithCsrfAsync(
            $"/api/books/{book.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        var second = await secondCreate.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var job = await db.NarrationJobs.SingleAsync(item => item.Id == first.Id, cancellationToken);
            job.Claim("integration-worker", DateTimeOffset.UtcNow.AddMinutes(20));
            var relativePath = Path.Combine(job.OwnerId.ToString("N"), $"{job.Id:N}.mp3");
            var absolutePath = Path.Combine(factory.StorageRoot, "audio", relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            await File.WriteAllBytesAsync(absolutePath, [1, 2, 3, 4, 5, 6, 7, 8], cancellationToken);
            job.Complete(relativePath, 8);
            await db.SaveChangesAsync(cancellationToken);
        }

        using var otherGet = await other.GetAsync($"/api/narrations/{first.Id}/", cancellationToken);
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/narrations/{first.Id}/audio");
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 3);
        using var rangeResponse = await owner.SendAsync(rangeRequest, cancellationToken);
        var rangeBytes = await rangeResponse.Content.ReadAsByteArrayAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal("audio/mpeg", rangeResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3, 4], rangeBytes);

        var secondBook = await ImportTextAsync(owner, cancellationToken);
        using var queuedCreate = await owner.PostWithCsrfAsync(
            $"/api/books/{secondBook.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        var queued = await queuedCreate.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(queued);
        using var cancel = await owner.PostWithCsrfAsync(
            $"/api/narrations/{queued.Id}/cancel",
            new { },
            cancellationToken);
        var cancelled = await cancel.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(cancelled);
        Assert.Equal(NarrationJobStatus.Cancelled.ToString(), cancelled.Status);
    }

    [Fact]
    public async Task Regular_endpoints_hide_staged_and_historical_artifacts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(client, cancellationToken);
        using var publishedCreate = await client.PostWithCsrfAsync(
            $"/api/books/{book.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        var published = await publishedCreate.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(published);

        NarrationJob staged;
        NarrationJob queuedStaged;
        NarrationJob historical;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var ownerId = await db.Books
                .Where(candidate => candidate.Id == book.Id)
                .Select(candidate => candidate.OwnerId!.Value)
                .SingleAsync(cancellationToken);
            staged = NarrationArtifactTestData.CompletedStaged(ownerId, book.Id, book.Id);
            queuedStaged = NarrationArtifactTestData.QueuedStaged(ownerId, book.Id, book.Id);
            historical = NarrationArtifactTestData.CompletedHistorical(ownerId, book.Id, book.Id);
            db.NarrationJobs.AddRange(staged, queuedStaged, historical);
            await db.SaveChangesAsync(cancellationToken);
        }

        await NarrationArtifactTestData.WriteAudioAsync(factory.StorageRoot, staged, cancellationToken);
        await NarrationArtifactTestData.WriteAudioAsync(factory.StorageRoot, historical, cancellationToken);

        using var listResponse = await client.GetAsync(
            $"/api/books/{book.Id}/narrations/",
            cancellationToken);
        var listed = await listResponse.Content.ReadFromJsonAsync<NarrationJobResponse[]>(cancellationToken);
        Assert.NotNull(listed);
        Assert.Equal([published.Id], listed.Select(job => job.Id));

        foreach (var hidden in new[] { staged, queuedStaged, historical })
        {
            using var get = await client.GetAsync($"/api/narrations/{hidden.Id}/", cancellationToken);
            using var audio = await client.GetAsync($"/api/narrations/{hidden.Id}/audio", cancellationToken);
            using var cancel = await client.PostWithCsrfAsync(
                $"/api/narrations/{hidden.Id}/cancel",
                new { },
                cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, audio.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var persisted = await db.NarrationJobs.AsNoTracking()
                .Where(job => job.Id == staged.Id
                    || job.Id == queuedStaged.Id
                    || job.Id == historical.Id)
                .ToDictionaryAsync(job => job.Id, cancellationToken);
            Assert.Equal(NarrationArtifactVisibility.Staged, persisted[staged.Id].Visibility);
            Assert.Equal(NarrationArtifactVisibility.Staged, persisted[queuedStaged.Id].Visibility);
            Assert.Equal(NarrationArtifactVisibility.Historical, persisted[historical.Id].Visibility);
            Assert.Equal(NarrationJobStatus.Completed, persisted[staged.Id].Status);
            Assert.Equal(NarrationJobStatus.Queued, persisted[queuedStaged.Id].Status);
            Assert.False(persisted[queuedStaged.Id].CancellationRequested);
            Assert.Equal(NarrationJobStatus.Completed, persisted[historical.Id].Status);
        }
    }

    [Fact]
    public async Task Create_hides_a_historical_single_voice_idempotency_match()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(client, cancellationToken);
        using var firstCreate = await client.PostWithCsrfAsync(
            $"/api/books/{book.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        var original = await firstCreate.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(original);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var job = await db.NarrationJobs.SingleAsync(item => item.Id == original.Id, cancellationToken);
            job.Claim("historical-idempotency-test", DateTimeOffset.UtcNow.AddMinutes(5));
            job.Complete(Path.Combine(job.OwnerId.ToString("N"), $"{job.Id:N}.mp3"), 4);
            NarrationArtifactTestData.MarkHistorical(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        using var repeatedCreate = await client.PostWithCsrfAsync(
            $"/api/books/{book.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, repeatedCreate.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var persisted = await verifyDb.NarrationJobs.AsNoTracking()
            .SingleAsync(item => item.Id == original.Id, cancellationToken);
        Assert.Equal(NarrationArtifactVisibility.Historical, persisted.Visibility);
        Assert.Equal(NarrationJobStatus.Completed, persisted.Status);
    }

    [Fact]
    public async Task Concurrency_stamp_blocks_stale_completion_and_requeue_transitions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var firstBook = await ImportTextAsync(client, cancellationToken);
        using var firstCreate = await client.PostWithCsrfAsync(
            $"/api/books/{firstBook.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        var firstJob = await firstCreate.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(firstJob);

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var job = await setupDb.NarrationJobs.SingleAsync(item => item.Id == firstJob.Id, cancellationToken);
            job.Claim("worker-a:claim-a", DateTimeOffset.UtcNow.AddMinutes(20));
            await setupDb.SaveChangesAsync(cancellationToken);
        }

        await using (var cancelScope = factory.Services.CreateAsyncScope())
        await using (var staleWorkerScope = factory.Services.CreateAsyncScope())
        {
            var cancelDb = cancelScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var staleWorkerDb = staleWorkerScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var cancelJob = await cancelDb.NarrationJobs.SingleAsync(item => item.Id == firstJob.Id, cancellationToken);
            var staleWorkerJob = await staleWorkerDb.NarrationJobs.SingleAsync(item => item.Id == firstJob.Id, cancellationToken);

            cancelJob.RequestCancellation();
            await cancelDb.SaveChangesAsync(cancellationToken);
            staleWorkerJob.Complete("owner/stale.mp3", 42);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => staleWorkerDb.SaveChangesAsync(cancellationToken));
        }

        var secondBook = await ImportTextAsync(client, cancellationToken);
        using var secondCreate = await client.PostWithCsrfAsync(
            $"/api/books/{secondBook.Id}/narrations/",
            new CreateNarrationRequest(true),
            cancellationToken);
        var secondJob = await secondCreate.Content.ReadFromJsonAsync<NarrationJobResponse>(cancellationToken);
        Assert.NotNull(secondJob);
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var job = await setupDb.NarrationJobs.SingleAsync(item => item.Id == secondJob.Id, cancellationToken);
            job.RequestCancellation();
            await setupDb.SaveChangesAsync(cancellationToken);
        }

        await using (var firstRequeueScope = factory.Services.CreateAsyncScope())
        await using (var staleRequeueScope = factory.Services.CreateAsyncScope())
        {
            var firstDb = firstRequeueScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var staleDb = staleRequeueScope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var first = await firstDb.NarrationJobs.SingleAsync(item => item.Id == secondJob.Id, cancellationToken);
            var stale = await staleDb.NarrationJobs.SingleAsync(item => item.Id == secondJob.Id, cancellationToken);

            first.Requeue(DateTimeOffset.UtcNow);
            await firstDb.SaveChangesAsync(cancellationToken);
            stale.Requeue(DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => staleDb.SaveChangesAsync(cancellationToken));
        }
    }

    private static async Task<BookDetailsResponse> ImportTextAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("第一章\n這是合法測試正文，只用來驗證語音工作流程。"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", $"narration-{Guid.NewGuid():N}.txt");
        using var response = await client.PostMultipartWithCsrfAsync("/api/books/import", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Import response did not contain a book.");
    }
}
