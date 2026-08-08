using StoryVoice.Application.Insights;
using StoryVoice.Domain.Books;

namespace StoryVoice.UnitTests;

public sealed class ExtractiveSummaryGeneratorTests
{
    [Fact]
    public void Generate_preserves_exact_source_offsets_and_reading_order()
    {
        var book = Book.Create(Guid.NewGuid(), "月下故事", "比比工程師", "zh-TW", "story.txt");
        var first = book.AddChapter(1, "第一章", "  第一章第一句。第一章第二句。");
        var second = book.AddChapter(2, "第二章", "第二章唯一一句！");

        var result = ExtractiveSummaryGenerator.Generate(book.Chapters);

        Assert.Equal(64, result.SourceHash.Length);
        Assert.Equal([first.Id, second.Id], result.Excerpts.Select(item => item.ChapterId));
        Assert.Equal("第一章第一句。", result.Excerpts[0].Text);
        Assert.Equal("第二章唯一一句！", result.Excerpts[1].Text);
        foreach (var excerpt in result.Excerpts)
        {
            var chapter = book.Chapters.Single(item => item.Id == excerpt.ChapterId);
            Assert.Equal(
                excerpt.Text,
                chapter.OriginalText.Substring(excerpt.StartOffset, excerpt.Length));
        }
    }

    [Fact]
    public void Generate_is_deterministic_for_the_same_chapters()
    {
        var book = Book.Create(Guid.NewGuid(), "彼岸故事", "StoryVoice", "zh-TW", "story.epub");
        book.AddChapter(1, "序章", "故事從月色裡開始。後續內容不應進入第一版摘要。");

        var first = ExtractiveSummaryGenerator.Generate(book.Chapters);
        var second = ExtractiveSummaryGenerator.Generate(book.Chapters);

        Assert.Equal(first.SourceHash, second.SourceHash);
        Assert.Equal(first.Excerpts, second.Excerpts);
    }
}
