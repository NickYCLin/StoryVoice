using System.Buffers.Binary;

namespace StoryVoice.Infrastructure.Narrations;

public static class BlueMagpiePcmWaveValidator
{
    public static bool IsValid(ReadOnlySpan<byte> content)
    {
        if (content.Length < 46
            || !content[..4].SequenceEqual("RIFF"u8)
            || !content.Slice(8, 4).SequenceEqual("WAVE"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(4, 4)) != content.Length - 8)
        {
            return false;
        }

        var hasFormat = false;
        var hasAudio = false;
        var offset = 12;
        while (offset <= content.Length - 8)
        {
            var chunkId = content.Slice(offset, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(offset + 4, 4));
            var dataOffset = offset + 8;
            if (chunkSize > int.MaxValue || dataOffset > content.Length - (int)chunkSize)
            {
                return false;
            }

            var chunk = content.Slice(dataOffset, (int)chunkSize);
            if (chunkId.SequenceEqual("fmt "u8))
            {
                hasFormat = chunk.Length >= 16
                    && BinaryPrimitives.ReadUInt16LittleEndian(chunk[..2]) == 1
                    && BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2, 2)) == 1
                    && BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(4, 4)) == 48_000
                    && BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(8, 4)) == 96_000
                    && BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(12, 2)) == 2
                    && BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14, 2)) == 16;
                if (!hasFormat)
                {
                    return false;
                }
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                hasAudio = chunk.Length >= 2 && chunk.Length % 2 == 0;
            }

            var paddedSize = (long)chunkSize + (chunkSize & 1U);
            var nextOffset = dataOffset + paddedSize;
            if (nextOffset > content.Length)
            {
                return false;
            }

            offset = (int)nextOffset;
        }

        return offset == content.Length && hasFormat && hasAudio;
    }
}
