using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StoryVoice.Application.Books;

namespace StoryVoice.IntegrationTests;

public sealed class AuthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anonymous_user_receives_session_csrf_but_cannot_read_books()
    {
        using var client = factory.CreateCookieClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var sessionResponse = await client.GetAsync("/api/auth/session", cancellationToken);
        using var session = JsonDocument.Parse(
            await sessionResponse.Content.ReadAsStreamAsync(cancellationToken));
        using var booksResponse = await client.GetAsync("/api/books", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        Assert.False(session.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(session.RootElement.GetProperty("csrfToken").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, booksResponse.StatusCode);
    }

    [Fact]
    public async Task Register_requires_csrf_and_creates_authenticated_session()
    {
        using var client = factory.CreateCookieClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = $"reader-{Guid.NewGuid():N}@example.com";

        using var missingCsrfResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = "Moonlight!Story42" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrfResponse.StatusCode);

        using var registerResponse = await client.PostWithCsrfAsync(
            "/api/auth/register",
            new { email, password = "Moonlight!Story42" },
            cancellationToken);
        using var sessionResponse = await client.GetAsync("/api/auth/session", cancellationToken);
        using var session = JsonDocument.Parse(
            await sessionResponse.Content.ReadAsStreamAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        Assert.True(session.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(email, session.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_rejects_wrong_password_and_logout_clears_session()
    {
        using var client = factory.CreateCookieClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = $"reader-{Guid.NewGuid():N}@example.com";
        const string password = "Moonlight!Story42";
        await client.RegisterAsync(email, password, cancellationToken);

        using var logoutResponse = await client.PostWithCsrfAsync(
            "/api/auth/logout",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var wrongPasswordResponse = await client.PostWithCsrfAsync(
            "/api/auth/login",
            new { email, password = "Wrong!Password42", rememberMe = false },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);

        using var loginResponse = await client.PostWithCsrfAsync(
            "/api/auth/login",
            new { email, password, rememberMe = false },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Books_are_isolated_between_storyvoice_accounts()
    {
        using var ownerClient = factory.CreateCookieClient();
        using var otherClient = factory.CreateCookieClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        await ownerClient.RegisterAsync(
            $"owner-{Guid.NewGuid():N}@example.com",
            "Moonlight!Story42",
            cancellationToken);
        await otherClient.RegisterAsync(
            $"other-{Guid.NewGuid():N}@example.com",
            "Moonlight!Story42",
            cancellationToken);

        using var createResponse = await ownerClient.PostWithCsrfAsync(
            "/api/books",
            new CreateBookRequest(
                "只屬於我的故事",
                "StoryVoice",
                "zh-TW",
                "private.txt",
                [new CreateChapterRequest(1, "第一章", "只有擁有者能看見。")]),
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<BookDetailsResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var ownerBooks = await ownerClient.GetFromJsonAsync<BookDetailsResponse[]>(
            "/api/books",
            cancellationToken);
        using var otherListResponse = await otherClient.GetAsync("/api/books", cancellationToken);
        var otherBooks = await otherListResponse.Content.ReadFromJsonAsync<BookDetailsResponse[]>(
            cancellationToken);
        using var otherGetResponse = await otherClient.GetAsync(
            $"/api/books/{created.Id}",
            cancellationToken);

        Assert.Contains(ownerBooks!, book => book.Id == created.Id);
        Assert.DoesNotContain(otherBooks!, book => book.Id == created.Id);
        Assert.Equal(HttpStatusCode.NotFound, otherGetResponse.StatusCode);
    }

    [Fact]
    public async Task Companion_token_can_sync_bookshelf_only_for_issuing_user()
    {
        using var ownerClient = factory.CreateCookieClient();
        using var otherClient = factory.CreateCookieClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        await ownerClient.RegisterAsync(
            $"owner-{Guid.NewGuid():N}@example.com",
            "Moonlight!Story42",
            cancellationToken);
        await otherClient.RegisterAsync(
            $"other-{Guid.NewGuid():N}@example.com",
            "Moonlight!Story42",
            cancellationToken);

        using var tokenResponse = await ownerClient.PostWithCsrfAsync(
            "/api/auth/companion-token",
            new { },
            cancellationToken);
        using var tokenBody = JsonDocument.Parse(
            await tokenResponse.Content.ReadAsStreamAsync(cancellationToken));
        var accessToken = tokenBody.RootElement.GetProperty("accessToken").GetString();
        var expiresAt = tokenBody.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        Assert.StartsWith("svc_", accessToken, StringComparison.Ordinal);
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow.AddDays(6), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));

        using var cookieOnlySyncResponse = await ownerClient.PostWithCsrfAsync(
            "/api/books/sources/books-com-tw/import",
            new
            {
                books = new[]
                {
                    new
                    {
                        externalId = $"E{Guid.NewGuid():N}",
                        title = "一般登入不可冒充 Companion",
                        sourceUrl = "https://www.books.com.tw/products/E050145360"
                    }
                }
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, cookieOnlySyncResponse.StatusCode);

        var externalId = $"E{Guid.NewGuid():N}";
        using var syncRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/books/sources/books-com-tw/import")
        {
            Content = JsonContent.Create(new
            {
                books = new[]
                {
                    new
                    {
                        externalId,
                        title = "我的博客來藏書",
                        author = "博客來作者",
                        language = "zh-TW",
                        sourceUrl = $"https://www.books.com.tw/products/{externalId}",
                        coverImageUrl = $"https://im1.book.com.tw/image/getImage?i={externalId}"
                    }
                }
            })
        };
        syncRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var syncResponse = await otherClient.SendAsync(syncRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        using var bearerOnlyClient = factory.CreateCookieClient();
        using var bearerListRequest = new HttpRequestMessage(HttpMethod.Get, "/api/books");
        bearerListRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var bearerListResponse = await bearerOnlyClient.SendAsync(
            bearerListRequest,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, bearerListResponse.StatusCode);

        var ownerBooks = await ownerClient.GetFromJsonAsync<BookDetailsResponse[]>(
            "/api/books",
            cancellationToken);
        var otherBooks = await otherClient.GetFromJsonAsync<BookDetailsResponse[]>(
            "/api/books",
            cancellationToken);
        Assert.Contains(ownerBooks!, book => book.Title == "我的博客來藏書");
        Assert.DoesNotContain(otherBooks!, book => book.Title == "我的博客來藏書");

        using var revokeResponse = await ownerClient.PostWithCsrfAsync(
            "/api/auth/companion-token/revoke",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var revokedSyncRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/books/sources/books-com-tw/import")
        {
            Content = JsonContent.Create(new
            {
                books = new[]
                {
                    new
                    {
                        externalId = $"E{Guid.NewGuid():N}",
                        title = "已撤銷金鑰不可同步",
                        sourceUrl = "https://www.books.com.tw/products/E050145360"
                    }
                }
            })
        };
        revokedSyncRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var revokedSyncResponse = await bearerOnlyClient.SendAsync(revokedSyncRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedSyncResponse.StatusCode);
    }
}
