namespace Tnzi.Notification.Services;

/// <summary>
/// 退订服务：记录、撤销与查询"这个地址不要再收这类通知"。
/// </summary>
/// <remarks>
/// <para>
/// 群发合规（CASL / CAN-SPAM 一类）要求商业消息带一键退订，且退订须很快生效。
/// 这是框架能力而不是每个消费应用各造一遍的东西 —— 各造一遍就是各踩一遍合规风险。
/// </para>
/// <para>
/// <b>发送前判定用 <see cref="FilterAllowedAsync"/>，不要逐个 <see cref="IsOptedOutAsync"/>。</b>
/// 一次群发有上千个地址，逐个查是上千次往返；批量版一次查完。
/// </para>
/// </remarks>
public interface INotificationOptOutService
{
    /// <summary>
    /// 记录一次退订（幂等：同一 地址+渠道+分类 重复退订不产生第二条记录）。
    /// </summary>
    /// <param name="address">邮箱或手机号（内部归一化后存储）</param>
    /// <param name="channel">渠道</param>
    /// <param name="category">通知分类；<c>null</c> = 该渠道全部退订</param>
    /// <param name="source">来源说明，便于追溯（如 "one-click link"）</param>
    /// <param name="reason">收件人填写的原因，可空</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result> OptOutAsync(
        string address,
        NotificationType channel,
        string? category = null,
        string? source = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销退订（重新订阅）。不存在的记录视为已完成，不报错。
    /// </summary>
    Task<Result> OptInAsync(
        string address,
        NotificationType channel,
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单个地址是否已退订。整渠道退订（<c>Category = null</c>）覆盖该渠道下的任何分类。
    /// </summary>
    Task<bool> IsOptedOutAsync(
        string address,
        NotificationType channel,
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从一批地址里滤掉已退订的，返回仍可发送的那些（保持输入顺序，去重）。
    /// 群发前应当调这个，而不是逐个判定。
    /// </summary>
    Task<IReadOnlyList<string>> FilterAllowedAsync(
        IEnumerable<string> addresses,
        NotificationType channel,
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为一个地址签发一键退订令牌，供放进邮件正文的退订链接。
    /// </summary>
    /// <remarks>
    /// 令牌是<b>自包含且带签名</b>的，不落库：退订链接的寿命等同于那封邮件在收件箱里的寿命
    /// （可能是几年），为此维护一张永不过期的令牌表是纯粹的负担。签名用部署自己的密钥，
    /// 所以伪造需要拿到密钥，而拿到密钥的人有比"替别人退订"更值得做的事。
    /// </remarks>
    string CreateUnsubscribeToken(string address, NotificationType channel, string? category = null);

    /// <summary>
    /// 校验并解析一键退订令牌。无效返回 <c>null</c>。
    /// </summary>
    UnsubscribeTokenPayload? ResolveUnsubscribeToken(string token);
}

/// <summary>一键退订令牌承载的内容。</summary>
/// <param name="Address">收件地址</param>
/// <param name="Channel">渠道</param>
/// <param name="Category">分类；<c>null</c> = 整渠道</param>
public sealed record UnsubscribeTokenPayload(string Address, NotificationType Channel, string? Category);
