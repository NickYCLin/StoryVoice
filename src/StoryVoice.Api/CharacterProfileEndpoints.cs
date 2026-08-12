using StoryVoice.Application.Characters;

namespace StoryVoice.Api;

public static class CharacterProfileEndpoints
{
    private const long MaximumAvatarBytes = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapCharacterProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/character-profiles")
            .WithTags("CharacterProfiles")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapGet("/", async (
            ICharacterProfileService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        group.MapGet("/{characterProfileId:guid}", async (
            Guid characterProfileId,
            ICharacterProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.GetAsync(characterProfileId, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        group.MapPost("/", async (
            CreateCharacterProfileRequest request,
            HttpContext httpContext,
            ICharacterProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"{httpContext.Request.PathBase}/api/character-profiles/{profile.Id}", profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{characterProfileId:guid}", async (
            Guid characterProfileId,
            UpdateCharacterProfileRequest request,
            ICharacterProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.UpdateAsync(characterProfileId, request, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{characterProfileId:guid}/avatar", async (
            Guid characterProfileId,
            IFormFile avatar,
            ICharacterProfileService service,
            CancellationToken cancellationToken) =>
        {
            if (avatar.Length == 0)
            {
                throw new ArgumentException("頭像檔案不可為空白。", nameof(avatar));
            }

            if (avatar.Length > MaximumAvatarBytes)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Avatar is too large",
                    detail: "頭像檔案不可超過 5 MiB。");
            }

            await using var content = avatar.OpenReadStream();
            var profile = await service.SetAvatarAsync(
                characterProfileId,
                content,
                Path.GetFileName(avatar.FileName),
                cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .DisableAntiforgery()
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/{characterProfileId:guid}/avatar", async (
            Guid characterProfileId,
            ICharacterProfileService service,
            CancellationToken cancellationToken) =>
        {
            var avatar = await service.GetAvatarAsync(characterProfileId, cancellationToken);
            return avatar is null
                ? Results.NotFound()
                : Results.File(avatar.AbsolutePath, avatar.ContentType, enableRangeProcessing: true);
        });

        group.MapDelete("/{characterProfileId:guid}", async (
            Guid characterProfileId,
            ICharacterProfileService service,
            CancellationToken cancellationToken) =>
        {
            var deleted = await service.DeleteAsync(characterProfileId, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }
}
