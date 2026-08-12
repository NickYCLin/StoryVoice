using System.Net;
using System.Net.Http.Json;
using StoryVoice.Application.Characters;
using StoryVoice.Application.Narrations;

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
    public async Task A_designed_base_profile_is_ready_immediately_and_a_second_base_profile_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(client, cancellationToken);

        using var createResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/base/design",
            new { voicePrompt = "溫柔、略帶沙啞的女聲" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CharacterVoiceProfileResponse>(cancellationToken);
        Assert.NotNull(created);
        Assert.Equal("Base", created.Kind);
        Assert.Equal("Design", created.Mode);
        Assert.Equal("Ready", created.Status);
        Assert.Null(created.SceneCode);

        using var listResponse = await client.GetAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles",
            cancellationToken);
        var profiles = await listResponse.Content
            .ReadFromJsonAsync<CharacterVoiceProfileResponse[]>(cancellationToken);
        Assert.NotNull(profiles);
        Assert.Contains(profiles, profile => profile.Id == created.Id);

        using var duplicateResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/base/design",
            new { voicePrompt = "另一種聲音描述" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task A_designed_scene_profile_can_coexist_with_a_base_profile_for_the_same_character()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(client, cancellationToken);

        using var baseResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/base/design",
            new { voicePrompt = "平常說話的聲音" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, baseResponse.StatusCode);

        using var angryResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/scenes/angry/design",
            new { voicePrompt = "生氣、提高音量的聲音" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, angryResponse.StatusCode);
        var angryProfile = await angryResponse.Content
            .ReadFromJsonAsync<CharacterVoiceProfileResponse>(cancellationToken);
        Assert.NotNull(angryProfile);
        Assert.Equal("Scene", angryProfile.Kind);
        Assert.Equal("angry", angryProfile.SceneCode);

        using var duplicateSceneResponse = await client.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/scenes/angry/design",
            new { voicePrompt = "又一種生氣的聲音" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateSceneResponse.StatusCode);
    }

    [Fact]
    public async Task Voice_profiles_are_owner_scoped_and_a_designed_profile_has_no_reference_audio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerAClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        using var ownerBClient = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var characterProfileId = await CreateCharacterProfileAsync(ownerAClient, cancellationToken);

        using var createResponse = await ownerAClient.PostWithCsrfAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/base/design",
            new { voicePrompt = "溫柔、略帶沙啞的女聲" },
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CharacterVoiceProfileResponse>(cancellationToken);
        Assert.NotNull(created);

        using var otherOwnerListResponse = await ownerBClient.GetAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, otherOwnerListResponse.StatusCode);

        using var referenceAudioResponse = await ownerAClient.GetAsync(
            $"/api/character-profiles/{characterProfileId}/voice-profiles/{created.Id}/reference-audio",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, referenceAudioResponse.StatusCode);
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
}
