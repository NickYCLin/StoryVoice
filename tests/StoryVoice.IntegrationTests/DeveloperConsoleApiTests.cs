using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Infrastructure.ExternalVoices;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.IntegrationTests;

public sealed class DeveloperConsoleApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string KeyId = "dev_console_owner_01";
    private const string Alias = "private-synthetic-voice";
    private const string Password = "Moonlight!Story42";

    [Fact]
    public async Task Anonymous_user_cannot_read_developer_overview()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/developer/external-voice/overview",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Overview_is_owner_scoped_and_never_returns_token_or_evidence_material()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var tokenSha256 = new string('a', 64);
        var evidenceSha256 = new string('b', 64);
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            // Options 驗證器要求啟用時必須有合法的 local clone 內部設定。
            var localPrefix = $"LocalClonePreview:AllowedProfiles:{Guid.NewGuid():D}";
            builder.UseSetting("LocalClonePreview:Enabled", "false");
            builder.UseSetting("LocalClonePreview:InternalToken", new string('t', 32));
            builder.UseSetting(
                "LocalClonePreview:AssetRootPath",
                Path.Combine(factory.StorageRoot, "developer-console-assets"));
            builder.UseSetting($"{localPrefix}:Label", "developer console test voice");
            builder.UseSetting($"{localPrefix}:ReferenceAudioRelativePath", "voice/reference.wav");
            builder.UseSetting($"{localPrefix}:TranscriptRelativePath", "voice/transcript.txt");
            builder.UseSetting($"{localPrefix}:ExpectedReferenceAudioSha256", new string('c', 64));
            builder.UseSetting($"{localPrefix}:ExpectedTranscriptSha256", new string('d', 64));

            var consumerPrefix = $"ExternalVoiceApi:Consumers:{KeyId}";
            var voicePrefix = $"{consumerPrefix}:AllowedVoices:{Alias}";
            builder.UseSetting("ExternalVoiceApi:Enabled", "true");
            builder.UseSetting(
                $"{consumerPrefix}:AccessTier",
                ExternalVoiceAccessTiers.PrivateDevelopment);
            builder.UseSetting($"{consumerPrefix}:DisplayName", "console test project");
            builder.UseSetting($"{consumerPrefix}:ProjectId", "console-test-project");
            builder.UseSetting($"{consumerPrefix}:OwnerId", ownerId.ToString("D"));
            builder.UseSetting($"{consumerPrefix}:TokenSha256", tokenSha256);
            builder.UseSetting(
                $"{consumerPrefix}:EffectiveAtUtc",
                now.AddHours(-1).ToString("O", CultureInfo.InvariantCulture));
            builder.UseSetting(
                $"{consumerPrefix}:ExpiresAtUtc",
                now.AddDays(29).ToString("O", CultureInfo.InvariantCulture));
            builder.UseSetting(
                $"{voicePrefix}:AuthorizationEvidenceRelativePath",
                "evidence/console-test-grant.json");
            builder.UseSetting($"{voicePrefix}:AuthorizationEvidenceSha256", evidenceSha256);
        });

        var ownerEmail = $"console-owner-{Guid.NewGuid():N}@example.com";
        await using (var scope = configuredFactory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = ownerId, UserName = ownerEmail, Email = ownerEmail };
            var created = await userManager.CreateAsync(user, Password);
            Assert.True(created.Succeeded);
        }

        using var ownerClient = configuredFactory.CreateCookieClient();
        using var loginResponse = await ownerClient.PostWithCsrfAsync(
            "/api/auth/login",
            new { email = ownerEmail, password = Password, rememberMe = false },
            cancellationToken);
        loginResponse.EnsureSuccessStatusCode();

        using var overviewResponse = await ownerClient.GetAsync(
            "/api/developer/external-voice/overview",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, overviewResponse.StatusCode);
        Assert.Equal("no-store", overviewResponse.Headers.CacheControl?.ToString());

        var payload = await overviewResponse.Content.ReadAsStringAsync(cancellationToken);
        using var overview = JsonDocument.Parse(payload);
        Assert.True(overview.RootElement.GetProperty("serviceEnabled").GetBoolean());
        var projects = overview.RootElement.GetProperty("projects");
        Assert.Equal(1, projects.GetArrayLength());
        var project = projects[0];
        Assert.Equal(KeyId, project.GetProperty("keyId").GetString());
        Assert.Equal("console test project", project.GetProperty("displayName").GetString());
        Assert.Equal("svd1.", project.GetProperty("tokenPrefix").GetString());
        Assert.Equal("active", project.GetProperty("status").GetString());
        var voices = project.GetProperty("voices");
        Assert.Equal(1, voices.GetArrayLength());
        Assert.Equal(Alias, voices[0].GetProperty("voiceAlias").GetString());
        Assert.Equal("active", voices[0].GetProperty("status").GetString());

        Assert.DoesNotContain(tokenSha256, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(evidenceSha256, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("console-test-grant", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ownerId.ToString("D"), payload, StringComparison.OrdinalIgnoreCase);

        using var otherClient = await configuredFactory.CreateAuthenticatedClientAsync(cancellationToken);
        using var otherResponse = await otherClient.GetAsync(
            "/api/developer/external-voice/overview",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        using var otherOverview = JsonDocument.Parse(
            await otherResponse.Content.ReadAsStreamAsync(cancellationToken));
        Assert.Equal(0, otherOverview.RootElement.GetProperty("projects").GetArrayLength());
    }
}
