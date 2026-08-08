namespace StoryVoice.Application.Authentication;

public interface ICurrentUser
{
    Guid UserId { get; }
}
