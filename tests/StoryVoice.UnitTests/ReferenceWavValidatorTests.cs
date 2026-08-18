using System.Buffers.Binary;
using StoryVoice.Application.Narrations;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class ReferenceWavValidatorTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(45)]
    public async Task ValidateAsync_accepts_only_the_supported_duration_boundaries(int durationSeconds)
    {
        var result = await ValidateAsync(CreatePcmWav(durationSeconds));

        Assert.Equal(durationSeconds, result.DurationSeconds);
    }

    [Theory]
    [InlineData(9, 48_000, 1, 16)]
    [InlineData(46, 48_000, 1, 16)]
    [InlineData(10, 44_100, 1, 16)]
    [InlineData(10, 48_000, 2, 16)]
    [InlineData(10, 48_000, 1, 24)]
    public async Task ValidateAsync_rejects_duration_or_PCM_format_outside_the_contract(
        int durationSeconds,
        int sampleRate,
        short channels,
        short bitsPerSample)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ValidateAsync(CreatePcmWav(durationSeconds, sampleRate, channels, bitsPerSample)));
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_declared_RIFF_length_that_does_not_match_the_file()
    {
        var wav = CreatePcmWav(10);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), 36);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ValidateAsync(wav));
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_file_over_10_MiB_before_parsing_it()
    {
        var oversized = new byte[checked((int)CharacterVoiceProfileLimits.MaximumReferenceAudioBytes + 1)];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ValidateAsync(oversized));

        Assert.Contains("10 MiB", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<ValidatedReferenceWav> ValidateAsync(byte[] bytes)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "storyvoice-wav-validator-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reference.wav");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            return await ReferenceWavValidator.ValidateAsync(path, TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static byte[] CreatePcmWav(
        int durationSeconds,
        int sampleRate = 48_000,
        short channels = 1,
        short bitsPerSample = 16)
    {
        var bytesPerSample = bitsPerSample / 8;
        var blockAlign = checked((short)(channels * bytesPerSample));
        var byteRate = checked(sampleRate * blockAlign);
        var dataLength = checked(byteRate * durationSeconds);
        var wav = new byte[checked(44 + dataLength)];
        "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), checked((uint)(wav.Length - 8)));
        "WAVE"u8.CopyTo(wav.AsSpan(8, 4));
        "fmt "u8.CopyTo(wav.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22, 2), checked((ushort)channels));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24, 4), checked((uint)sampleRate));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28, 4), checked((uint)byteRate));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32, 2), checked((ushort)blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34, 2), checked((ushort)bitsPerSample));
        "data"u8.CopyTo(wav.AsSpan(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40, 4), checked((uint)dataLength));
        return wav;
    }
}
