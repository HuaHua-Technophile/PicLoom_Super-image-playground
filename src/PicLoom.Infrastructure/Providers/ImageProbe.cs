using System.Buffers.Binary;

namespace PicLoom.Infrastructure.Providers;

/// <summary>
/// Identifies the produced image format from its magic bytes and reads its pixel dimensions. The
/// MIME type is derived from the bytes only; upstream <c>Content-Type</c> headers are ignored.
/// </summary>
public static class ImageProbe
{
    public const string PngMime = "image/png";
    public const string JpegMime = "image/jpeg";
    public const string WebpMime = "image/webp";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads width, height and MIME type; returns false for anything that is not a PNG, JPEG or WebP.</summary>
    public static bool TryRead(byte[] bytes, out int width, out int height, out string mimeType)
    {
        if (bytes is null)
        {
            width = 0;
            height = 0;
            mimeType = "";
            return false;
        }

        return TryRead(bytes.AsSpan(), out width, out height, out mimeType);
    }

    /// <summary>Reads width, height and MIME type from a byte span.</summary>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out int width, out int height, out string mimeType)
    {
        width = 0;
        height = 0;
        mimeType = "";

        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(PngSignature))
            return TryReadPng(bytes, out width, out height, out mimeType);

        if (bytes.Length >= 12 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return TryReadJpeg(bytes, out width, out height, out mimeType);

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return TryReadWebp(bytes, out width, out height, out mimeType);

        return false;
    }

    /// <summary>
    /// Verifies that a PNG really contains image data and finishes: the IHDR dimensions are not
    /// enough, a truncated stream (signature + IHDR only) must not be accepted as a usable image.
    /// Walks the chunk list and requires at least one IDAT chunk and the terminal IEND chunk, with
    /// every declared length fitting inside the buffer.
    /// </summary>
    public static bool IsCompletePng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 + 12 || !bytes[..8].SequenceEqual(PngSignature)) return false;

        // IHDR must be the first chunk and carries the dimensions (13 bytes).
        var firstLength = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(8, 4));
        if (firstLength < 13 || !bytes.Slice(12, 4).SequenceEqual(IhdrChunk)) return false;

        var offset = 8;
        var sawData = false;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, 4));
            if (length < 0) return false;

            // 4 length + 4 type + data + 4 CRC
            var next = (long)offset + 12 + length;
            if (next > bytes.Length) return false;

            var type = bytes.Slice(offset + 4, 4);
            offset = (int)next;

            if (type.SequenceEqual(IdatChunk)) sawData = true;
            else if (type.SequenceEqual(IendChunk)) return sawData;
        }

        return false;
    }

    private static readonly byte[] IhdrChunk = "IHDR"u8.ToArray();
    private static readonly byte[] IdatChunk = "IDAT"u8.ToArray();
    private static readonly byte[] IendChunk = "IEND"u8.ToArray();

    private static bool TryReadPng(ReadOnlySpan<byte> bytes, out int width, out int height, out string mimeType)
    {
        width = 0;
        height = 0;
        mimeType = "";

        if (bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
            return false;

        var parsedWidth = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var parsedHeight = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        if (parsedWidth <= 0 || parsedHeight <= 0) return false;

        width = parsedWidth;
        height = parsedHeight;
        mimeType = PngMime;
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> bytes, out int width, out int height, out string mimeType)
    {
        width = 0;
        height = 0;
        mimeType = "";

        var index = 2; // skip SOI (FF D8)
        while (index + 1 < bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = bytes[index + 1];
            index += 2;

            // Standalone markers carry no payload length.
            if (marker is 0x01 or 0xD8 || marker is >= 0xD0 and <= 0xD7) continue;
            if (marker is 0xD9 or 0xDA) break; // EOI or start of scan: dimensions would be in a SOF we already saw

            if (index + 1 >= bytes.Length) break;
            var segmentLength = (bytes[index] << 8) | bytes[index + 1];
            if (segmentLength < 2) break;

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isStartOfFrame)
            {
                if (index + 6 >= bytes.Length) break;
                var parsedHeight = (bytes[index + 3] << 8) | bytes[index + 4];
                var parsedWidth = (bytes[index + 5] << 8) | bytes[index + 6];
                if (parsedWidth <= 0 || parsedHeight <= 0) return false;

                width = parsedWidth;
                height = parsedHeight;
                mimeType = JpegMime;
                return true;
            }

            index += segmentLength;
        }

        return false;
    }

    private static bool TryReadWebp(ReadOnlySpan<byte> bytes, out int width, out int height, out string mimeType)
    {
        width = 0;
        height = 0;
        mimeType = "";

        var chunk = bytes.Slice(12, 4);
        const int payload = 20;

        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (bytes.Length < payload + 10) return false;
            if (bytes[payload + 3] != 0x9D || bytes[payload + 4] != 0x01 || bytes[payload + 5] != 0x2A) return false;

            var parsedWidth = (bytes[payload + 6] | (bytes[payload + 7] << 8)) & 0x3FFF;
            var parsedHeight = (bytes[payload + 8] | (bytes[payload + 9] << 8)) & 0x3FFF;
            if (parsedWidth <= 0 || parsedHeight <= 0) return false;

            width = parsedWidth;
            height = parsedHeight;
            mimeType = WebpMime;
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (bytes.Length < payload + 5) return false;
            if (bytes[payload] != 0x2F) return false;

            var b0 = bytes[payload + 1];
            var b1 = bytes[payload + 2];
            var b2 = bytes[payload + 3];
            var b3 = bytes[payload + 4];

            var parsedWidth = 1 + (((b1 & 0x3F) << 8) | b0);
            var parsedHeight = 1 + (((b3 & 0x0F) << 10) | (b2 << 2) | ((b1 & 0xC0) >> 6));
            if (parsedWidth <= 0 || parsedHeight <= 0) return false;

            width = parsedWidth;
            height = parsedHeight;
            mimeType = WebpMime;
            return true;
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            if (bytes.Length < payload + 10) return false;

            var parsedWidth = 1 + (bytes[payload + 4] | (bytes[payload + 5] << 8) | (bytes[payload + 6] << 16));
            var parsedHeight = 1 + (bytes[payload + 7] | (bytes[payload + 8] << 8) | (bytes[payload + 9] << 16));
            if (parsedWidth <= 0 || parsedHeight <= 0) return false;

            width = parsedWidth;
            height = parsedHeight;
            mimeType = WebpMime;
            return true;
        }

        return false;
    }
}
