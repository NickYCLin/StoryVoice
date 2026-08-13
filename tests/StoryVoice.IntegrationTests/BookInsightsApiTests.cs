using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StoryVoice.Application.Books;
using StoryVoice.Application.Insights;

namespace StoryVoice.IntegrationTests;

public sealed class BookInsightsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Imported_text_generates_idempotent_exact_source_summary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(client, cancellationToken);

        using var firstResponse = await PutWithCsrfAsync(
            client,
            $"/api/books/{book.Id}/summary",
            cancellationToken);
        var first = await firstResponse.Content.ReadFromJsonAsync<ExtractiveBookSummaryResponse>(cancellationToken);
        using var secondResponse = await PutWithCsrfAsync(
            client,
            $"/api/books/{book.Id}/summary",
            cancellationToken);
        var second = await secondResponse.Content.ReadFromJsonAsync<ExtractiveBookSummaryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Extractive", first.Kind);
        Assert.Equal(first.SourceHash, second.SourceHash);
        Assert.Equal(first.GeneratedAt, second.GeneratedAt);
        Assert.NotEmpty(first.Excerpts);
        foreach (var excerpt in first.Excerpts)
        {
            var chapter = book.Chapters.Single(item => item.Id == excerpt.ChapterId);
            Assert.Equal(excerpt.Text, chapter.OriginalText.Substring(excerpt.StartOffset, excerpt.Length));
        }
    }

    [Fact]
    public async Task Metadata_only_book_rejects_summary_but_accepts_manual_book_note()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sessionClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var companionClient = await CreateCompanionClientAsync(sessionClient, cancellationToken);
        var linked = await ImportLinkedBookAsync(companionClient, cancellationToken);

        using var summaryResponse = await PutWithCsrfAsync(
            sessionClient,
            $"/api/books/{linked.Id}/summary",
            cancellationToken);
        using var problem = JsonDocument.Parse(await summaryResponse.Content.ReadAsStreamAsync(cancellationToken));
        using var noteResponse = await sessionClient.PostWithCsrfAsync(
            $"/api/books/{linked.Id}/notes",
            new CreateReadingNoteRequest("這是我自己的閱讀備忘。", null),
            cancellationToken);
        var note = await noteResponse.Content.ReadFromJsonAsync<ReadingNoteResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, summaryResponse.StatusCode);
        Assert.Equal(BookTextUnavailableException.StableCode, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, noteResponse.StatusCode);
        Assert.NotNull(note);
        Assert.Null(note.ChapterId);
    }

    [Fact]
    public async Task Explicit_owner_scoped_link_enables_summary_and_chapter_note()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sessionClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var companionClient = await CreateCompanionClientAsync(sessionClient, cancellationToken);
        var linked = await ImportLinkedBookAsync(companionClient, cancellationToken);
        var content = await ImportTextAsync(sessionClient, cancellationToken);

        using var linkResponse = await PutWithCsrfAsync(
            sessionClient,
            $"/api/books/{linked.Id}/content-link",
            new SetBookContentLinkRequest(content.Id),
            cancellationToken);
        var link = await linkResponse.Content.ReadFromJsonAsync<BookContentLinkResponse>(cancellationToken);
        using var summaryResponse = await PutWithCsrfAsync(
            sessionClient,
            $"/api/books/{linked.Id}/summary",
            cancellationToken);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<ExtractiveBookSummaryResponse>(cancellationToken);
        using var noteResponse = await sessionClient.PostWithCsrfAsync(
            $"/api/books/{linked.Id}/notes",
            new CreateReadingNoteRequest("第一章的手動筆記。", content.Chapters[0].Id),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        Assert.NotNull(link);
        Assert.Equal(content.Id, link.ContentBookId);
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        Assert.NotNull(summary);
        Assert.Equal(content.Id, summary.ContentBookId);
        Assert.Equal(HttpStatusCode.Created, noteResponse.StatusCode);
    }

    [Fact]
    public async Task Notes_and_content_links_are_owner_isolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var other = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(owner, cancellationToken);
        using var createNote = await owner.PostWithCsrfAsync(
            $"/api/books/{book.Id}/notes",
            new CreateReadingNoteRequest("只有擁有者看得到。", null),
            cancellationToken);

        using var listAsOther = await other.GetAsync($"/api/books/{book.Id}/notes", cancellationToken);
        using var linkAsOther = await PutWithCsrfAsync(
            other,
            $"/api/books/{book.Id}/content-link",
            new SetBookContentLinkRequest(book.Id),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createNote.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, listAsOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, linkAsOther.StatusCode);
    }

    [Fact]
    public async Task External_metadata_corrections_are_owner_scoped_and_reversible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var other = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var companion = await CreateCompanionClientAsync(owner, cancellationToken);
        var linked = await ImportLinkedBookAsync(companion, cancellationToken);

        using var update = await PutWithCsrfAsync(
            owner,
            $"/api/books/{linked.Id}/metadata-corrections",
            new UpdateBookMetadataCorrectionsRequest(
                "人工校正書名",
                "人工校正作者",
                "https://example.test/corrected-cover.jpg"),
            cancellationToken);
        var corrected = await update.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);
        using var otherUpdate = await PutWithCsrfAsync(
            other,
            $"/api/books/{linked.Id}/metadata-corrections",
            new UpdateBookMetadataCorrectionsRequest("越權", null, null),
            cancellationToken);
        using var clear = await PutWithCsrfAsync(
            owner,
            $"/api/books/{linked.Id}/metadata-corrections",
            new UpdateBookMetadataCorrectionsRequest(null, null, null),
            cancellationToken);
        var restored = await clear.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.NotNull(corrected);
        Assert.Equal("人工校正書名", corrected.Title);
        Assert.Equal("人工校正作者", corrected.Author);
        Assert.Equal("https://example.test/corrected-cover.jpg", corrected.CoverImageUrl);
        Assert.Equal(HttpStatusCode.NotFound, otherUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.NotNull(restored);
        Assert.Equal("外部書目", restored.Title);
        Assert.Equal("測試作者", restored.Author);
        Assert.Null(restored.TitleCorrection);
    }

    [Fact]
    public async Task Uploaded_book_accepts_manual_cover_correction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var uploaded = await ImportTextAsync(client, cancellationToken);

        using var response = await PutWithCsrfAsync(
            client,
            $"/api/books/{uploaded.Id}/metadata-corrections",
            new UpdateBookMetadataCorrectionsRequest(null, null, "https://example.test/corrected-cover.jpg"),
            cancellationToken);
        var corrected = await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(corrected);
        Assert.Equal("https://example.test/corrected-cover.jpg", corrected.CoverImageUrl);
    }

    [Fact]
    public async Task Synthetic_book_chapters_cannot_receive_chapter_notes_without_upload_provenance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var create = await client.PostWithCsrfAsync(
            "/api/books/",
            new CreateBookRequest(
                "合成測試書",
                "測試作者",
                "zh-TW",
                "synthetic.txt",
                [new CreateChapterRequest(1, "第一章", "沒有上傳來源的測試文字。")]),
            cancellationToken);
        var book = await create.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);
        Assert.NotNull(book);

        using var note = await client.PostWithCsrfAsync(
            $"/api/books/{book.Id}/notes",
            new CreateReadingNoteRequest("不可掛到未授權章節。", book.Chapters[0].Id),
            cancellationToken);
        using var problem = JsonDocument.Parse(await note.Content.ReadAsStreamAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.Conflict, note.StatusCode);
        Assert.Equal(BookTextUnavailableException.StableCode, problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Replacing_or_removing_content_link_detaches_existing_chapter_notes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var companion = await CreateCompanionClientAsync(client, cancellationToken);
        var linked = await ImportLinkedBookAsync(companion, cancellationToken);
        var firstContent = await ImportTextAsync(client, cancellationToken);
        var secondContent = await ImportTextAsync(client, cancellationToken);

        using var firstLink = await PutWithCsrfAsync(
            client,
            $"/api/books/{linked.Id}/content-link",
            new SetBookContentLinkRequest(firstContent.Id),
            cancellationToken);
        firstLink.EnsureSuccessStatusCode();
        using var firstNote = await client.PostWithCsrfAsync(
            $"/api/books/{linked.Id}/notes",
            new CreateReadingNoteRequest("換綁後保留為書籍筆記。", firstContent.Chapters[0].Id),
            cancellationToken);
        firstNote.EnsureSuccessStatusCode();

        using var replacement = await PutWithCsrfAsync(
            client,
            $"/api/books/{linked.Id}/content-link",
            new SetBookContentLinkRequest(secondContent.Id),
            cancellationToken);
        replacement.EnsureSuccessStatusCode();
        using var afterReplacement = await client.GetAsync($"/api/books/{linked.Id}/notes", cancellationToken);
        var replacedNotes = await afterReplacement.Content.ReadFromJsonAsync<ReadingNoteResponse[]>(cancellationToken);
        Assert.NotNull(replacedNotes);
        Assert.All(replacedNotes, note => Assert.Null(note.ChapterId));

        using var secondNote = await client.PostWithCsrfAsync(
            $"/api/books/{linked.Id}/notes",
            new CreateReadingNoteRequest("解綁後也保留為書籍筆記。", secondContent.Chapters[0].Id),
            cancellationToken);
        secondNote.EnsureSuccessStatusCode();
        using var unlink = await DeleteWithCsrfAsync(client, $"/api/books/{linked.Id}/content-link", cancellationToken);
        using var afterUnlink = await client.GetAsync($"/api/books/{linked.Id}/notes", cancellationToken);
        var unlinkedNotes = await afterUnlink.Content.ReadFromJsonAsync<ReadingNoteResponse[]>(cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        Assert.NotNull(unlinkedNotes);
        Assert.Equal(2, unlinkedNotes.Length);
        Assert.All(unlinkedNotes, note => Assert.Null(note.ChapterId));
    }

    [Fact]
    public async Task Removing_nonexistent_link_preserves_direct_upload_summary_and_chapter_notes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var uploaded = await ImportTextAsync(client, cancellationToken);

        using var summary = await PutWithCsrfAsync(
            client,
            $"/api/books/{uploaded.Id}/summary",
            cancellationToken);
        summary.EnsureSuccessStatusCode();
        using var note = await client.PostWithCsrfAsync(
            $"/api/books/{uploaded.Id}/notes",
            new CreateReadingNoteRequest("直傳正文的章節筆記。", uploaded.Chapters[0].Id),
            cancellationToken);
        note.EnsureSuccessStatusCode();

        using var unlink = await DeleteWithCsrfAsync(
            client,
            $"/api/books/{uploaded.Id}/content-link",
            cancellationToken);
        using var notesResponse = await client.GetAsync($"/api/books/{uploaded.Id}/notes", cancellationToken);
        var notes = await notesResponse.Content.ReadFromJsonAsync<ReadingNoteResponse[]>(cancellationToken);
        using var summaryResponse = await client.GetAsync($"/api/books/{uploaded.Id}/summary", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        Assert.NotNull(notes);
        Assert.Single(notes);
        Assert.Equal(uploaded.Chapters[0].Id, notes[0].ChapterId);
    }

    [Fact]
    public async Task Character_candidates_are_extracted_owner_scoped_and_ranked_by_frequency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var other = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await ImportTextAsync(
            owner,
            """
            第一章 起點
            小華問道：「你要走了？」
            小華問道：「真的嗎？」
            小明說：「再見。」
            """,
            cancellationToken);

        using var response = await owner.GetAsync(
            $"/api/books/{book.Id}/character-candidates",
            cancellationToken);
        var candidates = await response.Content.ReadFromJsonAsync<CharacterCandidateResponse[]>(cancellationToken);

        using var otherResponse = await other.GetAsync(
            $"/api/books/{book.Id}/character-candidates",
            cancellationToken);
        using var missingResponse = await owner.GetAsync(
            $"/api/books/{Guid.NewGuid()}/character-candidates",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(candidates);
        Assert.Equal(2, candidates.Length);
        Assert.Equal("小華", candidates[0].Name);
        Assert.Equal(2, candidates[0].OccurrenceCount);
        Assert.Equal("小明", candidates[1].Name);
        Assert.Equal(1, candidates[1].OccurrenceCount);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task Character_candidates_require_processable_authorized_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sessionClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var companionClient = await CreateCompanionClientAsync(sessionClient, cancellationToken);
        var linked = await ImportLinkedBookAsync(companionClient, cancellationToken);

        using var response = await sessionClient.GetAsync(
            $"/api/books/{linked.Id}/character-candidates",
            cancellationToken);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BookTextUnavailableException.StableCode, problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<BookDetailsResponse> ImportTextAsync(
        HttpClient client,
        string text,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", $"authorized-{Guid.NewGuid():N}.txt");
        using var response = await client.PostMultipartWithCsrfAsync(
            "/api/books/import",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Import response did not contain a book.");
    }

    private static async Task<BookDetailsResponse> ImportTextAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("""
            第一章 起點
            月色落在窗前。這是後續句子。

            第二章 回聲
            風裡傳來回答！這是第二段。
            """));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", $"authorized-{Guid.NewGuid():N}.txt");
        using var response = await client.PostMultipartWithCsrfAsync(
            "/api/books/import",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Import response did not contain a book.");
    }

    private async Task<HttpClient> CreateCompanionClientAsync(
        HttpClient sessionClient,
        CancellationToken cancellationToken)
    {
        using var tokenResponse = await sessionClient.PostWithCsrfAsync(
            "/api/auth/companion-token",
            new { },
            cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenBody = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(cancellationToken));
        var accessToken = tokenBody.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Companion token was missing.");
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task<BookDetailsResponse> ImportLinkedBookAsync(
        HttpClient companionClient,
        CancellationToken cancellationToken)
    {
        var externalId = $"E{Random.Shared.Next(100000000, 999999999)}";
        using var response = await companionClient.PostAsJsonAsync(
            "/api/books/sources/books-com-tw/import",
            new
            {
                books = new[]
                {
                    new
                    {
                        externalId,
                        title = "外部書目",
                        author = "測試作者",
                        language = "zh-TW",
                        sourceUrl = $"https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id={externalId}",
                        coverImageUrl = (string?)null,
                        nativeTtsAvailable = (bool?)null,
                        ebookLayout = "Reflowable"
                    }
                }
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var id = body.RootElement.GetProperty("books")[0].GetProperty("id").GetGuid();
        return new BookDetailsResponse(
            id,
            "外部書目",
            "測試作者",
            "zh-TW",
            $"{externalId}.link",
            "external",
            "Linked",
            DateTimeOffset.UtcNow,
            [],
            "books-com-tw",
            externalId,
            $"https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id={externalId}",
            null,
            null,
            "Reflowable",
            DateTimeOffset.UtcNow,
            null,
            false,
            null,
            null,
            null);
    }

    private static async Task<HttpResponseMessage> PutWithCsrfAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        return await client.SendWithCsrfAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> DeleteWithCsrfAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        return await client.SendWithCsrfAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PutWithCsrfAsync<T>(
        HttpClient client,
        string path,
        T? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendWithCsrfAsync(request, cancellationToken);
    }
}
