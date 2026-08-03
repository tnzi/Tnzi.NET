namespace Tnzi.Notification.Dtos;

/// <summary>
/// 退订落地页的确认信息（无副作用回显）。
/// </summary>
public class UnsubscribePreviewDto
{
    /// <summary>
    /// 掩码后的地址（`a***@example.com`）。
    /// </summary>
    /// <remarks>
    /// 退订链接可能被转发或出现在日志里，完整地址不该对拿到链接的任何人可见；
    /// 掩码保留的信息量刚够收件人确认"这是我"。
    /// </remarks>
    public string MaskedAddress { get; set; } = string.Empty;

    /// <summary>渠道</summary>
    public NotificationType Channel { get; set; }

    /// <summary>通知分类；<c>null</c> 表示整个渠道</summary>
    public string? Category { get; set; }
}

/// <summary>
/// 一键退订 / 重新订阅请求。
/// </summary>
public class UnsubscribeRequestDto
{
    /// <summary>邮件里那条链接携带的签名令牌</summary>
    public string Token { get; set; } = null!;

    /// <summary>退订原因（收件人可选填写）</summary>
    public string? Reason { get; set; }
}
