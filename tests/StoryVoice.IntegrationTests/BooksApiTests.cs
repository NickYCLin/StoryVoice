using System.Net;
using System.Net.Http.Json;
using StoryVoice.Application.Books;

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
