namespace Tnzi.Payment.Dtos;

/// <summary>
/// 支付统计概览 DTO
/// </summary>
public class PaymentStatisticsDto
{
    /// <summary>
    /// 统计时间范围开始
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 统计时间范围结束
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 总收入
    /// </summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>
    /// 总交易数
    /// </summary>
    public int TotalTransactions { get; set; }

    /// <summary>
    /// 成功交易数
    /// </summary>
    public int SuccessfulTransactions { get; set; }

    /// <summary>
    /// 失败交易数
    /// </summary>
    public int FailedTransactions { get; set; }

    /// <summary>
    /// 退款总额
    /// </summary>
    public decimal TotalRefunds { get; set; }

    /// <summary>
    /// 退款笔数
    /// </summary>
    public int RefundCount { get; set; }

    /// <summary>
    /// 退款率
    /// </summary>
    public decimal RefundRate { get; set; }

    /// <summary>
    /// 活跃订阅数
    /// </summary>
    public int ActiveSubscriptions { get; set; }

    /// <summary>
    /// 渠道分布
    /// </summary>
    public List<ChannelStatisticsDto> ChannelDistribution { get; set; } = new();
}

/// <summary>
/// 渠道统计 DTO
/// </summary>
public class ChannelStatisticsDto
{
    /// <summary>
    /// 渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 渠道收入
    /// </summary>
    public decimal Revenue { get; set; }

    /// <summary>
    /// 交易数量
    /// </summary>
    public int TransactionCount { get; set; }

    /// <summary>
    /// 占比百分比
    /// </summary>
    public decimal Percentage { get; set; }
}

/// <summary>
/// 统计查询 DTO
/// </summary>
public class StatisticsQueryDto
{
    /// <summary>
    /// 开始时间（默认30天前）
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间（默认当前）
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 收入趋势查询 DTO
/// </summary>
public class RevenueTrendQueryDto
{
    /// <summary>
    /// 开始时间（默认30天前）
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间（默认当前）
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 粒度（Day/Week/Month，默认 Day）
    /// </summary>
    public TrendGranularity Granularity { get; set; } = TrendGranularity.Day;
}

/// <summary>
/// 趋势粒度
/// </summary>
public enum TrendGranularity
{
    /// <summary>按天</summary>
    Day = 1,
    /// <summary>按周</summary>
    Week = 2,
    /// <summary>按月</summary>
    Month = 3
}

/// <summary>
/// 收入趋势数据点 DTO
/// </summary>
public class RevenueTrendPointDto
{
    /// <summary>
    /// 时间点（粒度起始时间）
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 收入
    /// </summary>
    public decimal Revenue { get; set; }

    /// <summary>
    /// 交易数
    /// </summary>
    public int TransactionCount { get; set; }

    /// <summary>
    /// 退款额
    /// </summary>
    public decimal RefundAmount { get; set; }

    /// <summary>
    /// 净收入（收入 - 退款）
    /// </summary>
    public decimal NetRevenue { get; set; }
}

/// <summary>
/// 订阅指标 DTO
/// </summary>
public class SubscriptionMetricsDto
{
    /// <summary>
    /// 月度经常性收入 (MRR)
    /// </summary>
    public decimal MonthlyRecurringRevenue { get; set; }

    /// <summary>
    /// 活跃订阅数
    /// </summary>
    public int ActiveSubscriptions { get; set; }

    /// <summary>
    /// 试用中订阅数
    /// </summary>
    public int TrialSubscriptions { get; set; }

    /// <summary>
    /// 本月新增订阅数
    /// </summary>
    public int NewSubscriptionsThisMonth { get; set; }

    /// <summary>
    /// 本月取消订阅数
    /// </summary>
    public int CancelledThisMonth { get; set; }

    /// <summary>
    /// 流失率（本月取消/上月活跃，百分比）
    /// </summary>
    public decimal ChurnRate { get; set; }

    /// <summary>
    /// 平均每用户收入 (ARPU)
    /// </summary>
    public decimal AverageRevenuePerUser { get; set; }

    /// <summary>
    /// 计划分布
    /// </summary>
    public List<PlanDistributionDto> PlanDistribution { get; set; } = new();
}

/// <summary>
/// 计划分布 DTO
/// </summary>
public class PlanDistributionDto
{
    /// <summary>
    /// 计划名称
    /// </summary>
    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// 订阅数
    /// </summary>
    public int SubscriptionCount { get; set; }

    /// <summary>
    /// 收入贡献
    /// </summary>
    public decimal Revenue { get; set; }
}

/// <summary>
/// 对账报表查询 DTO
/// </summary>
public class ReconciliationQueryDto
{
    /// <summary>
    /// 开始时间（默认本月1号）
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间（默认当前时间）
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 支付渠道代码（可选筛选）
    /// </summary>
    public string? ChannelCode { get; set; }

    /// <summary>
    /// 支付状态筛选（可选）
    /// </summary>
    public PaymentStatus? Status { get; set; }
}

/// <summary>
/// 对账报表行 DTO
/// </summary>
public class ReconciliationEntryDto
{
    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 外部交易流水号
    /// </summary>
    public string? ExternalTradeNo { get; set; }

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务类型
    /// </summary>
    public string BusinessType { get; set; } = string.Empty;

    /// <summary>
    /// 支付渠道
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 支付方式
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// 原始金额
    /// </summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 实付金额
    /// </summary>
    public decimal PaidAmount { get; set; }

    /// <summary>
    /// 退款金额
    /// </summary>
    public decimal RefundAmount { get; set; }

    /// <summary>
    /// 净收入（实付 - 退款）
    /// </summary>
    public decimal NetAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// 支付状态
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 支付完成时间
    /// </summary>
    public DateTime? PaidTime { get; set; }
}

/// <summary>
/// 对账报表导出结果 DTO
/// </summary>
public class ReconciliationExportResultDto
{
    /// <summary>
    /// CSV 文件内容
    /// </summary>
    public string CsvContent { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalRecords { get; set; }

    /// <summary>
    /// 汇总：总收入
    /// </summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>
    /// 汇总：总退款
    /// </summary>
    public decimal TotalRefunds { get; set; }

    /// <summary>
    /// 汇总：净收入
    /// </summary>
    public decimal NetRevenue { get; set; }
}

/// <summary>
/// 促销效果分析 DTO
/// </summary>
public class PromotionAnalyticsDto
{
    /// <summary>
    /// 促销ID
    /// </summary>
    public Guid PromotionId { get; set; }

    /// <summary>
    /// 促销名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 促销代码
    /// </summary>
    public string PromotionCode { get; set; } = string.Empty;

    /// <summary>
    /// 折扣类型
    /// </summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>
    /// 折扣值
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// 使用次数
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// 唯一用户数
    /// </summary>
    public int UniqueUsers { get; set; }

    /// <summary>
    /// 总折扣金额
    /// </summary>
    public decimal TotalDiscountAmount { get; set; }

    /// <summary>
    /// 平均每次折扣金额
    /// </summary>
    public decimal AverageDiscountPerUse { get; set; }

    /// <summary>
    /// 兑换率（已使用/总限额，无限额时为 -1）
    /// </summary>
    public decimal RedemptionRate { get; set; }

    /// <summary>
    /// 是否活跃
    /// </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// 退款分析 DTO
/// </summary>
public class RefundAnalyticsDto
{
    /// <summary>
    /// 退款总笔数
    /// </summary>
    public int TotalRefundCount { get; set; }

    /// <summary>
    /// 退款总金额
    /// </summary>
    public decimal TotalRefundAmount { get; set; }

    /// <summary>
    /// 平均退款处理时间（小时）
    /// </summary>
    public double AverageProcessingTimeHours { get; set; }

    /// <summary>
    /// 按退款原因分布
    /// </summary>
    public List<RefundReasonBreakdownDto> ReasonBreakdown { get; set; } = new();

    /// <summary>
    /// 按支付渠道分布
    /// </summary>
    public List<RefundChannelBreakdownDto> ChannelBreakdown { get; set; } = new();

    /// <summary>
    /// 按退款状态分布
    /// </summary>
    public List<RefundStatusBreakdownDto> StatusBreakdown { get; set; } = new();
}

/// <summary>
/// 退款原因分布 DTO
/// </summary>
public class RefundReasonBreakdownDto
{
    /// <summary>
    /// 退款原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 占比百分比
    /// </summary>
    public decimal Percentage { get; set; }
}

/// <summary>
/// 退款渠道分布 DTO
/// </summary>
public class RefundChannelBreakdownDto
{
    /// <summary>
    /// 渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 占比百分比
    /// </summary>
    public decimal Percentage { get; set; }
}

/// <summary>
/// 退款状态分布 DTO
/// </summary>
public class RefundStatusBreakdownDto
{
    /// <summary>
    /// 退款状态
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 占比百分比
    /// </summary>
    public decimal Percentage { get; set; }
}
