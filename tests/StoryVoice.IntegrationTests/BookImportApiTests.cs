using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using StoryVoice.Application.Books;

namespace StoryVoice.IntegrationTests;

public sealed class BookImportApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Import_txt_creates_book_and_extracted_chapters()
    {
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("""
            第一章 起點
            月色落在窗前。

            第二章 回聲
            風裡傳來回答。
            """));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", "月下故事.txt");

        var response = await client.PostAsync(
            "/api/books/import?author=StoryVoice&language=zh-TW",
            content,
            TestContext.Current.CancellationToken);
        var imported = await response.Content.ReadFromJsonAsync<BookDetailsResponse>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(imported);
        Assert.Equal("月下故事", imported.Title);
        Assert.Equal("StoryVoice", imported.Author);
        Assert.Equal("txt", imported.FileType);
        Assert.Equal(2, imported.Chapters.Count);
        Assert.Equal("第二章 回聲", imported.Chapters[1].Title);
    }

    [Fact]
    public async Task Import_rejects_unsupported_file_types()
    {
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([1, 2, 3]), "file", "story.pdf");

        var response = await client.PostAsync(
            "/api/books/import",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
