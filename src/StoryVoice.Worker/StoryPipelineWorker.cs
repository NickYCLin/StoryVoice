namespace StoryVoice.Worker;

public sealed class StoryPipelineWorker(ILogger<StoryPipelineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("StoryVoice pipeline worker is ready");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogDebug("StoryVoice pipeline worker heartbeat");
        }
    }
}
