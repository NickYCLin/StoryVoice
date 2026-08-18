using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure.Narrations;

internal sealed class CharacterVoicePreviewService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    IThreeWaSynthesisClient synthesisClient,
    IOptions<ThreeWaAiHubOptions> options) : ICharacterVoicePreviewService
{
    private const int MaximumPreviewTextLength = 200;
    private const int PollIntervalMs = 1_500;
    private const int MaxPollAttempts = 40;

    public async Task<VoiceProfilePreviewAudio?> PreviewAsync(
        Guid characterProfileId,
        Guid profileId,
        PreviewVoiceProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("試講文字不可為空白。", nameof(request));
        }

        if (text.Length > MaximumPreviewTextLength)
        {
            throw new ArgumentException($"試講文字不可超過 {MaximumPreviewTextLength} 個字元。", nameof(request));
        }

        var ownerId = EnsureCurrentOwnerId();
        var profile = await dbContext.CharacterVoiceProfiles.SingleOrDefaultAsync(
            candidate => candidate.OwnerId == ownerId
                && candidate.CharacterProfileId == characterProfileId
                && candidate.Id == profileId,
            cancellationToken);
        if (profile is null)
        {
            return null;
        }

        if (profile.Mode == CharacterVoiceProfileMode.Design
            && !ThreeWaSynthesisCapabilities.SupportsVoiceDesign)
        {
            throw new InvalidOperationException(
                ThreeWaSynthesisCapabilities.DesignVoiceUnavailableMessage);
        }

        if (profile.Status != CharacterVoiceProfileStatus.Ready)
        {
            throw new InvalidOperationException("這組聲線還沒就緒，無法試講。");
        }

        if (profile.Mode == CharacterVoiceProfileMode.Clone)
        {
            var privateEvaluationAuthorized = await dbContext.CharacterVoiceProfileOperations
                .AsNoTracking()
                .AnyAsync(
                    operation => operation.OwnerId == ownerId
                        && operation.CharacterProfileId == characterProfileId
                        && operation.NewProfileId == profile.Id
                        && operation.State == CharacterVoiceProfileOperationState.Activated
                        && operation.EvidenceVersion == CharacterVoiceConsentEvidence.CurrentEvidenceVersion
                        && operation.AttestationVersion == CharacterVoiceConsentEvidence.CurrentAttestationVersion
                        && operation.PrivateEvaluationAllowed,
                    cancellationToken);
            if (!privateEvaluationAuthorized)
            {
                throw new InvalidOperationException(
                    "這組克隆聲線缺少可驗證的私人試音授權，無法試講。");
            }
        }

        var mode = profile.Mode == CharacterVoiceProfileMode.Clone ? "ultimate_clone" : "design";
        var handle = await synthesisClient.SubmitAsync(
            new ThreeWaSynthesisRequest(text, mode, profile.VoiceProfileTaskId, profile.VoicePromptText),
            cancellationToken);

        string status;
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            status = await synthesisClient.GetTaskStatusAsync(handle.StatusUrl, cancellationToken);
            if (IsTerminalStatus(status))
            {
                break;
            }

            attempt++;
            if (attempt > MaxPollAttempts)
            {
                throw new InvalidOperationException("試講合成逾時未完成，請稍後再試。");
            }

            await Task.Delay(PollIntervalMs, cancellationToken);
        }

        if (!IsSuccessStatus(status))
        {
            throw new InvalidOperationException("試講合成失敗。");
        }

        var artifacts = await synthesisClient.GetResultArtifactsAsync(handle.ResultUrl, cancellationToken);
        var selectedArtifact = artifacts
            .Select(artifact => new
            {
                Artifact = artifact,
                ContentType = NormalizeAudioContentType(artifact.MimeType),
            })
            .FirstOrDefault(candidate => candidate.ContentType is not null)
            ?? throw new InvalidOperationException("試講合成沒有回傳可用的音訊。");
        if (string.IsNullOrWhiteSpace(handle.ArtifactUrlTemplate))
        {
            throw new InvalidOperationException("試講合成沒有回傳可下載的音訊位置。");
        }

        using var buffer = new BoundedMemoryStream(options.Value.MaximumAudioResponseBytes);
        await synthesisClient.DownloadArtifactAsync(
            handle.ArtifactUrlTemplate,
            selectedArtifact.Artifact.Id,
            buffer,
            cancellationToken);

        if (buffer.Length == 0)
        {
            throw new InvalidOperationException("試講合成沒有產生可用音訊。");
        }

        return new VoiceProfilePreviewAudio(buffer.ToArray(), selectedArtifact.ContentType!);
    }

    private static bool IsTerminalStatus(string status) =>
        IsSuccessStatus(status) || status is "failed" or "error" or "cancelled" or "canceled";

    private static bool IsSuccessStatus(string status) =>
        status is "succeeded" or "success" or "completed";

    private static string? NormalizeAudioContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!MediaTypeHeaderValue.TryParse(value, out var contentType)
            || string.IsNullOrWhiteSpace(contentType.MediaType)
            || !contentType.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return contentType.ToString();
    }

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("目前使用者識別碼無效。");
        }

        return currentUser.UserId;
    }

    private sealed class BoundedMemoryStream(int maximumBytes) : MemoryStream
    {
        public override void SetLength(long value)
        {
            if (value > maximumBytes)
            {
                throw TooLarge();
            }

            base.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureFits(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureFits(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureFits(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureFits(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void EnsureFits(int bytesToWrite)
        {
            if (maximumBytes < 1 || Position > maximumBytes - bytesToWrite)
            {
                throw TooLarge();
            }
        }

        private static InvalidOperationException TooLarge() =>
            new("試講音訊超過允許大小。");
    }
}
