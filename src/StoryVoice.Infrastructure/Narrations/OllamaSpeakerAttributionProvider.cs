using System.Buffers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Infrastructure.Insights;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// Sends one bounded chapter plan to host-local Ollama. The schema restricts output identities to
/// the current series cast, and source text is neither logged nor persisted by this provider.
/// </summary>
public sealed class OllamaSpeakerAttributionProvider(
    HttpClient httpClient,
    IOptions<LocalLlmCharacterAnalysisOptions> options) : ISpeakerAttributionProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly JsonDocumentOptions StrictJsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };
    private const int MaximumDialogueSegments = 400;
    private const int MaximumInnerJsonBytes = 14 * 1024;
    private const string KeepAlive = "5m";

    public async Task<IReadOnlyList<SpeakerAttributionResult>> AttributeAsync(
        SpeakerAttributionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dialogueIndexes = request.Segments
            .Where(segment => segment.Kind == SpeechSegmentKind.Dialogue)
            .Select(segment => segment.Index)
            .ToHashSet();
        if (dialogueIndexes.Count == 0 || request.KnownCharacters.Count == 0)
        {
            return [];
        }

        if (dialogueIndexes.Count > MaximumDialogueSegments)
        {
            throw new LocalSpeakerAttributionUnavailableException();
        }

        await LocalOllamaExecutionGate.Gate.WaitAsync(cancellationToken);
        try
        {
            using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat")
            {
                Content = JsonContent.Create(
                    new OllamaChatRequest(
                        options.Value.Model.Trim(),
                        Stream: false,
                        options.Value.ReasoningEffort.Trim().ToLowerInvariant(),
                        BuildResponseSchema(request.KnownCharacters),
                        new OllamaGenerationOptions(Temperature: 0, options.Value.NumContext),
                        [
                            new OllamaMessage("system", SystemPrompt),
                            new OllamaMessage("user", BuildPrompt(request)),
                        ],
                        KeepAlive),
                    options: SerializerOptions),
            };
            using var response = await httpClient.SendAsync(
                chatRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LocalSpeakerAttributionUnavailableException();
            }

            var payload = await ReadBoundedBodyAsync(response.Content, cancellationToken);
            var content = ParseCompletedChatContent(payload);
            if (Encoding.UTF8.GetByteCount(content) > MaximumInnerJsonBytes)
            {
                throw new LocalSpeakerAttributionUnavailableException();
            }

            return ParseResults(content, request, dialogueIndexes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LocalSpeakerAttributionUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or HttpIOException
            or IOException
            or JsonException
            or NotSupportedException
            or TaskCanceledException)
        {
            throw new LocalSpeakerAttributionUnavailableException(exception);
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

    private async Task UnloadAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.UnloadTimeoutSeconds));
        using var unloadRequest = new HttpRequestMessage(HttpMethod.Post, "api/generate")
        {
            Content = JsonContent.Create(
                new OllamaUnloadRequest(
                    options.Value.Model.Trim(),
                    Prompt: string.Empty,
                    Stream: false,
                    KeepAlive: 0),
                options: SerializerOptions),
        };
        using var response = await httpClient.SendAsync(
            unloadRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new LocalSpeakerAttributionUnavailableException();
        }

        var payload = await ReadBoundedBodyAsync(response.Content, timeout.Token);
        using var document = JsonDocument.Parse(payload, StrictJsonOptions);
        if (!document.RootElement.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True)
        {
            throw new LocalSpeakerAttributionUnavailableException();
        }
    }

    private async Task<string> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.MaximumResponseBytes;
        if (content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new LocalSpeakerAttributionUnavailableException();
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(4_096);
        try
        {
            using var output = new MemoryStream(Math.Min(maximumBytes, 4_096));
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > maximumBytes)
                {
                    throw new LocalSpeakerAttributionUnavailableException();
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
        using var document = JsonDocument.Parse(payload, StrictJsonOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("done", out var done)
            || done.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(content.GetString()))
        {
            throw new LocalSpeakerAttributionUnavailableException();
        }

        return content.GetString()!;
    }

    private static IReadOnlyList<SpeakerAttributionResult> ParseResults(
        string content,
        SpeakerAttributionRequest request,
        IReadOnlySet<int> dialogueIndexes)
    {
        using var document = JsonDocument.Parse(content, StrictJsonOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Count() != 1
            || !root.TryGetProperty("attributions", out var attributions)
            || attributions.ValueKind != JsonValueKind.Array
            || attributions.GetArrayLength() > MaximumDialogueSegments)
        {
            throw new LocalSpeakerAttributionUnavailableException();
        }

        var knownIds = request.KnownCharacters.Select(character => character.CharacterId).ToHashSet();
        var covered = new HashSet<int>();
        var results = new List<SpeakerAttributionResult>(attributions.GetArrayLength());
        foreach (var item in attributions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || item.EnumerateObject().Count() != 3
                || !item.TryGetProperty("segmentIndex", out var indexValue)
                || !indexValue.TryGetInt32(out var index)
                || !dialogueIndexes.Contains(index)
                || !covered.Add(index)
                || !item.TryGetProperty("characterId", out var characterValue)
                || characterValue.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("confidence", out var confidenceValue)
                || !confidenceValue.TryGetInt32(out var confidence)
                || confidence is < 0 or > 100)
            {
                throw new LocalSpeakerAttributionUnavailableException();
            }

            var characterText = characterValue.GetString();
            Guid? characterId = string.IsNullOrEmpty(characterText)
                ? null
                : Guid.TryParse(characterText, out var parsed) && knownIds.Contains(parsed)
                    ? parsed
                    : throw new LocalSpeakerAttributionUnavailableException();
            var outcome = characterId is null || confidence < 45
                ? SpeakerAttributionOutcome.Unknown
                : confidence >= 85
                    ? SpeakerAttributionOutcome.Confirmed
                    : SpeakerAttributionOutcome.Suggested;
            results.Add(new SpeakerAttributionResult(
                index,
                outcome == SpeakerAttributionOutcome.Unknown ? null : characterId,
                outcome,
                outcome == SpeakerAttributionOutcome.Unknown ? 0 : confidence,
                SpeakerAttributionDecisionSource.LocalModel,
                outcome == SpeakerAttributionOutcome.Confirmed
                    ? "local_model_high_confidence"
                    : outcome == SpeakerAttributionOutcome.Suggested
                        ? "local_model_review_suggestion"
                        : "local_model_unknown"));
        }

        return results;
    }

    private static JsonElement BuildResponseSchema(IReadOnlyList<KnownCharacterIdentity> characters) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                attributions = new
                {
                    type = "array",
                    maxItems = MaximumDialogueSegments,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            segmentIndex = new { type = "integer" },
                            characterId = new
                            {
                                type = "string",
                                @enum = new[] { string.Empty }
                                    .Concat(characters.Select(character => character.CharacterId.ToString()))
                                    .ToArray(),
                            },
                            confidence = new { type = "integer", minimum = 0, maximum = 100 },
                        },
                        required = new[] { "segmentIndex", "characterId", "confidence" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "attributions" },
            additionalProperties = false,
        }, SerializerOptions);

    private static string BuildPrompt(SpeakerAttributionRequest request)
    {
        var cast = request.KnownCharacters.Select(character => new
        {
            characterId = character.CharacterId,
            canonicalName = character.NormalizedCanonicalName,
            aliases = character.NormalizedAliases,
            isPointOfViewCharacter = request.PointOfViewCharacterId == character.CharacterId,
        });
        var segments = request.Segments.Select(segment => new
        {
            segmentIndex = segment.Index,
            kind = segment.Kind.ToString(),
            text = segment.Text,
        });
        return $$"""
            請判斷下列中文小說章節中每一個 Dialogue 片段的說話者。
            只能使用 cast 內提供的 characterId；無法可靠判斷時 characterId 輸出空字串且 confidence 輸出 0。
            請利用相鄰敘述、發話動詞、對話輪替、別名、指代與第一人稱視角判斷。
            每個 Dialogue segmentIndex 最多輸出一次。不得建立新角色，不得輸出姓名、正文、推理、摘要或 markdown。

            cast={{JsonSerializer.Serialize(cast, SerializerOptions)}}
            segments={{JsonSerializer.Serialize(segments, SerializerOptions)}}
            """;
    }

    private const string SystemPrompt = "你是只在本機執行的中文小說逐句說話者分析器。嚴格遵守 JSON schema，只能從允許的角色 ID 中選擇。";

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

public sealed class LocalSpeakerAttributionUnavailableException : Exception
{
    public LocalSpeakerAttributionUnavailableException()
        : base("本機 LLM 說話者分析暫時無法使用。")
    {
    }

    public LocalSpeakerAttributionUnavailableException(Exception innerException)
        : base("本機 LLM 說話者分析暫時無法使用。", innerException)
    {
    }
}
