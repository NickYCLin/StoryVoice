namespace StoryVoice.Worker;

public sealed class BlueMagpieChunkCacheOptions
{
    public const string SectionName = "BlueMagpieChunkCache";
    private const long Gibibyte = 1024L * 1024 * 1024;

    public string RootPath { get; set; } = "/data/bluemagpie-chunk-cache";

    public long MaximumBytes { get; set; } = 32 * Gibibyte;

    public long LowWatermarkBytes { get; set; } = 24 * Gibibyte;

    public long MinimumFreeBytes { get; set; } = 64 * Gibibyte;

    public int RetentionHours { get; set; } = 7 * 24;

    public int CleanupIntervalMinutes { get; set; } = 30;

    public int TemporaryEntryRetentionMinutes { get; set; } = 60;

    public int LockRetryMilliseconds { get; set; } = 100;
}
