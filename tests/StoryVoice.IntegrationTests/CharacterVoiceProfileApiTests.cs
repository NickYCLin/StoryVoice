using System.Net;
using System.Net.Http.Json;
using StoryVoice.Application.Narrations;
using StoryVoice.Application.Series;

namespace StoryVoice.IntegrationTests;

public sealed class CharacterVoiceProfileApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Voice_profile_endpoints_require_authentication_and_mutations_require_csrf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var (seriesId, characterId) = await CreateSeriesWithCharacterAsync(client, cancellationToken);

        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var missingCsrfResponse = await client.PostAsJsonAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/base/design",
            new { voicePrompt = "溫柔、略帶沙啞的女聲" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrfResponse.StatusCode);
    }

    [Fact]
    public async Task A_designed_base_profile_is_ready_immediately_and_a_second_base_profile_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var (seriesId, characterId) = await CreateSeriesWithCharacterAsync(client, cancellationToken);

        using var createResponse = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/base/design",
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
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles",
            cancellationToken);
        var profiles = await listResponse.Content
            .ReadFromJsonAsync<CharacterVoiceProfileResponse[]>(cancellationToken);
        Assert.NotNull(profiles);
        Assert.Contains(profiles, profile => profile.Id == created.Id);

        using var duplicateResponse = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/base/design",
            new { voicePrompt = "另一種聲音描述" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task A_designed_scene_profile_can_coexist_with_a_base_profile_for_the_same_character()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateAuthenticatedClientAsync(cancellationToken);
        var (seriesId, characterId) = await CreateSeriesWithCharacterAsync(client, cancellationToken);

        using var baseResponse = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/base/design",
            new { voicePrompt = "平常說話的聲音" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, baseResponse.StatusCode);

        using var angryResponse = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/scenes/angry/design",
            new { voicePrompt = "生氣、提高音量的聲音" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, angryResponse.StatusCode);
        var angryProfile = await angryResponse.Content
            .ReadFromJsonAsync<CharacterVoiceProfileResponse>(cancellationToken);
        Assert.NotNull(angryProfile);
        Assert.Equal("Scene", angryProfile.Kind);
        Assert.Equal("angry", angryProfile.SceneCode);

        using var duplicateSceneResponse = await client.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/scenes/angry/design",
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
        var (seriesId, characterId) = await CreateSeriesWithCharacterAsync(ownerAClient, cancellationToken);

        using var createResponse = await ownerAClient.PostWithCsrfAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/base/design",
            new { voicePrompt = "溫柔、略帶沙啞的女聲" },
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CharacterVoiceProfileResponse>(cancellationToken);
        Assert.NotNull(created);

        using var otherOwnerListResponse = await ownerBClient.GetAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, otherOwnerListResponse.StatusCode);

        using var referenceAudioResponse = await ownerAClient.GetAsync(
            $"/api/series/{seriesId}/characters/{characterId}/voice-profiles/{created.Id}/reference-audio",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, referenceAudioResponse.StatusCode);
    }

    private static async Task<(Guid SeriesId, Guid CharacterId)> CreateSeriesWithCharacterAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var seriesResponse = await client.PostWithCsrfAsync(
            "/api/series",
            new
            {
                name = $"聲線測試系列-{Guid.NewGuid():N}",
                narratorProvider = "edge",
                narratorVoice = "zh-TW-YunJheNeural",
                narratorRate = "-5%",
                narratorPitch = "+0Hz",
                narratorVolume = "+0%",
                defaultSpeakerPauseMs = 350
            },
            cancellationToken);
        seriesResponse.EnsureSuccessStatusCode();
        var series = await seriesResponse.Content.ReadFromJsonAsync<StorySeriesDetailsResponse>(cancellationToken);
        Assert.NotNull(series);

        using var characterResponse = await client.PostWithCsrfAsync(
            $"/api/series/{series.Id}/characters",
            new
            {
                canonicalName = "測試角色",
                role = "Supporting",
                voiceProvider = "3wa-voxcpm2",
                voice = "custom",
                rate = "+0%",
                pitch = "+0Hz",
                volume = "+0%",
                notes = (string?)null
            },
            cancellationToken);
        Assert.True(
            characterResponse.StatusCode == HttpStatusCode.OK,
            $"Unexpected response: {await characterResponse.Content.ReadAsStringAsync(cancellationToken)}");
        var withCharacter = await characterResponse.Content
            .ReadFromJsonAsync<StorySeriesDetailsResponse>(cancellationToken);
        Assert.NotNull(withCharacter);
        var character = Assert.Single(withCharacter.Characters, c => c.CanonicalName == "測試角色");
        return (series.Id, character.Id);
    }
}
