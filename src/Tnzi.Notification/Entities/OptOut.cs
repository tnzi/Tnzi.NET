namespace Tnzi.Notification.Entities;

/// <summary>
/// 一条退订记录：某个地址（邮箱 / 手机号）表示不再接收某一类通知。
/// </summary>
/// <remarks>
/// <para>
/// <b>按地址而不是按用户</b>。群发的收件人未必是本系统的注册用户（客户名单、导入的联系人、
/// 已注销的账号都可能在里面），而退订权利与"是不是我们的用户"无关。地址是收件人唯一
/// 一定拥有的东西，也是发送时真正会用到的键。
/// </para>
/// <para>
/// <b>只记退订，不记"已同意"。</b>本表回答的是"这个地址明确说过不要"，它是发送前的
/// 一道否决。同意的记录（何时、经何种方式取得）是各法域要求各异的合规资料，属于消费应用
/// 自己的领域，框架不替它决定形态。
/// </para>
/// <para>
/// <b>为什么必须是框架能力。</b>CASL / CAN-SPAM 一类法规要求商业邮件提供一键退订，
/// 且退订必须在很短时间内生效。让每个消费应用各造一遍，等于让每个应用各踩一遍合规风险。
/// </para>
/// </remarks>
public class OptOut : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 退订的地址（邮箱 / 手机号）。存归一化后的值（去空白 + 小写），
    /// 因为收件人名单里的大小写与实际发送地址常常不一致。
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 退订的渠道。按渠道分别退订：退掉营销邮件的人并不因此放弃短信验证码。
    /// </summary>
    public NotificationType Channel { get; set; }

    /// <summary>
    /// 退订的通知分类（消费应用自定的分组键，如 <c>"marketing"</c> / <c>"newsletter"</c>）。
    /// <c>null</c> 表示该渠道<b>全部</b>退订。
    /// </summary>
    /// <remarks>
    /// 分类退订是"少发一点"与"一个都别发"之间的中间档。没有它，用户面对一封不想要的
    /// 促销邮件时唯一的选择就是退掉全部 —— 包括他其实想收的那些。
    /// </remarks>
    public string? Category { get; set; }

    /// <summary>退订来源说明（如 "one-click link" / "admin" / "provider complaint"），用于追溯。</summary>
    public string? Source { get; set; }

    /// <summary>退订原因（收件人填写，可空）。</summary>
    public string? Reason { get; set; }
}
