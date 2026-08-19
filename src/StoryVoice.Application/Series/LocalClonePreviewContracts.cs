namespace StoryVoice.Application.Series;

public sealed record LocalClonePreviewRequest(string? Text);

public sealed record LocalClonePreviewAvailabilityResponse(
    bool Available,
    string? Label);

public sealed record LocalClonePreviewAudio(
    byte[] Content,
    string ContentType);

public interface ILocalClonePreviewService
{
    Task<LocalClonePreviewAvailabilityResponse?> GetAvailabilityAsync(
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken);

    Task<LocalClonePreviewAudio?> PreviewAsync(
        Guid seriesId,
        Guid characterId,
        LocalClonePreviewRequest request,
        CancellationToken cancellationToken);

    Task<LocalClonePreviewAvailabilityResponse?> GetCharacterProfileAvailabilityAsync(
        Guid characterProfileId,
        CancellationToken cancellationToken);

    Task<LocalClonePreviewAudio?> PreviewCharacterProfileAsync(
        Guid characterProfileId,
        LocalClonePreviewRequest request,
        CancellationToken cancellationToken);
}

public enum LocalClonePreviewFailureKind
{
    Disabled = 0,
    NotConfigured = 1,
    AssetInvalid = 2,
    GatewayUnavailable = 3,
    GatewayContractInvalid = 4,
}

public sealed class LocalClonePreviewUnavailableException : Exception
{
    public LocalClonePreviewUnavailableException(LocalClonePreviewFailureKind failureKind)
        : this(failureKind, null)
    {
    }

    public LocalClonePreviewUnavailableException(
        LocalClonePreviewFailureKind failureKind,
        Exception? innerException)
        : base("本機聲音克隆私人試音暫時無法使用。", innerException)
    {
        FailureKind = failureKind;
    }

    public LocalClonePreviewFailureKind FailureKind { get; }

    public string StableCode => FailureKind switch
    {
        LocalClonePreviewFailureKind.Disabled => "local_clone_preview_disabled",
        LocalClonePreviewFailureKind.NotConfigured => "local_clone_preview_not_configured",
        LocalClonePreviewFailureKind.AssetInvalid => "local_clone_preview_asset_invalid",
        LocalClonePreviewFailureKind.GatewayUnavailable => "local_clone_preview_gateway_unavailable",
        LocalClonePreviewFailureKind.GatewayContractInvalid => "local_clone_preview_gateway_contract_invalid",
        _ => "local_clone_preview_unavailable",
    };
}
