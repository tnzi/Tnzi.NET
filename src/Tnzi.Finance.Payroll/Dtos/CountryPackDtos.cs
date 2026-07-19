namespace Tnzi.Finance.Payroll.Dtos;

/// <summary>
/// Country pack DTO（已注册的国家/地区薪酬包）
/// </summary>
public class CountryPackDto
{
    /// <summary>国家/地区代码（如 "US" / "CA" / "CN"）</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>显示名</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>说明</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Country pack 播种结果
/// </summary>
public class CountryPackSeedResult
{
    /// <summary>播种/更新的薪资组件数</summary>
    public int ComponentsSeeded { get; set; }

    /// <summary>播种/更新的税级表数</summary>
    public int BracketTablesSeeded { get; set; }
}

/// <summary>
/// 外部薪酬批次摄取请求（embedded provider 或历史迁移灌数据）
/// </summary>
/// <remarks>
/// 幂等：<see cref="ProviderRunId"/> 先查 + 唯一索引兜底返回赢家。
/// 每行必须携带本地已注册的组件 Code（未 seed 返回 400 并指引先 seed）。
/// <see cref="Source"/> = OpeningBalance 时禁过账/付款，只灌 payslip 供 Ytd() 聚合。
/// </remarks>
public class ExternalPayRunIngestDto
{
    /// <summary>外部批次标识（幂等键，必填）</summary>
    public string ProviderRunId { get; set; } = null!;

    /// <summary>来源（External 或 OpeningBalance）</summary>
    public PayRunSource Source { get; set; } = PayRunSource.External;

    /// <summary>周期开始日</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>周期结束日</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>发薪日</summary>
    public DateTime PayDate { get; set; }

    /// <summary>发薪频率</summary>
    public PayFrequency Frequency { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>工资单集合</summary>
    public List<ExternalPayslipDto> Payslips { get; set; } = null!;
}

/// <summary>
/// 外部摄取的工资单
/// </summary>
public class ExternalPayslipDto
{
    /// <summary>员工编码（须为本地已存在员工）</summary>
    public string EmployeeCode { get; set; } = null!;

    /// <summary>实际出勤天数（可空，缺省取周期总天数）</summary>
    public decimal? WorkedDays { get; set; }

    /// <summary>工资单行</summary>
    public List<ExternalPayslipLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 外部摄取的工资单行
/// </summary>
public class ExternalPayslipLineDto
{
    /// <summary>本地已注册的组件 Code（决定过账方向与科目）</summary>
    public string ComponentCode { get; set; } = null!;

    /// <summary>金额（本位币，已由外部计算完成）</summary>
    public decimal Amount { get; set; }
}

// ── Embedded provider 契约用 DTO（v1 仅定契约，不做编排）────────────────────

/// <summary>
/// Embedded provider 批次提交请求
/// </summary>
public class EmbeddedPayRunRequest
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime PayDate { get; set; }
    public PayFrequency Frequency { get; set; }

    /// <summary>提交的员工编码集合（空 = 全部有分配的员工）</summary>
    public List<string> EmployeeCodes { get; set; } = [];
}

/// <summary>
/// Embedded provider 批次提交回执
/// </summary>
public class ExternalPayRunSubmission
{
    /// <summary>外部批次标识（后续状态/结果查询用）</summary>
    public string ProviderRunId { get; set; } = string.Empty;
}

/// <summary>
/// Embedded provider 批次状态
/// </summary>
public class ExternalPayRunStatusDto
{
    public string ProviderRunId { get; set; } = string.Empty;

    /// <summary>提供者侧状态（自由字符串，如 pending/processing/completed/failed）</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Embedded provider 批次结果（可直接映射为 <see cref="ExternalPayRunIngestDto"/> 摄取）
/// </summary>
public class ExternalPayRunResultDto
{
    public string ProviderRunId { get; set; } = string.Empty;

    /// <summary>计算完成的工资单</summary>
    public List<ExternalPayslipDto> Payslips { get; set; } = [];
}
