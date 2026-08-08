using System.Net;
using System.Net.Http.Json;

namespace StoryVoice.IntegrationTests;

public sealed class BooksComTwBookshelfApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ImportPath = "/api/books/sources/books-com-tw/import";

    [Fact]
    public async Task Import_is_idempotent_and_refreshes_visible_metadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var firstRequest = new
        {
            books = new[]
            {
                new
                {
                    externalId = "E050145360",
                    title = "月下的第一本書",
                    author = "初始作者",
                    language = "zh-TW",
                    sourceUrl = "https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360&access_token=provider-secret",
                    coverImageUrl = "https://im1.book.com.tw/image/getImage?i=E050145360&signature=provider-secret",
                    nativeTtsAvailable = (bool?)true,
                    ebookLayout = (string?)"Reflowable"
                },
                new
                {
                    externalId = "E050145361",
                    title = "火蝶的第二本書",
                    author = "另一位作者",
                    language = "zh-TW",
                    sourceUrl = "https://www.books.com.tw/products/E050145361",
                    coverImageUrl = "https://im2.book.com.tw/image/getImage?i=E050145361",
                    nativeTtsAvailable = (bool?)null,
                    ebookLayout = (string?)null
                }
            }
        };

        var firstResponse = await client.PostWithCsrfAsync(
            ImportPath,
            firstRequest,
            cancellationToken);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<ImportResultProbe>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstResult);
        Assert.Equal(2, firstResult.CreatedCount);
        Assert.Equal(0, firstResult.UpdatedCount);
        Assert.All(firstResult.Books, book => Assert.Equal("books-com-tw", book.SourceProvider));
        Assert.All(firstResult.Books, book => Assert.Equal("Linked", book.Status));
        var ttsBook = Assert.Single(firstResult.Books, book => book.ExternalSourceId == "E050145360");
        Assert.True(ttsBook.NativeTtsAvailable);
        Assert.Equal("Reflowable", ttsBook.EbookLayout);
        Assert.Equal(
            "https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360",
            ttsBook.SourceUrl);
        Assert.Equal(
            "https://im1.book.com.tw/image/getImage?i=E050145360",
            ttsBook.CoverImageUrl);

        var refreshResponse = await client.PostWithCsrfAsync(ImportPath, new
        {
            books = new[]
            {
                new
                {
                    externalId = "E050145360",
                    title = "月下的第一本書（新版）",
                    author = "更新作者",
                    language = "zh-TW",
                    sourceUrl = "https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360",
                    coverImageUrl = "https://im1.book.com.tw/image/getImage?i=E050145360-new",
                    nativeTtsAvailable = false,
                    ebookLayout = "Fixed"
                }
            }
        }, cancellationToken);
        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<ImportResultProbe>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotNull(refreshResult);
        Assert.Equal(0, refreshResult.CreatedCount);
        Assert.Equal(1, refreshResult.UpdatedCount);
        Assert.Single(refreshResult.Books);
        Assert.Equal("月下的第一本書（新版）", refreshResult.Books[0].Title);
        Assert.Equal("更新作者", refreshResult.Books[0].Author);
        Assert.False(refreshResult.Books[0].NativeTtsAvailable);
        Assert.Equal("Fixed", refreshResult.Books[0].EbookLayout);
    }

    [Fact]
    public async Task Import_rejects_non_books_com_tw_links()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var response = await client.PostWithCsrfAsync(ImportPath, new
        {
            books = new[]
            {
                new
                {
                    externalId = "unsafe",
                    title = "不安全來源",
                    author = "未知",
                    sourceUrl = "https://example.com/fake-book",
                    coverImageUrl = "https://example.com/tracker.png"
                }
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ImportResultProbe(
        int CreatedCount,
        int UpdatedCount,
        IReadOnlyList<BookProbe> Books);

    private sealed record BookProbe(
        Guid Id,
        string Title,
        string Author,
        string Status,
        string? SourceProvider,
        string? ExternalSourceId,
        string? SourceUrl,
        string? CoverImageUrl,
        bool? NativeTtsAvailable,
        string? EbookLayout);
}
