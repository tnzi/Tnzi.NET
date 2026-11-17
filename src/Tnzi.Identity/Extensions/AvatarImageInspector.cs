namespace Tnzi.Identity.Extensions;

/// <summary>
/// 头像文件的形态校验：看文件头的魔数认格式，数字节认大小。
/// </summary>
/// <remarks>
/// ★ 认的是<b>字节</b>不是文件名或 Content-Type：两者都由上传方自报，一份改名成 <c>.png</c> 的 PDF
/// 两样都能写对。头像会被发布成匿名可读（见 <see cref="Options.AvatarOptions"/>），
/// 所以这里要回答的是「这份字节是不是一张图」，而不是「它自称是什么」。
/// 只认 PNG / JPEG / WebP：浏览器都能直接渲染，且不含脚本（SVG 被刻意排除，它是会执行的文档）。
/// </remarks>
internal static class AvatarImageInspector
{
    /// <summary>判定格式所需的文件头长度（WebP 要读到第 12 字节）。</summary>
    public const int HeaderLength = 12;

    /// <summary>头部字节是不是一种允许的图片格式；是则返回格式名，否则 <see langword="null"/>。</summary>
    public static string? DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return "png";

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "jpeg";

        // RIFF....WEBP
        if (header.Length >= 12
            && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            return "webp";

        return null;
    }

    /// <summary>
    /// 读流：返回格式（不认识为 <see langword="null"/>）与「是否超过 <paramref name="maxBytes"/>」。
    /// </summary>
    /// <remarks>
    /// 不可寻址的流（对象存储的下载流）没有可信的 <c>Length</c>，于是边读边数、过了上限就停，
    /// 一份超大文件不会被整个读进来才被拒绝。
    /// </remarks>
    public static async Task<(string? Format, bool TooLarge)> InspectAsync(Stream stream, long maxBytes, CancellationToken cancellationToken = default)
    {
        Check.NotNull(stream);

        var header = new byte[HeaderLength];
        var read = 0;
        while (read < HeaderLength)
        {
            var n = await stream.ReadAsync(header.AsMemory(read, HeaderLength - read), cancellationToken);
            if (n == 0) break;
            read += n;
        }

        var format = DetectFormat(header.AsSpan(0, read));

        if (stream.CanSeek)
            return (format, stream.Length > maxBytes);

        long total = read;
        var buffer = new byte[81920];
        while (total <= maxBytes)
        {
            var n = await stream.ReadAsync(buffer, cancellationToken);
            if (n == 0) break;
            total += n;
        }

        return (format, total > maxBytes);
    }
}
