using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure.Narrations;

internal sealed class CharacterVoicePreviewService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    IThreeWaSynthesisClient synthesisClient) : ICharacterVoicePreviewService
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

        if (profile.Status != CharacterVoiceProfileStatus.Ready)
        {
            throw new InvalidOperationException("這組聲線還沒就緒，無法試講。");
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
        var artifact = artifacts.FirstOrDefault()
            ?? throw new InvalidOperationException("試講合成沒有回傳可用的音訊。");
        if (string.IsNullOrWhiteSpace(handle.ArtifactUrlTemplate))
        {
            throw new InvalidOperationException("試講合成沒有回傳可下載的音訊位置。");
        }

        using var buffer = new MemoryStream();
        await synthesisClient.DownloadArtifactAsync(handle.ArtifactUrlTemplate, artifact.Id, buffer, cancellationToken);
        await synthesisClient.AcknowledgeArtifactAsync(handle.AckUrlTemplate, artifact.Id, cancellationToken);

        if (buffer.Length == 0)
        {
            throw new InvalidOperationException("試講合成沒有產生可用音訊。");
        }

        return new VoiceProfilePreviewAudio(buffer.ToArray(), artifact.MimeType ?? "audio/mpeg");
    }

    private static bool IsTerminalStatus(string status) =>
        IsSuccessStatus(status) || status is "failed" or "error" or "cancelled" or "canceled";

    private static bool IsSuccessStatus(string status) =>
        status is "succeeded" or "success" or "completed";

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("目前使用者識別碼無效。");
        }

        return currentUser.UserId;
    }
}
