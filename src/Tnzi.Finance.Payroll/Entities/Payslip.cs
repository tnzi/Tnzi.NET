namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 工资单（发薪批次内单个员工的计算结果 + 过账/付款引用）
/// </summary>
/// <remarks>
/// 员工主数据与结构在计算时快照落列，历史保真不受后续改名/改结构影响。
/// <see cref="CalculationError"/> 非空 = 该员工计算失败（金额为负/净额为负/公式错误/钩子否决），
/// 不炸整批，但只要批次内存在任一 Error 即禁止过账。
/// </remarks>
public class Payslip : MultiTenantAuditedEntity<Guid>
{
    /// <summary>所属发薪批次</summary>
    public Guid PayRunId { get; set; }

    /// <summary>员工</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>员工编码快照</summary>
    public string EmployeeCode { get; set; } = string.Empty;

    /// <summary>员工姓名快照</summary>
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary>薪资结构快照</summary>
    public Guid StructureId { get; set; }

    /// <summary>基薪快照（公式变量 BASE）</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>期间总天数（公式变量 PERIOD_DAYS）</summary>
    public decimal PeriodDays { get; set; }

    /// <summary>实际出勤天数（公式变量 WORKED_DAYS；单张输入可改）</summary>
    public decimal WorkedDays { get; set; }

    /// <summary>毛收入（本位币）</summary>
    public decimal GrossPay { get; set; }

    /// <summary>扣减合计（本位币）</summary>
    public decimal TotalDeductions { get; set; }

    /// <summary>雇主承担合计（本位币）</summary>
    public decimal EmployerCost { get; set; }

    /// <summary>实发净额（本位币；= GrossPay - TotalDeductions）</summary>
    public decimal NetPay { get; set; }

    /// <summary>计算失败原因（null = 成功）</summary>
    public string? CalculationError { get; set; }

    /// <summary>过账凭证（过账时落定；行数分块时各 payslip 记各自凭证）</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>付款凭证（付款时落定）</summary>
    public Guid? PaymentJournalEntryId { get; set; }

    /// <summary>付款状态</summary>
    public PayslipPaymentStatus PaymentStatus { get; set; } = PayslipPaymentStatus.Unpaid;

    /// <summary>付款方式（付款时记录，自由字符串）</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>工资单行（导航属性）</summary>
    public virtual ICollection<PayslipLine> Lines { get; set; } = new List<PayslipLine>();
}
