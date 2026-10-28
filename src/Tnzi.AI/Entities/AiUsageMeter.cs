namespace Tnzi.AI.Entities;

/// <summary>
/// 一个用户的用量计量：两个窗口各自从什么时候开始、已经用了多少（API 等价费用，美元）。
/// </summary>
/// <remarks>
/// <para>
/// 只经条件 <c>ExecuteUpdate</c> 写（开窗口、判上限、累加、跨界重置都是一条语句），从不整行回写：
/// 两轮并发结算时，读改写会丢掉其中一轮。
/// </para>
/// <para>
/// 窗口的语义见 <see cref="UsageLimitWindow"/>。开始时间为 null = 这个窗口还没开始（5 小时窗口过期后也回到 null，
/// 下一次使用重新开）；每周窗口过期后滚到包含当前时刻的那个周期，开始时间不回到 null。
/// </para>
/// </remarks>
public class AiUsageMeter : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户 ID。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>用户 ID。</summary>
    public Guid UserId { get; set; }

    /// <summary>当前 5 小时窗口的开始时间（UTC）；null = 没有进行中的窗口。</summary>
    public DateTime? SessionStartedAt { get; set; }

    /// <summary>当前 5 小时窗口里已经用掉的费用（美元）。</summary>
    public decimal SessionCostUsd { get; set; }

    /// <summary>当前每周周期的开始时间（UTC）；null = 还没被计量过。</summary>
    public DateTime? WeeklyStartedAt { get; set; }

    /// <summary>当前每周周期里已经用掉的费用（美元）。</summary>
    public decimal WeeklyCostUsd { get; set; }

    /// <summary>最后一次被计量的时间（UTC）。</summary>
    public DateTime? LastUsedAt { get; set; }
}
