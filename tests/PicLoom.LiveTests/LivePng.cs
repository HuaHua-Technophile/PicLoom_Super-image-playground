using System.IO.Compression;
using System.Text;

namespace PicLoom.LiveTests;

/// <summary>
/// Builds a small but fully valid 8-bit RGB PNG. The reference-image path rejects truncated
/// containers (<c>reference_type_invalid</c>), so the fixture must contain real IDAT bytes and an
/// IEND chunk rather than a header-only stub.
/// </summary>
internal static class LivePng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Creates a <paramref name="width"/> x <paramref name="height"/> PNG with a colour ramp.</summary>
    public static byte[] Create(int width = 64, int height = 64)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        // One filter byte (0 = none) followed by three bytes per pixel.
        const int bytesPerPixel = 3;
        var stride = 1 + width * bytesPerPixel;
        var raw = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var pixel = row + 1 + x * bytesPerPixel;
                raw[pixel] = (byte)((x * 255) / Math.Max(1, width - 1));
                raw[pixel + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                raw[pixel + 2] = 0x40;
            }
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var deflate = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(raw, 0, raw.Length);
            compressed = buffer.ToArray();
        }

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: truecolour (RGB)
        header[10] = 0;  // compression: deflate
        header[11] = 0;  // filter method: adaptive
        header[12] = 0;  // interlace: none

        using var png = new MemoryStream();
        png.Write(Signature, 0, Signature.Length);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream target, string type, byte[] payload)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, payload.Length);
        target.Write(length, 0, 4);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        target.Write(typeBytes, 0, typeBytes.Length);
        target.Write(payload, 0, payload.Length);

        var crcInput = new byte[typeBytes.Length + payload.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(payload, 0, crcInput, typeBytes.Length, payload.Length);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32.Compute(crcInput)));
        target.Write(crc, 0, 4);
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    /// <summary>The CRC-32/PNG polynomial table used by every chunk checksum.</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
                crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            const uint polynomial = 0xEDB88320u;
            var table = new uint[256];
            for (var index = 0; index < table.Length; index++)
            {
                var value = (uint)index;
                for (var bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? polynomial ^ (value >> 1) : value >> 1;
                table[index] = value;
            }

            return table;
        }
    }
}
