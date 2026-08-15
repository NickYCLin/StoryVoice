using StoryVoice.Infrastructure.Insights;

namespace StoryVoice.UnitTests;

internal sealed class RecordingGpuExecutionGate : ILocalGpuExecutionGate
{
    public RecordingGpuExecutionLease? LastLease { get; private set; }

    public Task<ILocalGpuExecutionLease> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastLease = new RecordingGpuExecutionLease();
        return Task.FromResult<ILocalGpuExecutionLease>(LastLease);
    }

    public Task<ILocalGpuExecutionLease?> TryAcquireAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastLease = new RecordingGpuExecutionLease();
        return Task.FromResult<ILocalGpuExecutionLease?>(LastLease);
    }
}

internal sealed class RecordingGpuExecutionLease : ILocalGpuExecutionLease
{
    private readonly CancellationTokenSource ownershipLost = new();

    public bool Abandoned { get; private set; }

    public bool Disposed { get; private set; }

    public CancellationToken OwnershipLost => ownershipLost.Token;

    public void LoseOwnership() => ownershipLost.Cancel();

    public void Abandon() => Abandoned = true;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
