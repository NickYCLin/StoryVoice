namespace StoryVoice.Worker;

/// <summary>
/// Marks a provider failure that the job runner must not automatically replay. This is used for
/// billed synthesis APIs that do not expose an idempotency key: an ambiguous retry could generate
/// and charge for the same text twice.
/// </summary>
public sealed class PermanentNarrationProviderException(
    string errorCode,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string ErrorCode { get; } = string.IsNullOrWhiteSpace(errorCode)
        ? throw new ArgumentException("A permanent provider error code is required.", nameof(errorCode))
        : errorCode;
}
