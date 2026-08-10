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
    public async Task Txt_parser_detects_utf16_bom_and_splits_chinese_episode_headings()
    {
        const string text = """
            第一話 休假
            故事從休假日開始。

            第二話 夜行
            月色照亮返程。
            """;
        var encoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: true,
            throwOnInvalidBytes: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        await using var stream = new MemoryStream(bytes);
        var parser = new PlainTextBookParser();

        var result = await parser.ParseAsync(
            stream,
            "特殊傳說第三部.txt",
            TestContext.Current.CancellationToken);

        Assert.Collection(
            result.Chapters,
            first =>
            {
                Assert.Equal("第一話 休假", first.Title);
                Assert.Equal("故事從休假日開始。", first.OriginalText);
            },
            second =>
            {
                Assert.Equal("第二話 夜行", second.Title);
                Assert.Equal("月色照亮返程。", second.OriginalText);
            });
    }

    [Fact]
    public async Task Txt_parser_accepts_episode_heading_without_title_and_with_colon()
    {
        const string text = """
            第一話
            序幕。

            第二話：夜行
            月色照亮返程。
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var parsed = await new PlainTextBookParser().ParseAsync(
            stream,
            "story.txt",
            TestContext.Current.CancellationToken);

        Assert.Collection(
            parsed.Chapters,
            first =>
            {
                Assert.Equal("第一話", first.Title);
                Assert.Equal("序幕。", first.OriginalText);
            },
            second =>
            {
                Assert.Equal("第二話：夜行", second.Title);
                Assert.Equal("月色照亮返程。", second.OriginalText);
            });
    }

    [Fact]
    public async Task Txt_parser_does_not_treat_chinese_topic_word_as_episode_heading()
    {
        const string text = """
            第一話 休假
            第一話題是返校前的準備。

            第二話 夜行
            月色照亮返程。
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var parsed = await new PlainTextBookParser().ParseAsync(
            stream,
            "story.txt",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, parsed.Chapters.Count);
        Assert.Equal("第一話 休假", parsed.Chapters[0].Title);
        Assert.Equal("第一話題是返校前的準備。", parsed.Chapters[0].OriginalText);
        Assert.Equal("第二話 夜行", parsed.Chapters[1].Title);
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
