namespace Tnzi.Finance.Payroll.Dtos;

/// <summary>
/// 发薪批次 DTO
/// </summary>
public class PayRunDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public PayRunStatus Status { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime PayDate { get; set; }
    public PayFrequency Frequency { get; set; }
    public Guid? StructureId { get; set; }
    public string? StructureName { get; set; }
    public string? Memo { get; set; }
    public PayRunSource Source { get; set; }
    public string? ProviderRunId { get; set; }
    public int EmployeeCount { get; set; }

    /// <summary>计算失败的 payslip 数（&gt; 0 时禁止过账）</summary>
    public int ErrorCount { get; set; }

    public decimal GrossTotal { get; set; }
    public decimal DeductionTotal { get; set; }
    public decimal EmployerCostTotal { get; set; }
    public decimal NetTotal { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 发薪批次列表 DTO
/// </summary>
public class PayRunListDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public PayRunStatus Status { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime PayDate { get; set; }
    public PayFrequency Frequency { get; set; }
    public PayRunSource Source { get; set; }
    public int EmployeeCount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal NetTotal { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建发薪批次草稿请求
/// </summary>
public class CreatePayRunDto
{
    /// <summary>周期开始日</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>周期结束日</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>发薪日</summary>
    public DateTime PayDate { get; set; }

    /// <summary>发薪频率</summary>
    public PayFrequency Frequency { get; set; }

    /// <summary>薪资结构过滤（null = 全部有分配的员工）</summary>
    public Guid? StructureId { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }
}

/// <summary>
/// 更新发薪批次草稿请求（仅 Draft 态）
/// </summary>
public class UpdatePayRunDto : CreatePayRunDto
{
}

/// <summary>
/// 发薪批次查询请求
/// </summary>
public class PayRunQueryDto : PagedQueryDto
{
    /// <summary>状态</summary>
    public PayRunStatus? Status { get; set; }

    /// <summary>来源</summary>
    public PayRunSource? Source { get; set; }

    /// <summary>发薪日下限</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>发薪日上限</summary>
    public DateTime? DateTo { get; set; }

    /// <summary>关键字（批次编号模糊匹配）</summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// 付款请求（Posted/PartiallyPaid 态；可多次调用累进付清）
/// </summary>
public class PayRunPaymentDto
{
    /// <summary>付款的员工（null = 本批次全部未付 payslip）</summary>
    public List<Guid>? EmployeeIds { get; set; }

    /// <summary>资金科目（须为 CashEquivalent 可过账叶子）</summary>
    public Guid PaymentAccountId { get; set; }

    /// <summary>付款日期</summary>
    public DateTime PaymentDate { get; set; }

    /// <summary>付款方式（自由字符串，如 BankTransfer/Check）</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>外部参考号</summary>
    public string? Reference { get; set; }
}

/// <summary>
/// 工资单 DTO（含行）
/// </summary>
public class PayslipDto
{
    public Guid Id { get; set; }
    public Guid PayRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public Guid StructureId { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal PeriodDays { get; set; }
    public decimal WorkedDays { get; set; }
    public decimal GrossPay { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal EmployerCost { get; set; }
    public decimal NetPay { get; set; }
    public string? CalculationError { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? PaymentJournalEntryId { get; set; }
    public PayslipPaymentStatus PaymentStatus { get; set; }
    public string? PaymentMethod { get; set; }
    public List<PayslipLineDto> Lines { get; set; } = [];
}

/// <summary>
/// 工资单列表 DTO（不含行）
/// </summary>
public class PayslipListDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal GrossPay { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal EmployerCost { get; set; }
    public decimal NetPay { get; set; }
    public string? CalculationError { get; set; }
    public PayslipPaymentStatus PaymentStatus { get; set; }
}

/// <summary>
/// 工资单行 DTO
/// </summary>
public class PayslipLineDto
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public Guid ComponentId { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public SalaryComponentType ComponentType { get; set; }
    public decimal Amount { get; set; }
    /// <summary>该组件年初至今累计额（含本期）——逐行 YTD。</summary>
    public decimal YtdAmount { get; set; }
    public string? FormulaSnapshot { get; set; }
    public Guid? ExpenseAccountId { get; set; }
    public Guid? LiabilityAccountId { get; set; }
}

/// <summary>
/// 修改单张工资单输入请求（仅 Calculated 态；单独重算）
/// </summary>
public class UpdatePayslipInputsDto
{
    /// <summary>实际出勤天数（公式变量 WORKED_DAYS）</summary>
    public decimal WorkedDays { get; set; }
}
