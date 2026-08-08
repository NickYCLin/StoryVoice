using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Api;

public static class CompanionAuthenticationDefaults
{
    public const string Scheme = "StoryVoiceCompanion";
    public const string ScopeClaim = "storyvoice_scope";
    public const string BookshelfSyncScope = "bookshelf:sync";
}

public sealed class CompanionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CompanionTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var rawToken = authorization["Bearer ".Length..].Trim();
        var token = await tokenService.ValidateAsync(rawToken, Context.RequestAborted);
        if (token is null)
        {
            return AuthenticateResult.Fail("Companion access token is invalid or expired.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, token.UserId.ToString()),
            new Claim(ClaimTypes.Name, token.Email),
            new Claim(ClaimTypes.Email, token.Email),
            new Claim(CompanionAuthenticationDefaults.ScopeClaim, CompanionAuthenticationDefaults.BookshelfSyncScope)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
