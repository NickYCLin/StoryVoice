using StoryVoice.Domain.Books;

namespace StoryVoice.UnitTests;

public sealed class BookTests
{
    [Fact]
    public void Create_book_and_add_chapter_preserves_identity_and_order()
    {
        var ownerId = Guid.NewGuid();
        var book = Book.Create(ownerId, "月下故事", "比比工程師", "zh-TW", "story.epub");

        var chapter = book.AddChapter(1, "序章", "故事從月色裡開始。");

        Assert.NotEqual(Guid.Empty, book.Id);
        Assert.Equal(BookStatus.Uploaded, book.Status);
        Assert.Equal("epub", book.FileType);
        Assert.Same(chapter, Assert.Single(book.Chapters));
    }

    [Fact]
    public void Duplicate_chapter_number_is_rejected()
    {
        var ownerId = Guid.NewGuid();
        var book = Book.Create(ownerId, "月下故事", "比比工程師", "zh-TW", "story.epub");
        book.AddChapter(1, "序章", "第一段");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            book.AddChapter(1, "重複", "第二段"));

        Assert.Contains("已存在", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void External_book_preserves_and_refreshes_official_tts_metadata()
    {
        var book = Book.CreateExternal(
            Guid.NewGuid(),
            "月下故事",
            "比比工程師",
            "zh-TW",
            "books-com-tw",
            "E050145360",
            "https://www.books.com.tw/products/E050145360",
            null,
            nativeTtsAvailable: true,
            ebookLayout: EbookLayout.Reflowable);

        Assert.True(book.NativeTtsAvailable);
        Assert.Equal(EbookLayout.Reflowable, book.EbookLayout);

        book.UpdateExternalMetadata(
            "月下故事",
            "比比工程師",
            "zh-TW",
            "https://www.books.com.tw/products/E050145360",
            null,
            nativeTtsAvailable: false,
            ebookLayout: EbookLayout.Fixed);

        Assert.False(book.NativeTtsAvailable);
        Assert.Equal(EbookLayout.Fixed, book.EbookLayout);
    }
}
