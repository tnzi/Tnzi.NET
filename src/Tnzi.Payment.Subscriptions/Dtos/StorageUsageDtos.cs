namespace Tnzi.Payment.Subscriptions.Dtos;

/// <summary>
/// 账户的存储用量与套餐包含量（客户账单页的用量条）。字节一律按 1 GB = 2^30 字节换算。
/// </summary>
public class StorageUsageDto
{
    /// <summary>
    /// 有可显示的存储读数：在用的订阅计量存储，或还没有在用的订阅时这个产品有计量存储的计划。为 false 时其余字段都为空。
    /// </summary>
    public bool Metered { get; set; }

    /// <summary>
    /// 读数针对的是在用的订阅。false = 还没订（或首期款没付），读数对着要买的那个计划的包含量，不累计超额。
    /// </summary>
    public bool Subscribed { get; set; }

    /// <summary>存储模块在这台宿主上量得出用量。false = 计量中但量不出来（没有加载存储模块），不计超额。</summary>
    public bool UsageAvailable { get; set; }

    /// <summary>订阅 Id。</summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>计划名。</summary>
    public string? PlanName { get; set; }

    /// <summary>计划包含的字节数。还没订而产品卖几个计划时为空（每个计划各自不同）。</summary>
    public long? IncludedBytes { get; set; }

    /// <summary>当前用量：今天的每日样本，没有就是当场读数（缓存几分钟）；量不出来时为空。</summary>
    public long? UsedBytes { get; set; }

    /// <summary>读数的时刻（每日样本是那一天的 UTC 零点）。</summary>
    public DateTime? SampledOn { get; set; }

    /// <summary>本期开始。</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>本期结束（下次计费日，超额随那一次续费收取）。</summary>
    public DateTime? PeriodEnd { get; set; }

    /// <summary>本期至今采到样本的天数。</summary>
    public int SampleDays { get; set; }

    /// <summary>本期至今样本的平均字节数。</summary>
    public long? AverageBytes { get; set; }

    /// <summary>按本期至今的平均用量，超出部分折合的 GB-月。</summary>
    public decimal OverageQuantity { get; set; }

    /// <summary>每 GB-月单价；不收费时为空。</summary>
    public decimal? OverageUnitPrice { get; set; }

    /// <summary>
    /// 本期至今累计的超额（税前）：假如本期此刻结束，续费时会收的超额存储费。试用期内恒为 0（试用期不收超额）。
    /// </summary>
    public decimal AccruedOverageAmount { get; set; }

    /// <summary>币种。</summary>
    public string? Currency { get; set; }

    /// <summary>本期是试用期（不收超额）。</summary>
    public bool InTrial { get; set; }
}
