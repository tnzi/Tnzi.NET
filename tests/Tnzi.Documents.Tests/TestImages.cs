using System.IO.Compression;
using System.Text;

namespace Tnzi.Documents.Tests;

/// <summary>
/// 现造一张合法 PNG 供图片盖章测试使用。
/// </summary>
/// <remarks>
/// 不用硬编码的 base64 常量：那种常量出错时只会以「图片解不开」的形式浮现，读的人无从判断
/// 是编码写错了还是被测代码坏了。这里按 PNG 规范逐块拼（IHDR + IDAT + IEND，真 CRC32），
/// 出问题一眼能看出在哪一层。
/// </remarks>
internal static class TestImages
{
    /// <summary>造一张纯色的 24 位 RGB PNG。</summary>
    public static byte[] Png(int width, int height)
    {
        var raw = BuildScanlines(width, height);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", BuildHeader(width, height));
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BuildScanlines(int width, int height)
    {
        // 每行 = 1 字节 filter type(0 = None) + width * 3 字节 RGB
        var raw = new byte[height * (1 + (width * 3))];
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            raw[index++] = 0;
            for (var x = 0; x < width; x++)
            {
                raw[index++] = 0x20;
                raw[index++] = 0x40;
                raw[index++] = 0x80;
            }
        }

        return raw;
    }

    private static byte[] BuildHeader(int width, int height)
    {
        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // color type: truecolor RGB
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter: adaptive
        header[12] = 0; // interlace: none
        return header;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, unchecked((int)crc));
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    private static uint Crc32(params byte[][] segments)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var segment in segments)
        {
            foreach (var value in segment)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
