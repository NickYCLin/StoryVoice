using System.Net;
using System.Net.Http.Json;
using StoryVoice.Application.Books;

namespace StoryVoice.IntegrationTests;

public sealed class BooksApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_then_read_book_preserves_chapters()
    {
        using var client = factory.CreateClient();
        var request = new CreateBookRequest(
            "月下故事",
            "比比工程師",
            "zh-TW",
            "story.epub",
            [new CreateChapterRequest(1, "序章", "故事從月色裡開始。")]);

        var cancellationToken = TestContext.Current.CancellationToken;
        var createResponse = await client.PostAsJsonAsync("/api/books", request, cancellationToken);
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
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/books/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
