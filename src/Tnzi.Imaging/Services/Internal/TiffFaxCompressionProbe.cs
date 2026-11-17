namespace Tnzi.Imaging.Services;

/// <summary>
/// 读经典 TIFF 每一页 IFD 里的压缩方式与分块标签，找出 ImageSharp 3.1 会越界写的 CCITT 页（GHSA-v76p-62qx-wwq2）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要在 ImageSharp 之前读</b>：3.1 的三个 CCITT 解压器（Modified Huffman = 压缩方式 2、T4 = 3、T6 = 4）
/// 都经不检查边界的位写入器把解出的像素写进调用方给的缓冲区。
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>分块（tiled）</b>：缓冲区按 TileWidth 分配，解压器却按整幅图宽逐行写，第一块就线性写出界，越界长度由文件决定。
/// 这是公告本身，三种 CCITT 压缩都中。
/// </description></item>
/// <item><description>
/// <b>条带（strip）上的 2 与 3</b>：一个解出的游程长于本行剩余宽度时照样写下去（T4 根本不查，MH 写完才查），
/// 最后一行的越界就落在条带缓冲区之外。修复版在同一次发布里给这两个解压器补了「游程必须装得进本行」的检查。
/// </description></item>
/// <item><description>
/// <b>条带上的 4（Group 4）</b>不受影响：T6 先解进一条按图宽分配、带边界检查的扫描线，再逐行拷进条带缓冲区，
/// 行宽与行数都与缓冲区一致。扫描件与传真件最常见的就是这种，所以它照常放行。
/// </description></item>
/// </list>
/// <para>
/// 判定偏严：分块按「四个分块标签出现任意一个」认（ImageSharp 按其中之一判定分块），压缩方式在一页里出现多次时
/// 任意一个命中就算，读不全的条目按读到的那些判，页数超过 <see cref="MaxPages"/> 直接拒绝。
/// 读不到首个 IFD、条目区被截断这类结构问题不在这里判：ImageSharp 读到同样的字节，自己会报错。
/// </para>
/// </remarks>
internal static class TiffFaxCompressionProbe
{
    /// <summary>逐页检查的页数上限。超过即拒绝：只查前若干页等于把后面的页留给 ImageSharp。</summary>
    public const int MaxPages = 10_000;

    private const ushort CompressionTag = 259;
    private const ushort TileWidthTag = 322;
    private const ushort TileLengthTag = 323;
    private const ushort TileOffsetsTag = 324;
    private const ushort TileByteCountsTag = 325;

    private const uint ModifiedHuffman = 2;
    private const uint CcittT4 = 3;
    private const uint CcittT6 = 4;

    private const int HeaderLength = 8;
    private const int EntryLength = 12;

    /// <summary>
    /// 从流的当前位置读 TIFF；返回拒绝理由（英文，直接进异常消息），可以放行时返回 null。读完把位置放回原处。
    /// </summary>
    /// <remarks>
    /// 流的当前位置不在开头时，IFD 偏移按「相对文件头」与「相对流开头」两种读法各走一遍，任一种读出不安全的页就拒绝：
    /// ImageSharp 从当前位置认文件头，而偏移怎么解释是它的实现细节，按一种读法放行、它按另一种去解就是一条绕过。
    /// </remarks>
    /// <param name="seekable">可定位的流</param>
    /// <param name="useAsync">true 时用异步读；false 时整个方法同步完成</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async ValueTask<string?> FindRejectionAsync(Stream seekable, bool useAsync, CancellationToken cancellationToken)
    {
        var start = seekable.Position;
        try
        {
            var header = new byte[HeaderLength];
            if (await ReadAtAsync(seekable, start, header, useAsync, cancellationToken) < HeaderLength)
            {
                return null;
            }

            bool bigEndian;
            if (header[0] == (byte)'I' && header[1] == (byte)'I' && header[2] == 42 && header[3] == 0)
            {
                bigEndian = false;
            }
            else if (header[0] == (byte)'M' && header[1] == (byte)'M' && header[2] == 0 && header[3] == 42)
            {
                bigEndian = true;
            }
            else
            {
                // 不是经典 TIFF（BigTIFF 由 ImageDecodeGuard 在此之前整类拒绝）
                return null;
            }

            var firstIfd = ReadUInt32(header.AsSpan(4), bigEndian);
            return await WalkPagesAsync(seekable, start, firstIfd, bigEndian, useAsync, cancellationToken)
                ?? (start == 0 ? null : await WalkPagesAsync(seekable, 0, firstIfd, bigEndian, useAsync, cancellationToken));
        }
        finally
        {
            seekable.Position = start;
        }
    }

    /// <summary>沿 IFD 链逐页判定；偏移相对 <paramref name="origin"/>。结构读不下去时停止（交给 ImageSharp 自己报错）。</summary>
    private static async ValueTask<string?> WalkPagesAsync(
        Stream seekable, long origin, uint firstIfd, bool bigEndian, bool useAsync, CancellationToken cancellationToken)
    {
        var visited = new HashSet<uint>();
        var ifdOffset = firstIfd;
        var countBuffer = new byte[2];
        var nextBuffer = new byte[4];

        for (var page = 1; ifdOffset != 0 && visited.Add(ifdOffset); page++)
        {
            if (page > MaxPages)
            {
                return $"TIFF images with more than {MaxPages} pages are not supported.";
            }

            if (await ReadAtAsync(seekable, origin + ifdOffset, countBuffer, useAsync, cancellationToken) < countBuffer.Length)
            {
                return null;
            }

            var entries = new byte[ReadUInt16(countBuffer, bigEndian) * EntryLength];
            var entryBytes = await ReadAtAsync(seekable, origin + ifdOffset + 2, entries, useAsync, cancellationToken);

            // 条目区被截断时按读全了的那些条目判
            var completeEntries = entries.AsMemory(0, entryBytes - (entryBytes % EntryLength));
            if (await InspectPageAsync(seekable, origin, completeEntries, bigEndian, useAsync, cancellationToken) is { } rejection)
            {
                return rejection;
            }

            if (entryBytes < entries.Length
                || await ReadAtAsync(seekable, origin + ifdOffset + 2 + entries.Length, nextBuffer, useAsync, cancellationToken) < nextBuffer.Length)
            {
                return null;
            }

            ifdOffset = ReadUInt32(nextBuffer, bigEndian);
        }

        return null;
    }

    /// <summary>判定一页：任何 2 / 3 拒绝；4 只在分块时拒绝。</summary>
    private static async ValueTask<string?> InspectPageAsync(
        Stream seekable, long origin, ReadOnlyMemory<byte> entries, bool bigEndian, bool useAsync, CancellationToken cancellationToken)
    {
        var tiled = false;
        var compressions = new List<uint>();

        for (var at = 0; at + EntryLength <= entries.Length; at += EntryLength)
        {
            var tag = ReadUInt16(entries.Span.Slice(at, 2), bigEndian);
            if (tag is TileWidthTag or TileLengthTag or TileOffsetsTag or TileByteCountsTag)
            {
                tiled = true;
            }
            else if (tag == CompressionTag
                     && await ReadFirstValueAsync(seekable, origin, entries.Slice(at, EntryLength), bigEndian, useAsync, cancellationToken) is { } value)
            {
                compressions.Add(value);
            }
        }

        if (compressions.Any(c => c is ModifiedHuffman or CcittT4))
        {
            return "TIFF images with CCITT Group 3 or Modified Huffman compression are not supported. "
                + "Save the image as a CCITT Group 4, LZW or uncompressed TIFF, or as PNG or JPEG.";
        }

        if (tiled && compressions.Contains(CcittT6))
        {
            return "Tiled TIFF images with CCITT Group 4 compression are not supported. "
                + "Save the image as a strip-based TIFF, or as PNG or JPEG.";
        }

        return null;
    }

    /// <summary>
    /// 一个条目的第一个值（BYTE / SHORT / LONG）；值装得进 4 字节时就在条目里，否则在条目给出的偏移处。类型不认识时为 null。
    /// </summary>
    private static async ValueTask<uint?> ReadFirstValueAsync(
        Stream seekable, long origin, ReadOnlyMemory<byte> entry, bool bigEndian, bool useAsync, CancellationToken cancellationToken)
    {
        var type = ReadUInt16(entry.Span.Slice(2, 2), bigEndian);
        var size = type switch
        {
            1 => 1, // BYTE
            3 => 2, // SHORT
            4 => 4, // LONG
            _ => 0
        };
        if (size == 0)
        {
            return null;
        }

        var count = ReadUInt32(entry.Span.Slice(4, 4), bigEndian);
        if (count == 0)
        {
            return null;
        }

        ReadOnlyMemory<byte> value = entry.Slice(8, 4);
        if ((ulong)count * (uint)size > 4)
        {
            var external = new byte[size];
            if (await ReadAtAsync(seekable, origin + ReadUInt32(value.Span, bigEndian), external, useAsync, cancellationToken) < size)
            {
                return null;
            }

            value = external;
        }

        return size switch
        {
            1 => value.Span[0],
            2 => ReadUInt16(value.Span, bigEndian),
            _ => ReadUInt32(value.Span, bigEndian)
        };
    }

    private static async ValueTask<int> ReadAtAsync(Stream seekable, long position, byte[] buffer, bool useAsync, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0 || position >= seekable.Length)
        {
            return 0;
        }

        seekable.Position = position;
        return useAsync
            ? await seekable.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken)
            : seekable.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
