namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 发薪批次（一个发薪周期对一组员工的计算 → 过账 → 付款生命周期）
/// </summary>
/// <remarks>
/// 状态机见 <see cref="Metadata.PayRunStatus"/>。<see cref="Number"/> 过账时才分配
/// （scope "PayRun"，前缀取 <c>PayrollOptions.PayRunNumberPrefix</c>），草稿/已计算态为 null。
/// 过账/付款/作废全部经 Finance 的 <c>ILedgerPostingService</c> 扩展面投影到总账，
/// 凭证以 SourceType="PayRun"/"PayRun.Payment" + SourceId=批次Id 回链。
/// 聚合快照列在计算时落定，供列表展示与报表，权威金额在 payslip 行。
/// </remarks>
public class PayRun : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记（乐观并发，过账/付款/作废竞态由框架轮换 + 提交冲突 409）</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>批次编号（过账时分配，scope "PayRun"）</summary>
    public string? Number { get; set; }

    /// <summary>状态</summary>
    public PayRunStatus Status { get; set; } = PayRunStatus.Draft;

    /// <summary>周期开始日（date-only）</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>周期结束日（date-only）</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>发薪日（date-only；税级表与 Ytd() 按此解析）</summary>
    public DateTime PayDate { get; set; }

    /// <summary>发薪频率</summary>
    public PayFrequency Frequency { get; set; }

    /// <summary>薪资结构过滤（null = 全部有分配的员工）</summary>
    public Guid? StructureId { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>来源</summary>
    public PayRunSource Source { get; set; } = PayRunSource.Internal;

    /// <summary>外部提供者的批次标识（幂等键；唯一过滤索引）</summary>
    public string? ProviderRunId { get; set; }

    /// <summary>员工数快照</summary>
    public int EmployeeCount { get; set; }

    /// <summary>毛收入合计快照（本位币）</summary>
    public decimal GrossTotal { get; set; }

    /// <summary>扣减合计快照（本位币）</summary>
    public decimal DeductionTotal { get; set; }

    /// <summary>雇主承担合计快照（本位币）</summary>
    public decimal EmployerCostTotal { get; set; }

    /// <summary>实发净额合计快照（本位币）</summary>
    public decimal NetTotal { get; set; }

    /// <summary>Payslip 集合（导航属性）</summary>
    public virtual ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();
}
