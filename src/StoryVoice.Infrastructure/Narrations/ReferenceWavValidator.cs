using System.Buffers.Binary;
using StoryVoice.Application.Narrations;

namespace StoryVoice.Infrastructure.Narrations;

public sealed record ValidatedReferenceWav(double DurationSeconds);

/// <summary>
/// Parses the WAV container locally before any private recording is sent to 3wa. This deliberately
/// accepts one narrow recording-kit contract only: RIFF/WAVE, PCM format 1, mono, 48 kHz, 16-bit,
/// with 10–45 seconds of complete sample frames.
/// </summary>
public static class ReferenceWavValidator
{
    private const ushort PcmFormat = 1;
    private const ushort RequiredChannels = 1;
    private const uint RequiredSampleRate = 48_000;
    private const ushort RequiredBitsPerSample = 16;
    private const ushort RequiredBlockAlign = 2;
    private const uint RequiredByteRate = RequiredSampleRate * RequiredBlockAlign;
    private const double MinimumDurationSeconds = 10;
    private const double MaximumDurationSeconds = 45;

    public static async Task<ValidatedReferenceWav> ValidateAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        await using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length > CharacterVoiceProfileLimits.MaximumReferenceAudioBytes)
        {
            throw Invalid("參考音檔不可超過 10 MiB。");
        }

        if (stream.Length < 44)
        {
            throw Invalid("參考音檔不是完整的 PCM WAV。");
        }

        var header = new byte[12];
        await stream.ReadExactlyAsync(header, cancellationToken);
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw Invalid("參考音檔必須是 RIFF/WAVE 格式。");
        }

        var declaredRiffBytes = (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)) + 8;
        if (declaredRiffBytes != stream.Length)
        {
            throw Invalid("參考音檔的 RIFF 長度與實際檔案不一致。");
        }

        var foundFormat = false;
        long? dataBytes = null;
        var chunkHeader = new byte[8];
        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.Length - stream.Position < chunkHeader.Length)
            {
                throw Invalid("參考音檔包含不完整的 WAV chunk。");
            }

            await stream.ReadExactlyAsync(chunkHeader, cancellationToken);
            var chunkSize = (long)BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4, 4));
            var paddedChunkSize = checked(chunkSize + (chunkSize & 1));
            if (paddedChunkSize > stream.Length - stream.Position)
            {
                throw Invalid("參考音檔的 WAV chunk 長度無效。");
            }

            if (chunkHeader.AsSpan(0, 4).SequenceEqual("fmt "u8))
            {
                if (foundFormat || chunkSize < 16)
                {
                    throw Invalid("參考音檔的 PCM 格式區塊無效。");
                }

                var format = new byte[16];
                await stream.ReadExactlyAsync(format, cancellationToken);
                var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(0, 2));
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2, 2));
                var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4, 4));
                var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(8, 4));
                var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12, 2));
                var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14, 2));
                if (audioFormat != PcmFormat
                    || channels != RequiredChannels
                    || sampleRate != RequiredSampleRate
                    || byteRate != RequiredByteRate
                    || blockAlign != RequiredBlockAlign
                    || bitsPerSample != RequiredBitsPerSample)
                {
                    throw Invalid("參考音檔必須是 PCM 16-bit、48 kHz、單聲道 WAV。");
                }

                stream.Position += chunkSize - format.Length;
                if ((chunkSize & 1) != 0)
                {
                    stream.Position++;
                }

                foundFormat = true;
                continue;
            }

            if (chunkHeader.AsSpan(0, 4).SequenceEqual("data"u8))
            {
                if (dataBytes is not null || chunkSize == 0 || chunkSize % RequiredBlockAlign != 0)
                {
                    throw Invalid("參考音檔的 PCM 資料區塊無效。");
                }

                dataBytes = chunkSize;
            }

            stream.Position += paddedChunkSize;
        }

        if (!foundFormat || dataBytes is null)
        {
            throw Invalid("參考音檔缺少必要的 PCM 格式或資料區塊。");
        }

        var durationSeconds = dataBytes.Value / (double)RequiredByteRate;
        if (durationSeconds is < MinimumDurationSeconds or > MaximumDurationSeconds)
        {
            throw Invalid("參考錄音長度必須介於 10 到 45 秒之間。");
        }

        return new ValidatedReferenceWav(durationSeconds);
    }

    private static InvalidOperationException Invalid(string message) => new(message);
}
