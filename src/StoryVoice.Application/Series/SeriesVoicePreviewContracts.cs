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

public sealed class SeriesVoicePreviewUnavailableException : Exception
{
    public const string StableCode = "series_voice_preview_unavailable";

    public SeriesVoicePreviewUnavailableException()
        : base("本機台灣華語試音暫時無法使用，請稍後再試。")
    {
    }

    public SeriesVoicePreviewUnavailableException(Exception innerException)
        : base("本機台灣華語試音暫時無法使用，請稍後再試。", innerException)
    {
    }
}
