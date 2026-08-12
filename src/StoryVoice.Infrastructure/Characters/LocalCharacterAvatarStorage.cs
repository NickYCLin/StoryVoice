using Microsoft.Extensions.Options;

namespace StoryVoice.Infrastructure.Characters;

/// <summary>
/// Private, owner-scoped storage for character library avatars — mirrors
/// <c>LocalCharacterVoiceAudioStorage</c>'s path-traversal-safe relative-path resolution, just for
/// image files instead of reference audio.
/// </summary>
public sealed class LocalCharacterAvatarStorage(IOptions<CharacterAvatarStorageOptions> options)
{
    private static readonly Dictionary<string, string> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    private readonly string rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<(string RelativePath, string ContentType)> SaveAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        if (!ContentTypesByExtension.TryGetValue(extension, out var contentType))
        {
            throw new NotSupportedException($"不支援的頭像格式：{extension}。目前只接受 JPG／PNG／WEBP。");
        }

        var now = DateTimeOffset.UtcNow;
        var relativePath = Path.Combine(
            now.ToString("yyyy"),
            now.ToString("MM"),
            $"{Guid.NewGuid():N}{extension}");
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var destination = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await content.CopyToAsync(destination, cancellationToken);
        }

        return (relativePath, contentType);
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

    public string ResolveFullPath(string relativePath) => Resolve(relativePath);

    public string ResolveContentType(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return ContentTypesByExtension.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";
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
