using System.Text;
using StoryVoice.Infrastructure.BookImports;

namespace StoryVoice.UnitTests;

public sealed class BookImportParserTests
{
    [Fact]
    public async Task Txt_parser_splits_chinese_chapter_headings()
    {
        const string text = """
            第一章 月下相逢
            故事從月色裡開始。

            第二章 彼岸燈火
            她在燈火盡頭回頭。
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var parser = new PlainTextBookParser();

        var result = await parser.ParseAsync(
            stream,
            "月下故事.txt",
            TestContext.Current.CancellationToken);

        Assert.Equal("月下故事", result.Title);
        Assert.Collection(
            result.Chapters,
            first =>
            {
                Assert.Equal("第一章 月下相逢", first.Title);
                Assert.Equal("故事從月色裡開始。", first.OriginalText);
            },
            second =>
            {
                Assert.Equal("第二章 彼岸燈火", second.Title);
                Assert.Equal("她在燈火盡頭回頭。", second.OriginalText);
            });
    }

    [Fact]
    public async Task Txt_parser_uses_single_chapter_when_no_heading_exists()
    {
        const string text = """
            第一段。

            第二段。
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var parser = new PlainTextBookParser();

        var result = await parser.ParseAsync(
            stream,
            "short-story.txt",
            TestContext.Current.CancellationToken);

        var chapter = Assert.Single(result.Chapters);
        Assert.Equal("short-story", chapter.Title);
        Assert.Equal(
            string.Join((char)10, ["第一段。", string.Empty, "第二段。"]),
            chapter.OriginalText);
    }

    [Fact]
    public async Task Txt_parser_rejects_empty_books()
    {
        await using var stream = new MemoryStream([]);
        var parser = new PlainTextBookParser();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseAsync(
            stream,
            "empty.txt",
            TestContext.Current.CancellationToken));

        Assert.Contains("內容是空的", exception.Message);
    }
}
