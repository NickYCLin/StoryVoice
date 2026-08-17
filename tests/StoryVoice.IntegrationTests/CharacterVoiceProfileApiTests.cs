using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StoryVoice.Application.Characters;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class CharacterVoiceProfileApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Voice_profile_endpoints_require_authentication_and_mutations_require_csrf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(client, cancellationToken);

        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var missingCsrfResponse = await client.PostAsJsonAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/base/design",
            new { voicePrompt = "溫柔、略帶沙啞的女聲" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrfResponse.StatusCode);
    }

    [Fact]
    public async Task Design_creation_is_fail_closed_for_base_and_scene_without_persisting_a_profile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(client, cancellationToken);

        using var createResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/base/design",
            new { voicePrompt = "溫柔、略帶沙啞的女聲" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.Contains(
            "voice_prompt",
            await createResponse.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.Ordinal);

        using var sceneResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/scenes/angry/design",
            new { voicePrompt = "生氣、提高音量的聲音" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, sceneResponse.StatusCode);

        using var listResponse = await client.GetAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles",
            cancellationToken);
        var profiles = await listResponse.Content
            .ReadFromJsonAsync<CharacterVoiceProfileResponse[]>(cancellationToken);
        Assert.NotNull(profiles);
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task An_existing_designed_profile_remains_listed_but_has_no_reference_audio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerAClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var ownerBClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(ownerAClient, cancellationToken);
        var profileId = await SeedDesignedVoiceProfileAsync(
            factory,
            characterProfileId,
            cancellationToken);

        var profiles = await ownerAClient.GetFromJsonAsync<CharacterVoiceProfileResponse[]>(
            $"/api/character-profiles/{characterProfileId}/voice-profiles",
            cancellationToken);
        Assert.Contains(Assert.IsType<CharacterVoiceProfileResponse[]>(profiles), profile => profile.Id == profileId);

        using var otherOwnerListResponse = await ownerBClient.GetAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, otherOwnerListResponse.StatusCode);

        using var referenceAudioResponse = await ownerAClient.GetAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/{profileId}/reference-audio",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, referenceAudioResponse.StatusCode);
    }

    [Fact]
    public async Task Preview_rejects_blank_or_overlong_text_and_unknown_profiles_before_ever_calling_3wa()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(client, cancellationToken);
        var profileId = await SeedDesignedVoiceProfileAsync(factory, characterProfileId, cancellationToken);

        using var blankResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/{profileId}/preview",
            new { text = "   " },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, blankResponse.StatusCode);

        using var overlongResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/{profileId}/preview",
            new { text = new string('a', 500) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, overlongResponse.StatusCode);

        using var unknownProfileResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/{Guid.NewGuid()}/preview",
            new { text = "你好" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownProfileResponse.StatusCode);
    }

    [Fact]
    public async Task Existing_designed_profile_preview_is_owner_scoped_CSRF_protected_and_rejected_without_calling_3wa()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fake = new FakeThreeWaSynthesisClient(
            [0x52, 0x49, 0x46, 0x46, 0x57, 0x41, 0x56, 0x45],
            "audio/wav");
        using var previewFactory = CreatePreviewFactory(fake);
        using var owner = await previewFactory.CreateAuthenticatedClientAsync(cancellationToken);
        using var otherOwner = await previewFactory.CreateAuthenticatedClientAsync(cancellationToken);
        using var anonymous = previewFactory.CreateClient();
        var characterProfileId = await CreateCharacterProfileAsync(owner, cancellationToken);
        var profileId = await SeedDesignedVoiceProfileAsync(previewFactory, characterProfileId, cancellationToken);
        var previewPath = $"/api/character-profiles/{characterProfileId}/voice-profiles/{profileId}/preview";

        using var anonymousResponse = await anonymous.PostAsJsonAsync(
            previewPath,
            new { text = "你好，台灣。" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var missingCsrfResponse = await owner.PostAsJsonAsync(
            previewPath,
            new { text = "你好，台灣。" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrfResponse.StatusCode);

        using var otherOwnerResponse = await otherOwner.PostWithCsrfAsync(
            previewPath,
            new { text = "你好，台灣。" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, otherOwnerResponse.StatusCode);
        Assert.Equal(0, fake.SubmitCount);

        using var response = await owner.PostWithCsrfAsync(
            previewPath,
            new { text = "  你好，台灣。  " },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "voice_prompt",
            await response.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.Ordinal);
        Assert.Equal(0, fake.SubmitCount);
        Assert.Equal(0, fake.StatusCount);
        Assert.Equal(0, fake.ResultCount);
        Assert.Equal(0, fake.DownloadCount);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData(null)]
    public async Task Preview_rejects_untyped_or_non_audio_artifacts_without_downloading_them(
        string? contentType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fake = new FakeThreeWaSynthesisClient([1, 2, 3], contentType);
        using var previewFactory = CreatePreviewFactory(fake);
        using var owner = await previewFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(owner, cancellationToken);
        var profileId = await SeedReadyCloneVoiceProfileAsync(previewFactory, characterProfileId, cancellationToken);

        using var response = await owner.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/{profileId}/preview",
            new { text = "你好" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, fake.ResultCount);
        Assert.Equal(0, fake.DownloadCount);
    }

    [Fact]
    public async Task Preview_rejects_audio_larger_than_the_configured_limit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fake = new FakeThreeWaSynthesisClient(new byte[(64 * 1024) + 1], "audio/wav");
        using var previewFactory = CreatePreviewFactory(fake, maximumAudioResponseBytes: 64 * 1024);
        using var owner = await previewFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(owner, cancellationToken);
        var profileId = await SeedReadyCloneVoiceProfileAsync(previewFactory, characterProfileId, cancellationToken);

        using var response = await owner.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/{profileId}/preview",
            new { text = "你好" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, fake.DownloadCount);
    }

    [Fact]
    public async Task Clone_upload_with_header_based_csrf_reaches_the_service_layer_instead_of_failing_form_binding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(client, cancellationToken);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(file, "referenceAudio", "sample.wav");
        content.Add(new StringContent("explicit_permission"), "consentType");

        using var response = await client.PostMultipartWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/base",
            content,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ThreeWaAiHub__ApiToken", body);
    }

    private static async Task<Guid> CreateCharacterProfileAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostWithCsrfAsync(
            "/api/character-profiles",
            new
            {
                canonicalName = $"測試角色-{Guid.NewGuid():N}",
                age = (string?)null,
                gender = (string?)null,
                birthday = (string?)null,
                personality = (string?)null,
                catchphrase = (string?)null,
                background = (string?)null,
                speakingStyle = (string?)null
            },
            cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Unexpected response: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        var created = await response.Content.ReadFromJsonAsync<CharacterProfileResponse>(cancellationToken);
        Assert.NotNull(created);
        return created.Id;
    }

    private static async Task<Guid> SeedDesignedVoiceProfileAsync(
        WebApplicationFactory<Program> appFactory,
        Guid characterProfileId,
        CancellationToken cancellationToken)
    {
        await using var scope = appFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var ownerId = await dbContext.CharacterProfiles
            .Where(profile => profile.Id == characterProfileId)
            .Select(profile => profile.OwnerId)
            .SingleAsync(cancellationToken);
        var profile = CharacterVoiceProfile.CreateDesign(
            Guid.NewGuid(),
            ownerId,
            characterProfileId,
            CharacterVoiceProfileKind.Base,
            null,
            "溫柔、略帶沙啞的台灣華語女聲",
            DateTimeOffset.UtcNow);
        dbContext.CharacterVoiceProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        return profile.Id;
    }

    private static async Task<Guid> SeedReadyCloneVoiceProfileAsync(
        WebApplicationFactory<Program> appFactory,
        Guid characterProfileId,
        CancellationToken cancellationToken)
    {
        await using var scope = appFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var ownerId = await dbContext.CharacterProfiles
            .Where(profile => profile.Id == characterProfileId)
            .Select(profile => profile.OwnerId)
            .SingleAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var profile = CharacterVoiceProfile.CreateClone(
            Guid.NewGuid(),
            ownerId,
            characterProfileId,
            CharacterVoiceProfileKind.Base,
            null,
            CharacterVoiceConsentTypes.SelfRecorded,
            "seeded/reference.wav",
            new string('a', 64),
            8,
            ownerId,
            now);
        profile.AttachDraftTranscript("seeded-profile-task", "你好，台灣。", now);
        profile.ConfirmTranscript("你好，台灣。", now);
        dbContext.CharacterVoiceProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        return profile.Id;
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreatePreviewFactory(
        FakeThreeWaSynthesisClient fake,
        int maximumAudioResponseBytes = 20 * 1024 * 1024) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                $"{ThreeWaAiHubOptions.SectionName}:MaximumAudioResponseBytes",
                maximumAudioResponseBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IThreeWaSynthesisClient>();
                services.AddSingleton(fake);
                services.AddSingleton<IThreeWaSynthesisClient>(provider =>
                    provider.GetRequiredService<FakeThreeWaSynthesisClient>());
            });
        });

    private sealed class FakeThreeWaSynthesisClient(byte[] audio, string? contentType)
        : IThreeWaSynthesisClient
    {
        public byte[] Audio { get; } = audio;
        public ThreeWaSynthesisRequest? Request { get; private set; }
        public int SubmitCount { get; private set; }
        public int StatusCount { get; private set; }
        public int ResultCount { get; private set; }
        public int DownloadCount { get; private set; }

        public Task<ThreeWaSynthesisTaskHandle> SubmitAsync(
            ThreeWaSynthesisRequest request,
            CancellationToken cancellationToken)
        {
            SubmitCount++;
            Request = request;
            return Task.FromResult(new ThreeWaSynthesisTaskHandle(
                "731245",
                "fake-status",
                "fake-result",
                "fake-artifacts/{artifact_id}"));
        }

        public Task<string> GetTaskStatusAsync(string statusUrl, CancellationToken cancellationToken)
        {
            StatusCount++;
            return Task.FromResult("completed");
        }

        public Task<IReadOnlyList<ThreeWaSynthesisArtifact>> GetResultArtifactsAsync(
            string resultUrl,
            CancellationToken cancellationToken)
        {
            ResultCount++;
            IReadOnlyList<ThreeWaSynthesisArtifact> result =
                [new ThreeWaSynthesisArtifact("90210", contentType)];
            return Task.FromResult(result);
        }

        public async Task DownloadArtifactAsync(
            string artifactUrlTemplate,
            string artifactId,
            Stream destination,
            CancellationToken cancellationToken)
        {
            DownloadCount++;
            await destination.WriteAsync(Audio, cancellationToken);
        }
    }
}
