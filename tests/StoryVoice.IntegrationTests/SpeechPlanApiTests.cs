using System.Net;
using System.Net.Http.Json;
using StoryVoice.Application.Books;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Application.Series;

namespace StoryVoice.IntegrationTests;

public sealed class SpeechPlanApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Speech_plan_endpoints_require_authentication_and_mutations_require_csrf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = factory.CreateClient();
        var seriesId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        using var anonymousResponse = await anonymousClient.GetAsync(
            $"/api/series/{seriesId}/books/{bookId}/chapters/{chapterId}/speech-plan",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var authenticatedClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var missingCsrfResponse = await authenticatedClient.PostAsJsonAsync(
            $"/api/series/{seriesId}/books/{bookId}/chapters/{chapterId}/speech-plan",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrfResponse.StatusCode);
    }

    [Fact]
    public async Task Building_a_draft_auto_confirms_an_exact_reporting_clause_and_the_whole_plan_can_be_confirmed()
    {
        const string chapterTitle = "序章";
        const string chapterBody = "「你回來了？」艾莉絲說。她轉過身，慢慢地走向窗邊。";
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var series = await CreateSeriesAsync(client, "劇本審核測試系列", cancellationToken);
        var book = await CreateBookAsync(client, chapterTitle, chapterBody, cancellationToken);
        await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books",
            new { bookId = book.Id, volumeLabel = "第一冊", sortOrder = 1 },
            cancellationToken);
        var character = await AddCharacterAsync(client, series.Id, "艾莉絲", cancellationToken);
        var chapterId = book.Chapters.Single().Id;

        using var buildResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapterId}/speech-plan",
            new { },
            cancellationToken);
        Assert.True(
            buildResponse.StatusCode == HttpStatusCode.OK,
            $"Unexpected response: {await buildResponse.Content.ReadAsStringAsync(cancellationToken)}");
        var draft = await buildResponse.Content.ReadFromJsonAsync<ChapterSpeechPlanDraftResponse>(cancellationToken);
        Assert.NotNull(draft);
        Assert.True(draft.Segments.Count >= 2);
        Assert.Equal("ChapterTitle", draft.Segments[0].SourceKind);
        Assert.Equal("Narrator", draft.Segments[0].Kind);
        Assert.Equal("Confirmed", draft.Segments[0].ReviewStatus);

        var dialogueSegment = Assert.Single(draft.Segments, segment => segment.Kind == "Dialogue");
        Assert.Equal(character.Id, dialogueSegment.CharacterId);
        Assert.Equal("Confirmed", dialogueSegment.ReviewStatus);
        Assert.Equal("Rule", dialogueSegment.DecisionSource);
        Assert.True(dialogueSegment.Confidence >= 90);
        Assert.Equal("ReadyToConfirm", draft.Status);

        using var getResponse = await client.GetAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapterId}/speech-plan",
            cancellationToken);
        var responseText = await getResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.DoesNotContain(chapterBody, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain("你回來了", responseText, StringComparison.Ordinal);

        using var confirmResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/speech-plan-drafts/{draft.Id}/confirm",
            new { },
            cancellationToken);
        var revision = await confirmResponse.Content
            .ReadFromJsonAsync<ConfirmedSpeechPlanRevisionResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.NotNull(revision);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(draft.Segments.Count, revision.SegmentCount);
        Assert.False(string.IsNullOrWhiteSpace(revision.Fingerprint));
    }

    [Fact]
    public async Task Ambiguous_dialogue_needs_manual_confirmation_before_the_plan_can_be_confirmed()
    {
        const string chapterTitle = "第一章";
        const string chapterBody = "他站在門口。「快走。」她低聲說了什麼。";
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var series = await CreateSeriesAsync(client, "手動審核系列", cancellationToken);
        var book = await CreateBookAsync(client, chapterTitle, chapterBody, cancellationToken);
        await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books",
            new { bookId = book.Id, volumeLabel = "第一冊", sortOrder = 1 },
            cancellationToken);
        var character = await AddCharacterAsync(client, series.Id, "鮑伯", cancellationToken);
        var chapterId = book.Chapters.Single().Id;

        using var buildResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapterId}/speech-plan",
            new { },
            cancellationToken);
        var draft = await buildResponse.Content.ReadFromJsonAsync<ChapterSpeechPlanDraftResponse>(cancellationToken);
        Assert.NotNull(draft);
        var dialogueSegment = Assert.Single(draft.Segments, segment => segment.Kind == "Dialogue");
        Assert.Equal("Suggested", dialogueSegment.ReviewStatus);
        Assert.Null(dialogueSegment.CharacterId);
        Assert.Equal("NeedsReview", draft.Status);

        using var prematureConfirmResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/speech-plan-drafts/{draft.Id}/confirm",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, prematureConfirmResponse.StatusCode);

        using var confirmSegmentRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/series/{series.Id}/speech-plan-drafts/{draft.Id}/segments/{dialogueSegment.Id}/confirm")
        {
            Content = JsonContent.Create(new ConfirmSpeechSegmentRequest(character.Id)),
        };
        using var confirmSegmentResponse = await client.SendWithCsrfAsync(confirmSegmentRequest, cancellationToken);
        var updatedDraft = await confirmSegmentResponse.Content
            .ReadFromJsonAsync<ChapterSpeechPlanDraftResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmSegmentResponse.StatusCode);
        Assert.NotNull(updatedDraft);
        Assert.Equal("ReadyToConfirm", updatedDraft.Status);
        var updatedSegment = Assert.Single(updatedDraft.Segments, segment => segment.Kind == "Dialogue");
        Assert.Equal(character.Id, updatedSegment.CharacterId);
        Assert.Equal("Confirmed", updatedSegment.ReviewStatus);
        Assert.Equal("User", updatedSegment.DecisionSource);

        using var confirmResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/speech-plan-drafts/{draft.Id}/confirm",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
    }

    [Fact]
    public async Task Written_title_is_confirmed_as_point_of_view_inner_monologue_not_dialogue()
    {
        const string chapterTitle = "第一章";
        const string chapterBody = "最厚的一迭有用活頁夾子整理起來，叫做『新生入學介紹與如何自保』。我繼續往下看。";
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var series = await CreateSeriesAsync(client, "內心默讀測試系列", cancellationToken);
        var book = await CreateBookAsync(client, chapterTitle, chapterBody, cancellationToken);
        await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books",
            new { bookId = book.Id, volumeLabel = "第一冊", sortOrder = 1 },
            cancellationToken);
        var pointOfViewCharacter = await AddCharacterAsync(client, series.Id, "主角", cancellationToken);
        var chapterId = book.Chapters.Single().Id;
        using var initialBuildResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapterId}/speech-plan",
            new { },
            cancellationToken);
        var initialDraft = await initialBuildResponse.Content.ReadFromJsonAsync<ChapterSpeechPlanDraftResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialBuildResponse.StatusCode);
        Assert.NotNull(initialDraft);
        Assert.DoesNotContain(initialDraft.Segments, segment => segment.Kind == "InnerMonologue");
        Assert.Contains(
            initialDraft.Segments,
            segment => segment.Kind == "Narrator" && segment.StartOffset == chapterBody.IndexOf('『'));
        using var initialConfirmResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/speech-plan-drafts/{initialDraft.Id}/confirm",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialConfirmResponse.StatusCode);

        using var pointOfViewResponse = await client.PutWithCsrfAsync(
            $"/api/series/{series.Id}/point-of-view-character",
            new SetSeriesPointOfViewCharacterRequest(pointOfViewCharacter.Id),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, pointOfViewResponse.StatusCode);

        using var buildResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapterId}/speech-plan",
            new { },
            cancellationToken);
        var draft = await buildResponse.Content.ReadFromJsonAsync<ChapterSpeechPlanDraftResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, buildResponse.StatusCode);
        Assert.NotNull(draft);
        Assert.Equal(initialDraft.PlanVersion + 1, draft.PlanVersion);
        Assert.Null(draft.ConfirmedRevisionId);
        var innerMonologue = Assert.Single(draft.Segments, segment => segment.Kind == "InnerMonologue");
        Assert.Equal(pointOfViewCharacter.Id, innerMonologue.CharacterId);
        Assert.Equal(pointOfViewCharacter.CanonicalName, innerMonologue.CharacterName);
        Assert.Equal(100, innerMonologue.Confidence);
        Assert.Equal("Rule", innerMonologue.DecisionSource);
        Assert.Equal("Confirmed", innerMonologue.ReviewStatus);
        Assert.DoesNotContain(draft.Segments, segment => segment.Kind == "Dialogue");
        Assert.Equal("ReadyToConfirm", draft.Status);
    }

    [Fact]
    public async Task Non_owner_and_non_member_book_cannot_touch_the_speech_plan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var strangerClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);

        var series = await CreateSeriesAsync(ownerClient, "隔離測試系列", cancellationToken);
        var book = await CreateBookAsync(ownerClient, "章名", "「嗨。」他說。", cancellationToken);
        var chapterId = book.Chapters.Single().Id;

        using var notMemberResponse = await ownerClient.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapterId}/speech-plan",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, notMemberResponse.StatusCode);

        await ownerClient.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books",
            new { bookId = book.Id, volumeLabel = "第一冊", sortOrder = 1 },
            cancellationToken);

        using var strangerResponse = await strangerClient.PostWithCsrfAsync(
            $"/api/series/{series.Id}/books/{book.Id}/chapters/{chapterId}/speech-plan",
            new { },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
    }

    private static async Task<StorySeriesDetailsResponse> CreateSeriesAsync(
        HttpClient client,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            "/api/series",
            new
            {
                name,
                narratorProvider = "edge",
                narratorVoice = "zh-TW-YunJheNeural",
                narratorRate = "-5%",
                narratorPitch = "+0Hz",
                narratorVolume = "+0%",
                defaultSpeakerPauseMs = 350,
            },
            cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<StorySeriesDetailsResponse>(created);
    }

    private static async Task<StorySeriesCharacterResponse> AddCharacterAsync(
        HttpClient client,
        Guid seriesId,
        string canonicalName,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters",
            new
            {
                canonicalName,
                role = "Main",
                voiceProvider = "edge",
                voice = "zh-TW-HsiaoChenNeural",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null,
            },
            cancellationToken);
        var details = await response.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        return details.Characters.Single(candidate => candidate.CanonicalName == canonicalName);
    }

    private static async Task<BookDetailsResponse> CreateBookAsync(
        HttpClient client,
        string chapterTitle,
        string chapterBody,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            "/api/books",
            new CreateBookRequest(
                $"Synthetic book {Guid.NewGuid():N}",
                "Synthetic author",
                "zh-TW",
                "synthetic.txt",
                [new CreateChapterRequest(1, chapterTitle, chapterBody)]),
            cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<BookDetailsResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<BookDetailsResponse>(created);
    }
}
