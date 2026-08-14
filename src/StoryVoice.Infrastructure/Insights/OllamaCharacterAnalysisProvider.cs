using System.Buffers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Insights;

namespace StoryVoice.Infrastructure.Insights;

/// <summary>
/// Sends bounded complete chapters to a host-local Ollama server. A process-wide gate prevents
/// overlapping batches; every terminal path requires a confirmed unload before it returns.
/// </summary>
public sealed class OllamaCharacterAnalysisProvider(
    HttpClient httpClient,
    IOptions<LocalLlmCharacterAnalysisOptions> options) : ILocalLlmCharacterAnalysisProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonDocumentOptions StrictJsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };
    private const int MaximumCandidatesPerChapter = 30;
    private const int MaximumNameLength = 80;
    private const int MaximumEvidenceCount = 1_000;
    private const int MaximumInnerJsonBytes = 8 * 1024;
    private const string BatchKeepAlive = "10m";
    private static readonly JsonElement ResponseSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "candidates": {
              "type": "array",
              "maxItems": 30,
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string", "minLength": 1, "maxLength": 80 },
                  "confidence": { "type": "string", "enum": ["high", "medium"] },
                  "dialogueEvidenceCount": { "type": "integer", "minimum": 1, "maximum": 1000 },
                  "aliases": {
                    "type": "array",
                    "maxItems": 12,
                    "items": { "type": "string", "minLength": 1, "maxLength": 80 }
                  }
                },
                "required": ["name", "confidence", "dialogueEvidenceCount", "aliases"],
                "additionalProperties": false
              }
            }
          },
          "required": ["candidates"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public string Model => options.Value.Model.Trim();

    public async Task<IReadOnlyList<LocalLlmChapterCharacterAnalysis>> AnalyzeAsync(
        LocalLlmCharacterAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        await LocalOllamaExecutionGate.Gate.WaitAsync(cancellationToken);
        try
        {
            var results = new List<LocalLlmChapterCharacterAnalysis>(request.Chapters.Count);
            foreach (var chapter in request.Chapters)
            {
                var candidates = await AnalyzeChapterCoreAsync(chapter, cancellationToken);
                results.Add(new LocalLlmChapterCharacterAnalysis(chapter.ChapterNumber, candidates));
            }

            return results;
        }
        finally
        {
            try
            {
                await UnloadAsync();
            }
            finally
            {
                LocalOllamaExecutionGate.Gate.Release();
            }
        }
    }

    private async Task<IReadOnlyList<LocalLlmCharacterCandidate>> AnalyzeChapterCoreAsync(
        LocalLlmCharacterAnalysisChapter chapter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chapter.Text))
        {
            return [];
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
            {
                Content = JsonContent.Create(
                    new OllamaChatRequest(
                        Model,
                        Stream: false,
                        options.Value.ReasoningEffort.Trim().ToLowerInvariant(),
                        ResponseSchema,
                        new OllamaGenerationOptions(Temperature: 0, options.Value.NumContext),
                        [
                            new OllamaMessage("system", SystemPrompt),
                            new OllamaMessage("user", BuildChapterPrompt(chapter)),
                        ],
                        BatchKeepAlive),
                    options: SerializerOptions),
            };
            using var response = await SendAsync(request, cancellationToken, preserveCallerCancellation: true);
            if (!response.IsSuccessStatusCode)
            {
                throw new LocalLlmCharacterAnalysisUnavailableException();
            }

            var payload = await ReadBoundedBodyAsync(response.Content, cancellationToken);
            var content = ParseCompletedChatContent(payload);
            if (Encoding.UTF8.GetByteCount(content) > MaximumInnerJsonBytes)
            {
                throw new LocalLlmCharacterAnalysisUnavailableException();
            }

            return ParseCandidates(content)
                .Where(candidate => chapter.Text.Contains(candidate.Name, StringComparison.Ordinal))
                .Select(candidate => candidate with
                {
                    Aliases = (candidate.Aliases ?? [])
                        .Where(alias => chapter.Text.Contains(alias, StringComparison.Ordinal))
                        .Where(alias => !string.Equals(alias, candidate.Name, StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                })
                .ToArray();
        }
        catch (LocalLlmCharacterAnalysisUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new LocalLlmCharacterAnalysisUnavailableException(exception);
        }
    }

    private async Task UnloadAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.UnloadTimeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/generate")
            {
                Content = JsonContent.Create(
                    new OllamaUnloadRequest(Model, Prompt: string.Empty, Stream: false, KeepAlive: 0),
                    options: SerializerOptions),
            };
            using var response = await SendAsync(request, timeout.Token, preserveCallerCancellation: false);
            if (!response.IsSuccessStatusCode)
            {
                throw new LocalLlmCharacterAnalysisUnavailableException();
            }

            ValidateCompletedUnload(await ReadBoundedBodyAsync(response.Content, timeout.Token));
        }
        catch (LocalLlmCharacterAnalysisUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new LocalLlmCharacterAnalysisUnavailableException(exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool preserveCallerCancellation)
    {
        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (preserveCallerCancellation && cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new LocalLlmCharacterAnalysisUnavailableException(exception);
        }
    }

    private async Task<string> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var maximumResponseBytes = options.Value.MaximumResponseBytes;
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumResponseBytes)
        {
            throw new LocalLlmCharacterAnalysisUnavailableException();
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(4_096);
        try
        {
            using var output = new MemoryStream(Math.Min(maximumResponseBytes, 4_096));
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > maximumResponseBytes)
                {
                    throw new LocalLlmCharacterAnalysisUnavailableException();
                }

                output.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ParseCompletedChatContent(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, StrictJsonOptions);
            var root = RequireObject(document.RootElement);
            var done = RequireSingleProperty(root, "done");
            var message = RequireObject(RequireSingleProperty(root, "message"));
            var content = RequireSingleProperty(message, "content");
            if (done.ValueKind != JsonValueKind.True || content.ValueKind != JsonValueKind.String)
            {
                throw new LocalLlmCharacterAnalysisUnavailableException();
            }

            return content.GetString() is { Length: > 0 } value
                ? value
                : throw new LocalLlmCharacterAnalysisUnavailableException();
        }
        catch (JsonException exception)
        {
            throw new LocalLlmCharacterAnalysisUnavailableException(exception);
        }
    }

    private static IReadOnlyList<LocalLlmCharacterCandidate> ParseCandidates(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, StrictJsonOptions);
            var root = RequireObject(document.RootElement);
            RequireExactProperties(root, ["candidates"]);
            var candidates = RequireSingleProperty(root, "candidates");
            if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() > MaximumCandidatesPerChapter)
            {
                throw new LocalLlmCharacterAnalysisUnavailableException();
            }

            var result = new List<LocalLlmCharacterCandidate>(candidates.GetArrayLength());
            foreach (var candidate in candidates.EnumerateArray())
            {
                var candidateObject = RequireObject(candidate);
                RequireExactProperties(candidateObject, ["name", "confidence", "dialogueEvidenceCount", "aliases"]);
                var name = RequireSingleProperty(candidateObject, "name");
                var confidence = RequireSingleProperty(candidateObject, "confidence");
                var evidenceCount = RequireSingleProperty(candidateObject, "dialogueEvidenceCount");
                var aliases = RequireSingleProperty(candidateObject, "aliases");
                if (name.ValueKind != JsonValueKind.String
                    || confidence.ValueKind != JsonValueKind.String
                    || evidenceCount.ValueKind != JsonValueKind.Number
                    || aliases.ValueKind != JsonValueKind.Array
                    || aliases.GetArrayLength() > 12
                    || !evidenceCount.TryGetInt32(out var count))
                {
                    throw new LocalLlmCharacterAnalysisUnavailableException();
                }

                var normalizedName = LocalLlmCharacterAnalysisSource.NormalizeName(name.GetString());
                var normalizedConfidence = LocalLlmCharacterAnalysisSource.NormalizeConfidence(confidence.GetString());
                if (normalizedName.Length is < 1 or > MaximumNameLength
                    || normalizedConfidence is null
                    || count is < 1 or > MaximumEvidenceCount)
                {
                    throw new LocalLlmCharacterAnalysisUnavailableException();
                }

                var normalizedAliases = aliases.EnumerateArray()
                    .Select(alias => alias.ValueKind == JsonValueKind.String
                        ? LocalLlmCharacterAnalysisSource.NormalizeName(alias.GetString())
                        : throw new LocalLlmCharacterAnalysisUnavailableException())
                    .Where(alias => alias.Length is >= 1 and <= MaximumNameLength)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (normalizedAliases.Length != aliases.GetArrayLength())
                {
                    throw new LocalLlmCharacterAnalysisUnavailableException();
                }

                result.Add(new LocalLlmCharacterCandidate(
                    normalizedName,
                    normalizedConfidence,
                    count,
                    normalizedAliases));
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new LocalLlmCharacterAnalysisUnavailableException(exception);
        }
    }

    private static void ValidateCompletedUnload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, StrictJsonOptions);
            var root = RequireObject(document.RootElement);
            if (RequireSingleProperty(root, "done").ValueKind != JsonValueKind.True)
            {
                throw new LocalLlmCharacterAnalysisUnavailableException();
            }
        }
        catch (JsonException exception)
        {
            throw new LocalLlmCharacterAnalysisUnavailableException(exception);
        }
    }

    private static JsonElement RequireObject(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? value
        : throw new LocalLlmCharacterAnalysisUnavailableException();

    private static JsonElement RequireSingleProperty(JsonElement value, string propertyName)
    {
        JsonElement result = default;
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                result = property.Value;
                count++;
            }
        }

        return count == 1 ? result : throw new LocalLlmCharacterAnalysisUnavailableException();
    }

    private static void RequireExactProperties(JsonElement value, IReadOnlyList<string> expected)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!actual.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new LocalLlmCharacterAnalysisUnavailableException();
            }
        }

        if (actual.Count != expected.Count)
        {
            throw new LocalLlmCharacterAnalysisUnavailableException();
        }
    }

    private static bool IsProviderFailure(Exception exception) => exception is HttpRequestException
        or HttpIOException
        or IOException
        or JsonException
        or NotSupportedException
        or TaskCanceledException;

    private static string BuildChapterPrompt(LocalLlmCharacterAnalysisChapter chapter) => $$"""
        請分析下列中文小說的完整單一章節。列出「正文中實際出現名稱」且有明確對白歸屬證據的角色候選。
        你必須讀取完整章節，並運用相鄰對話輪替、敘事觀點、指代、明確發話動詞與上下文判斷。
        name 是角色最適合放進系列角色表的 canonical 名稱；aliases 是正文中逐字出現、確定指向同一角色的稱呼（暱稱、稱號或譯名），沒有就輸出空陣列。
        name 與每個 alias 都必須逐字出現在正文；只有高或中信心才可輸出。不確定、只有代名詞、僅泛稱、或無法可靠歸屬者一律省略。
        dialogueEvidenceCount 是此章內你能支持的對白次數，至少 1。不得建立角色、不得猜測真名、不得輸出推理、引句、摘要或 markdown。

        章節標題：{{chapter.ChapterTitle}}
        正文開始
        {{chapter.Text}}
        正文結束
        """;

    private const string SystemPrompt = "你是只在本機執行的中文小說角色與別名分析器。輸出必須符合 JSON schema，沒有足夠證據就輸出空 candidates。";

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        string Think,
        JsonElement Format,
        OllamaGenerationOptions Options,
        IReadOnlyList<OllamaMessage> Messages,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record OllamaGenerationOptions(
        int Temperature,
        [property: JsonPropertyName("num_ctx")] int NumContext);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed record OllamaUnloadRequest(
        string Model,
        string Prompt,
        bool Stream,
        [property: JsonPropertyName("keep_alive")] int KeepAlive);
}
