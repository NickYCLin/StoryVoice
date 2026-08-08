using StoryVoice.Domain.Books;

namespace StoryVoice.UnitTests;

public sealed class BookTests
{
    [Fact]
    public void Create_book_and_add_chapter_preserves_identity_and_order()
    {
        var book = Book.Create("月下故事", "比比工程師", "zh-TW", "story.epub");

        var chapter = book.AddChapter(1, "序章", "故事從月色裡開始。");

        Assert.NotEqual(Guid.Empty, book.Id);
        Assert.Equal(BookStatus.Uploaded, book.Status);
        Assert.Equal("epub", book.FileType);
        Assert.Same(chapter, Assert.Single(book.Chapters));
    }

    [Fact]
    public void Duplicate_chapter_number_is_rejected()
    {
        var book = Book.Create("月下故事", "比比工程師", "zh-TW", "story.epub");
        book.AddChapter(1, "序章", "第一段");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            book.AddChapter(1, "重複", "第二段"));

        Assert.Contains("已存在", exception.Message, StringComparison.Ordinal);
    }
}
