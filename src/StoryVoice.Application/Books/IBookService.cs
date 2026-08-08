namespace StoryVoice.Application.Books;

public interface IBookService
{
    Task<BookDetailsResponse> CreateAsync(CreateBookRequest request, CancellationToken cancellationToken);

    Task<BookDetailsResponse> CreateImportedAsync(
        CreateImportedBookRequest request,
        CancellationToken cancellationToken);

    Task<BookDetailsResponse?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookSummaryResponse>> ListAsync(CancellationToken cancellationToken);
}
