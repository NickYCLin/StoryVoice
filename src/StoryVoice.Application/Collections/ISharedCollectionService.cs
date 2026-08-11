namespace StoryVoice.Application.Collections;

public interface ISharedCollectionService
{
    Task<IReadOnlyList<SharedCollectionSummaryResponse>> ListSharedWithMeAsync(
        CancellationToken cancellationToken);

    Task<SharedCollectionDetailsResponse?> GetSharedAsync(
        Guid collectionId,
        CancellationToken cancellationToken);

    Task<SharedCollectionBookContentResponse?> GetSharedBookContentAsync(
        Guid collectionId,
        Guid bookId,
        CancellationToken cancellationToken);
}
