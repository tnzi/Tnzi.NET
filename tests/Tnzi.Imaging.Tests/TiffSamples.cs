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

    /// <summary>TIFF 压缩方式（标签 259）里本测试要用到的几个值。</summary>
    public const ushort CompressionNone = 1;
    public const ushort CompressionModifiedHuffman = 2;
    public const ushort CompressionCcittT4 = 3;
    public const ushort CompressionCcittT6 = 4;
    public const ushort CompressionLzw = 5;

    /// <summary>
    /// 一页的描述：尺寸、压缩方式、条带还是分块、像素数据（已按该压缩方式编好）。
    /// </summary>
    /// <param name="Width">页宽</param>
    /// <param name="Height">页高</param>
    /// <param name="Compression">压缩方式（标签 259）</param>
    /// <param name="Tiled">分块存储（整页一块）还是条带存储（整页一条）</param>
    /// <param name="Data">按压缩方式编好的像素数据</param>
    /// <param name="BitsPerSample">CCITT 页为 1，其余为 8</param>
    /// <param name="CompressionAsLong">压缩方式按 LONG 而不是 SHORT 写（规范写 SHORT，读取方都要认两种）</param>
    public sealed record Page(int Width, int Height, ushort Compression, bool Tiled, byte[] Data, int BitsPerSample, bool CompressionAsLong = false);

    /// <summary>
    /// 一页 CCITT 压缩的页，像素数据是随便填的：拒绝发生在解码之前，数据不会被读到。
    /// 分块时整页 64×16 是一块（TIFF 要求块边长是 16 的倍数）。
    /// </summary>
    public static Page Ccitt(ushort compression, bool tiled, bool compressionAsLong = false)
        => tiled
            ? new Page(64, 16, compression, Tiled: true, Data: new byte[32], BitsPerSample: 1, compressionAsLong)
            : new Page(8, 2, compression, Tiled: false, Data: [0xC0], BitsPerSample: 1, compressionAsLong);

    /// <summary>
    /// 条带存储、CCITT Group 4（T6）压缩的全白页：每一行是一个 V0 码字（一个比特 <c>1</c>，
    /// 参考行全白时 a1 = b1 = 行宽，一行即告结束）。这是扫描件与传真件最常见的形态，必须照常解得开。
    /// </summary>
    public static Page WhiteGroup4Strip(int width, int height)
    {
        var data = new byte[(height + 7) / 8];
        for (var row = 0; row < height; row++)
        {
            data[row / 8] |= (byte)(0x80 >> (row % 8));
        }

        return new Page(width, height, CompressionCcittT6, Tiled: false, data, BitsPerSample: 1);
    }

    /// <summary>不压缩的 8 位灰度页；分块时整页是一块，边长须为 16 的倍数。</summary>
    public static Page Uncompressed(int width, int height, bool tiled = false)
    {
        var data = new byte[width * height];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        return new Page(width, height, CompressionNone, tiled, data, BitsPerSample: 8);
    }

    /// <summary>
    /// 按页拼出经典 TIFF（魔数 42）。条带页写 StripOffsets/RowsPerStrip/StripByteCounts，分块页写 TileWidth/TileLength/
    /// TileOffsets/TileByteCounts（整页一块）；CCITT 页的光度解释为 WhiteIsZero。
    /// </summary>
    public static byte[] Pages(bool bigEndian, params Page[] pages)
    {
        var layouts = pages.Select(page => (Page: page, Entries: EntriesFor(page))).ToList();

        var cursor = 8;
        var ifdOffsets = new int[pages.Length];
        for (var i = 0; i < layouts.Count; i++)
        {
            ifdOffsets[i] = cursor;
            cursor += 2 + (layouts[i].Entries.Count * 12) + 4;
        }

        var dataOffsets = new int[pages.Length];
        for (var i = 0; i < layouts.Count; i++)
        {
            dataOffsets[i] = cursor;
            cursor += layouts[i].Page.Data.Length;
        }

        var bytes = new byte[cursor];
        WriteByteOrderMark(bytes, bigEndian);
        WriteUInt16(bytes, 2, ClassicMagic, bigEndian);
        WriteUInt32(bytes, 4, (uint)ifdOffsets[0], bigEndian);

        for (var i = 0; i < layouts.Count; i++)
        {
            var (page, entries) = layouts[i];
            var at = ifdOffsets[i];
            WriteUInt16(bytes, at, (ushort)entries.Count, bigEndian);
            for (var e = 0; e < entries.Count; e++)
            {
                var entry = at + 2 + (e * 12);
                var (tag, type, value) = entries[e];
                if (tag is 273 or 324)
                {
                    value = (uint)dataOffsets[i];
                }

                WriteUInt16(bytes, entry, tag, bigEndian);
                WriteUInt16(bytes, entry + 2, type, bigEndian);
                WriteUInt32(bytes, entry + 4, 1, bigEndian);
                if (type == TypeShort)
                    WriteUInt16(bytes, entry + 8, (ushort)value, bigEndian);
                else
                    WriteUInt32(bytes, entry + 8, value, bigEndian);
            }

            var next = i + 1 < layouts.Count ? (uint)ifdOffsets[i + 1] : 0u;
            WriteUInt32(bytes, at + 2 + (entries.Count * 12), next, bigEndian);

            page.Data.CopyTo(bytes, dataOffsets[i]);

        }

        return bytes;
    }


    private static List<(ushort Tag, ushort Type, uint Value)> EntriesFor(Page page)
    {
        var photometric = page.BitsPerSample == 1 ? (uint)0 : 1; // WhiteIsZero : BlackIsZero
        var compressionType = page.CompressionAsLong ? TypeLong : TypeShort;
        var entries = new List<(ushort Tag, ushort Type, uint Value)>
        {
            (256, TypeLong, (uint)page.Width),
            (257, TypeLong, (uint)page.Height),
            (258, TypeShort, (uint)page.BitsPerSample),
            (259, compressionType, page.Compression),
            (262, TypeShort, photometric)
        };

        if (page.Tiled)
        {
            entries.Add((277, TypeShort, 1));
            entries.Add((322, TypeShort, (uint)page.Width));
            entries.Add((323, TypeShort, (uint)page.Height));
            entries.Add((324, TypeLong, 0));
            entries.Add((325, TypeLong, (uint)page.Data.Length));
        }
        else
        {
            entries.Add((273, TypeLong, 0));
            entries.Add((277, TypeShort, 1));
            entries.Add((278, TypeLong, (uint)page.Height));
            entries.Add((279, TypeLong, (uint)page.Data.Length));
        }

        return entries;
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
