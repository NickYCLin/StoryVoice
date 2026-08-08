using System.Net;

namespace StoryVoice.IntegrationTests;

public sealed class HealthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Root_returns_project_identity()
    {
        using var client = factory.CreateClient();

        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await client.GetAsync("/", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("StoryVoice", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_returns_healthy()
    {
        using var client = factory.CreateClient();

        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await client.GetAsync("/health/live", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body);
    }
}
