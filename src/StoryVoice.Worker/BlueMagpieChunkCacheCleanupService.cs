using Microsoft.Extensions.Options;

namespace StoryVoice.Worker;

public sealed class BlueMagpieChunkCacheCleanupService(
    IBlueMagpieChunkCache cache,
    IOptions<BlueMagpieChunkCacheOptions> options,
    ILogger<BlueMagpieChunkCacheCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes));
        do
        {
            if (!await RunCleanupOnceAsync(stoppingToken))
            {
                break;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<bool> RunCleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cache.CleanupAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "BlueMagpie cache cleanup failed safely");
            return true;
        }
    }
}
