using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class CloneConsentReceiptValidatorTests
{
    private const string ExpectedTranscript = "這是一段經過授權的臺灣華語測試逐字稿。";
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ReferenceAudioSha256 = Hash("reference-audio");

    [Fact]
    public async Task ValidateAsync_accepts_a_complete_v2_receipt_and_returns_privacy_reduced_evidence()
    {
        var receipt = BuildReceipt();

        var evidence = await ValidateAsync(receipt);

        Assert.Equal(CharacterVoiceConsentEvidence.CurrentEvidenceVersion, evidence.EvidenceVersion);
        Assert.Equal(CharacterVoiceConsentEvidence.CurrentAttestationVersion, evidence.AttestationVersion);
        Assert.Equal("已授權測試錄音者", evidence.RecorderName);
        Assert.Equal(new DateOnly(2026, 8, 16), evidence.RecordingDate);
        Assert.Equal(new DateOnly(2026, 8, 17), evidence.ConsentSignedDate);
        Assert.Equal(CharacterVoiceConsentTypes.SelfRecorded, evidence.ConsentType);
        Assert.True(evidence.PrivateEvaluationAllowed);
        Assert.True(evidence.FormalNarrationAllowed);
        Assert.False(evidence.PublicDistributionAllowed);
        Assert.False(evidence.CommercialUseAllowed);
        Assert.Equal(Hash("signed-consent-record"), evidence.ConsentRecordSha256);
        Assert.Equal(Hash(receipt), evidence.ConsentReceiptSha256);
        Assert.Equal(
            CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(ExpectedTranscript),
            evidence.ExpectedTranscriptSha256);
        Assert.Equal(
            [
                CharacterVoiceConsentScopes.PrivateEvaluation,
                CharacterVoiceConsentScopes.FormalNarration,
            ],
            evidence.UsageScopes);
    }

    [Fact]
    public async Task ValidateAsync_treats_no_newline_LF_and_CRLF_transcript_tails_as_the_same_canonical_hash()
    {
        var canonicalHash = CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(ExpectedTranscript);
        var receipt = BuildReceipt(transcriptSha256: canonicalHash);

        foreach (var transcript in new[]
                 {
                     ExpectedTranscript,
                     ExpectedTranscript + "\n",
                     ExpectedTranscript + "\r\n",
                 })
        {
            var evidence = await ValidateAsync(receipt, expectedTranscript: transcript);

            Assert.Equal(canonicalHash, evidence.ExpectedTranscriptSha256);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_embedded_newline_in_the_expected_transcript()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ValidateAsync(BuildReceipt(), expectedTranscript: "這是一段\n內嵌換行的逐字稿。"));

        Assert.Contains("內嵌控制或格式字元", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_mark_only_expected_transcript()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ValidateAsync(BuildReceipt(), expectedTranscript: "\u0301"));

        Assert.Contains("必須包含可見字元", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_duplicate_root_property()
    {
        var receipt = AppendRootProperty(BuildReceipt(), "\"schema\":\"storyvoice-clone-consent-receipt/v2\"");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => ValidateAsync(receipt));

        Assert.Contains("重複欄位", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_unknown_root_property()
    {
        var receipt = AppendRootProperty(BuildReceipt(), "\"contactEmail\":\"private@example.test\"");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => ValidateAsync(receipt));

        Assert.Contains("不受支援的欄位", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_duplicate_usage_scope()
    {
        var receipt = BuildReceipt(
            usageScopes:
            [
                CharacterVoiceConsentScopes.PrivateEvaluation,
                CharacterVoiceConsentScopes.PrivateEvaluation,
            ]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => ValidateAsync(receipt));

        Assert.Contains("重複使用範圍", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_unknown_usage_scope()
    {
        var receipt = BuildReceipt(
            usageScopes:
            [
                CharacterVoiceConsentScopes.PrivateEvaluation,
                "unrestricted_external_training",
            ]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => ValidateAsync(receipt));

        Assert.Contains("不受支援的使用範圍", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_when_rights_attestation_is_not_explicitly_checked()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ValidateAsync(BuildReceipt(), rightsAttested: false));

        Assert.Equal("rightsAttested", exception.ParamName);
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_audio_hash_mismatch()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ValidateAsync(BuildReceipt(), referenceAudioSha256: Hash("different-audio")));

        Assert.Contains("參考錄音不一致", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_transcript_hash_mismatch()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ValidateAsync(BuildReceipt(), expectedTranscript: "完全不同的逐字稿。"));

        Assert.Contains("預期逐字稿不一致", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_uppercase_SHA256_fields()
    {
        var receipts = new[]
        {
            BuildReceipt(recordingSha256: ReferenceAudioSha256.ToUpperInvariant()),
            BuildReceipt(
                transcriptSha256: CharacterVoiceTranscriptCanonicalizer
                    .ComputeSha256Hex(ExpectedTranscript)
                    .ToUpperInvariant()),
            BuildReceipt(consentSha256: Hash("signed-consent-record").ToUpperInvariant()),
        };

        foreach (var receipt in receipts)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => ValidateAsync(receipt));
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_recorder_name_over_120_UTF16_code_units()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ValidateAsync(BuildReceipt(recorderName: new string('王', 121))));

        Assert.Contains("120", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_requires_a_canonical_UTC_round_trip_timestamp()
    {
        var nonUtc = Now.AddMinutes(-1).ToOffset(TimeSpan.FromHours(8))
            .ToString("O", CultureInfo.InvariantCulture);
        var missingFractionalDigits = Now.AddMinutes(-1)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

        foreach (var generatedAtUtc in new[] { nonUtc, missingFractionalDigits })
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                ValidateAsync(BuildReceipt(generatedAtUtc: generatedAtUtc)));

            Assert.Contains("嚴格 UTC O", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ValidateAsync_accepts_the_current_Taipei_calendar_date_around_UTC_midnight()
    {
        var utcNow = new DateTimeOffset(2026, 8, 17, 16, 30, 0, TimeSpan.Zero);
        var receipt = BuildReceipt(
            recordingDate: "2026-08-18",
            consentSignedDate: "2026-08-18",
            generatedAtUtc: utcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture));

        var evidence = await ValidateAsync(receipt, now: utcNow);

        Assert.Equal(new DateOnly(2026, 8, 18), evidence.RecordingDate);
        Assert.Equal(new DateOnly(2026, 8, 18), evidence.ConsentSignedDate);
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_date_after_the_current_Taipei_calendar_date()
    {
        var utcNow = new DateTimeOffset(2026, 8, 17, 16, 30, 0, TimeSpan.Zero);
        var receipt = BuildReceipt(
            recordingDate: "2026-08-19",
            consentSignedDate: "2026-08-19",
            generatedAtUtc: utcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ValidateAsync(receipt, now: utcNow));

        Assert.Contains("不可在未來", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_receipt_larger_than_32_KiB()
    {
        var oversized = new byte[CloneConsentReceiptValidator.MaximumReceiptBytes + 1];

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => ValidateAsync(oversized));

        Assert.Contains("32 KiB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_rejects_invalid_UTF8()
    {
        var prefix = "{\"schema\":\""u8.ToArray();
        var suffix = "\"}"u8.ToArray();
        var invalidUtf8 = new byte[prefix.Length + 1 + suffix.Length];
        prefix.CopyTo(invalidUtf8, 0);
        invalidUtf8[prefix.Length] = 0xff;
        suffix.CopyTo(invalidUtf8, prefix.Length + 1);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => ValidateAsync(invalidUtf8));

        Assert.Contains("UTF-8 JSON", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<CharacterVoiceConsentEvidence> ValidateAsync(
        byte[] receipt,
        bool rightsAttested = true,
        string? referenceAudioSha256 = null,
        string expectedTranscript = ExpectedTranscript,
        DateTimeOffset? now = null)
    {
        await using var stream = new MemoryStream(receipt, writable: false);
        return await CloneConsentReceiptValidator.ValidateAsync(
            stream,
            "consent-receipt.json",
            rightsAttested,
            referenceAudioSha256 ?? ReferenceAudioSha256,
            expectedTranscript,
            now ?? Now,
            TestContext.Current.CancellationToken);
    }

    private static byte[] BuildReceipt(
        IReadOnlyList<string>? usageScopes = null,
        string? recordingSha256 = null,
        string? transcriptSha256 = null,
        string? consentSha256 = null,
        string? recorderName = null,
        string recordingDate = "2026-08-16",
        string consentSignedDate = "2026-08-17",
        string? generatedAtUtc = null)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = CharacterVoiceConsentEvidence.CurrentEvidenceVersion,
            recorderName = recorderName ?? "已授權測試錄音者",
            recordingDate,
            consentSignedDate,
            consentType = CharacterVoiceConsentTypes.SelfRecorded,
            usageScopes = usageScopes
                ??
                [
                    CharacterVoiceConsentScopes.PrivateEvaluation,
                    CharacterVoiceConsentScopes.FormalNarration,
                ],
            recordingSha256 = recordingSha256 ?? ReferenceAudioSha256,
            expectedTranscriptCanonicalSha256 = transcriptSha256
                ?? CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(ExpectedTranscript),
            consentSha256 = consentSha256 ?? Hash("signed-consent-record"),
            subjectAttestationVersion = CharacterVoiceConsentEvidence.CurrentAttestationVersion,
            generatedAtUtc = generatedAtUtc
                ?? Now.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture),
        });
    }

    private static byte[] AppendRootProperty(byte[] receipt, string property)
    {
        var json = Encoding.UTF8.GetString(receipt);
        return Encoding.UTF8.GetBytes($"{json[..^1]},{property}}}");
    }

    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
