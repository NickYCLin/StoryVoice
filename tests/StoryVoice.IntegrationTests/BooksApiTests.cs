using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Application.Books;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class BooksApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_then_read_book_preserves_chapters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var request = new CreateBookRequest(
            "月下故事",
            "比比工程師",
            "zh-TW",
            "story.epub",
            [new CreateChapterRequest(1, "序章", "故事從月色裡開始。")]);

        var createResponse = await client.PostWithCsrfAsync(
            "/api/books",
            request,
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("月下故事", created.Title);
        Assert.Single(created.Chapters);
        Assert.False(created.AuthorizedTextAvailable);

        var getResponse = await client.GetAsync($"/api/books/{created.Id}", cancellationToken);
        var fetched = await getResponse.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(fetched);
        Assert.Equal("序章", fetched.Chapters[0].Title);
    }

    [Fact]
    public async Task Unknown_book_returns_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var response = await client.GetAsync(
            $"/api/books/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archived_book_is_hidden_from_library_but_recoverable_by_direct_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var createResponse = await client.PostWithCsrfAsync(
            "/api/books",
            new CreateBookRequest(
                "收進庫房的舊分冊",
                "StoryVoice",
                "zh-TW",
                "archived.txt",
                [new CreateChapterRequest(1, "序章", "仍應保留，但不應出現在一般書櫃。")]),
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var book = await db.Books.SingleAsync(book => book.Id == created.Id, cancellationToken);
            book.Archive();
            await db.SaveChangesAsync(cancellationToken);
        }

        var listed = await client.GetFromJsonAsync<BookSummaryResponse[]>("/api/books", cancellationToken);
        using var directResponse = await client.GetAsync($"/api/books/{created.Id}", cancellationToken);
        var direct = await directResponse.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);

        Assert.NotNull(listed);
        Assert.DoesNotContain(listed, book => book.Id == created.Id);
        Assert.Equal(HttpStatusCode.OK, directResponse.StatusCode);
        Assert.NotNull(direct);
        Assert.Single(direct.Chapters);
        Assert.False(direct.AuthorizedTextAvailable);
    }

    [Fact]
    public async Task Create_book_uses_allowed_forwarded_prefix_in_location_header()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/books")
        {
            Content = JsonContent.Create(new CreateBookRequest(
                "子路徑故事",
                "StoryVoice",
                "zh-TW",
                "subpath.txt",
                [new CreateChapterRequest(1, "序章", "從子路徑開始。")]))
        };
        request.Headers.Add("X-Forwarded-Prefix", "/StoryVoice");

        var response = await client.SendWithCsrfAsync(
            request,
            cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<BookDetailsResponse>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal($"/StoryVoice/api/books/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Create_book_ignores_unconfigured_forwarded_prefix()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/books")
        {
            Content = JsonContent.Create(new CreateBookRequest(
                "偽造前綴故事",
                "StoryVoice",
                "zh-TW",
                "untrusted.txt",
                [new CreateChapterRequest(1, "序章", "不反射任意前綴。")]))
        };
        request.Headers.Add("X-Forwarded-Prefix", "/untrusted");

        var response = await client.SendWithCsrfAsync(
            request,
            cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<BookDetailsResponse>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal($"/api/books/{created.Id}", response.Headers.Location?.OriginalString);
    }
}
