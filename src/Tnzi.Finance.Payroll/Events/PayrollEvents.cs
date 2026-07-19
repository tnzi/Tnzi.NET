namespace Tnzi.Finance.Payroll.Events;

/// <summary>
/// 发薪批次已计算事件（payslips 已生成/重建）
/// </summary>
/// <remarks>
/// 过账/作废复用 Finance 的 <see cref="Tnzi.Finance.Events.FinanceDocumentPostedEvent"/> /
/// <see cref="Tnzi.Finance.Events.FinanceDocumentVoidedEvent"/>（DocType="PayRun"）。
/// </remarks>
public class PayRunCalculatedEvent : EventBase
{
    /// <summary>发薪批次</summary>
    public Guid PayRunId { get; set; }

    /// <summary>员工数</summary>
    public int EmployeeCount { get; set; }

    /// <summary>计算失败的 payslip 数（&gt; 0 时批次不可过账）</summary>
    public int ErrorCount { get; set; }

    /// <summary>毛收入合计（本位币）</summary>
    public decimal GrossTotal { get; set; }

    /// <summary>实发净额合计（本位币）</summary>
    public decimal NetTotal { get; set; }
}

/// <summary>
/// 发薪批次已付款事件（可多次触发，每次付一批未付 payslip）
/// </summary>
public class PayRunPaidEvent : EventBase
{
    /// <summary>发薪批次</summary>
    public Guid PayRunId { get; set; }

    /// <summary>本次付款的员工数</summary>
    public int PaidEmployeeCount { get; set; }

    /// <summary>本次付款金额（本位币）</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>资金科目</summary>
    public Guid PaymentAccountId { get; set; }

    /// <summary>付款后批次是否已全部付清</summary>
    public bool FullyPaid { get; set; }
}
