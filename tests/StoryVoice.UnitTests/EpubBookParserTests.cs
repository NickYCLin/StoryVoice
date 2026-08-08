using StoryVoice.Infrastructure.BookImports;
using StoryVoice.Tests.Shared;

namespace StoryVoice.UnitTests;

public sealed class EpubBookParserTests
{
    [Fact]
    public async Task Epub_parser_reads_metadata_toc_and_chapters()
    {
        await using var stream = new MemoryStream(MinimalEpub.Create());
        var parser = new EpubBookParser();

        var result = await parser.ParseAsync(
            stream,
            "moon.epub",
            TestContext.Current.CancellationToken);

        Assert.Equal("月下 EPUB", result.Title);
        Assert.Equal("StoryVoice", result.Author);
        Assert.Equal("zh-TW", result.Language);
        Assert.Collection(
            result.Chapters,
            first =>
            {
                Assert.Equal("第一章 月影", first.Title);
                Assert.Contains("月色落在窗前。", first.OriginalText);
                Assert.DoesNotContain("<p>", first.OriginalText);
            },
            second =>
            {
                Assert.Equal("第二章 火蝶", second.Title);
                Assert.Contains("火蝶飛向彼岸。", second.OriginalText);
            });
    }
}
