namespace StoryVoice.Application.BookImports;

public interface IBookImportParser
{
    IReadOnlySet<string> SupportedExtensions { get; }

    Task<ParsedBook> ParseAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken);
}
