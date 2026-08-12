using System.Net;
using System.Net.Http.Json;
using StoryVoice.Application.Books;
using StoryVoice.Application.Series;

namespace StoryVoice.IntegrationTests;

public sealed class SeriesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Series_endpoints_require_authentication_and_mutations_require_csrf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync(
            "/api/series",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var authenticatedClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var missingCsrfResponse = await authenticatedClient.PostAsJsonAsync(
            "/api/series",
            CreateSeriesBody("缺少 CSRF 的系列"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrfResponse.StatusCode);
    }

    [Fact]
    public async Task Series_queries_are_owner_scoped_and_duplicate_names_are_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerAClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var ownerBClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var created = await CreateSeriesAsync(ownerAClient, "  公開測試系列  ", cancellationToken);

        using var listResponse = await ownerAClient.GetAsync("/api/series", cancellationToken);
        var ownerAList = await listResponse.Content.ReadFromJsonAsync<StorySeriesSummaryResponse[]>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(ownerAList);
        Assert.Contains(ownerAList, series => series.Id == created.Id && series.Name == "公開測試系列");

        using var voiceResponse = await ownerAClient.GetAsync(
            "/api/series/voice-options",
            cancellationToken);
        var voices = await voiceResponse.Content.ReadFromJsonAsync<SeriesVoiceOptionResponse[]>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, voiceResponse.StatusCode);
        Assert.NotNull(voices);
        var expectedVoices = new[]
        {
            "zh-TW-HsiaoChenNeural",
            "zh-TW-YunJheNeural",
            "zh-TW-HsiaoYuNeural",
            "zh-CN-YunxiNeural",
            "zh-CN-YunjianNeural",
            "zh-CN-XiaoxiaoNeural",
            "zh-CN-XiaoyiNeural"
        };
        Assert.All(expectedVoices, expectedVoice =>
            Assert.Contains(voices, voice => voice.Voice == expectedVoice));

        using var ownerBListResponse = await ownerBClient.GetAsync("/api/series", cancellationToken);
        var ownerBList = await ownerBListResponse.Content.ReadFromJsonAsync<StorySeriesSummaryResponse[]>(
            cancellationToken);
        Assert.NotNull(ownerBList);
        Assert.Empty(ownerBList);

        using var ownerBGetResponse = await ownerBClient.GetAsync(
            $"/api/series/{created.Id}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, ownerBGetResponse.StatusCode);

        using var duplicateResponse = await ownerAClient.PostWithCsrfAsync(
            "/api/series",
            CreateSeriesBody("公開測試系列"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Owner_can_manage_membership_cast_and_alias_without_exposing_book_text()
    {
        const string privateTextSentinel = "PRIVATE_TEXT_MUST_NOT_APPEAR_IN_SERIES_RESPONSE";
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var book = await CreateBookAsync(client, privateTextSentinel, cancellationToken);
        var series = await CreateSeriesAsync(client, "角色配音系列", cancellationToken);

        using var addBookResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books",
            new
            {
                bookId = book.Id,
                volumeLabel = "第一冊",
                sortOrder = 1
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, addBookResponse.StatusCode);

        using var addCharacterResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters",
            new
            {
                canonicalName = "艾莉絲",
                role = "Supporting",
                voiceProvider = "edge",
                voice = "zh-TW-HsiaoChenNeural",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = "公開的角色配音提示"
            },
            cancellationToken);
        var withCharacter = await addCharacterResponse.Content
            .ReadFromJsonAsync<StorySeriesDetailsResponse>(cancellationToken);
        Assert.True(
            addCharacterResponse.StatusCode == HttpStatusCode.OK,
            $"Unexpected response: {await addCharacterResponse.Content.ReadAsStringAsync(cancellationToken)}");
        Assert.NotNull(withCharacter);
        var character = Assert.Single(withCharacter.Characters);

        using var addAliasResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters/{character.Id}/aliases",
            new { alias = "隊長" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, addAliasResponse.StatusCode);

        using var duplicateAliasResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters/{character.Id}/aliases",
            new { alias = " 隊長 " },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateAliasResponse.StatusCode);

        using var disallowedVoiceResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters",
            new
            {
                canonicalName = "未知聲線角色",
                role = "Minor",
                voiceProvider = "untrusted-provider",
                voice = "private-voice-id",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, disallowedVoiceResponse.StatusCode);

        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/series/{series.Id}/characters/{character.Id}")
        {
            Content = JsonContent.Create(new
            {
                canonicalName = "艾莉絲隊長",
                role = "Main",
                voiceProvider = "edge",
                voice = "zh-TW-HsiaoYuNeural",
                rate = "-5%",
                pitch = "+2Hz",
                volume = "-3%",
                notes = "使用者確認的固定聲線"
            })
        };
        using var updateResponse = await client.SendWithCsrfAsync(
            updateRequest,
            cancellationToken);
        var updated = await updateResponse.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.NotNull(updated);
        var updatedCharacter = Assert.Single(updated.Characters);
        Assert.Equal("艾莉絲隊長", updatedCharacter.CanonicalName);
        Assert.Equal("Main", updatedCharacter.Role);
        Assert.Equal("zh-TW-HsiaoYuNeural", updatedCharacter.Voice);
        Assert.Equal("隊長", Assert.Single(updatedCharacter.Aliases).Value);

        using var getResponse = await client.GetAsync($"/api/series/{series.Id}", cancellationToken);
        var responseText = await getResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.DoesNotContain(privateTextSentinel, responseText, StringComparison.Ordinal);
        Assert.Contains("角色配音系列", responseText, StringComparison.Ordinal);
        Assert.Contains("第一冊", responseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_owner_cannot_attach_books_or_mutate_another_users_series()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerAClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var ownerBClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var ownerASeries = await CreateSeriesAsync(ownerAClient, "擁有者 A 系列", cancellationToken);
        var ownerBBook = await CreateBookAsync(ownerBClient, "Synthetic public text", cancellationToken);

        using var ownerAAddsForeignBookResponse = await ownerAClient.PostWithCsrfAsync(
            $"/api/series/{ownerASeries.Id}/books",
            new { bookId = ownerBBook.Id, volumeLabel = "外部冊次", sortOrder = 1 },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, ownerAAddsForeignBookResponse.StatusCode);

        using var ownerBMutatesForeignSeriesResponse = await ownerBClient.PostWithCsrfAsync(
            $"/api/series/{ownerASeries.Id}/characters",
            new
            {
                canonicalName = "越權角色",
                role = "Main",
                voiceProvider = "edge",
                voice = "zh-TW-YunJheNeural",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, ownerBMutatesForeignSeriesResponse.StatusCode);
    }

    private static object CreateSeriesBody(string name) => new
    {
        name,
        narratorProvider = "edge",
        narratorVoice = "zh-TW-YunJheNeural",
        narratorRate = "-5%",
        narratorPitch = "+0Hz",
        narratorVolume = "+0%",
        defaultSpeakerPauseMs = 350
    };

    private static async Task<StorySeriesDetailsResponse> CreateSeriesAsync(
        HttpClient client,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            "/api/series",
            CreateSeriesBody(name),
            cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<StorySeriesDetailsResponse>(created);
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
