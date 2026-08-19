using System.Globalization;
using System.Text;
using System.Text.Json;
using StoryVoice.Infrastructure.VoiceCatalog;
namespace StoryVoice.Infrastructure.ExternalVoices;

internal sealed record ExternalVoiceUsageAuthorizationEvidence(
    Guid CharacterProfileId,
    string SyntheticVoiceAuthorizationSha256,
    DateTimeOffset EffectiveAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal static class ExternalVoiceAuthorizationEvidenceValidator
{
    public const string CurrentSchema = "voice-api-synthetic-usage-grant/v1";
    public const int MaximumBytes = 32 * 1024;

    private static readonly IReadOnlySet<string> RequiredProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "schema",
            "grantId",
            "consumerKeyId",
            "ownerId",
            "voiceAlias",
            "characterProfileId",
            "syntheticVoiceAuthorizationSha256",
            "effectiveAtUtc",
            "expiresAtUtc",
            "revokedAtUtc",
            "projectId",
            "consumerFamilyId",
            "territoryCountryCode",
            "activation",
        };

    private static readonly IReadOnlySet<string> ActivationProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "state",
            "accountSubjectId",
            "auditEventId",
            "issuedAtUtc",
        };

    public static ExternalVoiceUsageAuthorizationEvidence Validate(
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
            throw new InvalidDataException("Invalid external voice authorization evidence.");
        }

        string json;
        try
        {
            json = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Invalid external voice authorization evidence.", exception);
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
            throw new InvalidDataException("Invalid external voice authorization evidence.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Invalid external voice authorization evidence.");
            }

            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!RequiredProperties.Contains(property.Name)
                    || !properties.TryAdd(property.Name, property.Value))
                {
                    throw new InvalidDataException("Invalid external voice authorization evidence.");
                }
            }

            if (properties.Count != RequiredProperties.Count
                || RequiredProperties.Any(property => !properties.ContainsKey(property))
                || !MatchesString(properties, "schema", CurrentSchema)
                || !MatchesString(properties, "consumerKeyId", consumerKeyId)
                || !MatchesString(properties, "ownerId", consumer.OwnerId.ToString("D"))
                || !MatchesString(properties, "voiceAlias", voiceAlias)
                || properties["revokedAtUtc"].ValueKind != JsonValueKind.Null
                || grant.RevokedAtUtc is not null)
            {
                throw new InvalidDataException("External voice authorization evidence does not match its grant.");
            }

            RequireIdentifier(GetString(properties, "grantId"));

            if (!MatchesString(properties, "projectId", consumer.ProjectId)
                || !MatchesString(
                    properties,
                    "consumerFamilyId",
                    consumer.ConsumerFamilyId)
                || !MatchesString(
                    properties,
                    "territoryCountryCode",
                    consumer.TerritoryCountryCode))
            {
                throw new InvalidDataException(
                    "External voice authorization evidence does not match its commercial grant.");
            }

            var characterProfileId = CanonicalGuid(properties["characterProfileId"]);
            var authorizationSha256 = GetString(
                properties,
                "syntheticVoiceAuthorizationSha256");
            if (!ExternalVoiceApiOptionsValidator.IsCanonicalSha256(authorizationSha256))
            {
                throw new InvalidDataException("Invalid external voice authorization evidence.");
            }

            var effectiveAtUtc = CanonicalUtc(properties["effectiveAtUtc"]);
            var expiresAtUtc = CanonicalUtc(properties["expiresAtUtc"]);
            var activation = ExactObject(properties["activation"], ActivationProperties);
            if (!MatchesString(activation, "state", "active"))
            {
                throw new InvalidDataException("External voice authorization evidence is not active.");
            }

            RequireIdentifier(GetString(activation, "accountSubjectId"));
            RequireIdentifier(GetString(activation, "auditEventId"));
            var issuedAtUtc = CanonicalUtc(activation["issuedAtUtc"]);
            if (expiresAtUtc <= effectiveAtUtc
                || now < effectiveAtUtc
                || now >= expiresAtUtc
                || issuedAtUtc > effectiveAtUtc
                || effectiveAtUtc < consumer.EffectiveAtUtc
                || expiresAtUtc > consumer.ExpiresAtUtc)
            {
                throw new InvalidDataException("External voice authorization evidence is inactive.");
            }

            return new ExternalVoiceUsageAuthorizationEvidence(
                characterProfileId,
                authorizationSha256,
                effectiveAtUtc,
                expiresAtUtc);
        }
    }

    private static string GetString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        var element = properties[name];
        if (element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString())
            || !string.Equals(element.GetString(), element.GetString()!.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid external voice authorization evidence.");
        }

        return element.GetString()!;
    }

    private static IReadOnlyDictionary<string, JsonElement> ExactObject(
        JsonElement element,
        IReadOnlySet<string> expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Invalid external voice authorization evidence.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expectedProperties.Contains(property.Name)
                || !properties.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException("Invalid external voice authorization evidence.");
            }
        }

        if (properties.Count != expectedProperties.Count
            || expectedProperties.Any(property => !properties.ContainsKey(property)))
        {
            throw new InvalidDataException("Invalid external voice authorization evidence.");
        }

        return properties;
    }

    private static void RequireIdentifier(string value)
    {
        if (!VoiceCatalogOptionsValidator.IsCanonicalGrantIdentifier(value))
        {
            throw new InvalidDataException("Invalid external voice authorization evidence.");
        }
    }

    private static Guid CanonicalGuid(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(element.GetString(), "D", out var parsed)
            || parsed == Guid.Empty
            || !string.Equals(element.GetString(), parsed.ToString("D"), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid external voice authorization evidence.");
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
            throw new InvalidDataException("Invalid external voice authorization evidence.");
        }

        return parsed;
    }

    private static bool MatchesString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string expected) =>
        properties[name].ValueKind == JsonValueKind.String
        && string.Equals(properties[name].GetString(), expected, StringComparison.Ordinal);

}
