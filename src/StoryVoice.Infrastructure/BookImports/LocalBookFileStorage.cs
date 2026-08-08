using Microsoft.Extensions.Options;
using StoryVoice.Application.BookImports;

namespace StoryVoice.Infrastructure.BookImports;

public sealed class LocalBookFileStorage(IOptions<BookStorageOptions> options) : IBookFileStorage
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".epub", ".txt" };

    private readonly string rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredBookFile> SaveAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new UnsupportedBookFormatException(extension);
        }

        var now = DateTimeOffset.UtcNow;
        var relativePath = Path.Combine(
            now.ToString("yyyy"),
            now.ToString("MM"),
            $"{Guid.NewGuid():N}{extension}");
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var destination = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await content.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);

        return new StoredBookFile(relativePath);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Resolve(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Storage path must be relative.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage path escapes the configured root.", nameof(relativePath));
        }

        return fullPath;
    }
}
