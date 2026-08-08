using System.Text.RegularExpressions;
using StoryVoice.Application.Books;
using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Bookshelves;

public sealed class BooksComTwBookshelfService(IBookRepository repository) : IBooksComTwBookshelfService
{
    public const string Provider = "books-com-tw";
    private const int MaximumBooksPerRequest = 200;
    private static readonly Regex ExternalIdPattern = new(
        "^[A-Za-z0-9._:-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<BooksComTwBookshelfImportResponse> ImportAsync(
        BooksComTwBookshelfImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Books is null || request.Books.Count == 0)
        {
            throw new ArgumentException("至少需要一本博客來書籍 metadata。", nameof(request));
        }

        if (request.Books.Count > MaximumBooksPerRequest)
        {
            throw new ArgumentException($"單次最多同步 {MaximumBooksPerRequest} 本書。", nameof(request));
        }

        var normalizedBooks = request.Books
            .Select(Normalize)
            .GroupBy(book => book.ExternalId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var results = new List<BookDetailsResponse>(normalizedBooks.Length);
        var createdCount = 0;
        var updatedCount = 0;

        foreach (var metadata in normalizedBooks)
        {
            var book = await repository.GetBySourceAsync(
                Provider,
                metadata.ExternalId,
                cancellationToken);
            if (book is null)
            {
                book = Book.CreateExternal(
                    metadata.Title,
                    metadata.Author,
                    metadata.Language,
                    Provider,
                    metadata.ExternalId,
                    metadata.SourceUrl,
                    metadata.CoverImageUrl);
                await repository.AddAsync(book, cancellationToken);
                createdCount++;
            }
            else
            {
                book.UpdateExternalMetadata(
                    metadata.Title,
                    metadata.Author,
                    metadata.Language,
                    metadata.SourceUrl,
                    metadata.CoverImageUrl);
                updatedCount++;
            }

            results.Add(BookResponseMapper.ToDetails(book));
        }

        await repository.SaveChangesAsync(cancellationToken);
        return new BooksComTwBookshelfImportResponse(createdCount, updatedCount, results);
    }

    private static NormalizedBook Normalize(BooksComTwBookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var externalId = Require(metadata.ExternalId, "externalId", 128);
        if (!ExternalIdPattern.IsMatch(externalId))
        {
            throw new ArgumentException("博客來書籍識別碼格式不正確。", nameof(metadata.ExternalId));
        }

        return new NormalizedBook(
            externalId,
            Require(metadata.Title, "title", 500),
            Optional(metadata.Author, 300) ?? "未知作者",
            Optional(metadata.Language, 20) ?? "zh-TW",
            ValidateBooksUrl(metadata.SourceUrl, "sourceUrl", allowAssetDomain: false),
            string.IsNullOrWhiteSpace(metadata.CoverImageUrl)
                ? null
                : ValidateBooksUrl(metadata.CoverImageUrl, "coverImageUrl", allowAssetDomain: true));
    }

    private static string ValidateBooksUrl(string value, string fieldName, bool allowAssetDomain)
    {
        var normalized = Require(value, fieldName, 2_000);
        var isBooksDomain = false;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Port != 443)
        {
            throw new ArgumentException($"{fieldName} 必須是安全的博客來 HTTPS 網址。", fieldName);
        }

        isBooksDomain = uri.Host.Equals("books.com.tw", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".books.com.tw", StringComparison.OrdinalIgnoreCase);
        var isAssetDomain = allowAssetDomain &&
            (uri.Host.Equals("book.com.tw", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".book.com.tw", StringComparison.OrdinalIgnoreCase));
        if (!isBooksDomain && !isAssetDomain)
        {
            throw new ArgumentException($"{fieldName} 必須是安全的博客來 HTTPS 網址。", fieldName);
        }

        return uri.AbsoluteUri;
    }

    private static string Require(string? value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} 不可為空白。", fieldName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{fieldName} 長度不可超過 {maximumLength} 個字元。", fieldName);
        }

        return normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"欄位長度不可超過 {maximumLength} 個字元。", nameof(value));
        }

        return normalized;
    }

    private sealed record NormalizedBook(
        string ExternalId,
        string Title,
        string Author,
        string Language,
        string SourceUrl,
        string? CoverImageUrl);
}
