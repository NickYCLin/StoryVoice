using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Insights;
using StoryVoice.Domain.Books;
using StoryVoice.Infrastructure;
using StoryVoice.Infrastructure.Insights;

namespace StoryVoice.UnitTests;

public sealed class LocalLlmCharacterAnalysisTests
{
    private const string ValidAnalysis = """
        {
          "model":"gpt-oss:20b",
          "done":true,
          "message":{"content":"{\"candidates\":[{\"name\":\"阿明\",\"confidence\":\"high\",\"dialogueEvidenceCount\":2},{\"name\":\"不存在\",\"confidence\":\"high\",\"dialogueEvidenceCount\":9}]}"}
        }
        """;

    [Fact]
    public void Merge_preserves_only_explicit_LLM_confidence_and_aggregates_full_chapter_evidence()
    {
        var result = LocalLlmCharacterAnalysisSource.Merge(
        [
            (1, (IReadOnlyList<LocalLlmCharacterCandidate>)
            [
                new("阿明", "medium", 1),
                new("", "high", 99),
                new("小華", "low", 2),
            ]),
            (2, (IReadOnlyList<LocalLlmCharacterCandidate>)
            [
                new("阿明", "high", 2),
                new("小華", "medium", 1),
            ]),
        ]);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("阿明", first.Name);
                Assert.Equal("high", first.Confidence);
                Assert.Equal(3, first.DialogueEvidenceCount);
                Assert.Equal([1, 2], first.EvidenceChapterNumbers);
            },
            second =>
            {
                Assert.Equal("小華", second.Name);
                Assert.Equal("medium", second.Confidence);
                Assert.Equal(1, second.DialogueEvidenceCount);
                Assert.Equal([2], second.EvidenceChapterNumbers);
            });
    }

    [Fact]
    public async Task Ollama_provider_sends_the_complete_chapter_discards_absent_names_and_confirms_unload()
    {
        var handler = new CapturingHandler(ValidAnalysis);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://local-ollama/") };
        var provider = CreateProvider(client);
        const string chapterText = "阿明說：「你好。」\n小華回答：「再見。」\n完整章節結尾 sentinel。";

        var chapters = await provider.AnalyzeAsync(
            new LocalLlmCharacterAnalysisRequest(
                "test-source-hash",
                [new LocalLlmCharacterAnalysisChapter(3, "第三章", chapterText)]),
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(Assert.Single(chapters).Candidates);
        Assert.Equal("阿明", candidate.Name);
        Assert.Equal("high", candidate.Confidence);
        Assert.Equal(2, candidate.DialogueEvidenceCount);
        using var chatRequest = JsonDocument.Parse(handler.ChatRequestBody);
        var messages = chatRequest.RootElement.GetProperty("messages");
        Assert.Contains(chapterText, messages[1].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Equal("gpt-oss:20b", chatRequest.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, chatRequest.RootElement.GetProperty("options").GetProperty("temperature").GetInt32());
        using var unloadRequest = JsonDocument.Parse(handler.UnloadRequestBody);
        Assert.Equal(string.Empty, unloadRequest.RootElement.GetProperty("prompt").GetString());
        Assert.False(unloadRequest.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(0, unloadRequest.RootElement.GetProperty("keep_alive").GetInt32());
    }

    [Theory]
    [InlineData("{\"done\":true,\"message\":{\"content\":\"{\\\"candidates\\\":null}\"}}")]
    [InlineData("{\"done\":true,\"message\":{\"content\":\"{\\\"candidates\\\":[{\\\"name\\\":\\\"阿明\\\",\\\"confidence\\\":\\\"high\\\",\\\"dialogueEvidenceCount\\\":1,\\\"unexpected\\\":true}]}\"}}")]
    public async Task Ollama_provider_rejects_malformed_or_non_schema_model_output(string response)
    {
        using var client = new HttpClient(new CapturingHandler(response)) { BaseAddress = new Uri("http://local-ollama/") };

        await Assert.ThrowsAsync<LocalLlmCharacterAnalysisUnavailableException>(() =>
            CreateProvider(client).AnalyzeAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ollama_provider_maps_chat_http_failure_to_unavailable()
    {
        using var client = new HttpClient(new CapturingHandler(ValidAnalysis, chatStatus: HttpStatusCode.ServiceUnavailable))
        {
            BaseAddress = new Uri("http://local-ollama/"),
        };

        await Assert.ThrowsAsync<LocalLlmCharacterAnalysisUnavailableException>(() =>
            CreateProvider(client).AnalyzeAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ollama_provider_fails_closed_when_unload_is_not_confirmed()
    {
        using var client = new HttpClient(new CapturingHandler(ValidAnalysis, unloadStatus: HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("http://local-ollama/"),
        };

        await Assert.ThrowsAsync<LocalLlmCharacterAnalysisUnavailableException>(() =>
            CreateProvider(client).AnalyzeAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ollama_provider_rejects_a_response_exceeding_its_byte_cap()
    {
        var oversized = "{\"done\":true,\"message\":{\"content\":\"{\\\"candidates\\\":[]}\"},\"padding\":\"" + new string('x', 2_000) + "\"}";
        using var client = new HttpClient(new CapturingHandler(oversized)) { BaseAddress = new Uri("http://local-ollama/") };

        await Assert.ThrowsAsync<LocalLlmCharacterAnalysisUnavailableException>(() =>
            CreateProvider(client, maximumResponseBytes: 1_024).AnalyzeAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Source_rejects_oversized_full_chapters_instead_of_truncating_them()
    {
        var book = Book.Create(Guid.NewGuid(), "測試", "作者", "zh-TW", "test.txt");
        book.AddChapter(1, "第一章", new string('甲', LocalLlmCharacterAnalysisSource.MaximumChapterCharacters + 1));

        Assert.Throws<LocalLlmCharacterAnalysisInputTooLargeException>(() =>
            LocalLlmCharacterAnalysisSource.Create(book.Chapters));
    }

    [Fact]
    public void Source_hash_changes_when_any_complete_chapter_content_changes()
    {
        var first = Book.Create(Guid.NewGuid(), "測試", "作者", "zh-TW", "test.txt");
        first.AddChapter(1, "第一章", "阿明說：「你好。」");
        var second = Book.Create(Guid.NewGuid(), "測試", "作者", "zh-TW", "test.txt");
        second.AddChapter(1, "第一章", "阿明說：「你好。」結尾已變更。");

        var firstSource = LocalLlmCharacterAnalysisSource.Create(first.Chapters);
        var secondSource = LocalLlmCharacterAnalysisSource.Create(second.Chapters);

        Assert.NotEqual(firstSource.SourceHash, secondSource.SourceHash);
        Assert.Equal("v1-full-chapter-context", LocalLlmCharacterAnalysisSource.PromptVersion);
    }

    [Fact]
    public void Infrastructure_options_reject_any_model_except_approved_20B()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1",
                ["LocalLlmCharacterAnalysis:BaseUrl"] = "http://127.0.0.1:11434/",
                ["LocalLlmCharacterAnalysis:Model"] = "llama3.3:70b",
                ["LocalLlmCharacterAnalysis:TimeoutSeconds"] = "600",
                ["LocalLlmCharacterAnalysis:UnloadTimeoutSeconds"] = "15",
                ["LocalLlmCharacterAnalysis:MaximumResponseBytes"] = "16384",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddStoryVoiceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<LocalLlmCharacterAnalysisOptions>>().Value);

        Assert.Contains("gpt-oss:20b", exception.Message, StringComparison.Ordinal);
    }

    private static OllamaCharacterAnalysisProvider CreateProvider(HttpClient client, int maximumResponseBytes = 16 * 1024) =>
        new(
            client,
            Options.Create(new LocalLlmCharacterAnalysisOptions
            {
                Model = "gpt-oss:20b",
                TimeoutSeconds = 600,
                UnloadTimeoutSeconds = 3,
                MaximumResponseBytes = maximumResponseBytes,
            }));

    private static LocalLlmCharacterAnalysisRequest Request() => new(
        "test-source-hash",
        [new LocalLlmCharacterAnalysisChapter(3, "第三章", "阿明說：「你好。」")]);

    private sealed class CapturingHandler(
        string chatResponse,
        HttpStatusCode chatStatus = HttpStatusCode.OK,
        HttpStatusCode unloadStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string ChatRequestBody { get; private set; } = string.Empty;
        public string UnloadRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri?.AbsolutePath == "/api/chat")
            {
                ChatRequestBody = requestBody;
                return Response(chatStatus, chatResponse);
            }

            Assert.Equal("/api/generate", request.RequestUri?.AbsolutePath);
            UnloadRequestBody = requestBody;
            return Response(unloadStatus, "{\"done\":true}");
        }

        private static HttpResponseMessage Response(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
