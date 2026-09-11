namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 提交上来的签名图是不是一份<b>解得出来</b>的载荷：data URL（<c>data:image/png;base64,...</c>）或裸 base64。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>Tnzi.Documents</c> 盖章时的解码规则同口径（可带 data URL 前缀、允许折行空白、其余必须是合法 base64）。
/// 在提交那一刻校验而不是等到密封：签名图存进去之后收件人就是 <c>Signed</c>，<c>CheckSignable</c>
/// 不再接受他重交；若图解不出来，密封会在盖章那一步失败，信封停在 <c>InProgress</c>，而<b>没有任何人能修</b> ——
/// 一次拼错的提交就把整份请求钉死了。
/// </para>
/// <para>
/// 这里只判「解得出字节」，不判「是不是一张图」：图片格式由盖章方按内容识别，两处各判一遍会漂。
/// </para>
/// </remarks>
internal static class SignatureImagePayload
{
    private const string DataUrlPrefix = "data:";
    private const string Base64Marker = ";base64,";

    /// <summary>形态合法：非空、能解出至少一个字节。</summary>
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
        return Convert.TryFromBase64String(payload, buffer, out var written) && written > 0;
    }
}
