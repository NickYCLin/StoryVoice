namespace StoryVoice.Application.Collections;

public interface ICollectionService
{
    Task<IReadOnlyList<BookCollectionSummaryResponse>> ListAsync(
        CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse?> GetAsync(
        Guid collectionId,
        CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse> CreateAsync(
        CreateBookCollectionRequest request,
        CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse?> UpdateAsync(
        Guid collectionId,
        UpdateBookCollectionRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid collectionId, CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse?> AddBookAsync(
        Guid collectionId,
        AddCollectionBookRequest request,
        CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse?> UpdateBookAsync(
        Guid collectionId,
        Guid bookId,
        UpdateCollectionBookRequest request,
        CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse?> RemoveBookAsync(
        Guid collectionId,
        Guid bookId,
        CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse?> AddShareAsync(
        Guid collectionId,
        AddCollectionShareRequest request,
        CancellationToken cancellationToken);

    Task<BookCollectionDetailsResponse?> RevokeShareAsync(
        Guid collectionId,
        Guid shareId,
        CancellationToken cancellationToken);
}
