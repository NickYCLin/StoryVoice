using Microsoft.AspNetCore.Antiforgery;

namespace StoryVoice.Api;

public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (context.HttpContext.User.Identities.Any(identity =>
                identity.IsAuthenticated &&
                identity.AuthenticationType == CompanionAuthenticationDefaults.Scheme))
        {
            return await next(context);
        }

        await antiforgery.ValidateRequestAsync(context.HttpContext);
        return await next(context);
    }
}
