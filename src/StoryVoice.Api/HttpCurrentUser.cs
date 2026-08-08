using System.Security.Claims;
using StoryVoice.Application.Authentication;

namespace StoryVoice.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var userId)
                ? userId
                : throw new InvalidOperationException("目前要求沒有有效的 StoryVoice 使用者。");
        }
    }
}
