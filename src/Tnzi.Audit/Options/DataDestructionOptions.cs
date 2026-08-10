namespace Tnzi.Audit.Options;

/// <summary>
/// 策略驱动数据销毁选项（配置节 <c>Audit:DataDestruction</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <strong>默认关闭。</strong>关闭时 <c>AuditDataDestruction</c> 实体<strong>不映射为数据表</strong>，
/// 后台服务直接退出，不使用该能力的应用不会多出一张空表，也没有任何运行时开销。
/// </para>
/// <para>
/// 开启后还需要至少一个 <c>IRetentionPolicyProvider</c> 声明保留策略——
/// 框架不会替你猜哪些数据该在多久之后销毁。
/// </para>
/// </remarks>
[ConfigSection("Audit:DataDestruction")]
public class DataDestructionOptions
{
    /// <summary>
    /// 是否启用策略驱动的数据销毁。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 从 <c>false</c> 改为 <c>true</c> 需要一次数据库迁移（新增 <c>Audit_DataDestruction</c> 表）。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// <strong>空跑模式</strong>：照常扫描、照常出销毁证明，但<strong>不真的删除任何数据</strong>。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上线一个会永久删除生产数据的东西之前，先用它跑几个周期，
    /// 看证明里的条数是否与预期相符。空跑产生的证明其 <c>IsDryRun</c> 为真，
    /// 与真实销毁的证明区分得开——否则事后没人说得清那批数据到底还在不在。
    /// </para>
    /// <para>
    /// 空跑仍然会照常询问诉讼保全，因此也能用来验证保全接线是否生效。
    /// </para>
    /// </remarks>
    public bool DryRun { get; set; }

    /// <summary>
    /// 两次扫描之间的间隔小时数。默认 <c>24</c>。
    /// </summary>
    /// <remarks>
    /// 保留期以天为量级，没有必要扫得更勤。这个值只影响「到期后多久被销毁」的延迟上界。
    /// </remarks>
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// 单次扫描中每条策略最多销毁的记录数。默认 <c>500</c>。
    /// </summary>
    /// <remarks>
    /// 上限存在的理由是首次启用时可能有大量历史数据同时到期，
    /// 一次性删几百万行会长时间持锁。剩下的会在下一个周期继续销毁。
    /// </remarks>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// 是否在销毁证明里保留被销毁记录的标识清单。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认只存标识集合的哈希摘要：足以证明「这一批确实是这些记录」，
    /// 但不会把一份「曾经存在过哪些记录」的清单永久留在库里——
    /// 对匿名举报一类的场景，这份清单本身就是元数据泄漏。
    /// </para>
    /// <para>
    /// 若监管要求能逐条回答「我的第 X 号记录销毁了吗」，把它打开，
    /// 并自行评估这份清单的保管级别。
    /// </para>
    /// </remarks>
    public bool StoreIdentifiers { get; set; }
}
