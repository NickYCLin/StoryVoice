namespace StoryVoice.Application.BookImports;

public sealed class UnsupportedBookFormatException(string extension)
    : Exception($"尚不支援 {extension} 書籍格式。")
{
    public string Extension { get; } = extension;
}
