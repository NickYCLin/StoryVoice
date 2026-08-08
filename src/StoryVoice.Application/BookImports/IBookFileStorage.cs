namespace StoryVoice.Application.BookImports;

public interface IBookFileStorage
{
    Task<StoredBookFile> SaveAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed record StoredBookFile(string RelativePath);
