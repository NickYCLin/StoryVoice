namespace StoryVoice.Application.Library;

public interface ILibraryStatusService
{
    Task<IReadOnlyList<LibraryBookStatusResponse>> ListAsync(CancellationToken cancellationToken);
}
