using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Infrastructure.Narrations;

internal static class CloneConsentReceiptValidator
{
    internal const int MaximumReceiptBytes = 32 * 1024;
    private static readonly TimeSpan TaipeiUtcOffset = TimeSpan.FromHours(8);

    private static readonly IReadOnlySet<string> AllowedProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "schema",
        "recorderName",
        "recordingDate",
        "consentSignedDate",
        "consentType",
        "usageScopes",
        "recordingSha256",
        "expectedTranscriptCanonicalSha256",
        "consentSha256",
        "subjectAttestationVersion",
        "generatedAtUtc",
    };

    internal static async Task<CharacterVoiceConsentEvidence> ValidateAsync(
        Stream receiptStream,
        string receiptFileName,
        bool rightsAttested,
        string referenceAudioSha256,
        string expectedTranscript,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiptStream);
        if (!rightsAttested)
        {
            throw new ArgumentException("必須明確確認已取得這份聲音克隆授權。", nameof(rightsAttested));
        }

        if (!string.Equals(Path.GetExtension(receiptFileName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("授權 receipt 必須是 JSON 檔案。", nameof(receiptFileName));
        }

        var bytes = await ReadBoundedAsync(receiptStream, cancellationToken);
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
        {
            throw new ArgumentException("授權 receipt 必須是無 BOM 的 UTF-8 JSON。", nameof(receiptStream));
        }

        string json;
        try
        {
            json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("授權 receipt 不是有效的 UTF-8 JSON。", nameof(receiptStream), exception);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("授權 receipt 不是有效的 UTF-8 JSON。", nameof(receiptStream), exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("授權 receipt 根節點必須是 JSON object。", nameof(receiptStream));
            }

            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!AllowedProperties.Contains(property.Name))
                {
                    throw new ArgumentException("授權 receipt 包含不受支援的欄位。", nameof(receiptStream));
                }

                if (!properties.TryAdd(property.Name, property.Value))
                {
                    throw new ArgumentException("授權 receipt 不可包含重複欄位。", nameof(receiptStream));
                }
            }

            if (properties.Count != AllowedProperties.Count
                || AllowedProperties.Any(property => !properties.ContainsKey(property)))
            {
                throw new ArgumentException("授權 receipt 缺少必要欄位。", nameof(receiptStream));
            }

            var schema = GetRequiredString(properties, "schema");
            var recorderName = GetRequiredString(properties, "recorderName");
            var recordingDate = ParseDate(GetRequiredString(properties, "recordingDate"), "錄音日期");
            var consentSignedDate = ParseDate(GetRequiredString(properties, "consentSignedDate"), "同意簽署日期");
            var consentType = GetRequiredString(properties, "consentType");
            var usageScopes = ParseScopes(properties["usageScopes"]);
            var receiptRecordingSha256 = GetRequiredString(properties, "recordingSha256");
            var receiptTranscriptSha256 = GetRequiredString(properties, "expectedTranscriptCanonicalSha256");
            var consentSha256 = GetRequiredString(properties, "consentSha256");
            var attestationVersion = GetRequiredString(properties, "subjectAttestationVersion");
            var generatedAt = ParseTimestamp(GetRequiredString(properties, "generatedAtUtc"));
            if (generatedAt > now.AddMinutes(5))
            {
                throw new ArgumentException("授權 receipt 產生時間不可在未來。", nameof(receiptStream));
            }

            if (!IsSha256(receiptRecordingSha256)
                || !string.Equals(receiptRecordingSha256, referenceAudioSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException("授權 receipt 與參考錄音不一致。", nameof(receiptStream));
            }

            var expectedTranscriptSha256 = CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(expectedTranscript);
            if (!IsSha256(receiptTranscriptSha256)
                || !string.Equals(receiptTranscriptSha256, expectedTranscriptSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException("授權 receipt 與預期逐字稿不一致。", nameof(receiptStream));
            }

            if (!IsSha256(consentSha256))
            {
                throw new ArgumentException("授權文件雜湊格式無效。", nameof(receiptStream));
            }

            var receiptSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            // Consent dates are calendar dates in StoryVoice's recording locale. Derive the
            // boundary from the injected clock at UTC+08:00 so Windows/Linux hosts agree around
            // UTC midnight without accepting tomorrow in Taiwan.
            var taipeiToday = DateOnly.FromDateTime(now.ToOffset(TaipeiUtcOffset).DateTime);
            return CharacterVoiceConsentEvidence.Create(
                recorderName,
                recordingDate,
                consentSignedDate,
                consentType,
                usageScopes,
                consentSha256,
                receiptSha256,
                expectedTranscriptSha256,
                schema,
                attestationVersion,
                taipeiToday);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > MaximumReceiptBytes)
            {
                throw new ArgumentException("授權 receipt 不可超過 32 KiB。", nameof(stream));
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("授權 receipt 不可為空白。", nameof(stream));
        }

        return content.ToArray();
    }

    private static string GetRequiredString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        var value = properties[name];
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException("授權 receipt 欄位型別或內容無效。", name);
        }

        return value.GetString()!;
    }

    private static IReadOnlyList<string> ParseScopes(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("usageScopes 必須是 JSON array。", nameof(value));
        }

        var scopes = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
                || !CharacterVoiceConsentScopes.All.Contains(item.GetString()!))
            {
                throw new ArgumentException("授權 receipt 包含不受支援的使用範圍。", nameof(value));
            }

            if (scopes.Contains(item.GetString()!, StringComparer.Ordinal))
            {
                throw new ArgumentException("授權 receipt 不可包含重複使用範圍。", nameof(value));
            }

            scopes.Add(item.GetString()!);
        }

        return scopes;
    }

    private static DateOnly ParseDate(string value, string label)
    {
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new ArgumentException($"{label}必須是 YYYY-MM-DD。", nameof(value));
        }

        return parsed;
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
            || parsed.Offset != TimeSpan.Zero
            || !string.Equals(
                value,
                parsed.ToString("O", CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "receipt 產生時間必須是嚴格 UTC O 格式（7 位小數與 +00:00）。",
                nameof(value));
        }

        return parsed;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(character =>
            (character >= '0' && character <= '9')
            || (character >= 'a' && character <= 'f'));
}
