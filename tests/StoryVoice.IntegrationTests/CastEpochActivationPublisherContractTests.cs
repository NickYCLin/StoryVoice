using Microsoft.EntityFrameworkCore;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class CastEpochActivationPublisherContractTests
{
    [Fact]
    public async Task Empty_command_is_rejected_before_database_access_with_stable_failure()
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1")
            .Options;
        await using var db = new StoryVoiceDbContext(options);
        var publisher = new PostgreSqlCastEpochActivationPublisher(db);

        var exception = await Assert.ThrowsAsync<CastEpochActivationRejectedException>(() =>
            publisher.ActivateAsync(default, TestContext.Current.CancellationToken));

        Assert.Equal(CastEpochActivationFailure.InvalidCommand, exception.Failure);
        Assert.Equal("Cast epoch activation was rejected.", exception.Message);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
