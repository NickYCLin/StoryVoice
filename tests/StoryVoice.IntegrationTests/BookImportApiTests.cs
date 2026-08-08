using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Application.Books;
using StoryVoice.Infrastructure.Persistence;
using StoryVoice.Tests.Shared;

namespace StoryVoice.IntegrationTests;

public sealed class BookImportApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Import_epub_uses_metadata_toc_and_reading_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(MinimalEpub.Create());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");
        content.Add(file, "file", "moon.epub");

        var response = await client.PostMultipartWithCsrfAsync(
            "/api/books/import",
            content,
            TestContext.Current.CancellationToken);
        var imported = await response.Content.ReadFromJsonAsync<BookDetailsResponse>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(imported);
        Assert.Equal("月下 EPUB", imported.Title);
        Assert.Equal("StoryVoice", imported.Author);
        Assert.Equal("zh-TW", imported.Language);
        Assert.Equal("epub", imported.FileType);
        Assert.Equal(2, imported.Chapters.Count);
        Assert.Equal("第一章 月影", imported.Chapters[0].Title);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var persisted = await database.Books.SingleAsync(
            book => book.Id == imported.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(persisted.StoragePath);
        Assert.True(File.Exists(Path.Combine(factory.StorageRoot, persisted.StoragePath)));
    }

    [Fact]
    public async Task Import_invalid_epub_returns_bad_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");
        content.Add(file, "file", "broken.epub");

        var response = await client.PostMultipartWithCsrfAsync(
            "/api/books/import",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_txt_creates_book_and_extracted_chapters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("""
            第一章 起點
            月色落在窗前。

            第二章 回聲
            風裡傳來回答。
            """));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", "月下故事.txt");

        var response = await client.PostMultipartWithCsrfAsync(
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
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([1, 2, 3]), "file", "story.pdf");

        var response = await client.PostMultipartWithCsrfAsync(
            "/api/books/import",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
