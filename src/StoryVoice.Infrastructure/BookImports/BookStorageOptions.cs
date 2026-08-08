namespace StoryVoice.Infrastructure.BookImports;

public sealed class BookStorageOptions
{
    public const string SectionName = "BookStorage";

    public string RootPath { get; set; } = "storage/books";
}
