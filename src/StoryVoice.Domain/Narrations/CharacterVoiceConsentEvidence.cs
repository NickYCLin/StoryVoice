using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using StoryVoice.Domain.Series;

namespace StoryVoice.Domain.Narrations;

public static class CharacterVoiceConsentScopes
{
    public const string PrivateEvaluation = "storyvoice_private_evaluation";
    public const string FormalNarration = "storyvoice_formal_narration";
    public const string PublicDistribution = "public_distribution";
    public const string CommercialUse = "commercial_use";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PrivateEvaluation,
        FormalNarration,
        PublicDistribution,
        CommercialUse,
    };
}

/// <summary>
/// Canonical transcript identity shared by receipt validation and durable clone operations.
/// The canonical bytes are UTF-8 without a BOM over text processed in this exact order: Unicode
/// NFKC, <see cref="string.Trim()"/>, then rejection of any remaining Control or Format rune.
/// Boundary CR/LF is therefore equivalent to no boundary newline, while embedded newlines fail.
/// </summary>
public static class CharacterVoiceTranscriptCanonicalizer
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("預期逐字稿不可為空白。", nameof(value));
        }

        string normalized;
        try
        {
            normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("預期逐字稿包含無效的 Unicode 字元。", nameof(value), exception);
        }

        if (normalized.Length > 2_000)
        {
            throw new ArgumentException("預期逐字稿不可超過 2000 個字元。", nameof(value));
        }

        var hasVisibleContent = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                throw new ArgumentException("預期逐字稿不可包含內嵌控制或格式字元。", nameof(value));
            }

            if (!Rune.IsWhiteSpace(rune)
                && category is not UnicodeCategory.NonSpacingMark
                && category is not UnicodeCategory.SpacingCombiningMark
                && category is not UnicodeCategory.EnclosingMark)
            {
                hasVisibleContent = true;
            }
        }

        if (!hasVisibleContent)
        {
            throw new ArgumentException("預期逐字稿必須包含可見字元。", nameof(value));
        }

        return normalized;
    }

    public static string ComputeSha256Hex(string value)
    {
        var canonical = Normalize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

/// <summary>
/// Minimal, privacy-reduced snapshot extracted from a detached consent receipt. Contact details,
/// signatures and the consent document itself deliberately remain outside StoryVoice.
/// </summary>
public sealed class CharacterVoiceConsentEvidence
{
    public const string CurrentEvidenceVersion = "storyvoice-clone-consent-receipt/v2";
    public const string CurrentAttestationVersion = "storyvoice-clone-subject-consent/v1";
    public const int MaximumRecorderNameLength = 120;
    public const int MaximumVersionLength = 80;

    private CharacterVoiceConsentEvidence(
        string recorderName,
        DateOnly recordingDate,
        DateOnly consentSignedDate,
        string consentType,
        bool privateEvaluationAllowed,
        bool formalNarrationAllowed,
        bool publicDistributionAllowed,
        bool commercialUseAllowed,
        string consentRecordSha256,
        string consentReceiptSha256,
        string expectedTranscriptSha256,
        string evidenceVersion,
        string attestationVersion)
    {
        RecorderName = recorderName;
        RecordingDate = recordingDate;
        ConsentSignedDate = consentSignedDate;
        ConsentType = consentType;
        PrivateEvaluationAllowed = privateEvaluationAllowed;
        FormalNarrationAllowed = formalNarrationAllowed;
        PublicDistributionAllowed = publicDistributionAllowed;
        CommercialUseAllowed = commercialUseAllowed;
        ConsentRecordSha256 = consentRecordSha256;
        ConsentReceiptSha256 = consentReceiptSha256;
        ExpectedTranscriptSha256 = expectedTranscriptSha256;
        EvidenceVersion = evidenceVersion;
        AttestationVersion = attestationVersion;
    }

    public string RecorderName { get; }
    public DateOnly RecordingDate { get; }
    public DateOnly ConsentSignedDate { get; }
    public string ConsentType { get; }
    public bool PrivateEvaluationAllowed { get; }
    public bool FormalNarrationAllowed { get; }
    public bool PublicDistributionAllowed { get; }
    public bool CommercialUseAllowed { get; }
    public string ConsentRecordSha256 { get; }
    public string ConsentReceiptSha256 { get; }
    public string ExpectedTranscriptSha256 { get; }
    public string EvidenceVersion { get; }
    public string AttestationVersion { get; }

    public IReadOnlyList<string> UsageScopes
    {
        get
        {
            var scopes = new List<string>(4);
            if (PrivateEvaluationAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.PrivateEvaluation);
            }

            if (FormalNarrationAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.FormalNarration);
            }

            if (PublicDistributionAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.PublicDistribution);
            }

            if (CommercialUseAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.CommercialUse);
            }

            return scopes;
        }
    }

    public static CharacterVoiceConsentEvidence Create(
        string recorderName,
        DateOnly recordingDate,
        DateOnly consentSignedDate,
        string consentType,
        IEnumerable<string> usageScopes,
        string consentRecordSha256,
        string consentReceiptSha256,
        string expectedTranscriptSha256,
        string evidenceVersion,
        string attestationVersion,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(usageScopes);
        var normalizedRecorderName = SeriesValueValidator.NormalizePrintable(
            recorderName,
            MaximumRecorderNameLength,
            nameof(recorderName));
        if (recordingDate > today || consentSignedDate > today)
        {
            throw new ArgumentException("錄音日期與同意簽署日期不可在未來。");
        }

        if (consentSignedDate < recordingDate)
        {
            throw new ArgumentException("同意簽署日期不可早於錄音日期。");
        }

        var normalizedConsentType = SeriesValueValidator.NormalizePrintable(
            consentType,
            32,
            nameof(consentType));
        if (!CharacterVoiceConsentTypes.All.Contains(normalizedConsentType))
        {
            throw new ArgumentException("授權同意類型無效。", nameof(consentType));
        }

        var scopes = usageScopes.ToArray();
        if (scopes.Length != scopes.Distinct(StringComparer.Ordinal).Count()
            || scopes.Any(scope => !CharacterVoiceConsentScopes.All.Contains(scope)))
        {
            throw new ArgumentException("授權使用範圍無效。", nameof(usageScopes));
        }

        if (!scopes.Contains(CharacterVoiceConsentScopes.PrivateEvaluation, StringComparer.Ordinal))
        {
            throw new ArgumentException("必須明確允許 StoryVoice 私人評估。", nameof(usageScopes));
        }

        if (!string.Equals(evidenceVersion, CurrentEvidenceVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("授權證據版本不受支援。", nameof(evidenceVersion));
        }

        if (!string.Equals(attestationVersion, CurrentAttestationVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("授權聲明版本不受支援。", nameof(attestationVersion));
        }

        return new CharacterVoiceConsentEvidence(
            normalizedRecorderName,
            recordingDate,
            consentSignedDate,
            normalizedConsentType,
            privateEvaluationAllowed: true,
            formalNarrationAllowed: scopes.Contains(CharacterVoiceConsentScopes.FormalNarration, StringComparer.Ordinal),
            publicDistributionAllowed: scopes.Contains(CharacterVoiceConsentScopes.PublicDistribution, StringComparer.Ordinal),
            commercialUseAllowed: scopes.Contains(CharacterVoiceConsentScopes.CommercialUse, StringComparer.Ordinal),
            ValidateSha256(consentRecordSha256, nameof(consentRecordSha256)),
            ValidateSha256(consentReceiptSha256, nameof(consentReceiptSha256)),
            ValidateSha256(expectedTranscriptSha256, nameof(expectedTranscriptSha256)),
            evidenceVersion,
            attestationVersion);
    }

    private static string ValidateSha256(string value, string parameterName)
    {
        var normalized = SeriesValueValidator.NormalizePrintable(value, 64, parameterName);
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("證據雜湊必須是 64 字元的十六進位字串。", parameterName);
        }

        return normalized.ToLowerInvariant();
    }
}
