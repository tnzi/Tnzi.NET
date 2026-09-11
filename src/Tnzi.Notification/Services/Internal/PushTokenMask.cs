namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 推送令牌对外展示时的掩码。
/// </summary>
/// <remarks>
/// ★★ <b>令牌是凭据，不只是地址。</b>注册表按令牌寻址，所以持有它的人可以改写
/// 或掐掉那台设备的推送归属 —— 这不是理论：这两条写入路径曾经就这么工作，
/// 一个未认证的调用者拿到令牌即可删掉某个账号的设备行。掩码同时还保护<b>那张清单</b>：
/// 一次列表查询就能导出「谁在哪几台设备上装了这个 App」。
/// <para>
/// 保留尾部若干位是为了让人还能认出某一行（自己的设备列表、对着日志核对），
/// 而这几位不足以复原整个令牌。
/// </para>
/// <para>
/// 取<b>尾部</b>而不是头部：FCM 令牌的前缀在同一个应用里高度雷同（同项目同 sender），
/// 露头部既不帮助辨认、也多暴露一点结构。
/// </para>
/// </remarks>
internal static class PushTokenMask
{
    /// <summary>保留的尾部位数。</summary>
    internal const int VisibleTailLength = 8;

    private const string Ellipsis = "…";

    /// <summary>把一个令牌变成可以对外展示的掩码。</summary>
    /// <remarks>
    /// 短到不足以掩码的值<b>整个隐藏</b>，不退化成原样返回 —— 一个「太短所以直接给你看」
    /// 的分支，会在遇到非预期格式的令牌时安静地把它完整露出来。
    /// </remarks>
    internal static string Of(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return string.Empty;

        return token.Length <= VisibleTailLength
            ? Ellipsis
            : Ellipsis + token[^VisibleTailLength..];
    }
}
