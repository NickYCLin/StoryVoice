namespace StoryVoice.Application.Books;

public interface IBookMetadataCorrectionService
{
    Task<BookDetailsResponse?> UpdateAsync(
        Guid bookId,
        UpdateBookMetadataCorrectionsRequest request,
        CancellationToken cancellationToken);
}
