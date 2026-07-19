namespace Tnzi.Finance.Payroll.Metadata;

/// <summary>
/// 薪资组件类型（决定过账方向与净额影响）
/// </summary>
public enum SalaryComponentType
{
    /// <summary>收入项（借 费用科目；计入 Gross 与 Net）</summary>
    Earning = 1,

    /// <summary>扣减项（贷 负债科目；减少 Net）</summary>
    Deduction = 2,

    /// <summary>雇主承担项（借 费用科目 + 贷 负债科目双边；不影响 Net）</summary>
    EmployerContribution = 3
}

/// <summary>
/// 发薪频率
/// </summary>
public enum PayFrequency
{
    /// <summary>月薪（12 期/年）</summary>
    Monthly = 1,

    /// <summary>半月薪（24 期/年）</summary>
    SemiMonthly = 2,

    /// <summary>双周薪（26 期/年）</summary>
    BiWeekly = 3,

    /// <summary>周薪（52 期/年）</summary>
    Weekly = 4
}

/// <summary>
/// Ytd() 年度累计口径
/// </summary>
public enum YtdBasis
{
    /// <summary>日历年（默认）</summary>
    CalendarYear = 1,

    /// <summary>会计年度（未定义会计年度时回退日历年并记录警告）</summary>
    FiscalYear = 2
}

/// <summary>
/// 发薪批次状态机
/// </summary>
/// <remarks>
/// Draft → Calculated → Posted → PartiallyPaid → Paid；Posted 及之后可 Voided（冲销全部凭证）。
/// Draft 可删（payslips 级联硬删）。
/// </remarks>
public enum PayRunStatus
{
    /// <summary>草稿（未计算，可编辑/删除）</summary>
    Draft = 0,

    /// <summary>已计算（payslips 已生成；仅此态可改单张输入）</summary>
    Calculated = 1,

    /// <summary>已过账（总账凭证已生成，不可再改）</summary>
    Posted = 2,

    /// <summary>部分付款</summary>
    PartiallyPaid = 3,

    /// <summary>已付清</summary>
    Paid = 4,

    /// <summary>已作废（付款与过账凭证已全部冲销）</summary>
    Voided = 5
}

/// <summary>
/// 发薪批次来源
/// </summary>
public enum PayRunSource
{
    /// <summary>内部计算（本模块公式引擎）</summary>
    Internal = 1,

    /// <summary>外部薪酬提供者摄取（embedded provider）</summary>
    External = 2,

    /// <summary>期初余额（年中上线灌历史累计，禁过账/付款、不入总账、只供 Ytd() 聚合）</summary>
    OpeningBalance = 3
}

/// <summary>
/// 单张 payslip 的付款状态
/// </summary>
public enum PayslipPaymentStatus
{
    /// <summary>未付款</summary>
    Unpaid = 0,

    /// <summary>已付款</summary>
    Paid = 1
}
