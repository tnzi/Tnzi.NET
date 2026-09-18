namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 提交上来的签名图是不是一张<b>盖章方认得的图</b>：data URL（<c>data:image/png;base64,...</c>）或裸 base64，
/// 解出来的字节以 PNG 或 JPEG 的文件头开始。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>Tnzi.Documents</c> 盖章时的解码规则同口径（可带 data URL 前缀、允许折行空白、其余必须是合法 base64），
/// 认的格式也与它一致（<c>PdfSharpPdfStamper</c>：PNG 与 JPEG）。
/// 在提交那一刻校验而不是等到密封：签名图存进去之后收件人就是 <c>Signed</c>，<c>CheckSignable</c>
/// 不再接受他重交；若图盖不上去，密封会在盖章那一步失败，信封停在 <c>InProgress</c> —— 一次错误的提交
/// 就把整份请求钉死了（管理端现在有一个重新密封的动作，但那是给瞬时故障用的，它救不了一张永远解不出的图）。
/// </para>
/// <para>
/// ★ 此前这里只判「解得出字节」，理由是「图片格式由盖章方按内容识别，两处各判一遍会漂」。
/// 但 <c>AAAA</c> 是合法 base64，签名板导出的 SVG / WEBP 也是 —— 解得出字节的非图片照样走到密封才炸，
/// 上面那段话要防的事一件都没防住。两处的格式集合确实会漂，所以钉在一个常量上，并在测试里用真实的 PNG 头过一遍。
/// </para>
/// </remarks>
internal static class SignatureImagePayload
{
    private const string DataUrlPrefix = "data:";
    private const string Base64Marker = ";base64,";

    /// <summary>盖章方认得的图片格式的文件头。PNG：<c>89 50 4E 47 0D 0A 1A 0A</c>；JPEG：<c>FF D8 FF</c>。</summary>
    private static readonly byte[][] AcceptedSignatures =
    [
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
        [0xFF, 0xD8, 0xFF],
    ];

    /// <summary>形态合法：非空、解得出字节、且是一张 PNG 或 JPEG。</summary>
    public static bool IsWellFormed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var payload = value.Trim();
        if (payload.StartsWith(DataUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var marker = payload.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                return false;

            payload = payload[(marker + Base64Marker.Length)..];
        }

        payload = payload
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (payload.Length == 0)
            return false;

        var buffer = new byte[payload.Length];
        if (!Convert.TryFromBase64String(payload, buffer, out var written) || written == 0)
            return false;

        return IsAcceptedImage(buffer.AsSpan(0, written));
    }

    private static bool IsAcceptedImage(ReadOnlySpan<byte> bytes)
    {
        foreach (var signature in AcceptedSignatures)
        {
            if (bytes.Length >= signature.Length && bytes[..signature.Length].SequenceEqual(signature))
                return true;
        }

        return false;
    }
}
