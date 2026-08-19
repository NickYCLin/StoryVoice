using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Series;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure.Narrations;

internal sealed record LocalCloneProfileAvailability(bool Available, string? Label);

internal sealed class LocalCloneProfileSynthesizer(
    StoryVoiceDbContext dbContext,
    ILocalCloneGatewayClient gatewayClient,
    IOptions<LocalClonePreviewOptions> options)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<LocalCloneProfileAvailability> GetAvailabilityAsync(
        Guid ownerId,
        Guid characterProfileId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAllowedProfile(characterProfileId, out var asset)
            || !await ProfileIsActiveAsync(ownerId, characterProfileId, cancellationToken))
        {
            return new LocalCloneProfileAvailability(false, null);
        }

        return new LocalCloneProfileAvailability(true, asset.Label);
    }

    public async Task<LocalCloneGatewayAudio> SynthesizeAsync(
        Guid ownerId,
        Guid characterProfileId,
        string text,
        CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty
            || characterProfileId == Guid.Empty
            || string.IsNullOrWhiteSpace(text)
            || !TryGetAllowedProfile(characterProfileId, out var asset)
            || !await ProfileIsActiveAsync(ownerId, characterProfileId, cancellationToken))
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.NotConfigured);
        }

        LocalClonePrivateAssets privateAssets;
        try
        {
            privateAssets = await ReadAndValidateAssetsAsync(asset, cancellationToken);
        }
        catch (LocalClonePreviewUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
            or DecoderFallbackException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.AssetInvalid,
                exception);
        }

        // This is the commit boundary. Client disconnects after submission must not
        // cancel the GPU operation and create an uncertain in-flight inference.
        return await gatewayClient.SynthesizeAsync(
            new LocalCloneGatewayRequest(
                text,
                privateAssets.CanonicalTranscript,
                privateAssets.ReferenceAudio),
            CancellationToken.None);
    }

    private Task<bool> ProfileIsActiveAsync(
        Guid ownerId,
        Guid characterProfileId,
        CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty || characterProfileId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        return dbContext.CharacterProfiles
            .AsNoTracking()
            .AnyAsync(
                profile => profile.OwnerId == ownerId
                    && profile.Id == characterProfileId
                    && profile.IsActive,
                cancellationToken);
    }

    private bool TryGetAllowedProfile(
        Guid characterProfileId,
        out LocalClonePreviewAssetOptions asset)
    {
        var expectedKey = characterProfileId.ToString("D");
        foreach (var candidate in options.Value.AllowedProfiles)
        {
            if (string.Equals(candidate.Key, expectedKey, StringComparison.Ordinal))
            {
                asset = candidate.Value;
                return true;
            }
        }

        asset = null!;
        return false;
    }

    private async Task<LocalClonePrivateAssets> ReadAndValidateAssetsAsync(
        LocalClonePreviewAssetOptions asset,
        CancellationToken cancellationToken)
    {
        var physicalRoot = PrivateAssetFileValidator.ResolveExistingPhysicalPath(
            Path.GetFullPath(options.Value.AssetRootPath),
            requireDirectory: true);
        var referencePath = PrivateAssetFileValidator.ResolvePrivateFile(
            physicalRoot,
            asset.ReferenceAudioRelativePath);
        var transcriptPath = PrivateAssetFileValidator.ResolvePrivateFile(
            physicalRoot,
            asset.TranscriptRelativePath);

        var referenceAudio = await PrivateAssetFileValidator.ReadBoundedAsync(
            referencePath,
            LocalClonePreviewOptions.MaximumReferenceAudioBytes,
            cancellationToken);
        LocalClonePcmWaveValidator.ValidateReference(referenceAudio);
        PrivateAssetFileValidator.VerifySha256(
            referenceAudio,
            asset.ExpectedReferenceAudioSha256);

        var transcriptBytes = await PrivateAssetFileValidator.ReadBoundedAsync(
            transcriptPath,
            LocalClonePreviewOptions.MaximumTranscriptBytes,
            cancellationToken);
        var canonicalTranscript = CharacterVoiceTranscriptCanonicalizer.Normalize(
            StrictUtf8.GetString(transcriptBytes));
        PrivateAssetFileValidator.VerifySha256(
            Encoding.UTF8.GetBytes(canonicalTranscript),
            asset.ExpectedTranscriptSha256);
        return new LocalClonePrivateAssets(referenceAudio, canonicalTranscript);
    }

    private sealed record LocalClonePrivateAssets(
        byte[] ReferenceAudio,
        string CanonicalTranscript);
}

internal static class PrivateAssetFileValidator
{
    public static string ResolvePrivateFile(string physicalRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "" or "." or ".."))
        {
            throw new IOException("Invalid private asset path.");
        }

        var candidate = Path.GetFullPath(Path.Combine(physicalRoot, relativePath));
        if (!IsWithinRoot(candidate, physicalRoot))
        {
            throw new IOException("Private asset path escapes its root.");
        }

        var physicalCandidate = ResolveExistingPhysicalPath(candidate, requireDirectory: false);
        if (!IsWithinRoot(physicalCandidate, physicalRoot)
            || Directory.Exists(physicalCandidate))
        {
            throw new IOException("Private asset target escapes its root.");
        }

        return physicalCandidate;
    }

    public static string ResolveExistingPhysicalPath(string path, bool requireDirectory)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath)
            ?? throw new IOException("Private asset path has no filesystem root.");
        var current = pathRoot;
        var relative = fullPath[pathRoot.Length..];
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     Path.AltDirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo info;
            if (Directory.Exists(next))
            {
                info = new DirectoryInfo(next);
            }
            else if (File.Exists(next))
            {
                info = new FileInfo(next);
            }
            else
            {
                throw new FileNotFoundException("Private asset does not exist.");
            }

            current = Path.GetFullPath(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? info.FullName);
        }

        if (requireDirectory && !Directory.Exists(current))
        {
            throw new DirectoryNotFoundException("Private asset root does not exist.");
        }

        return current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static async Task<byte[]> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
        {
            throw new InvalidDataException("Private asset size is outside the accepted bounds.");
        }

        var content = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(content, cancellationToken);
        return content;
    }

    public static void VerifySha256(ReadOnlySpan<byte> content, string expectedHex)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHex);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Invalid configured asset digest.", exception);
        }

        if (expected.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidDataException("Invalid configured asset digest.");
        }

        Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(content, actual);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new InvalidDataException("Private asset digest mismatch.");
        }
    }

    private static bool IsWithinRoot(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(candidate, root, comparison))
        {
            return true;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, comparison);
    }
}
