using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Infrastructure.Insights;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class OllamaSpeakerAttributionTests
{
    [Fact]
    public async Task Provider_constrains_schema_to_known_ids_and_confirms_only_high_confidence()
    {
        var characterId = Guid.NewGuid();
        var content = JsonSerializer.Serialize(new
        {
            attributions = new[]
            {
                new { segmentIndex = 1, characterId = characterId.ToString(), confidence = 91 },
                new { segmentIndex = 3, characterId = characterId.ToString(), confidence = 72 },
            },
        });
        var handler = new CapturingHandler(ChatResponse(content));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://local-ollama/") };
        var provider = CreateProvider(client);
        var request = Request(characterId);

        var results = await provider.AttributeAsync(request, TestContext.Current.CancellationToken);

        Assert.Collection(
            results.OrderBy(result => result.SegmentIndex),
            high =>
            {
                Assert.Equal(SpeakerAttributionOutcome.Confirmed, high.Outcome);
                Assert.Equal(SpeakerAttributionDecisionSource.LocalModel, high.Source);
                Assert.Equal(characterId, high.CharacterId);
            },
            medium =>
            {
                Assert.Equal(SpeakerAttributionOutcome.Suggested, medium.Outcome);
                Assert.Equal(72, medium.Confidence);
            });
        using var chatRequest = JsonDocument.Parse(handler.ChatRequestBody);
        var schema = chatRequest.RootElement.GetProperty("format");
        var allowedIds = schema.GetProperty("properties")
            .GetProperty("attributions")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("characterId")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        Assert.Contains(string.Empty, allowedIds);
        Assert.Contains(characterId.ToString(), allowedIds);
        Assert.Contains("第一句", chatRequest.RootElement.GetProperty("messages")[1].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(handler.UnloadRequestBody));
    }

    [Fact]
    public async Task Provider_rejects_an_identity_outside_the_current_series_cast()
    {
        var content = JsonSerializer.Serialize(new
        {
            attributions = new[]
            {
                new { segmentIndex = 1, characterId = Guid.NewGuid().ToString(), confidence = 99 },
            },
        });
        using var client = new HttpClient(new CapturingHandler(ChatResponse(content)))
        {
            BaseAddress = new Uri("http://local-ollama/"),
        };

        await Assert.ThrowsAsync<LocalSpeakerAttributionUnavailableException>(() =>
            CreateProvider(client).AttributeAsync(Request(Guid.NewGuid()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Hybrid_preserves_confirmed_rules_and_uses_model_for_unresolved_turns()
    {
        var ruleCharacter = Guid.NewGuid();
        var modelCharacter = Guid.NewGuid();
        var request = new SpeakerAttributionRequest(
            [
                new KnownCharacterIdentity(ruleCharacter, "規則角色", []),
                new KnownCharacterIdentity(modelCharacter, "模型角色", []),
            ],
            [
                new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Dialogue, "規則已確認"),
                new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Dialogue, "需要模型"),
            ]);
        var rules = new StubProvider([
            new(1, ruleCharacter, SpeakerAttributionOutcome.Confirmed, 92, SpeakerAttributionDecisionSource.Rule, "exact"),
            new(2, null, SpeakerAttributionOutcome.Unknown, 0, SpeakerAttributionDecisionSource.Rule, "unknown"),
        ]);
        var model = new StubProvider([
            new(1, modelCharacter, SpeakerAttributionOutcome.Confirmed, 99, SpeakerAttributionDecisionSource.LocalModel, "model"),
            new(2, modelCharacter, SpeakerAttributionOutcome.Confirmed, 90, SpeakerAttributionDecisionSource.LocalModel, "model"),
        ]);

        var results = await new HybridSpeakerAttributionProvider(rules, model)
            .AttributeAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(ruleCharacter, results.Single(result => result.SegmentIndex == 1).CharacterId);
        Assert.Equal(SpeakerAttributionDecisionSource.Rule, results.Single(result => result.SegmentIndex == 1).Source);
        Assert.Equal(modelCharacter, results.Single(result => result.SegmentIndex == 2).CharacterId);
        Assert.Equal(SpeakerAttributionDecisionSource.LocalModel, results.Single(result => result.SegmentIndex == 2).Source);
    }

    private static OllamaSpeakerAttributionProvider CreateProvider(HttpClient client) => new(
        client,
        Options.Create(new LocalLlmCharacterAnalysisOptions
        {
            Model = "gpt-oss:20b",
            ReasoningEffort = "low",
            NumContext = 16_384,
            TimeoutSeconds = 600,
            UnloadTimeoutSeconds = 3,
            MaximumResponseBytes = 16 * 1024,
        }));

    private static SpeakerAttributionRequest Request(Guid characterId) => new(
        [new KnownCharacterIdentity(characterId, "阿明", ["小明"])],
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Narrator, "阿明說："),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Dialogue, "第一句"),
            new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Narrator, "他停了一下。"),
            new SpeechSegmentAttributionInput(3, SpeechSegmentKind.Dialogue, "第二句"),
        ]);

    private static string ChatResponse(string content) => JsonSerializer.Serialize(new
    {
        model = "gpt-oss:20b",
        done = true,
        message = new { content },
    });

    private sealed class CapturingHandler(string chatResponse) : HttpMessageHandler
    {
        public string ChatRequestBody { get; private set; } = string.Empty;
        public string UnloadRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri?.AbsolutePath == "/api/chat")
            {
                ChatRequestBody = body;
                return Response(chatResponse);
            }

            UnloadRequestBody = body;
            return Response("{\"done\":true}");
        }

        private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubProvider(IReadOnlyList<SpeakerAttributionResult> results)
        : ISpeakerAttributionProvider
    {
        public Task<IReadOnlyList<SpeakerAttributionResult>> AttributeAsync(
            SpeakerAttributionRequest request,
            CancellationToken cancellationToken) => Task.FromResult(results);
    }
}
