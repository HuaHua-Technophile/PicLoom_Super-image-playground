using System.Buffers.Binary;
using PicLoom.Core;

namespace PicLoom.Infrastructure.Providers;

/// <summary>
/// Structural validation for images that are about to be stored as reference inputs. Magic bytes
/// alone are not enough: a truncated container parses as a "valid" header but can never be rendered
/// by the UI nor accepted by an upstream edit call. Each supported format is walked far enough to
/// prove it contains pixel data and is terminated.
/// </summary>
public static class ImageIntegrity
{
    /// <summary>Returns true when <paramref name="bytes"/> is a complete, usable image of the given MIME type.</summary>
    public static bool IsUsable(byte[] bytes, string mimeType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) return false;

        return mimeType switch
        {
            ImageProbe.PngMime => ImageProbe.IsCompletePng(bytes),
            ImageProbe.JpegMime => IsUsableJpeg(bytes),
            ImageProbe.WebpMime => IsUsableWebp(bytes),
            _ => false
        };
    }

    /// <summary>Throws <c>reference_type_invalid</c> for an unusable container.</summary>
    public static void Require(byte[] bytes, string mimeType)
    {
        if (!IsUsable(bytes, mimeType)) throw new DomainException("reference_type_invalid");
    }

    /// <summary>
    /// SOI, at least one SOF frame header, SOS (scan header) and EOI. A SOF only proves that the
    /// header block is complete: without SOS there is no entropy-coded scan data, so the file cannot
    /// be decoded and an upstream edit call would reject it later. Accepting such a stream would
    /// only move the failure from upload time to dispatch time.
    /// </summary>
    private static bool IsUsableJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return false;

        var sawFrame = false;
        var index = 2;
        while (index + 1 < bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = bytes[index + 1];
            index += 2;

            if (marker == 0xD9) return false; // EOI before any scan: not a decodable image
            if (marker is 0x01 or 0xD8 || marker is >= 0xD0 and <= 0xD7) continue;
            if (marker == 0xDA)
            {
                // Start of scan: entropy data follows the segment header. The stream is complete
                // only if it terminates with EOI (trailing whitespace allowed).
                return sawFrame && HasEndOfImage(bytes);
            }

            if (index + 1 >= bytes.Length) return false;
            var segmentLength = (bytes[index] << 8) | bytes[index + 1];
            if (segmentLength < 2 || (long)index + segmentLength > bytes.Length) return false;

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isStartOfFrame) sawFrame = true;

            index += segmentLength;
        }

        return false; // ran out of bytes before SOS + EOI
    }

    private static bool HasEndOfImage(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] is 0x09 or 0x0A or 0x0D or 0x20) end--;
        return end >= 2 && bytes[end - 2] == 0xFF && bytes[end - 1] == 0xD9;
    }

    /// <summary>RIFF size must describe the actual byte count (±1 pad byte) and start with a VP8 variant.</summary>
    private static bool IsUsableWebp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20) return false;
        if (bytes[0] != (byte)'R' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' || bytes[3] != (byte)'F')
            return false;
        if (bytes[8] != (byte)'W' || bytes[9] != (byte)'E' || bytes[10] != (byte)'B' || bytes[11] != (byte)'P')
            return false;

        var declared = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4));
        if (declared < 12 || Math.Abs((long)declared + 8 - bytes.Length) > 1) return false;

        var chunk = bytes.Slice(12, 4);
        return chunk.SequenceEqual("VP8 "u8) || chunk.SequenceEqual("VP8L"u8) || chunk.SequenceEqual("VP8X"u8);
    }
}
