namespace Tnzi.Payment.Services;

/// <summary>
/// 消费应用自己知道的计费方资料：账户自己没填账单资料时，票据抬头与收件人从这里取（<b>可选契约</b>）。
/// </summary>
/// <remarks>
/// <para>
/// 计费账户不是用户时（例如整个安装记一本账），付钱的是机构，而框架只认识按下付款按钮的那个用户：
/// 没有这个扩展点，收据的「Received From」就是那个人的登录名。机构的名称、地址与账单邮箱往往早已在消费应用
/// 自己的设置里，让它从那里答，比要求管理员在账单页再填一遍更不容易漏。
/// </para>
/// <para>
/// 询问顺序（逐项）：账户自己的资料（<c>Payment_BillingAccount</c>，账单页里填的）→ 本契约 → 经办用户
/// （显示名与账号邮箱）。返回 <see langword="null"/> 或某项留空 = 这一项我不知道，继续往下问。
/// 未注册时与没有这个扩展点完全相同。
/// </para>
/// <para>
/// 每个账户范围都会被询问（含 <see cref="BillingAccountTypes.User"/>）；只管机构账户的实现对其它范围返回
/// <see langword="null"/> 即可。异常不被吞掉：资料答错比答不上更糟，而答不上本来就有退路。
/// </para>
/// </remarks>
public interface IBillingAccountProfileProvider
{
    /// <summary>账户 <paramref name="account"/> 的计费方资料；不认识它时返回 <see langword="null"/>。</summary>
    Task<BillingAccountProfile?> GetProfileAsync(BillingAccountRef account, CancellationToken cancellationToken = default);
}

/// <summary>消费应用给出的计费方资料，每一项都可以留空。</summary>
public sealed class BillingAccountProfile
{
    /// <summary>票据上的客户名称（机构名）。</summary>
    public string? DisplayName { get; init; }

    /// <summary>公司 / 法人名称（与显示名不同时才需要）。</summary>
    public string? Company { get; init; }

    /// <summary>账单地址，可多行。</summary>
    public string? Address { get; init; }

    /// <summary>客户税号。</summary>
    public string? TaxId { get; init; }

    /// <summary>账单邮箱，第一个是主收件人。</summary>
    public IReadOnlyList<string> Emails { get; init; } = [];
}
