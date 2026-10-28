using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 签名图的测试素材：真实编码器产出的小图，以及按规范手工拼出来的 PNG（CRC 是真算的）。
/// </summary>
/// <remarks>
/// 手工拼是为了造出编码器不会给的形态：声明 20000 × 20000 像素却只有几个字节的图、CRC 被改坏的图、
/// 图像数据被截断的图。CRC 在这里单独实现一遍，不复用被测代码里的那一份，免得两边错得一样。
/// </remarks>
internal static class TestImages
{
    /// <summary>1 × 1 RGBA PNG（真实编码器产出）。</summary>
    public const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    /// <summary>3 × 2 RGB PNG（Pillow 产出）。</summary>
    public const string SmallRgbPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAFElEQVR4nGPkEpFjAAMmCMXAwAAABGAAQPJRTH0AAAAASUVORK5CYII=";

    /// <summary>4 × 2 灰度基线 JPEG（Pillow 产出）。</summary>
    public const string SmallJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDABALDA4MChAODQ4SERATGCgaGBYWGDEjJR0oOjM9PDkzODdASFxOQERXRTc4UG1RV19iZ2hnPk1xeXBkeFxlZ2P/wAALCAACAAQBAREA/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/AOkr/9k=";

    public static byte[] SmallJpeg => Convert.FromBase64String(SmallJpegBase64);

    /// <summary>8 × 4 RGB 渐进 JPEG（Pillow 产出）。</summary>
    public const string ProgressiveRgbJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDABALDA4MChAODQ4SERATGCgaGBYWGDEjJR0oOjM9PDkzODdASFxOQERXRTc4UG1RV19iZ2hnPk1xeXBkeFxlZ2P/2wBDARESEhgVGC8aGi9jQjhCY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2P/wgARCAAEAAgDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAVAQEBAAAAAAAAAAAAAAAAAAAEBf/aAAwDAQACEAMQAAABng9b/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABBQJ//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAwEBPwF//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAgEBPwF//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQAGPwJ//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPyF//9oADAMBAAIAAwAAABD/AP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQMBAT8Qf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQIBAT8Qf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8Qf//Z";

    /// <summary>8 × 4 CMYK 基线 JPEG（Pillow 产出）：结构完整，盖章方不认 CMYK。</summary>
    public const string CmykJpegBase64 =
        "/9j/7gAOQWRvYmUAZAAAAAAA/9sAQwAQCwwODAoQDg0OEhEQExgoGhgWFhgxIyUdKDozPTw5Mzg3QEhcTkBEV0U3OFBtUVdfYmdoZz5NcXlwZHhcZWdj/8AAFAgABAAIBEMRAE0RAFkRAEsRAP/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/aAA4EQwBNAFkASwAAPwDu67auxrrK/9k=";

    /// <summary>
    /// 拼一张 5 × 5（或给定尺寸）的 PNG：任一色彩类型 × 位深，隔行与否；每行过滤字节为 0，像素随意，调色板图带一块 PLTE。
    /// </summary>
    public static byte[] PngOf(int colorType, int bitDepth, bool interlaced, int width = 5, int height = 5)
    {
        var channels = colorType switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        int RowBytes(int w) => ((w * channels * bitDepth) + 7) / 8;

        using var raw = new MemoryStream();
        void Rows(int w, int h)
        {
            if (w == 0 || h == 0) return;
            for (var y = 0; y < h; y++)
            {
                raw.WriteByte(0);
                raw.Write(Enumerable.Repeat((byte)0x55, RowBytes(w)).ToArray());
            }
        }

        if (!interlaced)
        {
            Rows(width, height);
        }
        else
        {
            (int X, int Y, int Dx, int Dy)[] passes = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
            foreach (var (x, y, dx, dy) in passes)
                Rows(width > x ? (width - x + dx - 1) / dx : 0, height > y ? (height - y + dy - 1) / dy : 0);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw.ToArray());

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = (byte)bitDepth;
        header[9] = (byte)colorType;
        header[12] = (byte)(interlaced ? 1 : 0);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        if (colorType == 3)
            WriteChunk(png, "PLTE", [.. Enumerable.Range(0, 256).SelectMany(i => new[] { (byte)i, (byte)i, (byte)i })]);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    public static string DataUrl(string mediaType, byte[] bytes) => $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

    /// <summary>
    /// 拼一张 RGBA 8 位、不隔行的 PNG。<paramref name="pixelRows"/> 为 null 时按尺寸生成全零像素
    /// （只适合小尺寸）；给出时原样作为解压后的数据（可以故意给少）。
    /// </summary>
    public static byte[] Png(int width, int height, byte[]? pixelRows = null)
    {
        var raw = pixelRows ?? new byte[height * (1 + (width * 4))];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        return PngWithRawIdat(width, height, compressed.ToArray());
    }

    /// <summary>拼一张 RGBA 8 位、不隔行的 PNG，IDAT 的内容原样给出（可以不是合法的 zlib 流），CRC 照常算对。</summary>
    public static byte[] PngWithRawIdat(int width, int height, byte[] idat)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;  // 位深
        header[9] = 6;  // RGBA

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", idat);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>一份 JPEG 的 SOF0 帧头被改成声明 <paramref name="width"/> × <paramref name="height"/> 像素。</summary>
    public static byte[] JpegDeclaring(int width, int height)
    {
        var bytes = SmallJpeg;
        for (var i = 2; i < bytes.Length - 9; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xC0)
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i + 5, 2), (ushort)height);
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i + 7, 2), (ushort)width);
                return bytes;
            }
        }

        throw new InvalidOperationException("The fixture has no SOF0 marker.");
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typeAndData);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}
