namespace Tnzi.Audit.Options;

/// <summary>
/// 记录级读取审计选项（配置节 <c>Audit:RecordAccess</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <strong>默认关闭。</strong>关闭时 <c>AuditRecordAccess</c> 实体<strong>不映射为数据表</strong>，
/// 不使用该能力的应用不会多出一张空表，也不会有任何运行时开销。
/// </para>
/// <para>
/// 开启后仍需业务代码显式调用 <c>IRecordAccessAuditor.RecordAsync</c>：
/// 框架无法替你判断「哪一次查询算是读了一条敏感记录」——
/// 列表页扫过一百条与详情页打开一条，合规意义完全不同。
/// </para>
/// </remarks>
[ConfigSection("Audit:RecordAccess")]
public class RecordAccessAuditOptions
{
    /// <summary>
    /// 是否启用记录级读取审计。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 从 <c>false</c> 改为 <c>true</c> 需要一次数据库迁移（新增 <c>Audit_RecordAccess</c> 表）。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// 单个用户每小时可读取的记录条数上限；<c>0</c> 表示不限制。默认 <c>0</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是<strong>批量导出防线</strong>，不是性能限流。2026 年 3 月那次同业泄露中，
    /// 攻击者用一个被社工的账号导出了整库 —— 没有任何一层察觉到「这个账号今天读的量
    /// 是平时的一万倍」。
    /// </para>
    /// <para>
    /// 超限时 <c>RecordAsync</c> 返回失败（HTTP 429），由调用方决定是拒绝这次读取
    /// 还是仅告警。<strong>框架不替你选</strong>：对举报平台该拒绝，对客服系统可能只该告警。
    /// </para>
    /// </remarks>
    public int MaxReadsPerUserPerHour { get; set; }

    /// <summary>
    /// 写入冲突时的重试次数。默认 <c>3</c>。
    /// </summary>
    /// <remarks>
    /// 哈希链靠 <c>(UserId, Sequence)</c> 唯一索引保证完整：并发写入抢到同一序号时
    /// 数据库拒绝其一，服务层重新读取链尾后重试。
    /// </remarks>
    public int MaxWriteRetries { get; set; } = 3;
}
