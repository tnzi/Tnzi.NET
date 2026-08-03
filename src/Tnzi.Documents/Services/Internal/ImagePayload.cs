namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 图片盖章的载荷解码：原始字节 / data URL / 裸 base64。
/// </summary>
/// <remarks>
/// 前端签名板给出来的通常是 <c>data:image/png;base64,...</c>，也有直接给 base64 串的，
/// 两种都接受 —— 这是边界解析，宁可宽进也不要让调用方去猜格式。
/// </remarks>
internal static class ImagePayload
{
    private const string DataUrlPrefix = "data:";
    private const string Base64Marker = ";base64,";

    /// <summary>解出图片字节；无法解出时返回 false。</summary>
    /// <param name="stamp">图片盖章。</param>
    /// <param name="bytes">解出的字节。</param>
    public static bool TryResolve(PdfImageStamp stamp, out byte[] bytes)
    {
        Check.NotNull(stamp);

        if (stamp.Content is { Length: > 0 })
        {
            bytes = stamp.Content;
            return true;
        }

        return TryDecode(stamp.DataUrl, out bytes);
    }

    /// <summary>解码 data URL 或裸 base64；失败返回 false。</summary>
    /// <param name="value">data URL 或裸 base64 串。</param>
    /// <param name="bytes">解出的字节。</param>
    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var payload = value.Trim();
        if (payload.StartsWith(DataUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var markerIndex = payload.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return false;

            payload = payload[(markerIndex + Base64Marker.Length)..];
        }

        // base64 里不该有空白，但 data URL 经过换行折行的情况很常见
        payload = payload.Replace("\r", string.Empty, StringComparison.Ordinal)
                         .Replace("\n", string.Empty, StringComparison.Ordinal)
                         .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (payload.Length == 0)
            return false;

        // 解码后长度必然小于 base64 串长度，按串长开缓冲一定够
        var buffer = new byte[payload.Length];
        if (Convert.TryFromBase64String(payload, buffer, out var written) && written > 0)
        {
            bytes = buffer[..written];
            return true;
        }

        return false;
    }
}
