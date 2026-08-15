namespace StoryVoice.Worker;

/// <summary>
/// Marks a provider failure that the job runner must not automatically replay. This covers billed
/// APIs without idempotency as well as deterministic local contract failures (for example a pinned
/// model/version mismatch) that retrying cannot repair.
/// </summary>
public sealed class PermanentNarrationProviderException(
    string errorCode,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string ErrorCode { get; } = string.IsNullOrWhiteSpace(errorCode)
        ? throw new ArgumentException("A permanent provider error code is required.", nameof(errorCode))
        : errorCode;
}
