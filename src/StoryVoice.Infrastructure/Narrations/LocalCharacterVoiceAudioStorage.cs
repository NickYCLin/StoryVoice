using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace StoryVoice.Infrastructure.Narrations;

public sealed record StoredCharacterVoiceAudio(string RelativePath, string Sha256Hex);

/// <summary>
/// Private, owner-scoped storage for character reference recordings — never a publicly reachable
/// static path. Mirrors <c>LocalBookFileStorage</c>'s path-traversal-safe relative-path resolution.
/// </summary>
public sealed class LocalCharacterVoiceAudioStorage(IOptions<CharacterVoiceStorageOptions> options)
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".wav" };

    private readonly string rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredCharacterVoiceAudio> SaveAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new NotSupportedException($"不支援的參考音檔格式：{extension}。目前只接受 WAV。");
        }

        var now = DateTimeOffset.UtcNow;
        var relativePath = Path.Combine(
            now.ToString("yyyy"),
            now.ToString("MM"),
            $"{Guid.NewGuid():N}{extension}");
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            using var sha256 = SHA256.Create();
            await using (var destination = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var hashing = new CryptoStream(destination, sha256, CryptoStreamMode.Write))
            {
                await content.CopyToAsync(hashing, cancellationToken);
                await hashing.FlushAsync(cancellationToken);
            }

            var hashHex = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            return new StoredCharacterVoiceAudio(relativePath, hashHex);
        }
        catch
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (IOException)
            {
                // Preserve the original upload/validation failure. The caller has not created a
                // durable operation yet, and a later storage sweep may remove this partial file.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above: never replace the primary failure with cleanup diagnostics.
            }

            throw;
        }
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
