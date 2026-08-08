using Microsoft.Extensions.Options;
using StoryVoice.Infrastructure.BookImports;

namespace StoryVoice.UnitTests;

public sealed class LocalBookFileStorageTests
{
    [Fact]
    public async Task Storage_writes_generated_safe_path_and_can_delete_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "storyvoice-storage-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalBookFileStorage(Options.Create(new BookStorageOptions
            {
                RootPath = root
            }));
            await using var content = new MemoryStream([1, 2, 3, 4]);

            var stored = await storage.SaveAsync(
                content,
                "../unsafe/book.epub",
                TestContext.Current.CancellationToken);

            Assert.EndsWith(".epub", stored.RelativePath);
            Assert.DoesNotContain("unsafe", stored.RelativePath);
            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(
                Path.Combine(root, stored.RelativePath),
                TestContext.Current.CancellationToken));

            await storage.DeleteAsync(stored.RelativePath, TestContext.Current.CancellationToken);
            Assert.False(File.Exists(Path.Combine(root, stored.RelativePath)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
