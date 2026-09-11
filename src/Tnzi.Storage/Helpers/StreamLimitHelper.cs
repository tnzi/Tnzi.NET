namespace Tnzi.Storage.Helpers;

/// <summary>
/// 带上限的流复制：解压条目时用它数出**真实**解出的字节数，而不是信 zip 头里声称的大小。
/// </summary>
public static class StreamLimitHelper
{
    private const int BufferSize = 81920;

    /// <summary>
    /// 把 <paramref name="source"/> 复制到 <paramref name="destination"/>，最多 <paramref name="limit"/> 字节。
    /// </summary>
    /// <returns>
    /// 复制的字节数；源在写满 <paramref name="limit"/> 之后仍有数据时返回 <c>-1</c>
    /// （此时 <paramref name="destination"/> 里已写入 <paramref name="limit"/> 字节以内的内容，由调用方丢弃）。
    /// </returns>
    /// <remarks>
    /// 越界即停，不把剩余的源读完：一个 zip bomb 的意义就在于「读完」这件事本身很贵。
    /// </remarks>
    public static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken = default)
    {
        Check.NotNull(source);
        Check.NotNull(destination);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > limit)
                    return -1;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
