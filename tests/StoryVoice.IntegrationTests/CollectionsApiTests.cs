using System.Net;
using System.Net.Http.Json;
using StoryVoice.Application.Books;
using StoryVoice.Application.Collections;

namespace StoryVoice.IntegrationTests;

public sealed class CollectionsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Collection_endpoints_require_authentication_and_mutations_require_csrf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/collections", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var authenticatedClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var missingCsrfResponse = await authenticatedClient.PostAsJsonAsync(
            "/api/collections",
            new CreateBookCollectionRequest("缺少 CSRF 的書冊", null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrfResponse.StatusCode);
    }

    [Fact]
    public async Task Collections_are_owner_scoped_and_duplicate_names_are_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerAClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var ownerBClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var created = await CreateCollectionAsync(ownerAClient, "奇幻三部曲", cancellationToken);

        using var listResponse = await ownerAClient.GetAsync("/api/collections", cancellationToken);
        var ownerAList = await listResponse.Content
            .ReadFromJsonAsync<BookCollectionSummaryResponse[]>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(ownerAList);
        Assert.Contains(ownerAList, collection => collection.Id == created.Id && collection.Name == "奇幻三部曲");

        using var ownerBListResponse = await ownerBClient.GetAsync("/api/collections", cancellationToken);
        var ownerBList = await ownerBListResponse.Content
            .ReadFromJsonAsync<BookCollectionSummaryResponse[]>(cancellationToken);
        Assert.NotNull(ownerBList);
        Assert.Empty(ownerBList);

        using var ownerBGetResponse = await ownerBClient.GetAsync(
            $"/api/collections/{created.Id}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, ownerBGetResponse.StatusCode);

        using var duplicateResponse = await ownerAClient.PostWithCsrfAsync(
            "/api/collections",
            new CreateBookCollectionRequest("奇幻三部曲", null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Owner_can_manage_membership_and_non_owner_cannot_attach_foreign_books()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerAClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var ownerBClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var collection = await CreateCollectionAsync(ownerAClient, "書冊", cancellationToken);
        var book = await CreateBookAsync(ownerAClient, "第一冊內文", cancellationToken);
        var foreignBook = await CreateBookAsync(ownerBClient, "別人的書", cancellationToken);

        using var addForeignBookResponse = await ownerAClient.PostWithCsrfAsync(
            $"/api/collections/{collection.Id}/books",
            new AddCollectionBookRequest(foreignBook.Id, "第一冊", 1),
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, addForeignBookResponse.StatusCode);

        using var addBookResponse = await ownerAClient.PostWithCsrfAsync(
            $"/api/collections/{collection.Id}/books",
            new AddCollectionBookRequest(book.Id, "第一冊", 1),
            cancellationToken);
        var withBook = await addBookResponse.Content
            .ReadFromJsonAsync<BookCollectionDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, addBookResponse.StatusCode);
        Assert.NotNull(withBook);
        var membership = Assert.Single(withBook.Books);
        Assert.Equal("第一冊", membership.VolumeLabel);

        using var ownerBMutatesResponse = await ownerBClient.PostWithCsrfAsync(
            $"/api/collections/{collection.Id}/books",
            new AddCollectionBookRequest(book.Id, "越權冊次", 2),
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, ownerBMutatesResponse.StatusCode);

        using var reorderResponse = await ownerAClient.SendWithCsrfAsync(
            new HttpRequestMessage(HttpMethod.Put, $"/api/collections/{collection.Id}/books/{book.Id}")
            {
                Content = JsonContent.Create(new UpdateCollectionBookRequest("卷一", 3))
            },
            cancellationToken);
        var reordered = await reorderResponse.Content
            .ReadFromJsonAsync<BookCollectionDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, reorderResponse.StatusCode);
        Assert.Equal("卷一", Assert.Single(reordered!.Books).VolumeLabel);
        Assert.Equal(3, Assert.Single(reordered.Books).SortOrder);

        using var removeResponse = await ownerAClient.SendWithCsrfAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/collections/{collection.Id}/books/{book.Id}"),
            cancellationToken);
        var afterRemoval = await removeResponse.Content
            .ReadFromJsonAsync<BookCollectionDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.Empty(afterRemoval!.Books);
    }

    [Fact]
    public async Task Owner_can_share_read_only_and_grantee_can_read_chapters_but_not_mutate()
    {
        const string chapterText = "分享書冊測試章節內容";
        var cancellationToken = TestContext.Current.CancellationToken;
        var granteeEmail = $"grantee-{Guid.NewGuid():N}@example.com";
        using var ownerClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var granteeClient = factory.CreateCookieClient();
        await granteeClient.RegisterAsync(granteeEmail, "Moonlight!Story42", cancellationToken);
        using var strangerClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var collection = await CreateCollectionAsync(ownerClient, "分享書冊", cancellationToken);
        var book = await CreateBookAsync(ownerClient, chapterText, cancellationToken);
        await ownerClient.PostWithCsrfAsync(
            $"/api/collections/{collection.Id}/books",
            new AddCollectionBookRequest(book.Id, "第一冊", 1),
            cancellationToken);

        using var unknownEmailShareResponse = await ownerClient.PostWithCsrfAsync(
            $"/api/collections/{collection.Id}/shares",
            new AddCollectionShareRequest("no-such-user@example.com"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, unknownEmailShareResponse.StatusCode);

        using var shareResponse = await ownerClient.PostWithCsrfAsync(
            $"/api/collections/{collection.Id}/shares",
            new AddCollectionShareRequest(granteeEmail),
            cancellationToken);
        var shared = await shareResponse.Content
            .ReadFromJsonAsync<BookCollectionDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, shareResponse.StatusCode);
        Assert.NotNull(shared);
        var share = Assert.Single(shared.Shares);
        Assert.Equal(granteeEmail, share.GranteeEmail);

        using var granteeListResponse = await granteeClient.GetAsync(
            "/api/collections/shared-with-me",
            cancellationToken);
        var granteeList = await granteeListResponse.Content
            .ReadFromJsonAsync<SharedCollectionSummaryResponse[]>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, granteeListResponse.StatusCode);
        Assert.NotNull(granteeList);
        Assert.Contains(granteeList, summary => summary.Id == collection.Id);

        using var granteeDetailResponse = await granteeClient.GetAsync(
            $"/api/collections/shared-with-me/{collection.Id}",
            cancellationToken);
        var granteeDetail = await granteeDetailResponse.Content
            .ReadFromJsonAsync<SharedCollectionDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, granteeDetailResponse.StatusCode);
        Assert.NotNull(granteeDetail);
        Assert.Single(granteeDetail.Books);

        using var granteeContentResponse = await granteeClient.GetAsync(
            $"/api/collections/shared-with-me/{collection.Id}/books/{book.Id}",
            cancellationToken);
        var granteeContent = await granteeContentResponse.Content
            .ReadFromJsonAsync<SharedCollectionBookContentResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, granteeContentResponse.StatusCode);
        Assert.NotNull(granteeContent);
        Assert.Contains(
            granteeContent.Chapters,
            chapter => chapter.OriginalText.Contains(chapterText, StringComparison.Ordinal));

        using var strangerDetailResponse = await strangerClient.GetAsync(
            $"/api/collections/shared-with-me/{collection.Id}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, strangerDetailResponse.StatusCode);

        using var revokeResponse = await ownerClient.SendWithCsrfAsync(
            new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/collections/{collection.Id}/shares/{share.Id}"),
            cancellationToken);
        var afterRevoke = await revokeResponse.Content
            .ReadFromJsonAsync<BookCollectionDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        Assert.Empty(afterRevoke!.Shares);

        using var granteeAfterRevokeResponse = await granteeClient.GetAsync(
            $"/api/collections/shared-with-me/{collection.Id}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, granteeAfterRevokeResponse.StatusCode);
    }

    private static async Task<BookCollectionDetailsResponse> CreateCollectionAsync(
        HttpClient client,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            "/api/collections",
            new CreateBookCollectionRequest(name, null),
            cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<BookCollectionDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<BookCollectionDetailsResponse>(created);
    }

    private static async Task<BookDetailsResponse> CreateBookAsync(
        HttpClient client,
        string originalText,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            "/api/books",
            new CreateBookRequest(
                $"Synthetic book {Guid.NewGuid():N}",
                "Synthetic author",
                "zh-TW",
                "synthetic.txt",
                [new CreateChapterRequest(1, "序章", originalText)]),
            cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<BookDetailsResponse>(created);
    }
}
