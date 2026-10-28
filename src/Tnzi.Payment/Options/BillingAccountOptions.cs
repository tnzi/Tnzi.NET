namespace Tnzi.Payment.Options;

/// <summary>
/// 计费账户配置：付款、已保存的卡、订阅与发票记在谁名下。配置路径：<c>Payment:BillingAccount</c>。
/// </summary>
/// <remarks>
/// <para>
/// 默认 <see cref="BillingAccountScope.User"/>：每个用户自己付自己的，与引入计费账户之前逐字相同。
/// 单客户部署（一套安装只服务一家机构、由几位管理者共同管账）配 <see cref="BillingAccountScope.Installation"/>；
/// 租户即客户的多租户部署配 <see cref="BillingAccountScope.Tenant"/>。后两种范围下，代表账户付款、开通订阅、
/// 增删卡片要 <c>payment.account.manage</c>，查看要 <c>payment.account.view</c>（或 manage）。
/// </para>
/// <para>
/// ★ <b>刻意不是运行期设置</b>（没有 <c>[RuntimeSetting]</c>）：范围一变，库里每一行的归属键就都对不上了：
/// 改成 Installation 之后，此前以 User 账户写下的卡与订阅谁都看不见，而接口照常答 200、列表是空的。
/// 换范围是一次数据迁移，不是一次点击。
/// </para>
/// <para>需要别的划分（按部门、按客户实体）的消费方注册自己的 <see cref="Services.IBillingAccountResolver"/>，这一节随之不再被读。</para>
/// </remarks>
[ConfigSection("Payment:BillingAccount")]
public class BillingAccountOptions
{
    /// <summary>
    /// 内置解析器的划分方式，默认 <see cref="BillingAccountScope.User"/>。
    /// </summary>
    public BillingAccountScope Scope { get; set; } = BillingAccountScope.User;

    /// <summary>
    /// <see cref="BillingAccountScope.Installation"/> 下那唯一一个账户的 Id，默认 <c>default</c>。
    /// </summary>
    /// <remarks>
    /// 同一个渠道账户（例如同一个 Stripe 账户）被几套安装共用时，给每套安装一个不同的值，
    /// 渠道侧客户上的 <c>BillingAccount</c> 元数据才分得清是谁的。
    /// </remarks>
    public string InstallationAccountId { get; set; } = DefaultInstallationAccountId;

    /// <summary><see cref="InstallationAccountId"/> 的出厂值。</summary>
    public const string DefaultInstallationAccountId = "default";
}
