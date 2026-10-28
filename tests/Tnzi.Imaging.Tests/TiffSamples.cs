using System.Buffers.Binary;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 逐字节拼出来的 TIFF：BigTIFF 攻击样本与经典 TIFF 正控制，两种字节序各一份。
/// </summary>
/// <remarks>
/// 不用 ImageSharp 的 <c>TiffEncoder</c> 生成：它只写小端，大端那一半就没人覆盖；
/// 而这里要守的恰恰是「只看前四个字节」这条判定两侧的边界，每个字节都应当在测试里看得见。
/// </remarks>
internal static class TiffSamples
{
    private const ushort ClassicMagic = 42;
    private const ushort BigTiffMagic = 43;

    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;

    /// <summary>
    /// 公开披露的 BigTIFF 样本（GHSA-wmxv-xphr-5c9g）：24 字节，首个 IFD 声明 50 亿个条目，其后没有任何条目字节。
    /// </summary>
    /// <remarks>
    /// ImageSharp 3.1 对每个声明的条目循环一次，而剩余不足一个条目（20 字节）时那一次读取直接返回、
    /// 不前进流。于是这 24 个字节就能让一个线程空转几十秒。
    /// </remarks>
    public static byte[] BigTiffBomb(bool bigEndian)
    {
        const ulong declaredEntries = 5_000_000_000UL;

        var bytes = new byte[24];
        WriteByteOrderMark(bytes, bigEndian);
        WriteUInt16(bytes, 2, BigTiffMagic, bigEndian);
        WriteUInt16(bytes, 4, 8, bigEndian);        // 偏移量字节数，BigTIFF 恒为 8
        WriteUInt16(bytes, 6, 0, bigEndian);        // 保留
        WriteUInt64(bytes, 8, 16, bigEndian);       // 首个 IFD 的偏移
        WriteUInt64(bytes, 16, declaredEntries, bigEndian);
        return bytes;
    }

    /// <summary>
    /// 一张最小的经典 TIFF（魔数 42）：8 位灰度、不压缩、单条带。
    /// </summary>
    /// <remarks>像素值由 <see cref="PixelValue"/> 决定，每个像素各不相同，好断言字节序没有读反。</remarks>
    public static byte[] ClassicGrayscale(bool bigEndian, int width, int height)
    {
        const int ifdOffset = 8;

        // TIFF 规范要求条目按标签号升序排列
        (ushort Tag, ushort Type, uint Value)[] entries =
        [
            (256, TypeShort, (uint)width),          // ImageWidth
            (257, TypeShort, (uint)height),         // ImageLength
            (258, TypeShort, 8),                    // BitsPerSample
            (259, TypeShort, 1),                    // Compression: none
            (262, TypeShort, 1),                    // PhotometricInterpretation: BlackIsZero
            (273, TypeLong, 0),                     // StripOffsets（下面回填）
            (277, TypeShort, 1),                    // SamplesPerPixel
            (278, TypeShort, (uint)height),         // RowsPerStrip
            (279, TypeLong, (uint)(width * height)) // StripByteCounts
        ];

        var pixelOffset = ifdOffset + 2 + (entries.Length * 12) + 4;
        entries[5].Value = (uint)pixelOffset;

        var bytes = new byte[pixelOffset + (width * height)];
        WriteByteOrderMark(bytes, bigEndian);
        WriteUInt16(bytes, 2, ClassicMagic, bigEndian);
        WriteUInt32(bytes, 4, ifdOffset, bigEndian);

        WriteUInt16(bytes, ifdOffset, (ushort)entries.Length, bigEndian);
        for (var i = 0; i < entries.Length; i++)
        {
            var at = ifdOffset + 2 + (i * 12);
            var (tag, type, value) = entries[i];

            WriteUInt16(bytes, at, tag, bigEndian);
            WriteUInt16(bytes, at + 2, type, bigEndian);
            WriteUInt32(bytes, at + 4, 1, bigEndian);

            // 值不足 4 字节时左对齐写进值字段，两种字节序都是如此
            if (type == TypeShort)
                WriteUInt16(bytes, at + 8, (ushort)value, bigEndian);
            else
                WriteUInt32(bytes, at + 8, value, bigEndian);
        }

        // 下一个 IFD 的偏移为 0：只有这一页（数组本来就是零，这里只是让结构完整可读）
        WriteUInt32(bytes, ifdOffset + 2 + (entries.Length * 12), 0, bigEndian);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bytes[pixelOffset + (y * width) + x] = PixelValue(x, y, width);
            }
        }

        return bytes;
    }

    /// <summary>
    /// 小端、多页的经典 TIFF：每页 8 位灰度、不压缩、单条带，各页尺寸各自声明。
    /// </summary>
    /// <remarks>
    /// 只有尺寸不超过 <paramref name="pixelDataLimit"/> 像素的页带像素数据；更大的页只声明尺寸，
    /// 条带偏移指向文件末尾。攻击样本的形状就是这样：文件极小，声明极大。
    /// </remarks>
    public static byte[] ClassicPages(int pixelDataLimit, params (int Width, int Height)[] pages)
    {
        const int entryCount = 9;
        const int ifdSize = 2 + (entryCount * 12) + 4;

        var ifdOffsets = new int[pages.Length];
        var dataOffsets = new int[pages.Length];
        var cursor = 8;
        for (var i = 0; i < pages.Length; i++)
        {
            ifdOffsets[i] = cursor;
            cursor += ifdSize;
        }

        for (var i = 0; i < pages.Length; i++)
        {
            var pixels = (long)pages[i].Width * pages[i].Height;
            dataOffsets[i] = cursor;
            if (pixels <= pixelDataLimit)
                cursor += (int)pixels;
        }

        var bytes = new byte[cursor];
        WriteByteOrderMark(bytes, bigEndian: false);
        WriteUInt16(bytes, 2, ClassicMagic, bigEndian: false);
        WriteUInt32(bytes, 4, (uint)ifdOffsets[0], bigEndian: false);

        for (var i = 0; i < pages.Length; i++)
        {
            var (width, height) = pages[i];
            (ushort Tag, ushort Type, uint Value)[] entries =
            [
                (256, TypeLong, (uint)width),
                (257, TypeLong, (uint)height),
                (258, TypeShort, 8),
                (259, TypeShort, 1),
                (262, TypeShort, 1),
                (273, TypeLong, (uint)dataOffsets[i]),
                (277, TypeShort, 1),
                (278, TypeLong, (uint)height),
                (279, TypeLong, (uint)((long)width * height))
            ];

            var at = ifdOffsets[i];
            WriteUInt16(bytes, at, entryCount, bigEndian: false);
            for (var e = 0; e < entries.Length; e++)
            {
                var entry = at + 2 + (e * 12);
                var (tag, type, value) = entries[e];
                WriteUInt16(bytes, entry, tag, bigEndian: false);
                WriteUInt16(bytes, entry + 2, type, bigEndian: false);
                WriteUInt32(bytes, entry + 4, 1, bigEndian: false);
                if (type == TypeShort)
                    WriteUInt16(bytes, entry + 8, (ushort)value, bigEndian: false);
                else
                    WriteUInt32(bytes, entry + 8, value, bigEndian: false);
            }

            var next = i + 1 < pages.Length ? (uint)ifdOffsets[i + 1] : 0u;
            WriteUInt32(bytes, at + 2 + (entryCount * 12), next, bigEndian: false);
        }

        return bytes;
    }

    /// <summary><see cref="ClassicGrayscale"/> 在 (x, y) 处写入的灰度值。</summary>
    public static byte PixelValue(int x, int y, int width) => (byte)(10 + (40 * ((y * width) + x)));

    private static void WriteByteOrderMark(byte[] bytes, bool bigEndian)
    {
        var mark = bigEndian ? (byte)'M' : (byte)'I';
        bytes[0] = mark;
        bytes[1] = mark;
    }

    private static void WriteUInt16(byte[] bytes, int at, ushort value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), value);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
    }

    private static void WriteUInt32(byte[] bytes, int at, uint value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    }

    private static void WriteUInt64(byte[] bytes, int at, ulong value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(at), value);
        else
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), value);
    }
}
