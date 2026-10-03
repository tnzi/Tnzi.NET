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
    /// <remarks>
    /// <b>返回的是传进来的那个原样地址</b>，不是内部用于比对的归一化形态：调用方拿着返回值
    /// 要跟自己手里的收件人对上号。地址按渠道归一化后比对（邮箱去空白加小写，
    /// 传真另走号码归一化），所以写法不同的同一个地址算同一个人，去重也按这个口径。
    /// </remarks>
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

    /// <summary>
    /// 管理端分页读取退订名单。
    /// </summary>
    /// <remarks>
    /// 这一面存在的理由：登记完拿不出来等于没登记。合规问询问的是「这个地址何时经哪个渠道退订」
    /// 「导出抑制名单」，客户来电说的是「我误点了请恢复」—— 没有读取面，这些一个都答不了。
    /// 多租户由 <c>OptOut</c> 的 <c>IMultiTenant</c> 过滤器约束，租户管理员只看得到自己租户的行。
    /// </remarks>
    Task<Result<IPagedList<OptOutDto>>> GetPagedListAsync(OptOutQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理端手工登记一条退订（服务商投诉转来、客户来电、导入的黑名单）。幂等：已存在则原样返回那一条。
    /// </summary>
    /// <remarks>
    /// <c>Source</c> 记为 <c>admin:{操作者 id}</c>：事后要能回答「这条是谁加的」，
    /// 一键链接的记录写的是 <c>one-click link</c>，两种来源在名单上要分得开。
    /// </remarks>
    Task<Result<OptOutDto>> RegisterAsync(CreateOptOutDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理端按 id 撤销一条退订（客户来电说误点了）。不存在答 404。
    /// </summary>
    /// <remarks>
    /// 按 id 而不是按 (地址, 渠道, 分类)：操作者是在列表里点的那一行，不该让他再把三元组抄一遍。
    /// 收件人自己的撤销走 <see cref="OptInAsync"/>（持签名令牌）。
    /// </remarks>
    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>一键退订令牌承载的内容。</summary>
/// <param name="Address">收件地址</param>
/// <param name="Channel">渠道</param>
/// <param name="Category">分类；<c>null</c> = 整渠道</param>
public sealed record UnsubscribeTokenPayload(string Address, NotificationType Channel, string? Category);
