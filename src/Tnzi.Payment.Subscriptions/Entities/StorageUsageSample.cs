namespace Tnzi.Payment.Subscriptions.Entities;

/// <summary>
/// 一个计费账户某一天（UTC）在存储里占的字节：超额存储费按一个计费周期里这些样本的平均值算。
/// </summary>
/// <remarks>
/// <para>
/// 一个账户一天至多一条（唯一索引），所以采样是幂等的：一天里跑几轮、几个实例同时跑，留下的都是一条。
/// 只为计划设了 <see cref="SubscriptionPlan.IncludedStorageBytes"/> 的在用订阅采样。
/// </para>
/// <para>
/// 刻意不是多租户实体：账户标识在各自的类型里本就全局唯一（用户 Id、租户 Id、安装只有一个），
/// 按安装计费的账户覆盖的又是全部租户的存储，挂在某一个租户名下反而读不到。
/// </para>
/// </remarks>
public class StorageUsageSample : CreationAuditedEntity<Guid>
{
    /// <summary>计费账户类型（<c>BillingAccountTypes</c>）。</summary>
    public string AccountType { get; set; } = string.Empty;

    /// <summary>计费账户标识。</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>样本所属的日期（UTC 零点）。</summary>
    public DateTime SampleDate { get; set; }

    /// <summary>那一刻占用的字节数。</summary>
    public long StoredBytes { get; set; }
}
