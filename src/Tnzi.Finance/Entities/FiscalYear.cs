namespace Tnzi.Finance.Entities;

/// <summary>
/// 会计年度
/// </summary>
/// <remarks>
/// 采用"锁定日期"模型而非强制年末结转：关闭年度即禁止在该日期区间内过账/冲销，
/// 利润表始终按日期区间从总账实时聚合，资产负债表的本年利润为计算行。
/// </remarks>
public class FiscalYear : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 名称（如 "FY2026"）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 起始日期（含）
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// 结束日期（含）
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// 是否已关闭（关闭后区间内禁止过账）
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// 关闭时间
    /// </summary>
    public DateTime? ClosedTime { get; set; }

    /// <summary>
    /// 关闭人ID
    /// </summary>
    public Guid? ClosedById { get; set; }

    /// <summary>
    /// 最近一次重开时间（重开=解除区间锁定，敏感控制动作，须留痕）
    /// </summary>
    public DateTime? ReopenedTime { get; set; }

    /// <summary>
    /// 最近一次重开人ID
    /// </summary>
    public Guid? ReopenedById { get; set; }
}
