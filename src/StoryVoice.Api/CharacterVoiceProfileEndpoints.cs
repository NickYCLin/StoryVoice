using StoryVoice.Application.Narrations;

namespace StoryVoice.Api;

public static class CharacterVoiceProfileEndpoints
{
    private const long MaximumReferenceAudioBytes = 20 * 1024 * 1024;

    public static IEndpointRouteBuilder MapCharacterVoiceProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/series/{seriesId:guid}/characters/{characterId:guid}/voice-profiles")
            .WithTags("CharacterVoiceProfiles")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapGet("/", async (
            Guid seriesId,
            Guid characterId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profiles = await service.ListAsync(seriesId, characterId, cancellationToken);
            return profiles is null ? Results.NotFound() : Results.Ok(profiles);
        });

        group.MapPost("/base", (
            Guid seriesId,
            Guid characterId,
            IFormFile referenceAudio,
            string consentType,
            HttpContext httpContext,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
            CreateClonedAsync(seriesId, characterId, "Base", sceneCode: null, consentType, referenceAudio, httpContext, service, cancellationToken))
        .DisableAntiforgery()
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/base/design", async (
            Guid seriesId,
            Guid characterId,
            CreateDesignedVoiceProfileRequest request,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.CreateDesignedAsync(seriesId, characterId, "Base", sceneCode: null, request, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/scenes/{sceneCode}", (
            Guid seriesId,
            Guid characterId,
            string sceneCode,
            IFormFile referenceAudio,
            string consentType,
            HttpContext httpContext,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
            CreateClonedAsync(seriesId, characterId, "Scene", sceneCode, consentType, referenceAudio, httpContext, service, cancellationToken))
        .DisableAntiforgery()
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/scenes/{sceneCode}/design", async (
            Guid seriesId,
            Guid characterId,
            string sceneCode,
            CreateDesignedVoiceProfileRequest request,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.CreateDesignedAsync(seriesId, characterId, "Scene", sceneCode, request, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{profileId:guid}/refresh-status", async (
            Guid seriesId,
            Guid characterId,
            Guid profileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.RefreshStatusAsync(seriesId, profileId, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{profileId:guid}/confirm-transcript", async (
            Guid seriesId,
            Guid characterId,
            Guid profileId,
            ConfirmVoiceProfileTranscriptRequest request,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.ConfirmTranscriptAsync(seriesId, profileId, request, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{profileId:guid}/rebuild", async (
            Guid seriesId,
            Guid characterId,
            Guid profileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.RebuildAsync(seriesId, profileId, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapDelete("/{profileId:guid}", async (
            Guid seriesId,
            Guid characterId,
            Guid profileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var deleted = await service.DeleteAsync(seriesId, profileId, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/{profileId:guid}/reference-audio", async (
            Guid seriesId,
            Guid characterId,
            Guid profileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var audio = await service.GetReferenceAudioAsync(seriesId, profileId, cancellationToken);
            return audio is null
                ? Results.NotFound()
                : Results.File(audio.AbsolutePath, audio.ContentType, enableRangeProcessing: true);
        });

        return endpoints;
    }

    private static async Task<IResult> CreateClonedAsync(
        Guid seriesId,
        Guid characterId,
        string kind,
        string? sceneCode,
        string consentType,
        IFormFile referenceAudio,
        HttpContext httpContext,
        ICharacterVoiceProfileService service,
        CancellationToken cancellationToken)
    {
        if (referenceAudio.Length == 0)
        {
            throw new ArgumentException("參考音檔不可為空白。", nameof(referenceAudio));
        }

        if (referenceAudio.Length > MaximumReferenceAudioBytes)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Reference audio is too large",
                detail: "參考音檔不可超過 20 MiB。");
        }

        await using var content = referenceAudio.OpenReadStream();
        var profile = await service.CreateClonedAsync(
            seriesId,
            characterId,
            kind,
            sceneCode,
            consentType,
            content,
            Path.GetFileName(referenceAudio.FileName),
            cancellationToken);
        return profile is null
            ? Results.NotFound()
            : Results.Created($"{httpContext.Request.PathBase}/api/series/{seriesId}/characters/{characterId}/voice-profiles", profile);
    }
}
