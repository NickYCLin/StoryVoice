using System.Security.Cryptography;
using System.Security;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Series;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure.Narrations;

internal sealed class LocalClonePreviewService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    ILocalCloneGatewayClient gatewayClient,
    IOptions<LocalClonePreviewOptions> options) : ILocalClonePreviewService
{
    private const int MaximumPreviewTextLength = 200;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<LocalClonePreviewAvailabilityResponse?> GetAvailabilityAsync(
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var link = await FindOwnerScopedLinkAsync(seriesId, characterId, cancellationToken);
        if (link is null)
        {
            return null;
        }

        if (!options.Value.Enabled
            || link.CharacterProfileId is not { } characterProfileId
            || !TryGetAllowedProfile(characterProfileId, out var asset)
            || !await ProfileExistsAsync(characterProfileId, cancellationToken))
        {
            return new LocalClonePreviewAvailabilityResponse(false, null);
        }

        return new LocalClonePreviewAvailabilityResponse(true, asset.Label);
    }

    public async Task<LocalClonePreviewAudio?> PreviewAsync(
        Guid seriesId,
        Guid characterId,
        LocalClonePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var link = await FindOwnerScopedLinkAsync(seriesId, characterId, cancellationToken);
        if (link is null)
        {
            return null;
        }

        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("試音文字不可為空白。", nameof(request));
        }

        if (text.Length > MaximumPreviewTextLength)
        {
            throw new ArgumentException(
                $"試音文字不可超過 {MaximumPreviewTextLength} 個字元。",
                nameof(request));
        }

        if (!options.Value.Enabled)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.Disabled);
        }

        if (link.CharacterProfileId is not { } characterProfileId
            || !TryGetAllowedProfile(characterProfileId, out var asset)
            || !await ProfileExistsAsync(characterProfileId, cancellationToken))
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

        // Once the GPU request is submitted, browser disconnects must not cancel the
        // server-side wait and create an uncertain in-flight inference. The typed
        // HttpClient still enforces the configured bounded request timeout.
        var audio = await gatewayClient.SynthesizeAsync(
            new LocalCloneGatewayRequest(
                text,
                privateAssets.CanonicalTranscript,
                privateAssets.ReferenceAudio),
            CancellationToken.None);
        return new LocalClonePreviewAudio(audio.Content, audio.ContentType);
    }

    private async Task<LocalClonePrivateAssets> ReadAndValidateAssetsAsync(
        LocalClonePreviewAssetOptions asset,
        CancellationToken cancellationToken)
    {
        var physicalRoot = ResolveExistingPhysicalPath(
            Path.GetFullPath(options.Value.AssetRootPath),
            requireDirectory: true);
        var referencePath = ResolvePrivateFile(physicalRoot, asset.ReferenceAudioRelativePath);
        var transcriptPath = ResolvePrivateFile(physicalRoot, asset.TranscriptRelativePath);

        var referenceAudio = await ReadBoundedAsync(
            referencePath,
            LocalClonePreviewOptions.MaximumReferenceAudioBytes,
            cancellationToken);
        LocalClonePcmWaveValidator.ValidateReference(referenceAudio);
        VerifySha256(referenceAudio, asset.ExpectedReferenceAudioSha256);

        var transcriptBytes = await ReadBoundedAsync(
            transcriptPath,
            LocalClonePreviewOptions.MaximumTranscriptBytes,
            cancellationToken);
        var canonicalTranscript = CharacterVoiceTranscriptCanonicalizer.Normalize(
            StrictUtf8.GetString(transcriptBytes));
        VerifySha256(
            Encoding.UTF8.GetBytes(canonicalTranscript),
            asset.ExpectedTranscriptSha256);
        return new LocalClonePrivateAssets(referenceAudio, canonicalTranscript);
    }

    private async Task<SeriesCharacterLink?> FindOwnerScopedLinkAsync(
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        return await dbContext.SeriesCharacters
            .AsNoTracking()
            .Where(character => character.OwnerId == ownerId
                && character.SeriesId == seriesId
                && character.Id == characterId)
            .Select(character => new SeriesCharacterLink(character.CharacterProfileId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<bool> ProfileExistsAsync(
        Guid characterProfileId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
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

    private static string ResolvePrivateFile(string physicalRoot, string relativePath)
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

    private static string ResolveExistingPhysicalPath(string path, bool requireDirectory)
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

    private static async Task<byte[]> ReadBoundedAsync(
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

    private static void VerifySha256(ReadOnlySpan<byte> content, string expectedHex)
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

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("目前使用者識別碼無效。");
        }

        return currentUser.UserId;
    }

    private sealed record SeriesCharacterLink(Guid? CharacterProfileId);

    private sealed record LocalClonePrivateAssets(
        byte[] ReferenceAudio,
        string CanonicalTranscript);
}
