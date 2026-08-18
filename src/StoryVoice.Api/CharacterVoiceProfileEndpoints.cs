using Microsoft.AspNetCore.Mvc;
using StoryVoice.Application.Narrations;

namespace StoryVoice.Api;

public static class CharacterVoiceProfileEndpoints
{
    public static IEndpointRouteBuilder MapCharacterVoiceProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/character-profiles/{characterProfileId:guid}/voice-profiles")
            .WithTags("CharacterVoiceProfiles")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapGet("/", async (
            Guid characterProfileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profiles = await service.ListAsync(characterProfileId, cancellationToken);
            return profiles is null ? Results.NotFound() : Results.Ok(profiles);
        });

        group.MapGet("/clone-operations", async (
            Guid characterProfileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var operations = await service.ListCloneOperationsAsync(characterProfileId, cancellationToken);
            return operations is null ? Results.NotFound() : Results.Ok(operations);
        });

        group.MapPost("/clone-operations/{operationId:guid}/resume-activation", async (
            Guid characterProfileId,
            Guid operationId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.ResumeCloneActivationAsync(
                characterProfileId,
                operationId,
                cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/base", (
            Guid characterProfileId,
            IFormFile referenceAudio,
            IFormFile consentReceipt,
            [FromForm] bool rightsAttested,
            [FromForm] string expectedTranscript,
            HttpContext httpContext,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
            CreateClonedAsync(
                characterProfileId,
                "Base",
                null,
                expectedTranscript,
                referenceAudio,
                consentReceipt,
                rightsAttested,
                httpContext,
                service,
                cancellationToken))
        .DisableAntiforgery()
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/base/design", async (
            Guid characterProfileId,
            CreateDesignedVoiceProfileRequest request,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.CreateDesignedAsync(characterProfileId, "Base", sceneCode: null, request, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/scenes/{sceneCode}", (
            Guid characterProfileId,
            string sceneCode,
            IFormFile referenceAudio,
            IFormFile consentReceipt,
            [FromForm] bool rightsAttested,
            [FromForm] string expectedTranscript,
            HttpContext httpContext,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
            CreateClonedAsync(
                characterProfileId,
                "Scene",
                sceneCode,
                expectedTranscript,
                referenceAudio,
                consentReceipt,
                rightsAttested,
                httpContext,
                service,
                cancellationToken))
        .DisableAntiforgery()
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/scenes/{sceneCode}/design", async (
            Guid characterProfileId,
            string sceneCode,
            CreateDesignedVoiceProfileRequest request,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.CreateDesignedAsync(characterProfileId, "Scene", sceneCode, request, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{profileId:guid}/refresh-status", async (
            Guid characterProfileId,
            Guid profileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.RefreshStatusAsync(characterProfileId, profileId, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{profileId:guid}/confirm-transcript", async (
            Guid characterProfileId,
            Guid profileId,
            ConfirmVoiceProfileTranscriptRequest request,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.ConfirmTranscriptAsync(characterProfileId, profileId, request, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{profileId:guid}/rebuild", async (
            Guid characterProfileId,
            Guid profileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.RebuildAsync(characterProfileId, profileId, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{profileId:guid}/replace-with-clone", async (
            Guid characterProfileId,
            Guid profileId,
            IFormFile referenceAudio,
            IFormFile consentReceipt,
            [FromForm] bool rightsAttested,
            [FromForm] string expectedTranscript,
            HttpContext httpContext,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var sizeFailure = ValidateCloneUpload(referenceAudio, consentReceipt);
            if (sizeFailure is not null)
            {
                return sizeFailure;
            }

            await using var content = referenceAudio.OpenReadStream();
            await using var receiptContent = consentReceipt.OpenReadStream();
            var profile = await service.ReplaceDesignedWithCloneAsync(
                characterProfileId,
                profileId,
                expectedTranscript,
                content,
                Path.GetFileName(referenceAudio.FileName),
                receiptContent,
                Path.GetFileName(consentReceipt.FileName),
                rightsAttested,
                cancellationToken);
            return profile is null
                ? Results.NotFound()
                : Results.Created(
                    $"{httpContext.Request.PathBase}/api/character-profiles/{characterProfileId}/voice-profiles",
                    profile);
        })
        .DisableAntiforgery()
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapDelete("/{profileId:guid}", async (
            Guid characterProfileId,
            Guid profileId,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var deleted = await service.DeleteAsync(characterProfileId, profileId, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/{profileId:guid}/reference-audio", async (
            Guid characterProfileId,
            Guid profileId,
            HttpContext httpContext,
            ICharacterVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var audio = await service.GetReferenceAudioAsync(characterProfileId, profileId, cancellationToken);
            httpContext.Response.Headers.CacheControl = "private, no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return audio is null
                ? Results.NotFound()
                : Results.File(audio.AbsolutePath, audio.ContentType, enableRangeProcessing: true);
        });

        group.MapPost("/{profileId:guid}/preview", async (
            Guid characterProfileId,
            Guid profileId,
            PreviewVoiceProfileRequest request,
            HttpContext httpContext,
            ICharacterVoicePreviewService previewService,
            CancellationToken cancellationToken) =>
        {
            var preview = await previewService.PreviewAsync(characterProfileId, profileId, request, cancellationToken);
            if (preview is null)
            {
                return Results.NotFound();
            }

            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.File(preview.Content, preview.ContentType);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> CreateClonedAsync(
        Guid characterProfileId,
        string kind,
        string? sceneCode,
        string expectedTranscript,
        IFormFile referenceAudio,
        IFormFile consentReceipt,
        bool rightsAttested,
        HttpContext httpContext,
        ICharacterVoiceProfileService service,
        CancellationToken cancellationToken)
    {
        var sizeFailure = ValidateCloneUpload(referenceAudio, consentReceipt);
        if (sizeFailure is not null)
        {
            return sizeFailure;
        }

        await using var content = referenceAudio.OpenReadStream();
        await using var receiptContent = consentReceipt.OpenReadStream();
        var profile = await service.CreateClonedAsync(
            characterProfileId,
            kind,
            sceneCode,
            expectedTranscript,
            content,
            Path.GetFileName(referenceAudio.FileName),
            receiptContent,
            Path.GetFileName(consentReceipt.FileName),
            rightsAttested,
            cancellationToken);
        return profile is null
            ? Results.NotFound()
            : Results.Created($"{httpContext.Request.PathBase}/api/character-profiles/{characterProfileId}/voice-profiles", profile);
    }

    private static IResult? ValidateCloneUpload(
        IFormFile referenceAudio,
        IFormFile consentReceipt)
    {
        if (referenceAudio.Length == 0)
        {
            throw new ArgumentException("參考音檔不可為空白。", nameof(referenceAudio));
        }

        if (referenceAudio.Length > CharacterVoiceProfileLimits.MaximumReferenceAudioBytes)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Reference audio is too large",
                detail: "參考音檔不可超過 10 MiB。");
        }

        if (consentReceipt.Length == 0)
        {
            throw new ArgumentException("授權 receipt 不可為空白。", nameof(consentReceipt));
        }

        if (consentReceipt.Length > CharacterVoiceProfileLimits.MaximumConsentReceiptBytes)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Consent receipt is too large",
                detail: "授權 receipt 不可超過 32 KiB。");
        }

        return null;
    }
}
