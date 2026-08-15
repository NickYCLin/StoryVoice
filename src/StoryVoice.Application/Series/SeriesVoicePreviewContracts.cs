namespace StoryVoice.Application.Series;

public sealed record SeriesVoicePreviewRequest(
    string Provider,
    string Voice);

public sealed record SeriesVoicePreviewAudio(
    byte[] Content,
    string ContentType,
    string ModelRevision,
    string Voice);

public interface ISeriesVoicePreviewService
{
    Task<SeriesVoicePreviewAudio> GenerateAsync(
        SeriesVoicePreviewRequest request,
        CancellationToken cancellationToken);
}

public enum SeriesVoicePreviewFailureKind
{
    Unavailable = 0,
    ContractViolation = 1,
}

public sealed class SeriesVoicePreviewUnavailableException : Exception
{
    public const string StableCode = "series_voice_preview_unavailable";

    public SeriesVoicePreviewUnavailableException()
        : this(SeriesVoicePreviewFailureKind.Unavailable)
    {
    }

    public SeriesVoicePreviewUnavailableException(Exception innerException)
        : this(SeriesVoicePreviewFailureKind.Unavailable, innerException)
    {
    }

    public SeriesVoicePreviewUnavailableException(SeriesVoicePreviewFailureKind failureKind)
        : this(failureKind, null)
    {
    }

    public SeriesVoicePreviewUnavailableException(
        SeriesVoicePreviewFailureKind failureKind,
        Exception? innerException)
        : base("本機台灣華語試音暫時無法使用，請稍後再試。", innerException)
    {
        FailureKind = failureKind;
    }

    public SeriesVoicePreviewFailureKind FailureKind { get; }
}
