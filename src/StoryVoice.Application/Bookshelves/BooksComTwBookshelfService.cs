using System.Text.RegularExpressions;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Books;
using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Bookshelves;

public sealed class BooksComTwBookshelfService(
    IBookRepository repository,
    ICurrentUser currentUser) : IBooksComTwBookshelfService
{
    public const string Provider = "books-com-tw";
    private const int MaximumBooksPerRequest = 200;
    private static readonly Regex ExternalIdPattern = new(
        "^[A-Za-z0-9._:-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string[] ViewerExternalIdQueryKeys =
        ["book_uni_id", "bookUniId", "book_id", "product_id", "id"];

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
                    currentUser.UserId,
                    metadata.Title,
                    metadata.Author,
                    metadata.Language,
                    Provider,
                    metadata.ExternalId,
                    metadata.SourceUrl,
                    metadata.CoverImageUrl,
                    metadata.NativeTtsAvailable,
                    metadata.EbookLayout);
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
                    metadata.CoverImageUrl,
                    metadata.NativeTtsAvailable,
                    metadata.EbookLayout);
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
            ValidateBooksUrl(metadata.SourceUrl, "sourceUrl", externalId, allowAssetDomain: false),
            string.IsNullOrWhiteSpace(metadata.CoverImageUrl)
                ? null
                : ValidateBooksUrl(metadata.CoverImageUrl, "coverImageUrl", externalId, allowAssetDomain: true),
            metadata.NativeTtsAvailable,
            ParseEbookLayout(metadata.EbookLayout));
    }

    private static EbookLayout? ParseEbookLayout(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "reflowable" => EbookLayout.Reflowable,
            "fixed" => EbookLayout.Fixed,
            _ => throw new ArgumentException("ebookLayout 僅支援 Reflowable 或 Fixed。", nameof(value))
        };
    }

    private static string ValidateBooksUrl(
        string value,
        string fieldName,
        string externalId,
        bool allowAssetDomain)
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

        if (!allowAssetDomain)
        {
            var isViewerUrl =
                uri.Host.Equals("viewer-ebook.books.com.tw", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/viewer/", StringComparison.OrdinalIgnoreCase) &&
                ViewerQueryMatchesExternalId(uri, externalId);
            var pathSegments = uri
                .GetComponents(UriComponents.Path, UriFormat.Unescaped)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            var isProductHost = uri.Host.Equals("books.com.tw", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("www.books.com.tw", StringComparison.OrdinalIgnoreCase);
            var isMatchingProductUrl = isProductHost &&
                pathSegments.Length == 2 &&
                pathSegments[0].Equals("products", StringComparison.OrdinalIgnoreCase) &&
                pathSegments[1].Equals(externalId, StringComparison.OrdinalIgnoreCase);

            if (!isViewerUrl && !isMatchingProductUrl)
            {
                throw new ArgumentException(
                    $"{fieldName} 必須指向相同 externalId 的博客來商品或閱讀器。",
                    fieldName);
            }
        }

        var sanitized = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = string.Empty
        };
        if (!allowAssetDomain &&
            uri.Host.Equals("viewer-ebook.books.com.tw", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/viewer/", StringComparison.OrdinalIgnoreCase))
        {
            sanitized.Query = $"book_uni_id={Uri.EscapeDataString(externalId)}";
        }
        else if (allowAssetDomain && isAssetDomain &&
                 uri.AbsolutePath.StartsWith("/image/getImage", StringComparison.OrdinalIgnoreCase))
        {
            sanitized.Query = $"i={Uri.EscapeDataString(externalId)}";
        }

        return sanitized.Uri.AbsoluteUri;
    }

    private static bool ViewerQueryMatchesExternalId(Uri uri, string externalId)
    {
        var foundExternalId = false;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(separator >= 0 ? pair[..separator] : pair);
            if (!ViewerExternalIdQueryKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foundExternalId = true;
            var value = separator >= 0 ? Uri.UnescapeDataString(pair[(separator + 1)..]) : string.Empty;
            if (!value.Equals(externalId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return foundExternalId;
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
        string? CoverImageUrl,
        bool? NativeTtsAvailable,
        EbookLayout? EbookLayout);
}
