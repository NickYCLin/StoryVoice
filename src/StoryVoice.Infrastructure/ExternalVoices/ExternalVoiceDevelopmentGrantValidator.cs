using System.Globalization;
using System.Text;
using System.Text.Json;
using StoryVoice.Infrastructure.VoiceCatalog;

namespace StoryVoice.Infrastructure.ExternalVoices;

internal sealed record ExternalVoiceDevelopmentGrantEvidence(
    Guid CharacterProfileId,
    string ReferenceAudioSha256,
    string TranscriptCanonicalSha256,
    DateTimeOffset EffectiveAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal static class ExternalVoiceDevelopmentGrantValidator
{
    public const string CurrentSchema = "voice-api-synthetic-development-grant/v1";
    public const string RequiredOrigin =
        "owner-created-synthetic-no-human-source-no-identifiable-imitation";
    public const int MaximumBytes = 32 * 1024;
    public static readonly TimeSpan MaximumGrantDuration = TimeSpan.FromDays(30);

    private static readonly IReadOnlySet<string> RequiredProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "schema",
            "grantId",
            "consumerKeyId",
            "ownerId",
            "voiceAlias",
            "characterProfileId",
            "referenceAudioSha256",
            "expectedTranscriptCanonicalSha256",
            "projectId",
            "effectiveAtUtc",
            "expiresAtUtc",
            "revokedAtUtc",
            "origin",
        };

    public static ExternalVoiceDevelopmentGrantEvidence Validate(
        ReadOnlySpan<byte> content,
        string consumerKeyId,
        ExternalVoiceConsumerOptions consumer,
        string voiceAlias,
        ExternalVoiceGrantOptions grant,
        DateTimeOffset now)
    {
        if (content.IsEmpty
            || content.Length > MaximumBytes
            || content.Length >= 3
                && content[0] == 0xef
                && content[1] == 0xbb
                && content[2] == 0xbf)
        {
            throw InvalidEvidence();
        }

        string json;
        try
        {
            json = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw InvalidEvidence(exception);
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
                    MaxDepth = 4,
                });
        }
        catch (JsonException exception)
        {
            throw InvalidEvidence(exception);
        }

        using (document)
        {
            var properties = ExactObject(document.RootElement, RequiredProperties);
            if (!MatchesString(properties, "schema", CurrentSchema)
                || !MatchesString(properties, "consumerKeyId", consumerKeyId)
                || !MatchesString(properties, "ownerId", consumer.OwnerId.ToString("D"))
                || !MatchesString(properties, "voiceAlias", voiceAlias)
                || !MatchesString(properties, "projectId", consumer.ProjectId)
                || !MatchesString(properties, "origin", RequiredOrigin)
                || properties["revokedAtUtc"].ValueKind != JsonValueKind.Null
                || grant.RevokedAtUtc is not null)
            {
                throw new InvalidDataException(
                    "External voice development grant does not match its consumer.");
            }

            RequireIdentifier(GetString(properties, "grantId"));
            var characterProfileId = CanonicalGuid(properties["characterProfileId"]);
            var referenceAudioSha256 = GetString(properties, "referenceAudioSha256");
            var transcriptCanonicalSha256 = GetString(
                properties,
                "expectedTranscriptCanonicalSha256");
            if (!ExternalVoiceApiOptionsValidator.IsCanonicalSha256(referenceAudioSha256)
                || !ExternalVoiceApiOptionsValidator.IsCanonicalSha256(
                    transcriptCanonicalSha256))
            {
                throw InvalidEvidence();
            }

            var effectiveAtUtc = CanonicalUtc(properties["effectiveAtUtc"]);
            var expiresAtUtc = CanonicalUtc(properties["expiresAtUtc"]);
            if (expiresAtUtc <= effectiveAtUtc
                || expiresAtUtc - effectiveAtUtc > MaximumGrantDuration
                || now < effectiveAtUtc
                || now >= expiresAtUtc
                || effectiveAtUtc < consumer.EffectiveAtUtc
                || expiresAtUtc > consumer.ExpiresAtUtc)
            {
                throw new InvalidDataException("External voice development grant is inactive.");
            }

            return new ExternalVoiceDevelopmentGrantEvidence(
                characterProfileId,
                referenceAudioSha256,
                transcriptCanonicalSha256,
                effectiveAtUtc,
                expiresAtUtc);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> ExactObject(
        JsonElement element,
        IReadOnlySet<string> expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidEvidence();
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expectedProperties.Contains(property.Name)
                || !properties.TryAdd(property.Name, property.Value))
            {
                throw InvalidEvidence();
            }
        }

        if (properties.Count != expectedProperties.Count
            || expectedProperties.Any(property => !properties.ContainsKey(property)))
        {
            throw InvalidEvidence();
        }

        return properties;
    }

    private static string GetString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        var element = properties[name];
        if (element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString())
            || !string.Equals(
                element.GetString(),
                element.GetString()!.Trim(),
                StringComparison.Ordinal))
        {
            throw InvalidEvidence();
        }

        return element.GetString()!;
    }

    private static Guid CanonicalGuid(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(element.GetString(), "D", out var parsed)
            || parsed == Guid.Empty
            || !string.Equals(element.GetString(), parsed.ToString("D"), StringComparison.Ordinal))
        {
            throw InvalidEvidence();
        }

        return parsed;
    }

    private static DateTimeOffset CanonicalUtc(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParseExact(
                element.GetString(),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
            || parsed.Offset != TimeSpan.Zero
            || !string.Equals(
                element.GetString(),
                parsed.ToString("O", CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw InvalidEvidence();
        }

        return parsed;
    }

    private static void RequireIdentifier(string value)
    {
        if (!VoiceCatalogOptionsValidator.IsCanonicalGrantIdentifier(value))
        {
            throw InvalidEvidence();
        }
    }

    private static bool MatchesString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string expected) =>
        properties[name].ValueKind == JsonValueKind.String
        && string.Equals(properties[name].GetString(), expected, StringComparison.Ordinal);

    private static InvalidDataException InvalidEvidence(Exception? inner = null) =>
        new("Invalid external voice development grant.", inner);
}
