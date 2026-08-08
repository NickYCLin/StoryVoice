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
            false);
    }

    private static async Task<HttpResponseMessage> PutWithCsrfAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
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
