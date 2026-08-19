using System.Net;

namespace StoryVoice.IntegrationTests;

public sealed class ExternalVoiceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Endpoint_is_not_mapped_when_external_api_is_disabled()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/external/v1/speech",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
