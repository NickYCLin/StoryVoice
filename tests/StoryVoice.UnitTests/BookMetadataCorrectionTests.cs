using StoryVoice.Domain.Books;

namespace StoryVoice.UnitTests;

public sealed class BookMetadataCorrectionTests
{
    [Fact]
    public void Corrections_override_display_metadata_without_mutating_source_metadata()
    {
        var book = Book.CreateExternal(
            Guid.NewGuid(),
            "來源書名",
            "來源作者",
            "zh-TW",
            "books-com-tw",
            "E123456789",
            "https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E123456789",
            "https://im2.book.com.tw/image/source.jpg");

        book.SetMetadataCorrections(" 校正書名 ", "校正作者", "https://example.test/corrected.jpg");

        Assert.Equal("來源書名", book.Title);
        Assert.Equal("來源作者", book.Author);
        Assert.Equal("https://im2.book.com.tw/image/source.jpg", book.CoverImageUrl);
        Assert.Equal("校正書名", book.TitleCorrection);
        Assert.Equal("校正作者", book.AuthorCorrection);
        Assert.Equal("https://example.test/corrected.jpg", book.CoverImageUrlCorrection);
    }

    [Fact]
    public void Empty_corrections_restore_source_metadata()
    {
        var book = Book.CreateExternal(
            Guid.NewGuid(), "來源書名", "來源作者", "zh-TW", "books-com-tw", "E123456788",
            "https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E123456788", null);
        book.SetMetadataCorrections("替代書名", "替代作者", "https://example.test/cover.jpg");

        book.SetMetadataCorrections(null, " ", null);

        Assert.Null(book.TitleCorrection);
        Assert.Null(book.AuthorCorrection);
        Assert.Null(book.CoverImageUrlCorrection);
    }

    [Fact]
    public void Uploaded_books_cannot_receive_provider_metadata_corrections()
    {
        var book = Book.Create(Guid.NewGuid(), "書名", "作者", "zh-TW", "book.txt");

        Assert.Throws<InvalidOperationException>(() =>
            book.SetMetadataCorrections("替代書名", null, null));
    }
}
